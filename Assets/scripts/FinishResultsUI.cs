using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Canvas popup shown when the player finishes a level: final time, three
/// medals (coins / enemies / time) and Retry + Main Menu buttons. Freezes the
/// player (and optionally the whole game via Time.timeScale) while it's open.
///
/// SETUP: attach to the SAME GameObject as GameManager. The Canvas, buttons
/// and (if the scene has none) an EventSystem are created automatically.
/// Requires the OnLevelCompleted event in GameManager.
/// </summary>
[RequireComponent(typeof(GameManager))]
public class FinishResultsUI : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("Also sets Time.timeScale = 0 while the popup is open (stops enemies, animations, physics). The player is always frozen either way.")]
    [SerializeField] private bool freezeTime = true;
    [Tooltip("Seconds to wait after finishing before the popup appears.")]
    [SerializeField] private float showDelay = 0.6f;
    [Tooltip("Exact name of the main menu scene (must be in File > Build Settings > Scenes In Build).")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Medal colours")]
    [SerializeField] private Color coinColor = new Color(1f, 0.82f, 0.2f);
    [SerializeField] private Color enemyColor = new Color(0.9f, 0.3f, 0.3f);
    [SerializeField] private Color timeColor = new Color(0.3f, 0.7f, 1f);
    [SerializeField] private Color lockedColor = new Color(0.35f, 0.35f, 0.4f);

    private GameManager gm;
    private bool subscribed;

    // Built UI
    private GameObject canvasObject;
    private CanvasGroup rootGroup;
    private RectTransform panel;
    private Text timeText, newBestText;
    private Text tierListText;
    private readonly RectTransform[] medalRoot = new RectTransform[3];
    private readonly Image[] medalOuter = new Image[3];
    private readonly Image[] medalInner = new Image[3];
    private readonly Text[] medalGlyph = new Text[3];
    private readonly Text[] medalName = new Text[3];
    private readonly Text[] medalDetail = new Text[3];
    private readonly bool[] medalEarned = new bool[3];

    private Sprite circleSprite;
    private Texture2D circleTex;
    private Font font;
    private GameObject spawnedEventSystem;
    private Coroutine showRoutine;
    private bool timeFrozenByUs;

    private void Start()
    {
        gm = GetComponent<GameManager>();
        // A duplicate GameManager destroys itself; only the real one listens.
        if (GameManager.Instance != gm) return;

        BuildUI();
        gm.OnLevelCompleted += HandleLevelCompleted;
        SceneManager.sceneLoaded += OnSceneLoaded;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed)
        {
            if (gm != null) gm.OnLevelCompleted -= HandleLevelCompleted;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        ResumeTime();
        if (circleTex != null) Destroy(circleTex);
        if (circleSprite != null) Destroy(circleSprite);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (showRoutine != null) { StopCoroutine(showRoutine); showRoutine = null; }
        if (canvasObject != null) canvasObject.SetActive(false);
        if (spawnedEventSystem != null) { Destroy(spawnedEventSystem); spawnedEventSystem = null; }
        ResumeTime();
    }

    // ===================================================================
    // Showing
    // ===================================================================

    private void HandleLevelCompleted(string levelId, bool newBest)
    {
        Debug.Log($"FinishResultsUI: levelId='{levelId}', totalCoins={gm.GetTotalCoinsInLevel(levelId)}, totalEnemies={gm.GetTotalEnemiesInLevel(levelId)}, slowestTimeTier={gm.GetTargetTime(levelId)}, Level Totals ids: {gm.DescribeLevelTotals()}");

        FreezePlayer();

        timeText.text = GameManager.FormatTime(gm.LastCompletionTime);
        newBestText.gameObject.SetActive(newBest);

        int coinTotal = gm.GetTotalCoinsInLevel(levelId);
        int enemyTotal = gm.GetTotalEnemiesInLevel(levelId);
        float target = gm.GetTargetTime(levelId);

        SetMedal(0, "COINS", "C", coinColor, coinTotal > 0, gm.HasCoinMedal(levelId),
            coinTotal > 0 ? $"{gm.GetCoinsForLevel(levelId)} / {coinTotal}" : $"{gm.GetCoinsForLevel(levelId)} collected");
        SetMedal(1, "ENEMIES", "E", enemyColor, enemyTotal > 0, gm.HasEnemyMedal(levelId),
            enemyTotal > 0 ? $"{gm.GetKillsForLevel(levelId)} / {enemyTotal}" : $"{gm.GetKillsForLevel(levelId)} killed");

        // Time medal: shows the tier (Bronze / Silver / Gold / Platinum) the saved best time earned.
        GameManager.TimeMedal timeMedal = gm.GetTimeMedal(levelId);
        bool timeEarned = timeMedal != GameManager.TimeMedal.None;
        SetMedal(2,
            timeEarned ? timeMedal.ToString().ToUpper() : "TIME",
            timeEarned ? timeMedal.ToString().Substring(0, 1) : "T",
            timeEarned ? GameManager.GetMedalColor(timeMedal) : timeColor,
            target > 0f, timeEarned,
            NextTierDetail(levelId, timeMedal));
        tierListText.text = gm.BuildTierLine(levelId);

        EnsureEventSystem();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (showRoutine != null) StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(ShowRoutine());
    }

    // Short text under the time medal: the next tier to aim for (or the first goal if none earned yet).
    private string NextTierDetail(string levelId, GameManager.TimeMedal current)
    {
        for (int i = (int)current + 1; i <= (int)GameManager.TimeMedal.Platinum; i++)
        {
            GameManager.TimeMedal tier = (GameManager.TimeMedal)i;
            float needed = gm.GetTierTime(levelId, tier);
            if (needed > 0f)
                return (current == GameManager.TimeMedal.None ? "Goal: " : "Next: ") + $"{tier} {GameManager.FormatTime(needed)}";
        }
        return current == GameManager.TimeMedal.None ? "-" : "Top tier!";
    }

    private void SetMedal(int i, string label, string glyph, Color color, bool available, bool earned, string detail)
    {
        medalEarned[i] = earned;
        medalName[i].text = label;
        medalDetail[i].text = detail;
        medalGlyph[i].text = glyph;

        Color baseCol = earned ? color : lockedColor;
        float alpha = earned ? 1f : (available ? 0.8f : 0.35f);
        medalOuter[i].color = new Color(baseCol.r * 0.6f, baseCol.g * 0.6f, baseCol.b * 0.6f, alpha);
        medalInner[i].color = new Color(baseCol.r, baseCol.g, baseCol.b, alpha);
        medalGlyph[i].color = new Color(1f, 1f, 1f, earned ? 1f : 0.6f);

        float textAlpha = earned ? 1f : 0.6f;
        medalName[i].color = new Color(1f, 1f, 1f, textAlpha);
        medalDetail[i].color = new Color(0.8f, 0.8f, 0.85f, textAlpha);
    }

    private IEnumerator ShowRoutine()
    {
        canvasObject.SetActive(false);
        yield return new WaitForSecondsRealtime(showDelay);

        canvasObject.SetActive(true);
        rootGroup.alpha = 0f;
        panel.localScale = Vector3.zero;
        for (int i = 0; i < 3; i++) medalRoot[i].localScale = medalEarned[i] ? Vector3.zero : Vector3.one;

        float total = 0.5f + 3 * 0.35f + 0.5f;
        float t = 0f;
        while (t < total)
        {
            t += Time.unscaledDeltaTime;
            rootGroup.alpha = Mathf.Clamp01(t / 0.25f);
            panel.localScale = Vector3.one * Mathf.Max(0.001f, EaseOutBack(Mathf.Clamp01(t / 0.35f)));

            for (int i = 0; i < 3; i++)
            {
                if (!medalEarned[i]) continue;
                float local = (t - (0.5f + i * 0.35f)) / 0.4f;
                float s = local <= 0f ? 0f : EaseOutBack(Mathf.Clamp01(local));
                medalRoot[i].localScale = Vector3.one * s;
            }
            yield return null;
        }

        rootGroup.alpha = 1f;
        panel.localScale = Vector3.one;
        for (int i = 0; i < 3; i++) medalRoot[i].localScale = Vector3.one;
        showRoutine = null;
    }

    // ===================================================================
    // Freezing
    // ===================================================================

    private void FreezePlayer()
    {
        Movement movement = FindAnyObjectByType<Movement>();
        if (movement != null)
        {
            // Disable the movement script so nothing drives the player any more.
            movement.enabled = false;

            Rigidbody rb = movement.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        if (freezeTime)
        {
            Time.timeScale = 0f;
            timeFrozenByUs = true;
        }
    }

    private void ResumeTime()
    {
        if (!timeFrozenByUs) return;
        Time.timeScale = 1f;
        timeFrozenByUs = false;
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;

        spawnedEventSystem = new GameObject("EventSystem (Results UI)", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        spawnedEventSystem.AddComponent<InputSystemUIInputModule>();
#else
        spawnedEventSystem.AddComponent<StandaloneInputModule>();
#endif
    }

    // ===================================================================
    // Buttons
    // ===================================================================

    private void Retry()
    {
        ResumeTime();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void GoToMainMenu()
    {
        ResumeTime();
        if (!string.IsNullOrEmpty(mainMenuSceneName) && Application.CanStreamedLevelBeLoaded(mainMenuSceneName))
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }
        else
        {
            Debug.LogWarning($"FinishResultsUI: scene '{mainMenuSceneName}' not found in Build Settings, loading build index 0 instead.");
            SceneManager.LoadScene(0);
        }
    }

    // ===================================================================
    // UI construction
    // ===================================================================

    private void BuildUI()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        circleTex = MakeCircleTexture(128);
        circleSprite = Sprite.Create(circleTex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f);

        // Canvas
        canvasObject = new GameObject("Finish Results Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        rootGroup = canvasObject.AddComponent<CanvasGroup>();

        // Dim background (full screen)
        Image dim = MakeImage("Dim", canvasObject.transform, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.65f));
        RectTransform dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

        // Panel with border
        Image border = MakeImage("Panel", canvasObject.transform, new Vector2(908f, 648f), Vector2.zero, new Color(1f, 1f, 1f, 0.9f));
        panel = border.rectTransform;
        MakeImage("Body", panel, new Vector2(900f, 640f), Vector2.zero, new Color(0.10f, 0.11f, 0.15f, 1f));

        MakeText("Title", panel, "LEVEL COMPLETE!", new Vector2(860f, 80f), new Vector2(0f, 245f), 68, Color.white, FontStyle.Bold);
        timeText = MakeText("Time", panel, "0:00.00", new Vector2(860f, 70f), new Vector2(0f, 165f), 58, new Color(0.85f, 0.9f, 1f), FontStyle.Normal);
        newBestText = MakeText("NewBest", panel, "NEW BEST TIME!", new Vector2(860f, 50f), new Vector2(0f, 112f), 34, Color.yellow, FontStyle.Bold);

        float[] xs = { -280f, 0f, 280f };
        for (int i = 0; i < 3; i++)
        {
            medalRoot[i] = MakeRect("Medal" + i, panel, new Vector2(140f, 140f), new Vector2(xs[i], 0f));
            medalOuter[i] = MakeImage("Outer", medalRoot[i], new Vector2(130f, 130f), Vector2.zero, Color.white, circleSprite);
            medalInner[i] = MakeImage("Inner", medalRoot[i], new Vector2(106f, 106f), Vector2.zero, Color.white, circleSprite);
            medalGlyph[i] = MakeText("Glyph", medalRoot[i], "", new Vector2(130f, 130f), Vector2.zero, 64, Color.white, FontStyle.Bold);

            medalName[i] = MakeText("Name" + i, panel, "", new Vector2(270f, 40f), new Vector2(xs[i], -105f), 34, Color.white, FontStyle.Bold);
            medalDetail[i] = MakeText("Detail" + i, panel, "", new Vector2(270f, 36f), new Vector2(xs[i], -143f), 30, Color.white, FontStyle.Normal);
        }

        // One line listing every medal tier time that's set up for this level.
        tierListText = MakeText("TierList", panel, "", new Vector2(860f, 34f), new Vector2(0f, -181f), 26, Color.white, FontStyle.Normal);

        MakeButton("Retry", panel, "RETRY", new Vector2(-170f, -245f), new Color(0.2f, 0.55f, 0.95f), Retry);
        MakeButton("MainMenu", panel, "MAIN MENU", new Vector2(170f, -245f), new Color(0.75f, 0.25f, 0.25f), GoToMainMenu);

        canvasObject.SetActive(false);
    }

    private RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 pos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    private Image MakeImage(string name, Transform parent, Vector2 size, Vector2 pos, Color color, Sprite sprite = null)
    {
        RectTransform rt = MakeRect(name, parent, size, pos);
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text MakeText(string name, Transform parent, string content, Vector2 size, Vector2 pos, int fontSize, Color color, FontStyle style)
    {
        RectTransform rt = MakeRect(name, parent, size, pos);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private void MakeButton(string name, Transform parent, string label, Vector2 pos, Color color, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rt = MakeRect(name, parent, new Vector2(300f, 90f), pos);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        ColorBlock cb = button.colors;
        cb.normalColor = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
        cb.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        cb.selectedColor = color;
        button.colors = cb;
        button.onClick.AddListener(onClick);

        MakeText("Label", rt, label, new Vector2(300f, 90f), Vector2.zero, 40, Color.white, FontStyle.Bold);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private static Texture2D MakeCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
            }
        }
        tex.Apply();
        return tex;
    }
}