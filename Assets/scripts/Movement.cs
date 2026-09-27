using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
public class Movement : MonoBehaviour
{
    private enum MoveState
    {
        Normal,
        AirLeap
    }

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Momentum")]
    [SerializeField] private float acceleration = 25f;
    [SerializeField] private float deceleration = 30f;
    [SerializeField] private float moveMomentumThreshold = 0.5f;

    [Header("Jumping & Gravity")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -20f;

    [Header("Long Jump")]
    [SerializeField] private float longJumpSpeed = 9f;
    [SerializeField] private float longJumpHeight = 1.2f;

    [Header("Backflip")]
    [SerializeField] private float backflipBackwardSpeed = 3f;
    [SerializeField] private float backflipHeight = 4f;

    [Header("Camera")]
    [SerializeField] private Transform cameraTransform;

    [Header("Animation Feel")]
    [Tooltip("How long to keep showing the run/walk animation after input stops, before dropping to idle.")]
    [SerializeField] private float idleDelay = 0.3f;

    [Header("Animation")]
    [Tooltip("Animator driving the character model. Auto-found on children if left empty (run Tools > Player > Generate Animator Controller first).")]
    [SerializeField] private Animator animator;

    [Header("Combat")]
    [Tooltip("How high the player hops after stomping an enemy's head.")]
    [SerializeField] private float stompBounceHeight = 3f;
    [Tooltip("After a stomp, touching another enemy from above within this many seconds also counts as a stomp — lets you chain stomps on enemies standing close together instead of dying on the bounce.")]
    [SerializeField] private float stompChainGrace = 0.25f;
    [Tooltip("Brief invulnerability after respawning so the player can't be killed again the instant they reappear.")]
    [SerializeField] private float respawnInvulnerabilityDuration = 1f;
    [Tooltip("Total time the player is frozen (can't move/act) during the death/respawn sequence.")]
    [SerializeField] private float deathFreezeDuration = 1f;
    [Tooltip("How long each fade to/from black takes. Should be well under half of Death Freeze Duration so there's time to actually see black before fading back in.")]
    [SerializeField] private float deathFadeDuration = 0.35f;
    [Tooltip("Fired whenever the player dies, right as the death sequence starts (before the fade/respawn).")]
    public UnityEvent onDeath;
    [Tooltip("Logs what killed the player to the Console every time they die — handy for tracking down unexpected deaths. Turn off once everything works.")]
    [SerializeField] private bool logDeathCauses = true;

    private CharacterController controller;
    private InputSystem_Actions inputActions;

    private Vector2 moveInput;
    private bool sprintHeld;
    private bool crouchHeld;
    private bool jumpPressed;
    private float verticalVelocity;
    private Vector3 horizontalVelocity;

    private MoveState state = MoveState.Normal;
    private Vector3 actionDirection;

    private Vector3 leapVelocity;
    private Quaternion leapBaseYaw;

    private float noInputTimer;
    private float animSpeedDisplay;

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float invulnerableUntil;
    private float lastStompTime = -Mathf.Infinity;
    private bool isFrozen;

    // Moving platform the player is currently standing on, and where on it
    // (in the platform's local space) the player stood last frame.
    private Transform currentPlatform;
    private Vector3 platformLocalPosition;
    private Transform platformHitThisFrame;

    // Animator parameter hashes (cached for performance)
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int LongJumpHash = Animator.StringToHash("LongJump");
    private static readonly int BackflipHash = Animator.StringToHash("Backflip");
    private static readonly int NoInputsHash = Animator.StringToHash("NoInputs");

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    private void OnEnable()
    {
        if (inputActions == null)
        {
            inputActions = new InputSystem_Actions();

            inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
            inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;

            inputActions.Player.Sprint.performed += ctx => sprintHeld = true;
            inputActions.Player.Sprint.canceled += ctx => sprintHeld = false;

            inputActions.Player.Crouch.performed += ctx => crouchHeld = true;
            inputActions.Player.Crouch.canceled += ctx => crouchHeld = false;

            inputActions.Player.Jump.performed += ctx => jumpPressed = true;
        }

        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        inputActions?.Player.Disable();
        currentPlatform = null; // don't snap back onto a platform that kept moving while disabled
    }

    private void Update()
    {
        if (!isFrozen)
        {
            FollowPlatform();

            switch (state)
            {
                case MoveState.Normal:
                    UpdateNormal();
                    break;
                case MoveState.AirLeap:
                    UpdateAirLeap();
                    break;
            }

            UpdatePlatformAttachment();
        }
        else
        {
            currentPlatform = null;
            platformHitThisFrame = null;
        }

        UpdateAnimator();
    }

    /// <summary>
    /// Moving platform support: a CharacterController is NOT carried along
    /// by a moving Rigidbody, so we do it ourselves — move the player by
    /// however much the spot they were standing on has moved since last frame.
    /// </summary>
    private void FollowPlatform()
    {
        if (currentPlatform == null || !controller.enabled) return;

        Vector3 target = currentPlatform.TransformPoint(platformLocalPosition);
        Vector3 delta = target - transform.position;
        if (delta.sqrMagnitude > 0.000001f)
            controller.Move(delta);
    }

    /// <summary>
    /// After this frame's movement: if we're standing on a MovingPlatform,
    /// remember where on it we are, so FollowPlatform can carry us next frame.
    /// Jumping or walking off clears it.
    /// </summary>
    private void UpdatePlatformAttachment()
    {
        if (platformHitThisFrame != null && controller.isGrounded)
        {
            currentPlatform = platformHitThisFrame;
            platformLocalPosition = currentPlatform.InverseTransformPoint(transform.position);
        }
        else
        {
            currentPlatform = null;
        }

        platformHitThisFrame = null;
    }

    private void UpdateNormal()
    {
        bool isGrounded = controller.isGrounded;

        if (isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 moveDirection = Vector3.zero;
        Vector3 targetVelocity = Vector3.zero;

        if (inputDirection.sqrMagnitude > 0f)
        {
            moveDirection = GetCameraRelativeDirection(inputDirection);
            float speed = sprintHeld ? sprintSpeed : walkSpeed;
            targetVelocity = moveDirection * speed;

            // Face wherever the input is pointing (camera-relative), not just
            // camera forward, so holding D turns the character to look right,
            // A to look left, and W+D to look diagonally between them.
            FaceDirection(moveDirection);
        }

        float accelerationRate = inputDirection.sqrMagnitude > 0f ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, accelerationRate * Time.deltaTime);

        bool isMoving = horizontalVelocity.sqrMagnitude > moveMomentumThreshold * moveMomentumThreshold;
        bool wantsJump = jumpPressed && isGrounded;
        jumpPressed = false;

        if (wantsJump)
        {
            if (crouchHeld && isMoving)
            {
                StartLongJump();
                return;
            }

            if (crouchHeld && !isMoving)
            {
                StartBackflip();
                return;
            }

            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            animator?.SetTrigger(JumpHash);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        controller.Move(motion);
    }

    private void StartLongJump()
    {
        Vector3 direction = horizontalVelocity.sqrMagnitude > 0.01f ? horizontalVelocity.normalized : GetFlatCameraDirection();
        animator?.SetTrigger(LongJumpHash);
        StartAirLeap(direction * longJumpSpeed, Mathf.Sqrt(longJumpHeight * -2f * gravity), direction);
    }

    private void StartBackflip()
    {
        Vector3 cameraForward = GetFlatCameraDirection();
        Vector3 backward = -cameraForward;
        animator?.SetTrigger(BackflipHash);
        StartAirLeap(backward * backflipBackwardSpeed, Mathf.Sqrt(backflipHeight * -2f * gravity), cameraForward);
    }

    private void StartAirLeap(Vector3 launchVelocity, float upwardVelocity, Vector3 facingDirection)
    {
        state = MoveState.AirLeap;
        leapVelocity = launchVelocity;
        verticalVelocity = upwardVelocity;
        horizontalVelocity = launchVelocity;

        Vector3 flatFacing = new Vector3(facingDirection.x, 0f, facingDirection.z);
        leapBaseYaw = flatFacing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flatFacing.normalized) : transform.rotation;

        // Lock facing for the duration of the leap; the Animator's clip
        // (Jump / LongJump / Backflip) supplies the actual flip motion.
        transform.rotation = leapBaseYaw;
    }

    private void UpdateAirLeap()
    {
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = (leapVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        controller.Move(motion);

        if (controller.isGrounded && verticalVelocity <= 0f)
        {
            state = MoveState.Normal;
            verticalVelocity = -2f;
            horizontalVelocity = leapVelocity;
            transform.rotation = leapBaseYaw;
        }

        jumpPressed = false;
    }

    /// <summary>
    /// Bounce pad support: CharacterController doesn't route through Unity's
    /// physics collision events (OnCollisionEnter), so BouncingPlatform can't
    /// detect or push us on its own — this is the player's side of that
    /// interaction. Unity calls this automatically every frame the
    /// CharacterController is touching something, for every component on
    /// this GameObject that defines it, so no extra wiring is needed beyond
    /// this method existing.
    /// </summary>
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Only react to hits coming from roughly below (landing on top of it).
        if (hit.normal.y < 0.5f) return;

        // Standing on a moving platform? Remember it so we move along with it.
        MovingPlatform movingPlatform = hit.collider.GetComponentInParent<MovingPlatform>();
        if (movingPlatform != null)
            platformHitThisFrame = movingPlatform.transform;

        // Only bounce if we were actually falling onto it — this also stops
        // the pad from re-triggering every frame while airborne right after
        // a bounce (OnControllerColliderHit keeps firing on contact).
        if (verticalVelocity >= 0f) return;

        BouncingPlatform pad = hit.collider.GetComponent<BouncingPlatform>();
        if (pad == null) return;

        float bounceVelocity = pad.GetBounceVelocity(-verticalVelocity);
        if (bounceVelocity <= 0f) return; // platform is on cooldown — no bounce this time

        verticalVelocity = bounceVelocity;

        // If a bounce happens mid-leap (long jump/backflip), drop back into
        // normal state so the bounce's vertical velocity isn't immediately
        // overridden by leap-specific logic next frame.
        if (state == MoveState.AirLeap)
        {
            state = MoveState.Normal;
            horizontalVelocity = leapVelocity;
            transform.rotation = leapBaseYaw;
        }

        animator?.SetTrigger(JumpHash);
    }

    /// <summary>
    /// Enemy contact detection, driven by a trigger instead of
    /// OnControllerColliderHit. This matters specifically because
    /// OnControllerColliderHit only fires when the PLAYER's own Move() call
    /// sweeps into something — it does NOT reliably fire when a
    /// NavMeshAgent-driven enemy walks into an otherwise-stationary player,
    /// since nothing about the player's own movement caused the overlap.
    /// Triggers, by contrast, are checked every physics step regardless of
    /// which side moved, so this catches the enemy walking into the player
    /// just as reliably as the player walking into the enemy.
    ///
    /// REQUIRES: the enemy's Collider must have "Is Trigger" checked and be
    /// tagged "Enemy". No extra Collider is needed on the player — the
    /// CharacterController itself is enough to receive trigger events.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // Kill floor: any trigger tagged "FloorKill" kills the player, no
        // extra script needed on it. (FloorKillTrigger.cs still works too.)
        // The "FloorKill" tag must exist in Tags & Layers.
        if (other.CompareTag("FloorKill"))
        {
            Die($"touched FloorKill trigger '{other.name}'", other);
            return;
        }

        CheckEnemyContact(other, false);
    }

