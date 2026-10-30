using UnityEngine;

/// <summary>
/// 娄山关的暴风雪：现在整张地图都在风雪里，这一层只负责把关隘的风雪
/// 再压重一点（山口的雪比河谷更急）。具体雾值由 DynamicWeather 统一合成。
/// </summary>
public class ChishuiWeather : MonoBehaviour
{
    private Transform player;
    private float current;
    private float target;

    /// <summary>0 = 无风雪，1 = 山口暴风雪。DynamicWeather 读取它叠加雾效。</summary>
    public float StormLevel => current;

    public void Initialize(Transform playerRef)
    {
        player = playerRef;
    }

    private void Update()
    {
        if (player == null) return;

        // 全图暴风雪：不论走到哪儿都拉满，关隘附近再加一点
        target = player.position.z > 238f ? 1.2f : 1f;
        current = Mathf.MoveTowards(current, target, Time.deltaTime * 0.5f);
    }
}
