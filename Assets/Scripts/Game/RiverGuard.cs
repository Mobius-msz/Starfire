using UnityEngine;

/// <summary>
/// 第一关的河面禁区与桥面高度基准。
///
/// AI 是靠直接改 transform.position 走路的，不吃物理碰撞，栏杆挡不住他们；
/// 这里给出一条"不下水"的硬约束：桥面通道以外不许踏进河面，
/// 桥上的地面高度直接锁死在桥板顶面，免得从板缝里掉到浮箱或河床上。
/// 第二关（赤水）不开，那里本来就要蹚水过河。
/// </summary>
public static class RiverGuard
{
    public static bool Active;

    public const float BridgeHalfWidth = 5.6f;
    public const float DeckY = 0.34f;
    public const float BridgeMinZ = 15.9f;
    public const float BridgeMaxZ = 44.3f;

    private const float RiverMinZ = 17.9f;
    private const float RiverMaxZ = 42.2f;

    public static bool OnBridge(Vector3 point)
    {
        return Active
               && Mathf.Abs(point.x) <= BridgeHalfWidth
               && point.z >= BridgeMinZ && point.z <= BridgeMaxZ;
    }

    public static bool InRiver(Vector3 point)
    {
        return Active && !OnBridge(point) && point.z > RiverMinZ && point.z < RiverMaxZ;
    }

    /// <summary>
    /// 把一帧的落点夹回合法区域：要么在桥面通道上，要么留在移动前所在的那一岸。
    /// </summary>
    public static Vector3 Clamp(Vector3 from, Vector3 to)
    {
        if (!Active) return to;
        if (OnBridge(to)) return to;
        if (to.z <= RiverMinZ || to.z >= RiverMaxZ) return to;

        // 人已经在河里（被挤下桥）：横向夹回桥面，别横穿江面瞬移
        if (from.z > RiverMinZ && from.z < RiverMaxZ)
        {
            to.x = Mathf.Clamp(to.x, -BridgeHalfWidth + 0.2f, BridgeHalfWidth - 0.2f);
            return to;
        }

        float middle = (RiverMinZ + RiverMaxZ) * 0.5f;
        to.z = from.z <= middle ? RiverMinZ - 0.05f : RiverMaxZ + 0.05f;
        return to;
    }
}