    /// <summary>
    /// Re-checks enemies we're still overlapping, so an enemy first touched
    /// from above while still rising (e.g. during a stomp bounce) gets
    /// stomped as soon as the player starts falling onto it. Only ever
    /// turns into a stomp here — side hits are decided on enter.
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        CheckEnemyContact(other, true);
    }

    private void CheckEnemyContact(Collider other, bool stompOnly)
    {
        if (!other.CompareTag("Enemy")) return;

        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy == null) enemy = other.GetComponentInParent<EnemyAI>();
        if (enemy == null) return;

        // Approximate the classic "landed on top" stomp check without
        // OnControllerColliderHit's contact normal: falling, and the
        // player's position is above the enemy's BODY center — NOT the
        // detection trigger's own bounds center. The trigger collider may
        // be deliberately inflated taller than the enemy's actual body (see
        // EnemyAI's Solid Blocker setup notes), which would otherwise raise
        // this threshold right along with it and make every stomp resolve
        // as a side hit instead.
        bool isAbove = transform.position.y > enemy.BodyCenterY;
        bool isFalling = verticalVelocity < 0f;

        // Right after a stomp the player is moving UP (the bounce), so a
        // second enemy touched in that moment would otherwise fail the
        // "falling" check and count as a side hit. Within the chain grace
        // window, being above the enemy is enough.
        bool inStompChain = Time.time - lastStompTime <= stompChainGrace;

        if (isAbove && (isFalling || inStompChain))
        {
            HandleEnemyContact(enemy, true);
        }
        else if (!isAbove && !stompOnly)
        {
            HandleEnemyContact(enemy, false);
        }
        // Above the enemy but still rising: not a stomp yet, but not a hit
        // either — OnTriggerStay turns it into a stomp once we start falling.
    }

    /// <summary>
    /// Classic stomp rule: landing on TOP of an enemy while falling kills it
    /// and gives the player a little hop; touching it from any other angle
    /// (side, front, or while jumping up into its underside) kills the player.
    /// </summary>
    private void HandleEnemyContact(EnemyAI enemy, bool isStomp)
    {
        if (isStomp)
        {
            enemy.Stomp();
            lastStompTime = Time.time;
            verticalVelocity = Mathf.Sqrt(stompBounceHeight * -2f * gravity);

            if (state == MoveState.AirLeap)
            {
                state = MoveState.Normal;
                horizontalVelocity = leapVelocity;
                transform.rotation = leapBaseYaw;
            }

            animator?.SetTrigger(JumpHash);
        }
        else
        {
            Die($"side hit by enemy '{enemy.name}' — player Y {transform.position.y:0.00} was not above the enemy's body center Y {enemy.BodyCenterY:0.00} (vertical speed {verticalVelocity:0.00})", enemy);
        }
    }

    /// <summary>
    /// Public entry point for anything outside this component that should
    /// kill the player outright — e.g. a bottomless-pit/kill-zone trigger
    /// (see FloorKillTrigger.cs). Runs the exact same freeze/fade/respawn
    /// sequence as dying to an enemy, invulnerability window included.
    /// </summary>
    /// <summary>Kills the player and logs who did it (used by FloorKillTrigger).</summary>
    public void KillBy(string cause, UnityEngine.Object source)
    {
        Die(cause, source);
    }

    public void Kill()
    {
        Die("Kill() was called by another script or an Inspector event (e.g. FloorKillTrigger, or an enemy's On Attack event) — expand this log to see the call stack", null);
    }

    /// <summary>
    /// Starts the death sequence: freezes the player, fades the screen to
    /// black, respawns them at the level start while the screen is fully
    /// black (so the teleport is never actually seen), then fades back in
    /// and unfreezes. Has a brief invulnerability window afterward so a
    /// lingering enemy can't immediately kill them again.
    /// </summary>
    private void Die(string cause, UnityEngine.Object source)
    {
        if (isFrozen) return; // already mid-death-sequence
        if (Time.time < invulnerableUntil) return;
        invulnerableUntil = Time.time + respawnInvulnerabilityDuration;

        // Clicking this log in the Console highlights the thing that killed
        // the player (when known) in the Hierarchy.
        if (logDeathCauses)
            Debug.Log($"[Death] Player died: {cause}", source != null ? source : this);

        StartCoroutine(DieRoutine());
    }

    private IEnumerator DieRoutine()
    {
        isFrozen = true;
        verticalVelocity = 0f;
        horizontalVelocity = Vector3.zero;

        onDeath?.Invoke();

        if (GameManager.Instance != null)
            yield return GameManager.Instance.FadeToBlack(deathFadeDuration);

        // Teleport while the screen is fully black, so the pop is never seen.
        state = MoveState.Normal;
        controller.enabled = false;
        transform.position = spawnPosition;
        transform.rotation = spawnRotation;
        controller.enabled = true;

        // Hold on black for whatever's left of the freeze duration after
        // accounting for both fade transitions, so the total frozen time
        // matches Death Freeze Duration.
        float remainingHold = Mathf.Max(0f, deathFreezeDuration - deathFadeDuration * 2f);
        if (remainingHold > 0f)
            yield return new WaitForSeconds(remainingHold);

        if (GameManager.Instance != null)
            yield return GameManager.Instance.FadeFromBlack(deathFadeDuration);

        isFrozen = false;
    }

    /// <summary>
    /// Feeds the CharacterController's real velocity and grounded state into the
    /// Animator every frame so the Locomotion blend tree (Idle/Walk/Run) and the
    /// Jump -> Air -> Land chain stay in sync, whatever state we're in.
    /// </summary>
    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        Vector3 flatVelocity = horizontalVelocity;
        flatVelocity.y = 0f;
        float speed = flatVelocity.magnitude;

        float normalizedSpeed;
        if (speed <= 0.05f)
        {
            normalizedSpeed = 0f;
        }
        else if (speed <= walkSpeed)
        {
            normalizedSpeed = Mathf.InverseLerp(0f, walkSpeed, speed) * 0.5f;
        }
        else
        {
            normalizedSpeed = 0.5f + Mathf.InverseLerp(walkSpeed, sprintSpeed, speed) * 0.5f;
        }

        // Hold the last "moving" speed for a short grace period after input
        // stops, so the run/walk animation doesn't cut to idle the instant
        // the key is released (before momentum has even had a chance to decay).
        bool hasInput = moveInput.sqrMagnitude > 0.0001f;
        if (hasInput)
        {
            noInputTimer = 0f;
            animSpeedDisplay = normalizedSpeed;
        }
        else
        {
            noInputTimer += Time.deltaTime;
            if (noInputTimer >= idleDelay)
                animSpeedDisplay = normalizedSpeed;
        }

        animator.SetFloat(SpeedHash, animSpeedDisplay);
        animator.SetBool(GroundedHash, controller.isGrounded);

        // Raw seconds since the last input, exposed directly so the Animator
        // can threshold off it (e.g. > 0.1s -> standing still, > 5s -> begin idle).
        animator.SetFloat(NoInputsHash, noInputTimer);
    }

    private Vector3 GetCameraRelativeDirection(Vector3 inputDirection)
    {
        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        return (forward * inputDirection.z + right * inputDirection.x).normalized;
    }

    private Vector3 GetFlatCameraDirection()
    {
        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
    }

    private void FaceDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0f)
            return;

        Quaternion targetRotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), rotationSpeed * Time.deltaTime);
        transform.rotation = targetRotation;
    }
}
