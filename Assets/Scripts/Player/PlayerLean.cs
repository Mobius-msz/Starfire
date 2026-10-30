using UnityEngine;

/// <summary>
/// 掩体探头：按住 Q 向左探身，按住 E 向右探身。
///
/// 镜头会横向偏移并轻微侧倾，射线仍从摄像机发出，
/// 因此探头后可以露出身体射击。侧向有障碍时自动收紧，
/// 避免镜头穿进沙袋或墙体。卧姿不可探头。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerLean : MonoBehaviour
{
    [Header("探头")]
    [SerializeField] private float maxLeanOffset = 0.34f;
    [SerializeField] private float maxLeanRoll = 5.5f;
    [SerializeField] private float leanSpeed = 7f;
    [SerializeField] private KeyCode leanLeftKey = KeyCode.Q;
    [SerializeField] private KeyCode leanRightKey = KeyCode.E;

    [Header("防穿模")]
    [SerializeField] private float obstacleRadius = 0.2f;

    public float LeanAmount => leanCurrent;

    private Transform cameraTransform;
    private Transform playerRoot;
    private MouseLook mouseLook;
    private PlayerMovement movement;

    private float leanCurrent;

    private void Awake()
    {
        playerRoot = transform;
        movement = GetComponent<PlayerMovement>();

        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null)
        {
            cameraTransform = cam.transform;
            mouseLook = cam.GetComponent<MouseLook>();
        }
    }

    private void LateUpdate()
    {
        if (cameraTransform == null) return;

        float input = 0f;
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            if (Input.GetKey(leanLeftKey)) input -= 1f;
            if (Input.GetKey(leanRightKey)) input += 1f;
        }

        if (movement != null && movement.CurrentStance == PlayerMovement.Stance.Prone)
        {
            input = 0f;
        }

        leanCurrent = Mathf.MoveTowards(leanCurrent, input, leanSpeed * Time.deltaTime);

        float offset = leanCurrent * maxLeanOffset;

        if (Mathf.Abs(offset) > 0.001f)
        {
            offset = ClampToObstacle(offset);
        }

        Vector3 localPos = cameraTransform.localPosition;
        localPos.x = offset;
        cameraTransform.localPosition = localPos;

        if (mouseLook != null) mouseLook.LeanRoll = -leanCurrent * maxLeanRoll;
    }

    /// <summary>向探头方向做球形检测，返回不穿进障碍的最大横向偏移。</summary>
    private float ClampToObstacle(float offset)
    {
        Vector3 origin = playerRoot.position
                         + Vector3.up * cameraTransform.localPosition.y;
        Vector3 direction = cameraTransform.right * Mathf.Sign(offset);

        float allowed = Mathf.Abs(offset);
        RaycastHit[] hits = Physics.SphereCastAll(
            origin, obstacleRadius, direction, allowed + obstacleRadius,
            ~0, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform == playerRoot) continue;
            if (hit.collider.transform.IsChildOf(playerRoot)) continue;

            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.02f));
        }

        return Mathf.Sign(offset) * allowed;
    }

    /// <summary>标题 / 结算界面时停用探头并复位镜头。</summary>
    public void SetActiveControl(bool value)
    {
        enabled = value;

        if (value) return;

        leanCurrent = 0f;

        if (cameraTransform != null)
        {
            Vector3 localPos = cameraTransform.localPosition;
            localPos.x = 0f;
            cameraTransform.localPosition = localPos;
        }

        if (mouseLook != null) mouseLook.ResetLeanRoll();
    }
}
