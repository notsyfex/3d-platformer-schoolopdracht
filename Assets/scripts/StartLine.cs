using UnityEngine;

// Put on a trigger collider (IsTrigger = true) at the race start.
public class StartLine : MonoBehaviour
{
    public string playerTag = "Player";

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        RaceTimer.Instance.BeginCountdown(other);
    }
}
