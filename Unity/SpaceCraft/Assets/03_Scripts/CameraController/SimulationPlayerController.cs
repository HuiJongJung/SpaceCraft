using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimulationPlayerController : MonoBehaviour
{
    [Header("Move Settings")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -9.81f;

    private CharacterController controller;
    public GameObject head;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            Debug.LogError("SimulationPlayerController: CharacterController가 없습니다.", this);
        }
    }

    void Start()
    {
        head.transform.position = transform.position + new Vector3(0f, 0.7f, 0f);
    }

    public void MoveRelative(Vector2 input, Vector3 camForward, Vector3 camRight, bool jumpPressed)
    {
        if (controller == null)
        {
            return;
        }
        
        camForward.y = 0f;
        camRight.y = 0f;

        if (camForward.sqrMagnitude > 0.0001f)
        {
            camForward = camForward.normalized;
        }

        if (camRight.sqrMagnitude > 0.0001f)
        {
            camRight = camRight.normalized;
        }

        Vector3 moveDir = (camForward * input.y) + (camRight * input.x);
        if (moveDir.sqrMagnitude > 1f)
        {
            moveDir = moveDir.normalized;
        }

        bool isGrounded = controller.isGrounded;

        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (isGrounded && jumpPressed)
        {
            float jumpVel = Mathf.Sqrt(2f * jumpHeight * -gravity);
            verticalVelocity = jumpVel;
        }

        verticalVelocity = verticalVelocity + gravity * Time.deltaTime;

        Vector3 velocity = moveDir * moveSpeed;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }
    
    // Head Position
    public Vector3 GetHeadPosition()
    {
        return head.transform.position;
    }
    
    // Teleport
    public void TeleportTo(Vector3 newRootPosition)
    {
        if (controller != null)
        {
            controller.enabled = false;
        }

        verticalVelocity = 0f;
        transform.position = newRootPosition;

        if (controller != null)
        {
            controller.enabled = true;
        }
    }
}
