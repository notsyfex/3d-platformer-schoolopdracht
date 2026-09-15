using UnityEngine;

// Put on a trigger collider (IsTrigger = true) at the race finish.
public class FinishLine : MonoBehaviour
{
    public string playerTag = "Player";

    [Tooltip("Empty GameObject placed just before the start line. The player is teleported here on finish. Leave empty to disable teleporting.")]
    public Transform respawnPoint;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        RaceTimer.Instance.FinishRace();

        if (respawnPoint != null)
            Teleport(other);
    }

    void Teleport(Collider player)
    {
        Rigidbody rb = player.attachedRigidbody;
        CharacterController cc = player.GetComponent<CharacterController>();

        if (rb != null)
        {
            // Move the Rigidbody itself, not just transform.position — otherwise
            // physics still thinks it's at the old spot and snaps it right back
            // on the next physics step.
            // Unity 6+ renamed velocity to rb.linearVelocity — rename here if you get a compile error.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = respawnPoint.position;
            rb.rotation = respawnPoint.rotation;
        }
        else if (cc != null)
        {
            // CharacterController also caches its own position and can override
            // a direct transform set — disable it for one frame to force it through.
            cc.enabled = false;
            player.transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
            cc.enabled = true;
        }
        else
        {
            player.transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
        }
    }
}
