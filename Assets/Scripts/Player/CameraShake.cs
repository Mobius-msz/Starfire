using UnityEngine;

/// <summary>
/// 受击镜头震动。只负责在 LateUpdate 里叠加一层瞬时偏移，
/// 基础位置仍由 PlayerMovement / CameraRig 决定，所以第一、第三人称都能用。
///
/// 震动强度用 trauma 表示（0~1），按 trauma² 衰减，前几帧最猛、尾巴干净，
/// 不会像线性衰减那样拖泥带水。
/// </summary>
[DisallowMultipleComponent]
public class CameraShake : MonoBehaviour
{
    [SerializeField] private float decay = 5.5f;
    [SerializeField] private float maxOffset = 0.075f;
    [SerializeField] private float maxAngle = 2.2f;

    private float trauma;
    private float seed;
    private Vector3 appliedOffset;

    private void Awake()
    {
        seed = Random.value * 50f;
    }

    public void Shake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + Mathf.Clamp(amount, 0.05f, 1f));
    }

    private void LateUpdate()
    {
        // 先把上一帧的偏移摘掉，避免叠加到基础镜头上、越抖越偏
        transform.localPosition -= appliedOffset;

        if (trauma <= 0.0001f)
        {
            appliedOffset = Vector3.zero;
            return;
        }

        trauma = Mathf.MoveTowards(trauma, 0f, decay * Time.deltaTime);

        float t = trauma * trauma;
        float time = Time.unscaledTime * 31f;

        appliedOffset = new Vector3(
            Mathf.PerlinNoise(seed, time) - 0.5f,
            Mathf.PerlinNoise(seed + 17.3f, time) - 0.5f,
            Mathf.PerlinNoise(seed + 41.7f, time) - 0.5f) * (maxOffset * 2f * t);

        transform.localPosition += appliedOffset;

        // 旋转抖动：MouseLook 每帧都会重写基准朝向，这里乘上偏移不会有累积
        float pitch = (Mathf.PerlinNoise(seed + 7.1f, time) - 0.5f) * maxAngle * 2f * t;
        float yaw = (Mathf.PerlinNoise(seed + 23.9f, time) - 0.5f) * maxAngle * 1.4f * t;
        float roll = (Mathf.PerlinNoise(seed + 61.3f, time) - 0.5f) * maxAngle * 2.4f * t;

        transform.localRotation *= Quaternion.Euler(pitch, yaw, roll);
    }
}
