using UnityEngine;

/// <summary>
/// Put on a trigger collider (IsTrigger = true) at the race start. When the
/// player crosses it, kicks off GameManager's race countdown.
///
/// SETUP:
/// 1. Make sure GameManager exists (start the game from the boot scene).
///    If it doesn't, this logs a warning instead of throwing an error.
/// 2. Attach this script to the start-line trigger GameObject.
/// 3. Make sure the player GameObject is tagged to match Player Tag below
///    (defaults to "Player", same convention as Coin.cs / EnemyAI.cs).
/// </summary>
public class StartLine : MonoBehaviour
{
    public string playerTag = "Player";

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (GameManager.Instance == null)
        {
            Debug.LogWarning($"{name}: no GameManager found — countdown not started. Start the game from the boot scene.", this);
            return;
        }

        GameManager.Instance.BeginCountdown(other);
    }
}