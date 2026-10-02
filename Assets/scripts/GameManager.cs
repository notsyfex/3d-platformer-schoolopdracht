using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// Single persistent singleton combining the two "always exists, boots once,
/// survives every scene" managers the platformer side of this project used
/// to keep as separate scripts:
///   - Level stats: coins collected + enemies killed, per level, saved to
///     disk (previously LevelStatsManager.cs).
///   - Screen fade: full-screen fade-to-black used for the death/respawn
///     sequence, and reusable for level transitions/cutscenes (previously
///     ScreenFader.cs).
/// Merging them means one boot GameObject and one script instead of two.
///   - Race / level timer: countdown, checkpoints, finish and best times
///     (previously RaceTimer.cs). StartLine, Checkpoint and FinishLine talk
///     to this, and GhostRecorder/GhostPlayer read its timer.
///
/// SETUP:
/// 1. Create ONE empty GameObject in your FIRST/boot scene (e.g.
///    "GameManager") and attach this script. It persists itself across
///    scene loads via DontDestroyOnLoad — don't place it in every scene.
/// 2. (Optional, for screen fade) In a Canvas set to "Screen Space -
///    Overlay", add a full-screen Image (stretched to fill the screen,
///    solid black) with a CanvasGroup on it (starting Alpha 0), and drag
///    that CanvasGroup into Fade Canvas Group below. If you don't need
///    fades, leave it empty — FadeToBlack/FadeFromBlack just do nothing.
/// 3. PlayerCoinCollector.cs and EnemyAI.cs report coins/kills here
///    automatically. Movement.cs calls FadeToBlack/FadeFromBlack here for
///    the death/respawn sequence. No other wiring needed.
/// 4. (Recommended) Fill in Level Totals: one entry per level with its
///    Level Id and how many coins and enemies exist in it. Saved coins and
///    kills for that level can then never go above those numbers —
///    replaying a level won't push "1 / 1" up to "2 / 1". Levels not listed
///    (or with a total of 0) are uncapped.
/// 5. Medals (per level, set up in the same Level Totals entry):
///      - Coin medal:  collect all Total Coins.
///      - Enemy medal: kill all Total Enemies.
///      - Time medals: Bronze / Silver / Gold / Platinum — finish the level at or
///        under that tier's time (seconds). A tier set to 0 isn't used.
///    A medal whose requirement is 0 isn't used for that level.
///    Use HasCoinMedal / HasEnemyMedal / HasTimeMedal / HasAllMedals to
///    check medals.
/// 6. Race / level timer:
///      - Put a StartLine trigger at the start: crossing it plays the
///        3, 2, 1, GO! countdown (player frozen) and then starts the timer.
///        A level with a FinishLine but NO StartLine starts the timer as
///        soon as the scene loads instead.
///      - Optional Checkpoint triggers (index 0, 1, 2, ...) must all be
///        passed in order before the finish counts.
///      - Put a FinishLine trigger at the end: it stops the timer and saves
///        the best time for this level (the same time the time medal uses).
///      The timer is drawn on screen automatically in levels that have a
///      StartLine or FinishLine (turn off Show Timer On Screen to hide it).
/// 7. Finish popup: attach FinishResultsUI to this same GameObject. It
///    listens to OnLevelCompleted (fired on every finish) and shows the
///    time, the three medals and Retry / Quit buttons.
///
/// Replaces RaceTimer.cs too — remove the RaceTimer component from your
/// scenes, then delete RaceTimer.cs.
///
/// Replaces LevelStatsManager.cs and ScreenFader.cs — delete those two
/// files and their scene components, replacing both with this one.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Screen Fade")]
    [Tooltip("CanvasGroup of a full-screen black Image, used for fade-to-black transitions (death/respawn, level changes, etc). Leave empty if you don't need screen fades.")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;

    [Header("Race / Level Timer")]
    [Tooltip("Seconds per countdown step (3, 2, 1, GO!).")]
    [SerializeField] private float countdownStepSeconds = 1f;
    [Tooltip("Draws the timer, best time and messages on screen during levels that have a StartLine or FinishLine. No Canvas setup needed.")]
    [SerializeField] private bool showTimerOnScreen = true;
    [SerializeField] private int timerFontSize = 40;

    /// <summary>Time medal tiers, from worst to best. None = no medal earned.</summary>
    public enum TimeMedal { None, Bronze, Silver, Gold, Platinum }

    [System.Serializable]
    public class LevelTotal
    {
        [Tooltip("Must match the Level Id on that level's LevelId component (or the scene name, if that level has no LevelId).")]
        public string levelId;
        [Tooltip("How many coins exist in this level. Saved coins for this level are capped at this number. 0 = uncapped.")]
        public int totalCoins;
        [Tooltip("How many enemies exist in this level. Saved kills for this level are capped at this number. 0 = uncapped.")]
        public int totalEnemies;
        [Tooltip("Bronze: finish the level in this many seconds or less. 0 = no bronze. Bronze is the slowest (biggest) time.")]
        [FormerlySerializedAs("targetTimeSeconds")] // an old single Target Time becomes the bronze time
        public float bronzeTimeSeconds;
        [Tooltip("Silver: finish in this many seconds or less. 0 = no silver.")]
        public float silverTimeSeconds;
        [Tooltip("Gold: finish in this many seconds or less. 0 = no gold.")]
        public float goldTimeSeconds;
        [Tooltip("Platinum: finish in this many seconds or less. 0 = no platinum. Platinum is the fastest (smallest) time.")]
        public float platinumTimeSeconds;
    }

    [Header("Level Totals")]
    [Tooltip("One entry per level: how many coins and enemies it has. Coins/kills are capped at these, so replaying a level can't give extra.")]
    [FormerlySerializedAs("levelEnemyTotals")]
    [SerializeField] private List<LevelTotal> levelTotals = new List<LevelTotal>();

    [System.Serializable]
    private class LevelStats
    {
        public int coins;
        public int kills;
        public float bestTime; // 0 = never finished
    }

    // Dictionary<levelId, stats for that level>
    private Dictionary<string, LevelStats> statsPerLevel = new Dictionary<string, LevelStats>();
    private Coroutine activeFade;

    // Race / level timer
    public bool IsRunning { get; private set; }
    public bool IsCountingDown { get; private set; }
    public float ElapsedTime { get; private set; }
    public int TotalCheckpoints { get; private set; }
    public int NextCheckpointIndex { get; private set; }
    public string CountdownText => countdownText;

    /// <summary>The time of the most recent finish (for a results screen). 0 if none yet.</summary>
    public float LastCompletionTime { get; private set; }

    /// <summary>The time medal tier the most recent finish earned (None if it missed every tier).</summary>
    public TimeMedal LastCompletionMedal { get; private set; }

    /// <summary>Fired the moment a finish beats this level's best time. GhostRecorder uses it to save the run.</summary>
    public event System.Action OnNewBestTime;

    /// <summary>Fired on every finish: (levelId, wasNewBest). The finish results popup listens to this.</summary>
    public event System.Action<string, bool> OnLevelCompleted;

    private bool sceneHasRace;
    private string lastMessage = "";
    private string countdownText = "";
    private string checkpointText = "";
    private Coroutine countdownRoutine;
    private Coroutine checkpointTextRoutine;

    private string StatsSavePath => Path.Combine(Application.persistentDataPath, "level_stats_save.json");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // The first GameManager (e.g. from the main menu) survives scene loads, so this
            // scene's Level Totals would be ignored. Hand them to the survivor before we go.
            Instance.MergeLevelTotals(levelTotals);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadStats();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ResetRaceForScene();

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = 0f;
            fadeCanvasGroup.blocksRaycasts = false;
        }
    }

    // ===================================================================
    // Level stats (coins + kills)
    // ===================================================================

    private LevelStats GetOrCreate(string levelId)
    {
        if (!statsPerLevel.TryGetValue(levelId, out LevelStats stats))
        {
            stats = new LevelStats();
            statsPerLevel[levelId] = stats;
        }
        return stats;
    }

    // --- Level totals -------------------------------------------------

    // Adds/overwrites entries (matched by Level Id) from another GameManager's Level Totals.
    private void MergeLevelTotals(List<LevelTotal> incoming)
    {
        if (incoming == null) return;
        foreach (var entry in incoming)
        {
            if (entry == null || string.IsNullOrEmpty(entry.levelId)) continue;
            int index = levelTotals.FindIndex(e => e != null && string.Equals(e.levelId, entry.levelId, System.StringComparison.OrdinalIgnoreCase));
            if (index >= 0) levelTotals[index] = entry;
            else levelTotals.Add(entry);
        }
    }

    /// <summary>For debugging: the Level Ids in Level Totals, in quotes so stray spaces are visible.</summary>
    public string DescribeLevelTotals()
    {
        if (levelTotals == null || levelTotals.Count == 0) return "(Level Totals is empty on the active GameManager)";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (var e in levelTotals)
        {
            if (e == null) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append('\'').Append(e.levelId).Append('\'');
        }
        return sb.ToString();
    }

    private LevelTotal FindLevelTotal(string levelId)
    {
        foreach (var entry in levelTotals)
        {
            if (entry != null && string.Equals(entry.levelId, levelId, System.StringComparison.OrdinalIgnoreCase)) return entry;
        }
        return null;
    }

    /// <summary>How many coins exist in the given level (from Level Totals). 0 = not set / uncapped.</summary>
    public int GetTotalCoinsInLevel(string levelId)
    {
        LevelTotal entry = FindLevelTotal(levelId);
        return entry != null ? Mathf.Max(0, entry.totalCoins) : 0;
    }

    /// <summary>How many enemies exist in the given level (from Level Totals). 0 = not set / uncapped.</summary>
    public int GetTotalEnemiesInLevel(string levelId)
    {
        LevelTotal entry = FindLevelTotal(levelId);
        return entry != null ? Mathf.Max(0, entry.totalEnemies) : 0;
    }

    private int ClampCoins(string levelId, int coins)
    {
        int max = GetTotalCoinsInLevel(levelId);
        return max > 0 ? Mathf.Min(coins, max) : coins;
    }

    private int ClampKills(string levelId, int kills)
    {
        int max = GetTotalEnemiesInLevel(levelId);
        return max > 0 ? Mathf.Min(kills, max) : kills;
    }

    // --- Coins --------------------------------------------------------

    /// <summary>
    /// Adds coins to the given level's total and saves immediately.
    /// Never goes above the level's coin total (if one is set), so
    /// replaying a level can't give extra coins.
    /// </summary>
    public void AddCoins(string levelId, int amount)
    {
        LevelStats stats = GetOrCreate(levelId);
        int before = stats.coins;
        stats.coins = ClampCoins(levelId, stats.coins + amount);

        if (stats.coins != before) SaveStats();
    }

    /// <summary>Coins collected so far in a specific level.</summary>
    public int GetCoinsForLevel(string levelId)
    {
        return statsPerLevel.TryGetValue(levelId, out LevelStats stats) ? stats.coins : 0;
    }

    /// <summary>Sum of coins collected across every level.</summary>
    public int GetTotalCoins()
    {
        int total = 0;
        foreach (var stats in statsPerLevel.Values) total += stats.coins;
        return total;
    }

    // --- Kills ----------------------------------------------------------

    /// <summary>
    /// Adds a kill to the given level's total and saves immediately.
    /// Never goes above the level's enemy total (if one is set), so
    /// replaying a level can't give extra kills.
    /// </summary>
    public void AddKill(string levelId, int amount = 1)
    {
        LevelStats stats = GetOrCreate(levelId);
        int before = stats.kills;
        stats.kills = ClampKills(levelId, stats.kills + amount);

        if (stats.kills != before) SaveStats();
    }

    /// <summary>Enemies killed so far in a specific level.</summary>
    public int GetKillsForLevel(string levelId)
    {
        return statsPerLevel.TryGetValue(levelId, out LevelStats stats) ? stats.kills : 0;
    }

    /// <summary>Sum of kills across every level.</summary>
    public int GetTotalKills()
    {
        int total = 0;
        foreach (var stats in statsPerLevel.Values) total += stats.kills;
        return total;
    }

    // ===================================================================
    // Race / level timer + medals
    // ===================================================================

    private void OnDestroy()
    {
        if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) ResetRaceForScene();
    }

    /// <summary>
    /// Resets the timer for a freshly loaded scene and counts its checkpoints.
    /// Levels with a FinishLine but no StartLine start the timer right away.
    /// </summary>
    private void ResetRaceForScene()
    {
        if (countdownRoutine != null) StopCoroutine(countdownRoutine);
        if (checkpointTextRoutine != null) StopCoroutine(checkpointTextRoutine);
        countdownRoutine = null;
        checkpointTextRoutine = null;

        IsRunning = false;
        IsCountingDown = false;
        ElapsedTime = 0f;
        NextCheckpointIndex = 0;
        lastMessage = "";
        countdownText = "";
        checkpointText = "";

        TotalCheckpoints = FindObjectsByType<Checkpoint>(FindObjectsSortMode.None).Length;
        bool hasStart = FindAnyObjectByType<StartLine>() != null;
        bool hasFinish = FindAnyObjectByType<FinishLine>() != null;
        sceneHasRace = hasStart || hasFinish;

        if (hasFinish && !hasStart) StartRace();
    }

    private void Update()
    {
        if (IsRunning) ElapsedTime += Time.deltaTime;
    }

    /// <summary>
    /// Called by StartLine: plays 3, 2, 1, GO! with the player frozen, then
    /// starts the timer. Pass the player's Collider so it can be frozen.
    /// </summary>
    public void BeginCountdown(Collider player = null)
    {
        if (IsRunning || IsCountingDown) return;
        countdownRoutine = StartCoroutine(CountdownRoutine(player));
    }

    private IEnumerator CountdownRoutine(Collider player)
    {
        IsCountingDown = true;
        lastMessage = "";

        CharacterController cc = player != null ? player.GetComponent<CharacterController>() : null;
        Movement movement = player != null ? player.GetComponent<Movement>() : null;
        Rigidbody rb = player != null ? player.attachedRigidbody : null;

        bool movementWasEnabled = movement != null && movement.enabled;
        bool rbWasKinematic = rb != null && rb.isKinematic;

        // Freeze: disable the movement script first so it doesn't call Move()
        // on a disabled CharacterController.
        if (movement != null) movement.enabled = false;
        if (cc != null) cc.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        for (int i = 3; i >= 1; i--)
        {
            countdownText = i.ToString();
            yield return new WaitForSeconds(countdownStepSeconds);
        }

        countdownText = "GO!";
        yield return new WaitForSeconds(countdownStepSeconds * 0.5f);
        countdownText = "";

        // Unfreeze.
        if (cc != null) cc.enabled = true;
        if (rb != null) rb.isKinematic = rbWasKinematic;
        if (movement != null) movement.enabled = movementWasEnabled;

        IsCountingDown = false;
        countdownRoutine = null;
        StartRace();
    }

    /// <summary>Starts the timer from 0 immediately (no countdown).</summary>
    public void StartRace()
    {
        ElapsedTime = 0f;
        IsRunning = true;
        NextCheckpointIndex = 0;
        lastMessage = "";
        checkpointText = "";
    }

    /// <summary>
    /// Called by Checkpoint. Only advances if this is the next checkpoint
    /// expected — passing one out of order (or twice) is ignored.
    /// </summary>
    public void PassCheckpoint(int index)
    {
        if (!IsRunning) return;
        if (index != NextCheckpointIndex) return;

        NextCheckpointIndex++;
        checkpointText = $"Checkpoint {NextCheckpointIndex}/{TotalCheckpoints}";

        if (checkpointTextRoutine != null) StopCoroutine(checkpointTextRoutine);
        checkpointTextRoutine = StartCoroutine(ClearCheckpointTextRoutine());
    }

    private IEnumerator ClearCheckpointTextRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        checkpointText = "";
        checkpointTextRoutine = null;
    }

    /// <summary>
    /// Called by FinishLine. Refuses to finish if a checkpoint was missed;
    /// otherwise stops the timer and saves the best time for the current level.
    /// </summary>
    public void FinishRace()
    {
        if (!IsRunning) return;

        if (NextCheckpointIndex < TotalCheckpoints)
        {
            lastMessage = "Missed a checkpoint!";
            return;
        }

        bool isNewBest = CompleteLevel(LevelId.CurrentLevelId);
        lastMessage = isNewBest ? "NEW BEST TIME!" : "Finish!";
        if (LastCompletionMedal != TimeMedal.None)
            lastMessage += $" <color=#{ColorUtility.ToHtmlStringRGB(GetMedalColor(LastCompletionMedal))}>{LastCompletionMedal} medal!</color>";
    }

    /// <summary>
    /// Stops the timer and saves the time if it's the best one for that level
    /// (skips the checkpoint check — FinishLine uses FinishRace instead).
    /// Fires OnLevelCompleted so the results popup can show up.
    /// Returns true if it was a new best time.
    /// </summary>
    public bool CompleteLevel(string levelId)
    {
        if (!IsRunning)
        {
            Debug.LogWarning($"GameManager: CompleteLevel('{levelId}') called but the timer isn't running (already finished?).", this);
            return false;
        }

        IsRunning = false;
        float time = ElapsedTime;
        LastCompletionTime = time;
        LastCompletionMedal = GetTimeMedalForTime(levelId, time);

        LevelStats stats = GetOrCreate(levelId);
        bool newBest = stats.bestTime <= 0f || time < stats.bestTime;
        if (newBest)
        {
            stats.bestTime = time;
            SaveStats();
            OnNewBestTime?.Invoke();
        }

        // Fired after the best time is saved, so the time medal is up to date.
        OnLevelCompleted?.Invoke(levelId, newBest);
        return newBest;
    }

    /// <summary>Seconds on the timer right now.</summary>
    public float GetCurrentLevelTime()
    {
        return ElapsedTime;
    }

    private void OnGUI()
    {
        if (!showTimerOnScreen || !sceneHasRace) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = timerFontSize,
            alignment = TextAnchor.UpperLeft
        };
        style.normal.textColor = Color.white;

        style.richText = true;
        GUI.Label(new Rect(20, 20, 800, 60), FormatTime(ElapsedTime), style);

        string levelId = LevelId.CurrentLevelId;
        float best = GetBestTime(levelId);
        string bestLine = best > 0f ? $"Best: {FormatTime(best)}" : "Best: --:--";
        TimeMedal bestMedal = GetTimeMedal(levelId);
        if (bestMedal != TimeMedal.None)
            bestLine += $"  <color=#{ColorUtility.ToHtmlStringRGB(GetMedalColor(bestMedal))}>{bestMedal}</color>";

        style.fontSize = timerFontSize / 2;
        GUI.Label(new Rect(20, 20 + timerFontSize, 800, 40), bestLine, style);

        int row = 1;
        string tierLine = BuildTierLine(levelId);
        if (!string.IsNullOrEmpty(tierLine))
            GUI.Label(new Rect(20, 20 + timerFontSize + 40 * row++, 800, 40), tierLine, style);

        if (!string.IsNullOrEmpty(lastMessage))
            GUI.Label(new Rect(20, 20 + timerFontSize + 40 * row++, 800, 40), lastMessage, style);

        if (!string.IsNullOrEmpty(checkpointText))
            GUI.Label(new Rect(20, 20 + timerFontSize + 40 * row++, 800, 40), checkpointText, style);

        if (!string.IsNullOrEmpty(countdownText))
        {
            GUIStyle countdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = timerFontSize * 3,
                alignment = TextAnchor.MiddleCenter
            };
            countdownStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(0, Screen.height / 2f - 100, Screen.width, 200), countdownText, countdownStyle);
        }
    }

    /// <summary>Best finish time for a level in seconds (0 = never finished).</summary>
    public float GetBestTime(string levelId)
    {
        return statsPerLevel.TryGetValue(levelId, out LevelStats stats) ? stats.bestTime : 0f;
    }

    /// <summary>
    /// The time you need to beat to earn ANY time medal (the slowest tier that's set up),
    /// in seconds. 0 = this level has no time medals. Kept so older UI code keeps working.
    /// </summary>
    public float GetTargetTime(string levelId)
    {
        LevelTotal entry = FindLevelTotal(levelId);
        if (entry == null) return 0f;
        return Mathf.Max(0f, entry.bronzeTimeSeconds, entry.silverTimeSeconds, entry.goldTimeSeconds, entry.platinumTimeSeconds);
    }

    /// <summary>The time (seconds) needed for a given medal tier in a level. 0 = that tier isn't set up.</summary>
    public float GetTierTime(string levelId, TimeMedal tier)
    {
        LevelTotal entry = FindLevelTotal(levelId);
        if (entry == null) return 0f;

        switch (tier)
        {
            case TimeMedal.Bronze:   return Mathf.Max(0f, entry.bronzeTimeSeconds);
            case TimeMedal.Silver:   return Mathf.Max(0f, entry.silverTimeSeconds);
            case TimeMedal.Gold:     return Mathf.Max(0f, entry.goldTimeSeconds);
            case TimeMedal.Platinum: return Mathf.Max(0f, entry.platinumTimeSeconds);
            default:                 return 0f;
        }
    }

    /// <summary>Which time medal a given finish time earns in a level (best tier it qualifies for).</summary>
    public TimeMedal GetTimeMedalForTime(string levelId, float time)
    {
        if (time <= 0f) return TimeMedal.None;

        for (int i = (int)TimeMedal.Platinum; i > (int)TimeMedal.None; i--)
        {
            TimeMedal tier = (TimeMedal)i;
            float needed = GetTierTime(levelId, tier);
            if (needed > 0f && time <= needed) return tier;
        }
        return TimeMedal.None;
    }

    /// <summary>The time medal earned by this level's saved best time (None if never finished or too slow).</summary>
    public TimeMedal GetTimeMedal(string levelId)
    {
        return GetTimeMedalForTime(levelId, GetBestTime(levelId));
    }

    /// <summary>Display name for a tier ("Gold", or "No medal").</summary>
    public static string GetMedalName(TimeMedal medal)
    {
        return medal == TimeMedal.None ? "No medal" : medal.ToString();
    }

    /// <summary>A colour for each tier, for tinting UI text or icons.</summary>
    public static Color GetMedalColor(TimeMedal medal)
    {
        switch (medal)
        {
            case TimeMedal.Bronze:   return new Color(0.80f, 0.50f, 0.20f);
            case TimeMedal.Silver:   return new Color(0.78f, 0.78f, 0.82f);
            case TimeMedal.Gold:     return new Color(1.00f, 0.84f, 0.00f);
            case TimeMedal.Platinum: return new Color(0.55f, 0.90f, 0.95f);
            default:                 return new Color(0.55f, 0.55f, 0.55f);
        }
    }

    // One line like "Bronze 1:00.00   Silver 0:50.00 ..." with only the tiers that are set up.
    public string BuildTierLine(string levelId)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = (int)TimeMedal.Bronze; i <= (int)TimeMedal.Platinum; i++)
        {
            TimeMedal tier = (TimeMedal)i;
            float needed = GetTierTime(levelId, tier);
            if (needed <= 0f) continue;

            if (sb.Length > 0) sb.Append("   ");
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(GetMedalColor(tier))}>{tier}</color> {FormatTime(needed)}");
        }
        return sb.ToString();
    }

    /// <summary>True if every coin in the level has been collected.</summary>
    public bool HasCoinMedal(string levelId)
    {
        int total = GetTotalCoinsInLevel(levelId);
        return total > 0 && GetCoinsForLevel(levelId) >= total;
    }

    /// <summary>True if every enemy in the level has been killed.</summary>
    public bool HasEnemyMedal(string levelId)
    {
        int total = GetTotalEnemiesInLevel(levelId);
        return total > 0 && GetKillsForLevel(levelId) >= total;
    }

    /// <summary>True if the level's best time earned at least a bronze (any time medal).</summary>
    public bool HasTimeMedal(string levelId)
    {
        return GetTimeMedal(levelId) != TimeMedal.None;
    }

    /// <summary>How many medals this level has set up (0–3).</summary>
    public int GetMedalCountAvailable(string levelId)
    {
        int count = 0;
        if (GetTotalCoinsInLevel(levelId) > 0) count++;
        if (GetTotalEnemiesInLevel(levelId) > 0) count++;
        if (GetTargetTime(levelId) > 0f) count++;
        return count;
    }

    /// <summary>How many medals have been earned in this level (0–3).</summary>
    public int GetMedalCountEarned(string levelId)
    {
        int count = 0;
        if (HasCoinMedal(levelId)) count++;
        if (HasEnemyMedal(levelId)) count++;
        if (HasTimeMedal(levelId)) count++;
        return count;
    }

    /// <summary>True if every medal that's set up for this level has been earned.</summary>
    public bool HasAllMedals(string levelId)
    {
        int available = GetMedalCountAvailable(levelId);
        return available > 0 && GetMedalCountEarned(levelId) == available;
    }

    /// <summary>Formats seconds as m:ss.ff (e.g. 1:05.32).</summary>
    public static string FormatTime(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int minutes = (int)(seconds / 60f);
        float rest = seconds - minutes * 60f;
        return $"{minutes}:{rest:00.00}";
    }

    // --- Resetting --------------------------------------------------------

    /// <summary>Resets a single level's coins, kills and best time (e.g. if the player restarts that level from scratch).</summary>
    public void ResetLevel(string levelId)
    {
        statsPerLevel[levelId] = new LevelStats();
        SaveStats();
    }

    /// <summary>Wipes all saved progress — every level's coins, kills and best times.</summary>
    public void ResetAll()
    {
        statsPerLevel.Clear();
        SaveStats();
    }

    // --- JSON persistence -------------------------------------------------
    // JsonUtility can't serialize a Dictionary directly, so we convert it
    // to/from a flat list of entries — one entry per level, holding both
    // stats together.

    [System.Serializable]
    private class LevelStatsEntry
    {
        public string levelId;
        public int coins;
        public int kills;
        public float bestTime;
    }

    [System.Serializable]
    private class StatsSaveFile
    {
        public List<LevelStatsEntry> entries = new List<LevelStatsEntry>();
    }

    private void SaveStats()
    {
        StatsSaveFile data = new StatsSaveFile();
        foreach (var kvp in statsPerLevel)
        {
            data.entries.Add(new LevelStatsEntry
            {
                levelId = kvp.Key,
                coins = kvp.Value.coins,
                kills = kvp.Value.kills,
                bestTime = kvp.Value.bestTime
            });
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(StatsSavePath, json);
    }

    private void LoadStats()
    {
        statsPerLevel.Clear();

        if (!File.Exists(StatsSavePath)) return;

        string json = File.ReadAllText(StatsSavePath);
        StatsSaveFile data = JsonUtility.FromJson<StatsSaveFile>(json);
        if (data == null) return;

        foreach (var entry in data.entries)
        {
            // Clamp on load too, so old saves with too many coins/kills get fixed.
            statsPerLevel[entry.levelId] = new LevelStats
            {
                coins = ClampCoins(entry.levelId, entry.coins),
                kills = ClampKills(entry.levelId, entry.kills),
                bestTime = entry.bestTime // 0 for old saves — fine
            };
        }
    }

    // ===================================================================
    // Screen fade
    // ===================================================================

    /// <summary>Fades the screen to solid black over Duration seconds.</summary>
    public Coroutine FadeToBlack(float duration)
    {
        return StartFade(1f, duration);
    }

    /// <summary>Fades the screen from black back to clear over Duration seconds.</summary>
    public Coroutine FadeFromBlack(float duration)
    {
        return StartFade(0f, duration);
    }

    private Coroutine StartFade(float targetAlpha, float duration)
    {
        if (activeFade != null) StopCoroutine(activeFade);
        activeFade = StartCoroutine(FadeRoutine(targetAlpha, duration));
        return activeFade;
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null) yield break;

        // Block clicks/UI interaction through the fade while it's at all visible.
        fadeCanvasGroup.blocksRaycasts = targetAlpha > 0f || fadeCanvasGroup.alpha > 0f;

        float startAlpha = fadeCanvasGroup.alpha;
        float t = 0f;

        if (duration <= 0f)
        {
            fadeCanvasGroup.alpha = targetAlpha;
        }
        else
        {
            while (t < duration)
            {
                t += Time.deltaTime;
                fadeCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t / duration);
                yield return null;
            }
            fadeCanvasGroup.alpha = targetAlpha;
        }

        fadeCanvasGroup.blocksRaycasts = fadeCanvasGroup.alpha > 0f;
        activeFade = null;
    }
}