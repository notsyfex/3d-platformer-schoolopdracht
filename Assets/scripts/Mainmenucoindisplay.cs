using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Reads saved per-level coin AND enemy-kill counts from GameManager
/// and displays them in the main menu — e.g.
/// "Level 1: 7 / 10 coins, 1 / 1 enemies killed".
///
/// SETUP:
/// 1. Put this script on an object in your MAIN MENU scene.
/// 2. Fill in the Levels list in the Inspector: one entry per level, with the
///    same Level Id you set on that level's LevelId component (see LevelId.cs),
///    or the exact scene name if that level has no LevelId component.
///    Coin and enemy totals come from GameManager's Level Totals list
///    automatically. Total Coins In Level / Total Enemies In Level here are
///    only a fallback for levels not listed there. If neither is set, just
///    the raw count shows.
/// 3. For each level entry, you can drag in your own TextMeshPro (TMP_Text) objects
///    directly — nothing is spawned for a level once you do:
///      - Name Text: shows just that level's name (Display Name, or Level Id if blank).
///      - Coins Text: shows just that level's coin count (e.g. "7 / 10 coins").
///      - Kills Text: shows just that level's kill count (e.g. "1 / 1 enemies killed").
///      - Row Text: shows BOTH combined on one line, if you'd rather have a
///        single Text field per level instead of two separate ones.
///    You can use Coins Text + Kills Text, or Row Text, or leave all four
///    empty to fall back to auto-spawning one combined row from Row Text
///    Prefab below (the old behavior). Mixing per level is fine too.
///    Medals (optional, per level entry): drag medal icons into Coin Medal /
///    Enemy Medal / Time Medal / All Medals — each is only shown once that
///    medal is earned. Best Time Text shows the best time (+ target time).
/// 4. Make sure GameManager exists before the main menu shows its data
///    — easiest is to put it in the main menu scene itself if that's the
///    first scene the game loads.
/// </summary>
public class MainMenuCoinDisplay : MonoBehaviour
{
    [System.Serializable]
    public class LevelEntry
    {
        [Tooltip("Must match the Level Id set on that level's LevelId component (or the scene name, if that level has no LevelId).")]
        public string levelId;
        [Tooltip("Friendly name shown in the UI. Leave blank to just use Level Id.")]
        public string displayName;
        [Tooltip("Fallback only: GameManager's coin total (Level Totals) is used first.")]
        public int totalCoinsInLevel = 0;
        [Tooltip("Fallback only: GameManager's enemy total (Level Totals) is used first.")]
        public int totalEnemiesInLevel = 0;
        [Tooltip("Optional: drag in a TextMeshPro (TMP_Text) object for THIS level's name specifically (e.g. \"Level 1\" or its Display Name).")]
        public TMP_Text nameText;
        [Tooltip("Optional: drag in a TextMeshPro (TMP_Text) object for THIS level's coin count specifically (e.g. \"7 / 10 coins\").")]
        public TMP_Text coinsText;
        [Tooltip("Optional: drag in a TextMeshPro (TMP_Text) object for THIS level's kill count specifically (e.g. \"1 / 1 enemies killed\").")]
        public TMP_Text killsText;
        [Tooltip("Optional: drag in a TextMeshPro (TMP_Text) object for THIS level's combined line (name + coins + kills all in one). If Coins Text/Kills Text above are also set, they take priority for their own stat.")]
        public TMP_Text rowText;

        [Header("Medals (optional)")]
        [Tooltip("Optional: object (e.g. a medal icon) that is shown only when all coins in this level are collected.")]
        public GameObject coinMedal;
        [Tooltip("Optional: object that is shown only when all enemies in this level are killed.")]
        public GameObject enemyMedal;
        [Tooltip("Optional: object that is shown only when this level was finished within its target time.")]
        public GameObject timeMedal;
        [Tooltip("Optional: object that is shown only when every medal of this level is earned.")]
        public GameObject allMedals;
        [Tooltip("Optional: shows this level's best time, and the target time if one is set (e.g. \"Best: 0:48.20 / 1:00.00\").")]
        public TMP_Text bestTimeText;
    }

    [Header("Levels to display")]
    [SerializeField] private List<LevelEntry> levels = new List<LevelEntry>();

    [Header("UI (fallback for levels with nothing assigned above)")]
    [Tooltip("A prefab with a single TextMeshProUGUI component — one is spawned per level entry that has no Coins Text, Kills Text, or Row Text assigned.")]
    [SerializeField] private TMP_Text rowTextPrefab;
    [Tooltip("Parent transform the row prefabs are spawned under (e.g. one with a Vertical Layout Group).")]
    [SerializeField] private Transform rowsParent;
    [Tooltip("Optional: shows the running coin total across all levels.")]
    [SerializeField] private TMP_Text totalCoinsText;
    [Tooltip("Optional: shows the running kill total across all levels.")]
    [SerializeField] private TMP_Text totalKillsText;

