using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MovingPlatform : MonoBehaviour
{
    [Header("Endpoints")]
    [SerializeField] private Transform pointA;
    [SerializeField] private Transform pointB;

    [Header("Movement")]
    [SerializeField] private float travelDuration = 3f;
    [SerializeField] private float waitTimeAtPoints = 0.5f;

    [SerializeField]
    private AnimationCurve easeCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [SerializeField] private bool startMovingImmediately = true;

    private Rigidbody rb;

    private float elapsed;
    private float waitTimer;

    private bool movingToB;
    private bool waiting;

    private Vector3 movementDelta;

    public Vector3 MovementDelta => movementDelta;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        if (pointA == null || pointB == null)
        {
            Debug.LogWarning(
                $"{name}: MovingPlatform needs both Point A and Point B assigned.",
                this
            );

            enabled = false;
            return;
        }

        rb.position = pointA.position;

        movingToB = true;
        elapsed = 0f;
        waitTimer = 0f;

        waiting = !startMovingImmediately;

        movementDelta = Vector3.zero;
    }

    private void FixedUpdate()
    {
        movementDelta = Vector3.zero;

        if (pointA == null || pointB == null)
            return;

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

        float t;

        if (travelDuration > 0f)
        {
            t = Mathf.Clamp01(elapsed / travelDuration);
        }
        else
        {
            t = 1f;
        }

        float easedT = easeCurve.Evaluate(t);

        Vector3 from = movingToB
            ? pointA.position
            : pointB.position;

        Vector3 to = movingToB
            ? pointB.position
            : pointA.position;

        Vector3 newPosition = Vector3.LerpUnclamped(
            from,
            to,
            easedT
        );

        movementDelta = newPosition - rb.position;

        rb.MovePosition(newPosition);

        if (t >= 1f)
        {
            elapsed = 0f;

            movingToB = !movingToB;

            if (waitTimeAtPoints > 0f)
            {
                waiting = true;
                waitTimer = 0f;
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (pointA == null || pointB == null)
            return;

        Gizmos.color = Color.yellow;

        Gizmos.DrawLine(
            pointA.position,
            pointB.position
        );

        Gizmos.DrawWireSphere(
            pointA.position,
            0.25f
        );

        Gizmos.DrawWireSphere(
            pointB.position,
            0.25f
        );
    }
}