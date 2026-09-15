using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Movement : MonoBehaviour
{
    private enum MoveState
    {
        Normal,
        Diving,
        Rolling,
        AirLeap
    }

    private enum LeapType
    {
        LongJump,
        RollJump,
        Backflip,
        SideFlip
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

    [Header("Dive")]
    [SerializeField] private float diveSpeed = 10f;
    [SerializeField] private float diveUpwardForce = 2f;
    [SerializeField] private float diveTiltAngle = 90f;

    [Header("Roll")]
    [SerializeField] private float rollForwardSpeed = 4f;
    [SerializeField] private float rollDuration = 0.25f;
    [SerializeField] private float rollYScale = 0.5f;

    [Header("Roll Jump")]
    [SerializeField] private float rollJumpSpeed = 7f;
    [SerializeField] private float rollJumpHeight = 1.5f;

    [Header("Long Jump")]
    [SerializeField] private float longJumpSpeed = 9f;
    [SerializeField] private float longJumpHeight = 1.2f;
    [SerializeField] private float longJumpSpinDuration = 0.35f;

    [Header("Backflip")]
    [SerializeField] private float backflipBackwardSpeed = 3f;
    [SerializeField] private float backflipHeight = 4f;
    [SerializeField] private float backflipSpinDuration = 0.6f;

    [Header("Side Flip")]
    [SerializeField] private float sideFlipSpeed = 7f;
    [SerializeField] private float sideFlipHeight = 2.5f;
    [SerializeField] private float sideFlipSpinDuration = 0.5f;
    [SerializeField, Range(-1f, 0f)] private float sideFlipDotThreshold = -0.3f;

    [Header("Camera")]
    [SerializeField] private Transform cameraTransform;

    [Header("Visual")]
    [SerializeField] private Transform visualTransform;

    private CharacterController controller;
    private InputSystem_Actions inputActions;

    private Vector2 moveInput;
    private bool sprintHeld;
    private bool crouchHeld;
    private bool jumpPressed;
    private bool divePressed;
    private bool rollPressed;
    private float verticalVelocity;
    private Vector3 horizontalVelocity;

    private MoveState state = MoveState.Normal;
    private Vector3 actionDirection;
    private float rollTimer;
    private Vector3 originalScale;
    private Quaternion originalVisualRotation;

    private LeapType currentLeap;
    private Vector3 leapVelocity;
    private float leapElapsed;
    private float leapSpinDuration;
    private Quaternion leapBaseYaw;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (visualTransform == null)
            visualTransform = transform;

        originalScale = visualTransform.localScale;
        originalVisualRotation = visualTransform.localRotation;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
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
    }

    private void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame)
                divePressed = true;

            if (Keyboard.current.eKey.wasPressedThisFrame)
                rollPressed = true;
        }

        switch (state)
        {
            case MoveState.Normal:
                UpdateNormal();
                break;
            case MoveState.Diving:
                UpdateDiving();
                break;
            case MoveState.Rolling:
                UpdateRolling();
                break;
            case MoveState.AirLeap:
                UpdateAirLeap();
                break;
        }
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
        }

        FaceDirection(GetFlatCameraDirection());

        float accelerationRate = inputDirection.sqrMagnitude > 0f ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, accelerationRate * Time.deltaTime);

        Vector3 facingOrMoveDirection = moveDirection.sqrMagnitude > 0f ? moveDirection : GetFlatCameraDirection();

        if (divePressed)
        {
            divePressed = false;
            rollPressed = false;
            StartDive(GetFlatCameraDirection());
            return;
        }

        if (rollPressed)
        {
            rollPressed = false;
            StartRoll(facingOrMoveDirection);
            return;
        }

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

            if (isMoving && inputDirection.sqrMagnitude > 0f && Vector3.Dot(horizontalVelocity.normalized, moveDirection) < sideFlipDotThreshold)
            {
                StartSideFlip(moveDirection);
                return;
            }

            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        controller.Move(motion);
    }

    private void StartDive(Vector3 direction)
    {
        state = MoveState.Diving;
        actionDirection = direction.normalized;
        verticalVelocity = diveUpwardForce;
        FaceDirection(actionDirection);
        SetDivePose();
    }

    private void UpdateDiving()
    {
        if (rollPressed)
        {
            rollPressed = false;
            divePressed = false;
            StartRoll(actionDirection);
            return;
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = actionDirection * diveSpeed + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        if (controller.isGrounded && verticalVelocity <= 0f)
        {
            state = MoveState.Normal;
            verticalVelocity = -2f;
            horizontalVelocity = Vector3.zero;
            ResetScale();
        }

        jumpPressed = false;
        divePressed = false;
        rollPressed = false;
    }

    private void StartRoll(Vector3 direction)
    {
        state = MoveState.Rolling;
        rollTimer = rollDuration;
        actionDirection = direction.normalized;
        FaceDirection(actionDirection);
        SetYScale(rollYScale);
    }

    private void UpdateRolling()
    {
        if (jumpPressed)
        {
            jumpPressed = false;
            rollPressed = false;
            StartRollJump();
            return;
        }

        if (rollPressed)
        {
            rollPressed = false;
            rollTimer = rollDuration;

            Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y);
            if (inputDirection.sqrMagnitude > 0f)
            {
                actionDirection = GetCameraRelativeDirection(inputDirection);
                FaceDirection(actionDirection);
            }
        }

        rollTimer -= Time.deltaTime;

        bool isGrounded = controller.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = actionDirection * rollForwardSpeed + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        if (rollTimer <= 0f && isGrounded)
        {
            state = MoveState.Normal;
            horizontalVelocity = Vector3.zero;
            ResetScale();
        }

        jumpPressed = false;
        divePressed = false;
        rollPressed = false;
    }

    private void StartRollJump()
    {
        ResetScale();
        StartAirLeap(LeapType.RollJump, actionDirection * rollJumpSpeed, Mathf.Sqrt(rollJumpHeight * -2f * gravity), 0f, actionDirection);
    }

    private void StartLongJump()
    {
        Vector3 direction = horizontalVelocity.sqrMagnitude > 0.01f ? horizontalVelocity.normalized : GetFlatCameraDirection();
        StartAirLeap(LeapType.LongJump, direction * longJumpSpeed, Mathf.Sqrt(longJumpHeight * -2f * gravity), longJumpSpinDuration, direction);
    }

    private void StartBackflip()
    {
        Vector3 cameraForward = GetFlatCameraDirection();
        Vector3 backward = -cameraForward;
        StartAirLeap(LeapType.Backflip, backward * backflipBackwardSpeed, Mathf.Sqrt(backflipHeight * -2f * gravity), backflipSpinDuration, cameraForward);
    }

    private void StartSideFlip(Vector3 direction)
    {
        StartAirLeap(LeapType.SideFlip, direction * sideFlipSpeed, Mathf.Sqrt(sideFlipHeight * -2f * gravity), sideFlipSpinDuration, direction);
    }

    private void StartAirLeap(LeapType type, Vector3 launchVelocity, float upwardVelocity, float spinDuration, Vector3 facingDirection)
    {
        state = MoveState.AirLeap;
        currentLeap = type;
        leapVelocity = launchVelocity;
        verticalVelocity = upwardVelocity;
        leapElapsed = 0f;
        leapSpinDuration = spinDuration;
        horizontalVelocity = launchVelocity;

        Vector3 flatFacing = new Vector3(facingDirection.x, 0f, facingDirection.z);
        leapBaseYaw = flatFacing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flatFacing.normalized) : transform.rotation;
    }

    private void UpdateAirLeap()
    {
        leapElapsed += Time.deltaTime;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = (leapVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        controller.Move(motion);

        ApplyLeapSpinVisual();

        if (controller.isGrounded && verticalVelocity <= 0f)
        {
            state = MoveState.Normal;
            verticalVelocity = -2f;
            horizontalVelocity = leapVelocity;
            transform.rotation = leapBaseYaw;
        }

        jumpPressed = false;
        divePressed = false;
        rollPressed = false;
    }

    private void ApplyLeapSpinVisual()
    {
        float t = leapSpinDuration > 0f ? Mathf.Clamp01(leapElapsed / leapSpinDuration) : 1f;

        switch (currentLeap)
        {
            case LeapType.Backflip:
                transform.rotation = leapBaseYaw * Quaternion.Euler(-360f * t, 0f, 0f);
                break;
            case LeapType.SideFlip:
                transform.rotation = leapBaseYaw * Quaternion.Euler(0f, 0f, -360f * t);
                break;
            case LeapType.LongJump:
                transform.rotation = leapBaseYaw * Quaternion.Euler(360f * t, 0f, 0f);
                break;
            default:
                transform.rotation = leapBaseYaw;
                break;
        }
    }

    private void SetYScale(float yScale)
    {
        visualTransform.localScale = new Vector3(originalScale.x, yScale, originalScale.z);
    }

    private void SetDivePose()
    {
        visualTransform.localRotation = originalVisualRotation * Quaternion.Euler(diveTiltAngle, 0f, 0f);
    }

    private void ResetScale()
    {
        visualTransform.localScale = originalScale;
        visualTransform.localRotation = originalVisualRotation;
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

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
