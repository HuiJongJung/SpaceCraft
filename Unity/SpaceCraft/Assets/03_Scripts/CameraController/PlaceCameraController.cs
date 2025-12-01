using UnityEngine;

public class PlaceCameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private RoomManager roomManager;

    [Header("Distance (Radius)")]
    private float distance = 8f;
    private float minDistance = 1f;
    private float maxDistance = 50f;
    private float zoomSpeed = 6f;

    [Header("Angles (deg)")]
    private float yaw = 0f;      // Y축 회전 (좌/우, A/D)
    private float pitch = 30f;   // X축 회전 (위/아래, W/S)
    private float yawSpeed = 90f;
    private float pitchSpeed = 90f;
    private float minPitch = 10f;
    private float maxPitch = 89f;
    
    private bool controlsEnabled = true;

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            Debug.LogError("PlaceCameraController: targetCamera 가 없습니다.", this);
            enabled = false;
            return;
        }

        if (roomManager == null)
        {
            roomManager = FindFirstObjectByType<RoomManager>(FindObjectsInactive.Include);
        }

        if (distance < minDistance)
        {
            distance = minDistance;
        }
        if (distance > maxDistance)
        {
            distance = maxDistance;
        }

        UpdateCameraTransform();
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }
        
        if (!controlsEnabled)
        {
            return;
        }

        float dt = Time.deltaTime;

        // --- 각도 입력 ---
        // W/S : pitch (위/아래)
        if (Input.GetKey(KeyCode.W))
        {
            pitch = pitch + pitchSpeed * dt;
        }
        if (Input.GetKey(KeyCode.S))
        {
            pitch = pitch - pitchSpeed * dt;
        }

        // pitch 클램프
        if (pitch < minPitch)
        {
            pitch = minPitch;
        }
        if (pitch > maxPitch)
        {
            pitch = maxPitch;
        }

        // A/D : yaw (좌/우)
        if (Input.GetKey(KeyCode.A))
        {
            yaw = yaw - yawSpeed * dt;
        }
        if (Input.GetKey(KeyCode.D))
        {
            yaw = yaw + yawSpeed * dt;
        }

        // --- 줌(거리) 입력 (마우스 휠) ---
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            distance -= scroll * zoomSpeed;
        }

        // 거리 클램프 (반지름 최소 0.1 이상)
        if (distance < minDistance)
        {
            distance = minDistance;
        }
        if (distance > maxDistance)
        {
            distance = maxDistance;
        }

        UpdateCameraTransform();
    }

    private Vector3 GetTargetPosition()
    {
        if (roomManager != null)
        {
            // RoomManager가 현재 방의 바닥 중심을 돌려주도록
            return roomManager.GetCurrentRoomCenterWorld();
        }

        // RoomManager 없으면 그냥 원점 기준
        return Vector3.zero;
    }
    
    public void ResetViewForRoom()
    {
        // Set Default Value
        pitch = 80f;   
        yaw   = 0f;    
        distance = 8f;

        // 현재 roomManager.currentRoomID 기준으로 위치 계산
        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        Transform camTransform = targetCamera.transform;
        Vector3 targetPos = GetTargetPosition();

        float yawRad = yaw * Mathf.Deg2Rad;
        float pitchRad = pitch * Mathf.Deg2Rad;

        float cosPitch = Mathf.Cos(pitchRad);
        float sinPitch = Mathf.Sin(pitchRad);
        float sinYaw = Mathf.Sin(yawRad);
        float cosYaw = Mathf.Cos(yawRad);

        // 구면 좌표계 -> 오프셋
        float x = distance * sinYaw * cosPitch;
        float y = distance * sinPitch;
        float z = distance * cosYaw * cosPitch;

        Vector3 offset = new Vector3(x, y, z);
        Vector3 camPos = targetPos + offset;

        camTransform.position = camPos;
        camTransform.LookAt(targetPos);
    }
    
    public void SetControlsEnabled(bool enabled)
    {
        controlsEnabled = enabled;
    }

    public void LockCameraControl()
    {
        controlsEnabled = false;
    }

    public void UnlockCameraControl()
    {
        controlsEnabled = true;
    }
}
