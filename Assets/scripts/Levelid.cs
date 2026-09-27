using UnityEngine;

/// <summary>
/// Put this on an empty GameObject in each level scene (e.g. name it
/// "LevelInfo") to give that level a custom label/ID to save coins under —
/// independent of the scene's actual name. This lets you rename scenes
/// freely, reuse one scene for multiple "levels", or just use friendlier
/// IDs like "Level1" / "ForestLevel" instead of raw scene names.
///
/// SETUP:
/// 1. Create an empty GameObject in the level scene.
/// 2. Attach this script and set Level Id to something unique, e.g. "Level1".
/// 3. (Optional) Set Display Name and Total Coins In Level for UI use later
///    (e.g. showing "Level 1: 7/10 coins" in the main menu).
///
/// If a scene has no LevelId component, GameManager/PlayerCoinCollector
/// fall back to using the raw scene name as the label — so this is optional,
/// but recommended once you care about consistent labels across scenes.
/// </summary>
public class LevelId : MonoBehaviour
{
    [Tooltip("Unique label coins collected in this level are saved under. Must be unique across all levels.")]
    public string levelId = "Level1";

    [Tooltip("Friendly name to show in UI (main menu, level select, etc). Defaults to Level Id if left blank.")]
    public string displayName = "";

    [Tooltip("Optional: total coins that exist in this level, for showing progress like \"7/10\". Leave 0 if you don't want to track this.")]
    public int totalCoinsInLevel = 0;

    [Tooltip("Optional: total enemies that exist in this level, for showing progress like \"3/5 enemies killed\". Leave 0 if you don't want to track this.")]
    public int totalEnemiesInLevel = 0;

    /// <summary>The LevelId active in the currently loaded scene, if any.</summary>
    public static LevelId Current { get; private set; }

    /// <summary>The label coins/kills should currently be saved under — this scene's LevelId if one exists, otherwise the raw scene name. Shared by PlayerCoinCollector and EnemyAI so both save under the same key.</summary>
    public static string CurrentLevelId => Current != null
        ? Current.levelId
        : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

    public string ResolvedDisplayName => string.IsNullOrEmpty(displayName) ? levelId : displayName;

    private void Awake()
    {
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}