    private void Start()
    {
        Refresh();
    }

    /// <summary>Rebuilds the displayed rows from GameManager's current saved data. Call this again if counts can change while the menu is open.</summary>
    public void Refresh()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null)
            Debug.LogWarning("MainMenuCoinDisplay: no GameManager found — has it been created yet (e.g. in the boot/main menu scene)?", this);

        // Clear any previously auto-spawned rows (for levels with nothing
        // manually assigned) before respawning them.
        if (rowsParent != null)
        {
            for (int i = rowsParent.childCount - 1; i >= 0; i--)
                Destroy(rowsParent.GetChild(i).gameObject);
        }

        foreach (LevelEntry level in levels)
        {
            int coinsCollected = gm != null ? gm.GetCoinsForLevel(level.levelId) : 0;
            int enemiesKilled = gm != null ? gm.GetKillsForLevel(level.levelId) : 0;
            string name = string.IsNullOrEmpty(level.displayName) ? level.levelId : level.displayName;

            // Enemy total: GameManager first, this entry's field as fallback.
            int totalEnemies = gm != null ? gm.GetTotalEnemiesInLevel(level.levelId) : 0;
            if (totalEnemies <= 0) totalEnemies = level.totalEnemiesInLevel;
            if (totalEnemies > 0) enemiesKilled = Mathf.Min(enemiesKilled, totalEnemies);

            // Coin total: GameManager first, this entry's field as fallback.
            int totalCoins = gm != null ? gm.GetTotalCoinsInLevel(level.levelId) : 0;
            if (totalCoins <= 0) totalCoins = level.totalCoinsInLevel;
            if (totalCoins > 0) coinsCollected = Mathf.Min(coinsCollected, totalCoins);

            string coinsLabel = totalCoins > 0
                ? $"{coinsCollected} / {totalCoins} coins"
                : $"{coinsCollected} coins";

            string killsLabel = totalEnemies > 0
                ? $"{enemiesKilled} / {totalEnemies} enemies killed"
                : $"{enemiesKilled} enemies killed";

            UpdateMedals(level, gm);

            bool handled = false;

            if (level.nameText != null)
            {
                level.nameText.text = name;
                handled = true;
            }

            if (level.coinsText != null)
            {
                level.coinsText.text = coinsLabel;
                handled = true;
            }

            if (level.killsText != null)
            {
                level.killsText.text = killsLabel;
                handled = true;
            }

            if (level.rowText != null)
            {
                level.rowText.text = $"{name}: {coinsLabel}, {killsLabel}";
                handled = true;
            }

            if (!handled)
            {
                if (rowTextPrefab != null && rowsParent != null)
                {
                    TMP_Text row = Instantiate(rowTextPrefab, rowsParent);
                    row.text = $"{name}: {coinsLabel}, {killsLabel}";
                }
                else
                {
                    Debug.LogWarning($"MainMenuCoinDisplay: level entry '{level.levelId}' has no Coins Text/Kills Text/Row Text assigned, and no Row Text Prefab/Rows Parent set as a fallback — nothing will show for it.", this);
                }
            }
        }

        if (totalCoinsText != null && gm != null)
            totalCoinsText.text = $"Total: {gm.GetTotalCoins()} coins";

        if (totalKillsText != null && gm != null)
            totalKillsText.text = $"Total: {gm.GetTotalKills()} enemies killed";
    }

    private void UpdateMedals(LevelEntry level, GameManager gm)
    {
        string id = level.levelId;
        SetShown(level.coinMedal, gm != null && gm.HasCoinMedal(id));
        SetShown(level.enemyMedal, gm != null && gm.HasEnemyMedal(id));
        SetShown(level.timeMedal, gm != null && gm.HasTimeMedal(id));
        SetShown(level.allMedals, gm != null && gm.HasAllMedals(id));

        if (level.bestTimeText != null)
        {
            float best = gm != null ? gm.GetBestTime(id) : 0f;
            float target = gm != null ? gm.GetTargetTime(id) : 0f;
            string bestLabel = best > 0f ? GameManager.FormatTime(best) : "--:--";
            level.bestTimeText.text = target > 0f
                ? $"Best: {bestLabel} / {GameManager.FormatTime(target)}"
                : $"Best: {bestLabel}";
        }
    }

    private static void SetShown(GameObject obj, bool shown)
    {
        if (obj != null) obj.SetActive(shown);
    }
}
