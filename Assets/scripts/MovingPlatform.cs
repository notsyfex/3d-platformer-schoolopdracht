using UnityEngine;

/// <summary>
/// Moves a platform smoothly back and forth between two points (A and B).
///
/// SETUP:
/// 1. Attach this script to your platform GameObject.
/// 2. Create two empty GameObjects in the scene ("PointA" and "PointB") and
///    drag them into the Point A / Point B fields — this lets you position
///    the endpoints visually and see the path drawn in the Scene view.
/// 3. Add a Rigidbody component to the platform (it's set to kinematic and
///    interpolated automatically).
///
/// The player (Movement.cs) notices when it's standing on a MovingPlatform
/// and moves along with it — a CharacterController is NOT carried by a
/// moving Rigidbody on its own. The platform's collider can be on this
/// object or on a child.
///
/// The Ease Curve lets you shape acceleration/deceleration in the Inspector
/// (default is a smooth ease-in/ease-out S-curve). Set it to linear for a
/// constant-speed platform.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MovingPlatform : MonoBehaviour
{
    [Header("Endpoints")]
    [Tooltip("Starting point of the platform's path.")]
    [SerializeField] private Transform pointA;
    [Tooltip("Ending point of the platform's path.")]
    [SerializeField] private Transform pointB;

    [Header("Movement")]
    [Tooltip("Seconds to travel from one point to the other.")]
    [SerializeField] private float travelDuration = 3f;
    [Tooltip("Seconds to pause at each endpoint before reversing.")]
    [SerializeField] private float waitTimeAtPoints = 0.5f;
    [Tooltip("Shapes the motion over one traversal (0 = at start point, 1 = at end point). Leave as a smooth S-curve for ease-in/ease-out, or make it linear for constant speed.")]
    [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("If true, starts the loop already moving toward Point B; otherwise waits at Point A first.")]
    [SerializeField] private bool startMovingImmediately = true;

    private Rigidbody rb;
    private float elapsed;
    private bool movingToB = true;
    private bool waiting;
    private float waitTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; // ensure it never gets pushed by physics itself
        // Smooth the platform's visible position between physics steps, so a
        // player riding it doesn't jitter at high frame rates.
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        if (pointA == null || pointB == null)
        {
            Debug.LogWarning($"{name}: MovingPlatform needs both Point A and Point B assigned.", this);
            enabled = false;
            return;
        }

        // Snap to the correct starting position.
        rb.position = startMovingImmediately ? pointA.position : pointA.position;
        waiting = !startMovingImmediately;
        waitTimer = 0f;
        elapsed = 0f;
    }

    private void FixedUpdate()
    {
        if (pointA == null || pointB == null) return;

        if (waiting)
        {
            waitTimer += Time.fixedDeltaTime;
            if (waitTimer >= waitTimeAtPoints)
            {
                waiting = false;
                waitTimer = 0f;
            }
            return;
        }

        elapsed += Time.fixedDeltaTime;
        float t = travelDuration > 0f ? Mathf.Clamp01(elapsed / travelDuration) : 1f;
        float easedT = easeCurve.Evaluate(t);

        Vector3 from = movingToB ? pointA.position : pointB.position;
        Vector3 to = movingToB ? pointB.position : pointA.position;
        Vector3 newPosition = Vector3.LerpUnclamped(from, to, easedT);

        rb.MovePosition(newPosition);

        if (t >= 1f)
        {
            elapsed = 0f;
            movingToB = !movingToB;
            waiting = waitTimeAtPoints > 0f;
            waitTimer = 0f;
        }
    }

    // Draws the path in the editor so you can see it without hitting Play.
    private void OnDrawGizmos()
    {
        if (pointA == null || pointB == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pointA.position, pointB.position);
        Gizmos.DrawWireSphere(pointA.position, 0.25f);
        Gizmos.DrawWireSphere(pointB.position, 0.25f);
    }
}