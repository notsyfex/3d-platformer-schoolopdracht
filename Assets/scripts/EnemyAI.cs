using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// A patrol/chase/attack enemy driven by Unity's NavMesh system.
///
/// SETUP:
/// 1. Bake a NavMesh for your level: Window > AI > Navigation, mark your
///    ground/platform geometry as "Navigation Static" (Inspector, top-right
///    dropdown next to Static), then in the Navigation window's Bake tab
///    click Bake. Without a baked NavMesh the agent can't move at all.
/// 2. Attach this script to your enemy GameObject (it auto-adds a
///    NavMeshAgent via RequireComponent).
/// 3. Create empty GameObjects as patrol waypoints and add them, in order,
///    to the Patrol Points list.
/// 4. Make sure your player GameObject is tagged "Player" (same convention
///    as Coin.cs / PlayerCoinCollector.cs from earlier).
/// 5. Wire onAttack in the Inspector to whatever the attack should actually
///    do — deal damage, play a VFX, spawn a projectile, etc. This script
///    handles the "when" (range + cooldown), not the "what", so it works
///    with whatever combat system you build.
///
/// Works fine on uneven/sloped terrain (like a mountain level) as long as
/// it's part of the baked NavMesh — NavMeshAgent handles slopes and stairs
/// automatically up to the Nav Mesh Agent's configured max slope angle.
///
/// STOMP-TO-KILL: this enemy doesn't detect the stomp itself — since the
/// player uses a CharacterController (see Movement.cs), it's the player's
/// OnControllerColliderHit that notices "I landed on top of an enemy" and
/// calls this enemy's public Stomp() method. See Movement.cs's
/// HandleEnemyContact for that side of the interaction.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    private enum State
    {
        Patrol,
        Chase,
        Attack
    }

    [Header("Colliders")]
    [Tooltip("The Collider the player detects for stomp/damage (Movement.cs's OnTriggerEnter). Must have 'Is Trigger' checked and be tagged 'Enemy'. Drag it in explicitly — leave empty to fall back to auto-detecting it among this enemy's own/child colliders.")]
    [SerializeField] private Collider stompTrigger;
    [Tooltip("Optional: a separate SOLID (non-trigger) collider that just physically blocks the player from walking through this enemy's body — only needed if you added a second collider for that. Leave empty if you don't have one. IMPORTANT: Stomp Trigger's bounds should extend a bit higher than this one, or top-down stomps won't register (see the warning this script logs if they don't).")]
    [SerializeField] private Collider solidBlocker;

    /// <summary>
    /// Stable Y reference used by Movement.cs to decide "stomp vs side hit" —
    /// deliberately NOT the Stomp Trigger's own bounds, since that collider
    /// may need to be inflated well above the enemy's actual body just to
    /// register overlap before the Solid Blocker physically stops the
    /// player (see the warning above). Uses the Solid Blocker's bounds
    /// center when assigned, since that one stays sized to match the
    /// enemy's real visible body; falls back to the Eye Offset height
    /// (roughly the enemy's mid-height) if there's no Solid Blocker.
    /// </summary>
    public float BodyCenterY => solidBlocker != null ? solidBlocker.bounds.center.y : transform.position.y + eyeOffset.y;

    [Header("Patrol")]
    [Tooltip("Waypoints visited in order, looping back to the first after the last.")]
    [SerializeField] private Transform[] patrolPoints;
    [Tooltip("Seconds to wait at each patrol point before moving to the next.")]
    [SerializeField] private float waitTimeAtPoint = 1.5f;
    [SerializeField] private float patrolSpeed = 2f;

    [Header("Detection")]
    [Tooltip("Player is spotted once within this distance.")]
    [SerializeField] private float detectionRadius = 10f;
    [Tooltip("If true, also requires an unobstructed line of sight (raycast) to spot the player, not just distance.")]
    [SerializeField] private bool requireLineOfSight = true;
    [Tooltip("Layers that block line of sight (walls, terrain — should NOT include the player's own layer).")]
    [SerializeField] private LayerMask lineOfSightBlockers;
    [Tooltip("Roughly where on the enemy the 'eyes' are, for line-of-sight checks.")]
    [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("If the player escapes further than this, give up the chase and return to patrol.")]
    [SerializeField] private float loseInterestRadius = 15f;

    [Header("Chase")]
    [SerializeField] private float chaseSpeed = 4.5f;
    [Tooltip("How often (seconds) the destination is updated while chasing — doesn't need to be every frame.")]
    [SerializeField] private float chaseUpdateInterval = 0.2f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [Tooltip("How fast the enemy turns to face the player while attacking.")]
    [SerializeField] private float attackFaceTurnSpeed = 10f;
    [Tooltip("Fired whenever an attack triggers — hook up damage, an animation trigger, a VFX, etc.")]
    public UnityEvent onAttack;

    [Header("Death (stomp)")]
    [Tooltip("Multiplier applied to the enemy's current scale when stomped — e.g. Y = 0.5 flattens it to half height, classic Mario-squish style.")]
    [SerializeField] private Vector3 squishScale = new Vector3(1f, 0.5f, 1f);
    [Tooltip("Seconds to wait before actually removing the enemy after being stomped — gives the squish (and any death animation/VFX) time to actually be seen. 0 removes it instantly, which hides the squish entirely.")]
    [SerializeField] private float destroyDelay = 0.3f;
    [Tooltip("Fired once when this enemy is stomped — hook up a death animation, VFX, sound, score, etc.")]
    public UnityEvent onDeath;

    [Header("Animation (optional)")]
    [Tooltip("Auto-found on children if left empty. Feeds a 'Speed' float param, matching the player's Movement.cs convention.")]
    [SerializeField] private Animator animator;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private NavMeshAgent agent;
    private Transform player;
    private State state = State.Patrol;

    private int currentPatrolIndex;
    private float waitTimer;
    private float chaseUpdateTimer;
    private float lastAttackTime = -Mathf.Infinity;
    private bool isDead;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // By default NavMeshAgent tilts the whole transform to match the
        // ground's slope as it walks, which makes something like a Goomba
        // look like it's leaning/tipping over on sloped terrain. Locking the
        // up-axis to world-up keeps it standing straight regardless of the
        // slope, while still turning normally (yaw) to face its movement.
        agent.updateUpAxis = false;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        ValidateStompSetup();
    }

    /// <summary>
    /// The stomp/kill interaction is driven entirely by the player's
    /// OnTriggerEnter (see Movement.cs), which requires a Collider — on
    /// this GameObject OR any child — that BOTH has "Is Trigger" checked
    /// AND is tagged "Enemy". Missing either one makes stomping silently do
    /// nothing, with no error anywhere else, so check for it loudly here.
    /// </summary>
    private void ValidateStompSetup()
    {
        // Prefer the explicitly-assigned Stomp Trigger field; fall back to
        // auto-detecting one among this enemy's own/child colliders for
        // anyone who left it unassigned.
        if (stompTrigger == null)
        {
            foreach (Collider col in GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger && col.CompareTag("Enemy"))
                {
                    stompTrigger = col;
                    break;
                }
            }
        }

        if (stompTrigger == null)
        {
            Debug.LogError($"{name}: no Stomp Trigger assigned (and none could be auto-detected) — the player can never detect this enemy, so it can't be stomped or damage the player. Drag the enemy's trigger Collider into the Stomp Trigger field, and make sure it has 'Is Trigger' checked and is tagged 'Enemy'.", this);
            return;
        }

        if (!stompTrigger.isTrigger || !stompTrigger.CompareTag("Enemy"))
        {
            Debug.LogError($"{name}: Stomp Trigger '{stompTrigger.name}' must have 'Is Trigger' checked AND be tagged 'Enemy' — stomping won't work until both are set.", this);
        }

        // Common setup: a second, SOLID (non-trigger) collider added just to
        // physically block the player from walking through the enemy's body.
        // If that solid collider is as tall as (or taller than) the stomp
        // trigger, the player's CharacterController rests directly on its
        // surface with ~zero penetration, so it never overlaps the trigger
        // deeply enough for OnTriggerEnter to fire on a top-down landing —
        // even though side hits still work fine. The trigger needs to extend
        // a bit higher than the solid collider so landing on top overlaps
        // the trigger BEFORE the player is physically stopped by the solid one.
        if (solidBlocker == null)
        {
            foreach (Collider col in GetComponentsInChildren<Collider>())
            {
                if (col != stompTrigger && !col.isTrigger)
                {
                    solidBlocker = col;
                    break;
                }
            }
        }

        if (solidBlocker != null && stompTrigger.bounds.max.y <= solidBlocker.bounds.max.y + 0.05f)
        {
            Debug.LogWarning($"{name}: Solid Blocker '{solidBlocker.name}' is as tall as or taller than Stomp Trigger '{stompTrigger.name}'. " +
                              "The player will physically land on the solid collider before overlapping the trigger, so top-down stomps won't register (side hits will still work). " +
                              "Make the trigger's bounds extend noticeably ABOVE the solid collider's top (e.g. 0.2+ units) to fix this.", this);
        }
    }

    private void Start()
    {
        agent.speed = patrolSpeed;
        if (patrolPoints != null && patrolPoints.Length > 0)
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
    }

    private void Update()
    {
        if (isDead) return;
        if (player == null) return; // no player in the scene yet

        switch (state)
        {
            case State.Patrol:
                UpdatePatrol();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Attack:
                UpdateAttack();
                break;
        }

        if (animator != null)
            animator.SetFloat(SpeedHash, agent.velocity.magnitude);
    }

    private void LateUpdate()
    {
        // Belt-and-suspenders fix for staying upright: forcibly strip out any
        // pitch/roll every frame, keeping only the yaw (facing direction).
        // This guarantees the model can never end up tilted or on its side,
        // regardless of starting rotation, slopes, or animation quirks.
        Vector3 euler = transform.eulerAngles;
        transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
    }

    private void UpdatePatrol()
    {
        if (CanSeePlayer())
        {
            EnterChase();
            return;
        }

        if (patrolPoints == null || patrolPoints.Length == 0) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waitTimeAtPoint)
            {
                waitTimer = 0f;
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
                agent.SetDestination(patrolPoints[currentPatrolIndex].position);
            }
        }
    }

    private void UpdateChase()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer > loseInterestRadius)
        {
            EnterPatrol();
            return;
        }

        if (distanceToPlayer <= attackRange)
        {
            EnterAttack();
            return;
        }

        chaseUpdateTimer += Time.deltaTime;
        if (chaseUpdateTimer >= chaseUpdateInterval)
        {
            chaseUpdateTimer = 0f;
            agent.SetDestination(player.position);
        }
    }

    private void UpdateAttack()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer > attackRange)
        {
            EnterChase();
            return;
        }

        // Face the player while attacking.
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toPlayer);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, attackFaceTurnSpeed * Time.deltaTime);
        }

        if (Time.time - lastAttackTime >= attackCooldown)
        {
            lastAttackTime = Time.time;
            onAttack?.Invoke();
        }
    }

    private void EnterPatrol()
    {
        state = State.Patrol;
        agent.isStopped = false;
        agent.speed = patrolSpeed;
        if (patrolPoints != null && patrolPoints.Length > 0)
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
    }

    private void EnterChase()
    {
        state = State.Chase;
        agent.isStopped = false;
        agent.speed = chaseSpeed;
    }

    private void EnterAttack()
    {
        state = State.Attack;
        agent.isStopped = true;
    }

    /// <summary>
    /// Called by the player when they land on this enemy's head. Stops all
    /// AI behavior immediately and removes the enemy (after an optional
    /// delay for a death animation/VFX).
    /// </summary>
    public void Stomp()
    {
        if (isDead) return;
        isDead = true;

        agent.isStopped = true;
        agent.enabled = false;

        // Disable every collider on this enemy (not just one) — with a
        // separate stomp trigger AND solid blocker, leaving either one
        // active would let the squished corpse still block the player or
        // re-fire a hit on the way down.
        foreach (Collider col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        transform.localScale = Vector3.Scale(transform.localScale, squishScale);

        if (GameManager.Instance != null)
            GameManager.Instance.AddKill(LevelId.CurrentLevelId);
        else
            Debug.LogWarning($"{name}: no GameManager found — kill not saved.", this);

        onDeath?.Invoke();

        Destroy(gameObject, destroyDelay);
    }

    private bool CanSeePlayer()
    {
        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > detectionRadius) return false;

        if (!requireLineOfSight) return true;

        Vector3 eyePosition = transform.position + eyeOffset;
        Vector3 targetPosition = player.position + Vector3.up * 0.5f;
        Vector3 direction = targetPosition - eyePosition;

        if (Physics.Raycast(eyePosition, direction.normalized, out RaycastHit hit, direction.magnitude, lineOfSightBlockers))
        {
            return false; // something is blocking the view
        }

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, loseInterestRadius);
    }
}
