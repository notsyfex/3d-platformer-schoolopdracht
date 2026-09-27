using UnityEngine;

/// <summary>
/// Minimal CharacterController mover showing how to detect landing on a
/// BouncingPlatform and launch the player upward. This is a REFERENCE —
/// if you already have your own player movement script, don't add this
/// whole file alongside it (two scripts both calling CharacterController.Move
/// on the same object will fight each other and cause jittery movement).
/// Instead, copy just the two pieces marked below into your existing script:
///   1. the OnControllerColliderHit method
///   2. the verticalVelocity field it depends on (you likely already have
///      an equivalent field for gravity/jumping — reuse that one instead)
///
/// WHY THIS IS NEEDED:
/// CharacterController doesn't use Unity's physics engine for collisions,
/// so it never fires OnCollisionEnter — which is what BouncingPlatform.cs
/// normally listens for. Instead, CharacterController fires
/// OnControllerColliderHit every frame it's touching something, and it's
/// the PLAYER's job to check what it hit and react — the platform can't
/// reach out and push a CharacterController on its own.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerBounceExample : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 2f;

    private CharacterController controller;

    // --- Piece 2: reuse your existing vertical velocity field for this ---
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        bool isGrounded = controller.isGrounded;

        if (isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f; // small downward value keeps the controller grounded

        if (isGrounded && Input.GetButtonDown("Jump"))
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 horizontal = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        Vector3 move = horizontal * moveSpeed + Vector3.up * verticalVelocity;

        controller.Move(move * Time.deltaTime);
    }

    // --- Piece 1: the actual bounce detection — copy this into your script ---
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Only care about hits coming from roughly below (landing on top of something).
        if (hit.normal.y < 0.5f) return;

        BouncingPlatform pad = hit.collider.GetComponent<BouncingPlatform>();
        if (pad == null) return;

        // Only bounce if we were actually falling onto it.
        if (verticalVelocity >= 0f) return;

        verticalVelocity = pad.GetBounceVelocity(-verticalVelocity);
    }
}