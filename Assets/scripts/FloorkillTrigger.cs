 using UnityEngine;

/// <summary>
/// Kills the player the instant they enter this trigger volume — typically
/// placed as an invisible "kill floor" well below the lowest legitimate
/// platform in a level, so falling off an edge or into a pit respawns the
/// player instead of letting them fall forever.
///
/// Reuses Movement's existing death/respawn sequence (screen fade, teleport
/// to spawn, brief invulnerability) via its public Kill() method — this
/// script only detects the fall, it doesn't implement death itself. Works
/// identically to how EnemyAI.Stomp() is triggered externally by the player.
///
/// SETUP:
/// 1. Create an empty GameObject (e.g. "FloorKillZone") positioned well
///    below the lowest point a player should ever legitimately reach —
///    give it plenty of margin so a bounce or long jump can't skim it.
/// 2. Add a BoxCollider (or a few, for an irregular level footprint) sized
///    to span the entire play area horizontally, thin on Y, and check
///    "Is Trigger" in the Inspector.
/// 3. Attach this script to the same GameObject as the trigger collider.
/// 4. Make sure the player GameObject is tagged "Player" and has a
///    Movement component (same convention as Coin.cs / EnemyAI.cs).
///
/// No Inspector fields to configure — this is intentionally a dumb
/// "touch it, die" volume. If you want a delay, warning shake, or a
/// different effect than the normal death sequence, that belongs in
/// Movement.cs's Die()/Kill(), not here.
/// </summary>
public class FloorKillTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        Movement movement = other.GetComponent<Movement>();
        if (movement == null) movement = other.GetComponentInParent<Movement>();
        if (movement == null)
        {
            Debug.LogWarning($"{name}: something tagged 'Player' entered the kill zone but has no Movement component — nothing killed.", this);
            return;
        }

        movement.KillBy($"entered kill zone '{name}' (FloorKillTrigger)", this);
    }
}