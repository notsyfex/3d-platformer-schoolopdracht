using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class MovementLegacy : MonoBehaviour
{
    private enum MoveState
    {
        Normal,
        Diving,
        Rolling
    }

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Jumping & Gravity")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -20f;

    [Header("Dive")]
    [SerializeField] private float diveSpeed = 10f;
    [SerializeField] private float diveUpwardForce = 4f;
    [SerializeField] private float rollForwardSpeed = 4f;
    [SerializeField] private float rollDuration = 0.25f;
    [SerializeField] private float diveYScale = 0.2f;
    [SerializeField] private float rollYScale = 0.5f;

    [Header("Camera")]
    [SerializeField] private Transform cameraTransform;

    private CharacterController controller;
    private InputSystem_Actions inputActions;

    private Vector2 moveInput;
    private bool sprintHeld;
    private bool jumpPressed;
    private bool divePressed;
    private bool rollPressed;
    private float verticalVelocity;

    private MoveState state = MoveState.Normal;
    private Vector3 actionDirection;
    private float rollTimer;
    private Vector3 originalScale;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        inputActions = new InputSystem_Actions();
        originalScale = transform.localScale;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();

        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        inputActions.Player.Sprint.performed += ctx => sprintHeld = true;
        inputActions.Player.Sprint.canceled += ctx => sprintHeld = false;

        inputActions.Player.Jump.performed += ctx => jumpPressed = true;
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
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
        }
    }

    private void UpdateNormal()
    {
        bool isGrounded = controller.isGrounded;

        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 moveDirection = Vector3.zero;
        Vector3 horizontalVelocity = Vector3.zero;

        if (inputDirection.sqrMagnitude > 0f)
        {
            moveDirection = GetCameraRelativeDirection(inputDirection);
            float speed = sprintHeld ? sprintSpeed : walkSpeed;
            horizontalVelocity = moveDirection * speed;

            FaceDirection(moveDirection);
        }

        Vector3 facingOrMoveDirection = moveDirection.sqrMagnitude > 0f ? moveDirection : transform.forward;

        if (divePressed)
        {
            divePressed = false;
            rollPressed = false;
            StartDive(facingOrMoveDirection);
            return;
        }

        if (rollPressed)
        {
            rollPressed = false;
            StartRoll(facingOrMoveDirection);
            return;
        }

        if (isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (jumpPressed && isGrounded)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        jumpPressed = false;

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
        SetYScale(diveYScale);
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
            ResetScale();
        }

        jumpPressed = false;
        divePressed = false;
        rollPressed = false;
    }

    private void SetYScale(float yScale)
    {
        transform.localScale = new Vector3(originalScale.x, yScale, originalScale.z);
    }

    private void ResetScale()
    {
        transform.localScale = originalScale;
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

    private void FaceDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
