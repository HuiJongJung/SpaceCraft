using UnityEngine;

public class SimulationCameraController : MonoBehaviour
{
    private enum CameraMode
    {
        Free,
        FirstPerson
    }

    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private SimulationPlayerController player;

    [Header("Mouse Look")]
    private float mouseSensitivity = 250f;

    // Free Fly Movement
    [SerializeField] private float freeMoveSpeed = 6f;
    private float minHeight = 0.5f;
    private float maxHeight = 50f;

    // First Person Movement
    private float fpMoveSpeed = 5f;

    
    private CameraMode mode = CameraMode.Free;
    private float yaw;
    private float pitch;
    private bool cursorLocked;

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogError("SimulationCameraController: targetCamera 가 없습니다.", this);
            enabled = false;
            return;
        }
        
        if (player == null)
        {
            Debug.LogError("SimulationCameraController: player 참조가 비어 있습니다. 인스펙터에서 SimulationPlayerController를 직접 할당하세요.", this);
            enabled = false;
            return;
        }

        Vector3 euler = targetCamera.transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleMode();
        }

        if (mode == CameraMode.Free)
        {
            UpdateFreeMode();
        }
        else if (mode == CameraMode.FirstPerson)
        {
            UpdateFirstPersonMode();
        }
    }

    private void ToggleMode()
    {
        if (mode == CameraMode.Free)
        {
            // Move Player To Camera
            SyncPlayerToCameraPosition();
            
            // Camera Pos = player Head Pos
            Vector3 headPos = player.GetHeadPosition();
            targetCamera.transform.position = headPos;
            mode = CameraMode.FirstPerson;
        }
        else if (mode == CameraMode.FirstPerson)
        {
            mode = CameraMode.Free;
        }

        UnlockCursor();
    }

    private void HandleMouseLook()
    {
        if (Input.GetMouseButtonDown(1))
        {
            LockCursor();
        }
        else if (Input.GetMouseButtonUp(1))
        {
            UnlockCursor();
        }

        if (!Input.GetMouseButton(1))
        {
            return;
        }

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        yaw += mouseX * mouseSensitivity * Time.deltaTime;
        pitch -= mouseY * mouseSensitivity * Time.deltaTime;

        if (pitch < -89f)
        {
            pitch = -89f;
        }
        if (pitch > 89f)
        {
            pitch = 89f;
        }
    }

    private void LockCursor()
    {
        if (cursorLocked)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursorLocked = true;
    }

    private void UnlockCursor()
    {
        if (!cursorLocked)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cursorLocked = false;
    }
    
    private void SyncPlayerToCameraPosition()
    {
        if (player == null || targetCamera == null)
        {
            return;
        }

        // 1) 일단 플레이어를 카메라 위치로 옮긴다.
        Vector3 camPos = targetCamera.transform.position;
        Debug.Log(camPos);
        Debug.Log("prev Pos : " +player.GetHeadPosition());

        // 2) 바닥 아래로 안 떨어지도록 최소 높이 보정
        if (camPos.y < 0f)
        {
            camPos.y = 0f;
        }
        
        player.TeleportTo(camPos);
        Debug.Log("next Pos : " + player.transform.position);
    }


    // Free Mode
    private void UpdateFreeMode()
    {
        // 마우스 회전 처리 (우클릭 중일 때만 yaw/pitch 변경)
        HandleMouseLook();

        // Apply Rotation
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        targetCamera.transform.rotation = rot;

        float dt = Time.deltaTime;
        
        // Move (W/A/S/D)
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");

        // Camera Local
        Vector3 forward = targetCamera.transform.forward;
        Vector3 right   = targetCamera.transform.right;
        Vector3 up      = targetCamera.transform.up;

        if (forward.sqrMagnitude > 0.0001f)
        {
            forward = forward.normalized;
        }
        if (right.sqrMagnitude > 0.0001f)
        {
            right = right.normalized;
        }
        if (up.sqrMagnitude > 0.0001f)
        {
            up = up.normalized;
        }

        Vector3 moveDir = forward * inputZ + right * inputX;

        // Q/E - Up/Down
        if (Input.GetKey(KeyCode.Q))
        {
            moveDir = moveDir - up;
        }

        if (Input.GetKey(KeyCode.E))
        {
            moveDir = moveDir + up;
        }

        if (moveDir.sqrMagnitude > 1f)
        {
            moveDir = moveDir.normalized;
        }

        Vector3 pos = targetCamera.transform.position;
        pos = pos + moveDir * freeMoveSpeed * dt;
        
        if (pos.y < minHeight)
        {
            pos.y = minHeight;
        }
        if (pos.y > maxHeight)
        {
            pos.y = maxHeight;
        }

        targetCamera.transform.position = pos;
    }

    // First Person Mode
    private void UpdateFirstPersonMode()
    {
        if (player == null)
        {
            return;
        }
        
        // 마우스 회전 처리 (우클릭 중일 때만 yaw/pitch 변경)
        HandleMouseLook();

        // Apply Rotation
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        targetCamera.transform.rotation = rot;
        
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        Vector2 moveInput = new Vector2(inputX, inputZ);

        bool jumpPressed = Input.GetKeyDown(KeyCode.Space);

        Vector3 camForward = targetCamera.transform.forward;
        Vector3 camRight = targetCamera.transform.right;

        player.MoveRelative(moveInput, camForward, camRight, jumpPressed);

        Vector3 headPos = player.GetHeadPosition();
        targetCamera.transform.position = headPos;
    }
}
