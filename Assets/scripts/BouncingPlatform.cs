using UnityEngine;
using UnityEngine.Events;
using System.Collections;

/// <summary>
/// A bounce pad / trampoline platform: launches whatever lands on it upward,
/// with an optional squash-and-stretch "juice" animation and a UnityEvent
/// hook for sound/particles.
///
/// SETUP:
/// 1. Attach this script to your platform GameObject.
/// 2. Make sure the platform has a (non-trigger) Collider.
/// 3. The object being bounced needs a Rigidbody (this script works via
///    physics collisions — see the note at the bottom for CharacterController
///    -based players, which don't have a Rigidbody).
/// 4. Optionally wire onBounce in the Inspector to play a sound or spawn a
///    particle effect.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BouncingPlatform : MonoBehaviour
{
    [Header("Bounce Settings")]
    [Tooltip("Every bounce launches to exactly this height, in units (default 10). Turn on Scale With Impact Velocity below if you want harder landings to bounce higher instead.")]
    [SerializeField] private float maxBounceHeight = 10f;
    [Tooltip("Gravity used to convert Max Bounce Height into a velocity — should match your player's gravity value (e.g. -20).")]
    [SerializeField] private float gravity = -20f;
    [Tooltip("If true, harder landings bounce higher (capped at Max Bounce Height); soft landings still get at least Min Bounce Force. If false, every bounce is a fixed Max Bounce Height.")]
    [SerializeField] private bool scaleWithImpactVelocity = false;
    [Tooltip("Multiplier applied to incoming downward speed when scaling with impact.")]
    [SerializeField] private float impactMultiplier = 1.5f;
    [Tooltip("Minimum bounce force, even for a very soft landing (only used when Scale With Impact Velocity is on).")]
    [SerializeField] private float minBounceForce = 8f;
    [Tooltip("Minimum seconds between bounces on this platform — a landing during the cooldown is simply ignored.")]
    [SerializeField] private float bounceCooldown = 5f;
    [Tooltip("Only objects on these layers will be bounced.")]
    [SerializeField] private LayerMask bounceableLayers = ~0;

    [Header("Squash & Stretch (juice)")]
    [SerializeField] private bool useSquashStretch = true;
    [SerializeField] private float squashAmount = 0.25f;
    [SerializeField] private float squashDuration = 0.15f;

    [Header("Events")]
    [Tooltip("Fired every time something bounces off this platform — hook up a sound effect or particle burst here.")]
    public UnityEvent onBounce;

    private Vector3 originalScale;
    private Coroutine squashRoutine;
    private float lastBounceTime = -Mathf.Infinity;

    private float MaxBounceVelocity => Mathf.Sqrt(Mathf.Max(0f, maxBounceHeight) * -2f * gravity);
    private bool IsOnCooldown => Time.time - lastBounceTime < bounceCooldown;

    private void Awake()
    {
        originalScale = transform.localScale;
        GetComponent<Collider>().isTrigger = false; // must be a solid collider for OnCollisionEnter
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsInLayerMask(collision.gameObject.layer, bounceableLayers)) return;

        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null) return; // no Rigidbody on the other object — nothing to bounce

        // Only trigger when landing on TOP of the platform, not hitting its side.
        Vector3 contactNormal = collision.GetContact(0).normal;
        if (Vector3.Dot(contactNormal, Vector3.up) < 0.5f) return;

        if (IsOnCooldown) return;

        float impactSpeed = Mathf.Max(0f, -otherRb.linearVelocity.y);
        float force = scaleWithImpactVelocity
            ? Mathf.Max(minBounceForce, impactSpeed * impactMultiplier)
            : MaxBounceVelocity;
        force = Mathf.Min(force, MaxBounceVelocity);

        // Cancel existing downward velocity, then launch upward.
        Vector3 velocity = otherRb.linearVelocity;
        velocity.y = 0f;
        otherRb.linearVelocity = velocity;
        otherRb.AddForce(Vector3.up * force, ForceMode.VelocityChange);

        lastBounceTime = Time.time;
        onBounce?.Invoke();

        if (useSquashStretch)
        {
            if (squashRoutine != null) StopCoroutine(squashRoutine);
            squashRoutine = StartCoroutine(SquashAndStretch());
        }
    }

    private IEnumerator SquashAndStretch()
    {
        Vector3 squashed = new Vector3(
            originalScale.x * (1f + squashAmount),
            originalScale.y * (1f - squashAmount),
            originalScale.z * (1f + squashAmount));

        float half = squashDuration * 0.5f;

        float t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(originalScale, squashed, t / half);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(squashed, originalScale, t / half);
            yield return null;
        }

        transform.localScale = originalScale;
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    // ---------------------------------------------------------------------
    // For CharacterController-based players: CharacterController doesn't use
    // physics collisions or have a Rigidbody, so OnCollisionEnter above never
    // fires for it. Your player's own movement script needs to call this
    // method itself — see the OnControllerColliderHit example in
    // PlayerBounceExample.cs.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Calculates the upward launch speed for a CharacterController-based
    /// player, given how fast they were falling when they hit the platform
    /// (pass a positive number — e.g. -verticalVelocity if your falling
    /// speed is stored as negative).
    /// Returns 0 if the platform is still on cooldown — callers should treat
    /// that as "no bounce happened" and leave their own velocity untouched.
    /// </summary>
    public float GetBounceVelocity(float impactSpeed)
    {
        if (IsOnCooldown) return 0f;

        float force = scaleWithImpactVelocity
            ? Mathf.Max(minBounceForce, Mathf.Max(0f, impactSpeed) * impactMultiplier)
            : MaxBounceVelocity;
        force = Mathf.Min(force, MaxBounceVelocity);

        lastBounceTime = Time.time;
        onBounce?.Invoke();

        if (useSquashStretch)
        {
            if (squashRoutine != null) StopCoroutine(squashRoutine);
            squashRoutine = StartCoroutine(SquashAndStretch());
        }

        return force;
    }
}