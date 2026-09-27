using UnityEngine;

// Put on a trigger collider (IsTrigger = true) along the track.
// Set `index` to the order checkpoints should be passed in: 0, 1, 2, ...
public class Checkpoint : MonoBehaviour
{
    public string playerTag = "Player";
    public int index;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (GameManager.Instance != null)
            GameManager.Instance.PassCheckpoint(index);
    }
}
