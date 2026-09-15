using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Attach to a manager GameObject. One instance per scene.
public class RaceTimer : MonoBehaviour
{
    public static RaceTimer Instance { get; private set; }

    [Header("UI (optional)")]
    public Text timeText;
    public Text bestTimeText;
    public Text messageText;

    [Header("Settings")]
    public string bestTimeKey = "BestTime_Track1";

    [Header("Built-in on-screen display")]
    [Tooltip("Draws the timer directly on screen with OnGUI, no Canvas/Text setup needed. Turn off if you're using the UI fields above instead.")]
    public bool useBuiltInDisplay = true;
    public int fontSize = 40;

    [Header("Countdown")]
    public float countdownStepSeconds = 1f;

    [Tooltip("Optional: your player car's Rigidbody. If set, it's made kinematic (frozen) during the countdown so the car can't move, then released on GO.")]
    public Rigidbody playerRigidbody;

    [Tooltip("Optional: your car's movement/input script. If set, it's disabled during the countdown and re-enabled on GO.")]
    public Behaviour playerControlScript;

    public bool IsRunning { get; private set; }
    public bool IsCountingDown { get; private set; }
    public float ElapsedTime { get; private set; }
    public float BestTime => bestTime;
    public string CountdownText => countdownText;
    public int TotalCheckpoints { get; private set; }
    public int NextCheckpointIndex { get; private set; }

    // Fired the instant a run beats the current best, before this frame ends.
    // GhostRecorder listens to this to know which recording to save.
    public event System.Action OnNewBestTime;

    float bestTime;
    string lastMessage = "";
    string countdownText = "";
    string checkpointText = "";
    bool rigidbodyWasKinematic;
    Coroutine checkpointTextRoutine;

    void Awake()
    {
        Instance = this;
        bestTime = PlayerPrefs.GetFloat(bestTimeKey, -1f);
        TotalCheckpoints = FindObjectsOfType<Checkpoint>().Length;
        UpdateBestTimeUI();
        if (messageText) messageText.text = "";
    }

    void OnGUI()
    {
        if (!useBuiltInDisplay) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            alignment = TextAnchor.UpperLeft
        };
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(20, 20, 400, 60), FormatTime(ElapsedTime), style);

        style.fontSize = fontSize / 2;
        GUI.Label(new Rect(20, 20 + fontSize, 400, 40),
            bestTime >= 0f ? $"Best: {FormatTime(bestTime)}" : "Best: --:--.---", style);

        if (!string.IsNullOrEmpty(lastMessage))
            GUI.Label(new Rect(20, 20 + fontSize + 40, 400, 40), lastMessage, style);

        if (!string.IsNullOrEmpty(checkpointText))
            GUI.Label(new Rect(20, 20 + fontSize + 80, 400, 40), checkpointText, style);

        if (!string.IsNullOrEmpty(countdownText))
        {
            GUIStyle countdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize * 3,
                alignment = TextAnchor.MiddleCenter
            };
            countdownStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(0, Screen.height / 2f - 100, Screen.width, 200), countdownText, countdownStyle);
        }
    }

    void Update()
    {
        if (IsRunning)
        {
            ElapsedTime += Time.deltaTime;
            UpdateTimeUI();
        }
    }

    // Call this from StartLine instead of StartRace() directly — it plays
    // 3, 2, 1, GO! and only then starts the clock. Pass the player's Collider
    // (the one that entered the start trigger) so the car can be auto-frozen
    // during the countdown with no manual Inspector setup.
    public void BeginCountdown(Collider player = null)
    {
        if (IsRunning || IsCountingDown) return;
        StartCoroutine(CountdownRoutine(player));
    }

    IEnumerator CountdownRoutine(Collider player)
    {
        IsCountingDown = true;
        lastMessage = "";
        if (messageText) messageText.text = "";

        CharacterController cc = player != null ? player.GetComponent<CharacterController>() : null;
        Rigidbody rb = player != null ? player.attachedRigidbody : playerRigidbody;
        FreezePlayer(true, cc, rb);

        for (int i = 3; i >= 1; i--)
        {
            countdownText = i.ToString();
            yield return new WaitForSeconds(countdownStepSeconds);
        }

        countdownText = "GO!";
        yield return new WaitForSeconds(countdownStepSeconds * 0.5f);
        countdownText = "";

        FreezePlayer(false, cc, rb);
        IsCountingDown = false;
        StartRace();
    }

    void FreezePlayer(bool freeze, CharacterController cc, Rigidbody rb)
    {
        // Disabling a CharacterController makes its Move()/SimpleMove() calls
        // no-ops, so whatever script drives it can't move it while frozen.
        if (cc != null)
            cc.enabled = !freeze;

        if (rb != null)
        {
            if (freeze)
            {
                rigidbodyWasKinematic = rb.isKinematic;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
            else
            {
                rb.isKinematic = rigidbodyWasKinematic;
            }
        }

        if (playerControlScript != null)
            playerControlScript.enabled = !freeze;
    }

    void StartRace()
    {
        ElapsedTime = 0f;
        IsRunning = true;
        NextCheckpointIndex = 0;
        lastMessage = "";
        checkpointText = "";
        if (messageText) messageText.text = "";
    }

    // Only advances if `index` is the next checkpoint expected — passing one
    // out of order (or twice) is ignored, so you can't skip ahead to fake a split.
    public void PassCheckpoint(int index)
    {
        if (!IsRunning) return;
        if (index != NextCheckpointIndex) return;

        NextCheckpointIndex++;
        checkpointText = $"Checkpoint {NextCheckpointIndex}/{TotalCheckpoints}";

        if (checkpointTextRoutine != null) StopCoroutine(checkpointTextRoutine);
        checkpointTextRoutine = StartCoroutine(ClearCheckpointTextRoutine());
    }

    IEnumerator ClearCheckpointTextRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        checkpointText = "";
    }

    public void FinishRace()
    {
        if (!IsRunning) return;

        if (NextCheckpointIndex < TotalCheckpoints)
        {
            lastMessage = "Missed a checkpoint!";
            if (messageText) messageText.text = lastMessage;
            return;
        }

        IsRunning = false;

        bool isNewBest = bestTime < 0f || ElapsedTime < bestTime;
        if (isNewBest)
        {
            bestTime = ElapsedTime;
            PlayerPrefs.SetFloat(bestTimeKey, bestTime);
            PlayerPrefs.Save();
            OnNewBestTime?.Invoke();
        }

        UpdateTimeUI();
        UpdateBestTimeUI();

        lastMessage = isNewBest ? "NEW BEST TIME!" : "Finish!";
        if (messageText) messageText.text = lastMessage;
    }

    void UpdateTimeUI()
    {
        if (timeText) timeText.text = FormatTime(ElapsedTime);
    }

    void UpdateBestTimeUI()
    {
        if (bestTimeText) bestTimeText.text = bestTime >= 0f ? $"Best: {FormatTime(bestTime)}" : "Best: --:--.---";
    }

    public static string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        int milliseconds = Mathf.FloorToInt((time * 1000f) % 1000f);
        return $"{minutes:00}:{seconds:00}.{milliseconds:000}";
    }
}
