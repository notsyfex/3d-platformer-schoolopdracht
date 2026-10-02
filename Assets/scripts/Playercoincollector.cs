using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach to the player GameObject (tagged "Player"). Reports collected
/// coins to GameManager, which persists the count per level across scene
/// changes (and across game sessions, since it saves to disk).
///
/// SETUP:
/// 1. Attach to your player GameObject.
/// 2. Make sure a GameManager exists in the scene (see GameManager.cs setup
///    notes) — it only needs to be placed once, in your first/boot scene.
/// 3. (Optional but recommended) Add a LevelId component to each level scene
///    so coins are labeled by a custom ID instead of the raw scene name —
///    see LevelId.cs.
/// 4. Wire onLevelCoinsChanged / onTotalCoinsChanged in the Inspector to a
///    UI Text/TMP_Text update method for a live coin counter.
/// 5. Coin.cs (the pickup script) calls CollectCoin() automatically when the
///    player walks over a coin.
/// </summary>
public class PlayerCoinCollector : MonoBehaviour
{
    [Header("Events")]
    [Tooltip("Fired with the current level's coin count whenever it changes.")]
    public UnityEvent<int> onLevelCoinsChanged;
    [Tooltip("Fired with the running total across all levels whenever it changes.")]
    public UnityEvent<int> onTotalCoinsChanged;

    public int CoinsThisLevel => GameManager.Instance != null
        ? GameManager.Instance.GetCoinsForLevel(LevelId.CurrentLevelId)
        : 0;

    public int TotalCoins => GameManager.Instance != null ? GameManager.Instance.GetTotalCoins() : 0;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        RefreshEvents();
    }

    /// <summary>Call this when the player picks up a coin (Coin.cs does this automatically).</summary>
    public void CollectCoin(int amount = 1)
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("PlayerCoinCollector: no GameManager in the scene — coin not saved.", this);
            return;
        }

        GameManager.Instance.AddCoins(LevelId.CurrentLevelId, amount);
        RefreshEvents();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshEvents();
    }

    private void RefreshEvents()
    {
        onLevelCoinsChanged?.Invoke(CoinsThisLevel);
        onTotalCoinsChanged?.Invoke(TotalCoins);
    }
}
