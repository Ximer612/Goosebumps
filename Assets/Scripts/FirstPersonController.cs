using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonController : MonoBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float sprintMultiplier = 2.0f;

    [Header("Look Sensitivity")]
    [SerializeField] private float mouseSensitivity = 2.0f;
    [SerializeField] private float upDownRange = 80.0f;

    [Header("Jump Parameters")]
    [SerializeField] private bool canJump = false;
    [SerializeField] private float JumpForce = 5.0f;
    [SerializeField] private float gravity = 9.81f;

    [Header("Crouch Parameters")]
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float standingHeight;
    [SerializeField] private float currentHeight;
    [SerializeField] private float crouchSpeedTransition = 10f;
    [SerializeField] private float crouchSpeedMultiplier = 0.1f;
    [SerializeField] private bool isCrouching => standingHeight - currentHeight > .1f;

    [Header("Pickup Interaction")]
    [SerializeField] private Transform objectHoldPoint;
    [SerializeField] private float lerpSpeed = 5.0f;
    private GameObject pickedUpObject;
    private Vector3 objectOriginalPosition;
    private Quaternion objectOriginalRotation;
    private bool isObjectPickedUp = false;
    private bool isInteracting = false;
    private bool isRotatingObject = false;

    [Header("FootStep Sounds")]
    [SerializeField] private AudioSource footstepSource;
    [SerializeField] private AudioClip[] footstepSounds;
    [SerializeField] private float walkStepInterval = 0.5f;
    [SerializeField] private float sprintStepInterval = 0.3f;
    [SerializeField] private float velocityThreshold = 2.0f;

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset PlayerControls;

    private int lastPlayedIndex = -1;
    private bool isMoving;
    private float nextStepTime;
    private Camera mainCamera;
    private Vector3 initialCameraPosition;
    private float verticalRotation;
    private Vector3 currentMovement = Vector3.zero;
    private CharacterController characterController;

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction jumpAction;
    private InputAction sprintAction;
    private InputAction crouchAction;
    private InputAction interactAction;
    private InputAction releaseAction;
    private InputAction rotateObjectAction;
    private Vector2 moveInput;
    private Vector2 lookInput;


    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        mainCamera = Camera.main;

        moveAction = PlayerControls.FindActionMap("Player").FindAction("Move");
        lookAction = PlayerControls.FindActionMap("Player").FindAction("Look");
        jumpAction = PlayerControls.FindActionMap("Player").FindAction("Jump");
        sprintAction = PlayerControls.FindActionMap("Player").FindAction("Sprint");
        crouchAction = PlayerControls.FindActionMap("Player").FindAction("Crouch");
        interactAction = PlayerControls.FindActionMap("Player").FindAction("Interact");
        releaseAction = PlayerControls.FindActionMap("Player").FindAction("Release");
        rotateObjectAction = PlayerControls.FindActionMap("Player").FindAction("RotateObject");

        initialCameraPosition = mainCamera.transform.localPosition;
        currentHeight = characterController.height;
        standingHeight = currentHeight;

        moveAction.performed += context => moveInput = context.ReadValue<Vector2>();
        moveAction.canceled += context => moveInput = Vector2.zero;

        lookAction.performed += context => lookInput = context.ReadValue<Vector2>();
        lookAction.canceled += context => lookInput = Vector2.zero;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        crouchAction.Enable();
        interactAction.Enable();
        releaseAction.Enable();
        rotateObjectAction.Enable();
        interactAction.performed += HandleInteract;
        releaseAction.performed += HandleRelease;
        rotateObjectAction.performed += StartRotatingObject;
        rotateObjectAction.canceled += StopRotatingObject;
    }

    private void OnDisable()
    {
        moveAction.Disable();
        lookAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
        crouchAction.Disable();
        interactAction.Disable();
        releaseAction.Disable();
        rotateObjectAction.Disable();
        interactAction.performed -= HandleInteract;
        releaseAction.performed -= HandleRelease;
        rotateObjectAction.performed -= StartRotatingObject;
        rotateObjectAction.canceled -= StopRotatingObject;
    }

    private void Update()
    {
        HandleMovement();
        HandleRotation();
        HandleCrouch();
        HandleFootsteps();
        HandleItemRotation();
        CheckObjectPickedUp();
    }

    void HandleMovement()
    {
        if (isInteracting) return;

        float speedMultiplier = sprintAction.ReadValue<float>() > 0 ? sprintMultiplier : 1f;
        float crouchMultiplier = crouchAction.ReadValue<float>() > 0 ? crouchSpeedMultiplier : 1f;
        float verticalSpeed = moveInput.y * walkSpeed * speedMultiplier * crouchMultiplier;
        float horizontalSpeed = moveInput.x * walkSpeed * speedMultiplier * crouchMultiplier;

        Vector3 horizontalMovement = new Vector3 (horizontalSpeed, 0, verticalSpeed);
        horizontalMovement = transform.rotation * horizontalMovement;

        HandleGravityAndJumping();

        currentMovement.x = horizontalMovement.x;
        currentMovement.z = horizontalMovement.z;

        characterController.Move(currentMovement * Time.deltaTime);

        isMoving = moveInput.y != 0 || moveInput.x != 0;
    }

    void HandleCrouch()
    {
        bool isTryingToCrouch = crouchAction.ReadValue<float>() > 0;
        float heightTarget = isTryingToCrouch ? crouchHeight : standingHeight;

        if(isCrouching && !isTryingToCrouch)
        {
            Vector3 castOrigin = transform.position + new Vector3(0, currentHeight / 2, 0);
            if (Physics.Raycast(castOrigin, Vector3.up, out RaycastHit hit, 0.2f))
            {
                float distanceToCeiling = hit.point.y - castOrigin.y;
                heightTarget = Mathf.Max
                (
                    currentHeight + distanceToCeiling - 0.1f,
                    crouchHeight
                );
            }
        }

        if (!Mathf.Approximately(heightTarget, currentHeight))
        {
            float crouchDelta = crouchSpeedTransition * Time.deltaTime;
            currentHeight = Mathf.Lerp(currentHeight, heightTarget, crouchDelta);

            Vector3 halfHeightDifference = new Vector3(0, (standingHeight - currentHeight) / 2, 0);
            Vector3 newCameraPosition = initialCameraPosition - halfHeightDifference;

            mainCamera.transform.localPosition = newCameraPosition;

            characterController.height = currentHeight;
        }
    }

    void HandleRotation()
    {
        if (isInteracting) return;

        float mouseXRotation = lookInput.x * mouseSensitivity;
        transform.Rotate(0, mouseXRotation, 0);

        verticalRotation -= lookInput.y * mouseSensitivity;
        verticalRotation = Mathf.Clamp(verticalRotation, -upDownRange, upDownRange);

        mainCamera.transform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
    }

    void HandleFootsteps()
    {
        float currentStepInterval = (sprintAction.ReadValue<float>() > 0 ? sprintStepInterval : walkStepInterval);

        if (characterController.isGrounded && isMoving && Time.time > nextStepTime && characterController.velocity.magnitude > velocityThreshold)
        {
            PlayFootstepSounds();
            nextStepTime = Time.time + currentStepInterval;
        }
    }

    void PlayFootstepSounds()
    {
        int randomIndex;
        if(footstepSounds.Length == 1)
        {
            randomIndex = 0;
        }
        else
        {
            randomIndex = Random.Range(0, footstepSounds.Length - 1);
            if(randomIndex >= lastPlayedIndex)
            {
                randomIndex++;
            }
        }
        lastPlayedIndex = randomIndex;
        footstepSource.clip = footstepSounds[randomIndex];
        footstepSource.Play();
    }

    void HandleGravityAndJumping()
    {
        if (canJump == false) return;

        if (characterController.isGrounded)
        {
            currentMovement.y = -0.5f;

            if(jumpAction.triggered)
            {
                currentMovement.y = JumpForce;
            }
        }
        else
        {
            currentMovement.y -= gravity * Time.deltaTime;
        }

    }

    private void HandleInteract(InputAction.CallbackContext context)
    {
        if (isObjectPickedUp) return; // Se un oggetto è già raccolto, ignora

        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 3f))
        {
            if (hit.collider.CompareTag("CanPickUp"))
            {
                pickedUpObject = hit.collider.gameObject;
                objectOriginalPosition = pickedUpObject.transform.position;
                objectOriginalRotation = pickedUpObject.transform.rotation;
                isObjectPickedUp = true;
                isInteracting = true;
            }
        }
    }

    private void HandleRelease(InputAction.CallbackContext context)
    {
        isInteracting = false;

        if (!isObjectPickedUp || pickedUpObject == null) return;

        // Ritorna l'oggetto alla posizione originale
        isObjectPickedUp = false;
        StartCoroutine(ReturnObjectToOriginalPosition());
    }

    private void MoveObjectToHoldPoint()
    {
        if (pickedUpObject == null) return;

        pickedUpObject.transform.position = Vector3.Lerp(pickedUpObject.transform.position, objectHoldPoint.position, Time.deltaTime * lerpSpeed);
        pickedUpObject.transform.rotation = Quaternion.Lerp(pickedUpObject.transform.rotation, objectHoldPoint.rotation, Time.deltaTime * lerpSpeed);
    }

    private System.Collections.IEnumerator ReturnObjectToOriginalPosition()
    {
        while (Vector3.Distance(pickedUpObject.transform.position, objectOriginalPosition) > 0.01f)
        {
            pickedUpObject.transform.position = Vector3.Lerp(pickedUpObject.transform.position, objectOriginalPosition, Time.deltaTime * lerpSpeed);
            pickedUpObject.transform.rotation = Quaternion.Lerp(pickedUpObject.transform.rotation, objectOriginalRotation, Time.deltaTime * lerpSpeed);
            yield return null;
        }
        pickedUpObject = null;
    }

    private void CheckObjectPickedUp()
    {
        if (isObjectPickedUp && pickedUpObject)
        {
            MoveObjectToHoldPoint();
        }
    }

    private void StartRotatingObject(InputAction.CallbackContext context)
    {
        if (isObjectPickedUp) // Assicurati che un oggetto sia stato raccolto
        {
            isRotatingObject = true;
            Cursor.lockState = CursorLockMode.None; // Sblocca il mouse
        }
    }

    private void StopRotatingObject(InputAction.CallbackContext context)
    {
        isRotatingObject = false;
        Cursor.lockState = CursorLockMode.Locked; // Rilocka il mouse
    }

    private void HandleItemRotation()
    {
        if (isRotatingObject && pickedUpObject != null)
        {
            float rotateX = lookInput.x; // Movimento orizzontale del mouse
            float rotateY = lookInput.y; // Movimento verticale del mouse

            // Rotazione attorno all'asse Y e X
            pickedUpObject.transform.Rotate(Vector3.up, -rotateX * mouseSensitivity, Space.World);
            pickedUpObject.transform.Rotate(Vector3.right, rotateY * mouseSensitivity, Space.World);
        }
    }
}
