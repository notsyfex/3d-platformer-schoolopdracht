
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MainMenuCoinDisplay : MonoBehaviour
{
    [System.Serializable]
    public class LevelEntry
    {
        [Header("Level")]
        [Tooltip("Must match the Level Id used by this level.")]
        public string levelId;

        [Tooltip("Name shown in the UI. Leave empty to use Level ID.")]
        public string displayName;

        [Header("ONE Text Field")]
        [Tooltip("One TMP text containing the entire level's statistics.")]
        public TMP_Text levelText;

        [Header("Fallback Totals")]
        public int totalCoinsInLevel = 0;
        public int totalEnemiesInLevel = 0;

        [Header("Medal UI")]
        [Tooltip("Optional Image that will display Bronze/Silver/Gold/Platinum.")]
        public Image timeMedalImage;

        [Tooltip("Bronze medal sprite.")]
        public Sprite bronzeMedal;

        [Tooltip("Silver medal sprite.")]
        public Sprite silverMedal;

        [Tooltip("Gold medal sprite.")]
        public Sprite goldMedal;

        [Tooltip("Platinum medal sprite.")]
        public Sprite platinumMedal;

        [Tooltip("Optional GameObject shown when all medals are earned.")]
        public GameObject allMedalsObject;
    }

    [Header("Levels")]
    [SerializeField]
    private List<LevelEntry> levels = new List<LevelEntry>();

    [Header("Optional Overall Totals")]
    [SerializeField]
    private TMP_Text totalCoinsText;

    [SerializeField]
    private TMP_Text totalKillsText;


    private void Start()
    {
        Refresh();
    }


    public void Refresh()
    {
        GameManager gm = GameManager.Instance;

        if (gm == null)
        {
            Debug.LogWarning(
                "MainMenuCoinDisplay: No GameManager found.",
                this
            );

            return;
        }

        foreach (LevelEntry level in levels)
        {
            UpdateLevel(level, gm);
        }

        // Overall totals
        if (totalCoinsText != null)
        {
            totalCoinsText.text =
                $"Total Coins: {gm.GetTotalCoins()}";
        }

        if (totalKillsText != null)
        {
            totalKillsText.text =
                $"Total Enemies: {gm.GetTotalKills()}";
        }
    }


    private void UpdateLevel(
        LevelEntry level,
        GameManager gm)
    {
        string id = level.levelId;

        // -----------------------------------------
        // LEVEL NAME
        // -----------------------------------------

        string levelName =
            string.IsNullOrEmpty(level.displayName)
                ? id
                : level.displayName;


        // -----------------------------------------
        // COINS
        // -----------------------------------------

        int coinsCollected =
            gm.GetCoinsForLevel(id);

        int totalCoins =
            gm.GetTotalCoinsInLevel(id);

        if (totalCoins <= 0)
            totalCoins = level.totalCoinsInLevel;

        if (totalCoins > 0)
        {
            coinsCollected =
                Mathf.Min(
                    coinsCollected,
                    totalCoins
                );
        }


        // -----------------------------------------
        // ENEMIES
        // -----------------------------------------

        int enemiesKilled =
            gm.GetKillsForLevel(id);

        int totalEnemies =
            gm.GetTotalEnemiesInLevel(id);

        if (totalEnemies <= 0)
            totalEnemies = level.totalEnemiesInLevel;

        if (totalEnemies > 0)
        {
            enemiesKilled =
                Mathf.Min(
                    enemiesKilled,
                    totalEnemies
                );
        }


        // -----------------------------------------
        // BEST TIME
        // -----------------------------------------

        string timeText =
            BuildTimeLabel(
                gm,
                id
            );


        // -----------------------------------------
        // ONE TEXT FIELD
        // -----------------------------------------

        if (level.levelText != null)
        {
            level.levelText.text =
                $"{levelName}\n" +
                $"Coins: {coinsCollected} / {totalCoins}\n" +
                $"Enemies: {enemiesKilled} / {totalEnemies}\n" +
                $"{timeText}";
        }


        // -----------------------------------------
        // TIME MEDAL
        // -----------------------------------------

        UpdateTimeMedal(
            level,
            gm,
            id
        );


        // -----------------------------------------
        // ALL MEDALS
        // -----------------------------------------

        if (level.allMedalsObject != null)
        {
            bool allMedals =
                gm.HasAllMedals(id);

            level.allMedalsObject.SetActive(
                allMedals
            );
        }
    }


    private void UpdateTimeMedal(
        LevelEntry level,
        GameManager gm,
        string levelId)
    {
        if (level.timeMedalImage == null)
            return;


        GameManager.TimeMedal medal =
            gm.GetTimeMedal(levelId);


        Sprite sprite = null;


        switch (medal)
        {
            case GameManager.TimeMedal.Bronze:

                sprite = level.bronzeMedal;
                break;


            case GameManager.TimeMedal.Silver:

                sprite = level.silverMedal;
                break;


            case GameManager.TimeMedal.Gold:

                sprite = level.goldMedal;
                break;


            case GameManager.TimeMedal.Platinum:

                sprite = level.platinumMedal;
                break;


            case GameManager.TimeMedal.None:

                sprite = null;
                break;
        }


        // Show medal if one exists
        if (sprite != null)
        {
            level.timeMedalImage.sprite = sprite;
            level.timeMedalImage.enabled = true;
        }
        else
        {
            level.timeMedalImage.sprite = null;
            level.timeMedalImage.enabled = false;
        }
    }


    private static string BuildTimeLabel(
        GameManager gm,
        string levelId)
    {
        float bestTime =
            gm.GetBestTime(levelId);


        if (bestTime <= 0f)
        {
            return "Best Time: --:--";
        }


        string result =
            $"Best Time: {GameManager.FormatTime(bestTime)}";


        GameManager.TimeMedal medal =
            gm.GetTimeMedal(levelId);


        if (medal ==
            GameManager.TimeMedal.None)
        {
            return result;
        }


        // Get the medal colour from GameManager
        string hex =
            ColorUtility.ToHtmlStringRGB(
                GameManager.GetMedalColor(
                    medal
                )
            );


        return
            $"{result} " +
            $"<color=#{hex}>{medal}</color>";
    }
}