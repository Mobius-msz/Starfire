using UnityEngine;

/// <summary>第一人称视角控制：鼠标转向。挂在主摄像机上。</summary>
public class MouseLook : MonoBehaviour
{
    [SerializeField] private float sensitivity = 2.2f;
    [SerializeField] private float minPitch = -85f;
    [SerializeField] private float maxPitch = 85f;

    private Transform body;
    private float pitch;
    private float recoilPitch;

    /// <summary>开火后坐：镜头被顶起来一点，然后自己回落。</summary>
    public void AddRecoil(float degrees)
    {
        recoilPitch = Mathf.Clamp(recoilPitch + degrees, 0f, 6f);
    }

    /// <summary>机瞄时由武器控制器降低，1 = 正常灵敏度。</summary>
    public float SensitivityMultiplier { get; set; } = 1f;

    /// <summary>探头时由 PlayerLean 注入的镜头侧倾角。</summary>
    public float LeanRoll { get; set; }

    /// <summary>机瞄时由武器控制器注入：头偏向右肩贴枪托。</summary>
    public float AimRoll { get; set; }

    /// <summary>结算 / 切回界面时清除侧倾，避免镜头一直歪着。</summary>
    public void ResetLeanRoll()
    {
        LeanRoll = 0f;
        AimRoll = 0f;
        recoilPitch = 0f;
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>演出用：直接转向指定朝向（水平角 + 俯仰）。</summary>
    public void SetLook(float yawDegrees, float pitchDegrees)
    {
        if (body == null) body = transform.root;

        pitch = Mathf.Clamp(pitchDegrees, minPitch, maxPitch);
        recoilPitch = 0f;
        LeanRoll = 0f;
        AimRoll = 0f;

        if (body != null) body.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    /// <summary>程序化生成玩家时注入身体（玩家根节点）。</summary>
    public void Initialize(Transform playerBody)
    {
        body = playerBody;
    }

    private void Start()
    {
        if (body == null) body = transform.root;
    }

    private void Update()
    {
        recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, 9f * Time.deltaTime);

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            // 点击游戏画面重新锁定鼠标
            if (Input.GetMouseButtonDown(0)) Cursor.lockState = CursorLockMode.Locked;
            return;
        }

        float scale = sensitivity * SensitivityMultiplier;
        float mouseX = Input.GetAxis("Mouse X") * scale;
        float mouseY = Input.GetAxis("Mouse Y") * scale;

        // pitch 越大越低头，后坐力要把镜头往天上顶一点
        pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
        transform.localRotation = Quaternion.Euler(pitch - recoilPitch, 0f, LeanRoll + AimRoll);

        if (body != null) body.Rotate(Vector3.up * mouseX, Space.Self);
    }
}
