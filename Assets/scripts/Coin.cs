using UnityEngine;

/// <summary>
/// Attach to a coin pickup GameObject. Requires a Collider set to "Is Trigger".
///
/// SETUP:
/// 1. Attach to your coin prefab.
/// 2. Add a Collider (SphereCollider works well) and check "Is Trigger".
/// 3. Make sure your player GameObject is tagged "Player" (default Unity tag)
///    and has the PlayerCoinCollector script on it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Coin : MonoBehaviour
{
    [Tooltip("How many coins this pickup is worth.")]
    [SerializeField] private int value = 1;

    [Tooltip("Optional VFX prefab spawned at the coin's position when collected (e.g. a sparkle burst).")]
    [SerializeField] private GameObject collectEffectPrefab;

    [Tooltip("Fallback sound, only used if there is no SoundManager in the scene (otherwise the SoundManager's \"Coin\" sound plays).")]
    [SerializeField] private AudioClip collectSound;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerCoinCollector collector = other.GetComponent<PlayerCoinCollector>();
        if (collector == null) collector = other.GetComponentInParent<PlayerCoinCollector>();
        if (collector == null)
        {
            Debug.LogWarning($"{name}: player has no PlayerCoinCollector component.", this);
            return;
        }

        collector.CollectCoin(value);

        if (collectEffectPrefab != null)
            Instantiate(collectEffectPrefab, transform.position, Quaternion.identity);

        // Prefer the SoundManager's "Coin" sound; fall back to this coin's own clip.
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySfx("Coin");
        else if (collectSound != null)
            AudioSource.PlayClipAtPoint(collectSound, transform.position);

        Destroy(gameObject);
    }
}