using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第二关赤水河的水面禁区。
///
/// 河道沿 z 轴延伸，河面是 |x| &lt; 13 的一条带。玩家可以踩着跳石、索桥、
/// 石堤和断桥横渡，敌军默认被挡在两岸隔河射击；但登记过的渡桥走廊
/// （目前是太平渡的木板索桥）敌军可以照着桥面走过去。
/// 队友沿着玩家走过的足迹过河，所以不禁行，只在踩空时兜住高度。
/// </summary>
public static class ChishuiWaterRule
{
    public static bool Active;

    /// <summary>水面半宽，与 ChishuiLevelBuilder 里的 RiverWater 尺寸一致。</summary>
    public const float WaterHalfWidth = 13f;

    /// <summary>河面高度：RiverWater 中心 -0.25、半高 0.04，顶面在 -0.21。</summary>
    public const float WaterSurfaceY = -0.21f;

    /// <summary>脚底沉到水面以下这么多，就算整个人没进水里。</summary>
    public const float SubmergeDepth = 0.9f;

    /// <summary>
    /// 岸线缓冲：敌军站在这一段边缘，脚踩泥滩，不进水面。
    /// 近岸 10.5~16 米是没入水下的斜坡，站在 13.6 处脚底还比水面低 0.3 米，
    /// 看上去就是半个人泡在河里，所以岸线要往后退到 15.2。
    /// </summary>
    public const float BankEdge = 15.2f;

    /// <summary>
    /// 出生线：近岸这一段是没入水下的斜坡，生成点必须再往外退，
    /// 否则守军会站在齐腰深的河床里。
    /// </summary>
    public const float SpawnBankEdge = WaterHalfWidth + 3f;

    private struct Corridor
    {
        public Vector3 Center;
        public float HalfX;
        public float HalfZ;
        public float DeckY;
    }

    private static readonly List<Corridor> corridors = new List<Corridor>();

    /// <summary>敌方可以通行的桥面。没有登记任何走廊时，敌军照旧不下水。</summary>
    public static void ClearCorridors()
    {
        corridors.Clear();
    }

    public static void AddBridgeCorridor(Vector3 center, float halfX, float halfZ, float deckY)
    {
        corridors.Add(new Corridor { Center = center, HalfX = halfX, HalfZ = halfZ, DeckY = deckY });
    }

    public static bool OnBridge(Vector3 point)
    {
        for (int i = 0; i < corridors.Count; i++)
        {
            Corridor c = corridors[i];
            if (Mathf.Abs(point.x - c.Center.x) <= c.HalfX
                && Mathf.Abs(point.z - c.Center.z) <= c.HalfZ)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>桥面高度：走廊里的桥板顶面是已知的，直接锁死，不靠射线打在栏杆或板缝里。</summary>
    public static bool TryDeck(Vector3 point, out float deckY)
    {
        for (int i = 0; i < corridors.Count; i++)
        {
            Corridor c = corridors[i];
            if (Mathf.Abs(point.x - c.Center.x) <= c.HalfX
                && Mathf.Abs(point.z - c.Center.z) <= c.HalfZ)
            {
                deckY = c.DeckY;
                return true;
            }
        }

        deckY = 0f;
        return false;
    }

    /// <summary>河面上方都算“过河路段”，桥上也一样：队友在这儿要收成一路纵队。</summary>
    public static bool InWater(Vector3 point)
    {
        return Active && Mathf.Abs(point.x) < WaterHalfWidth;
    }

    public static bool NearWater(Vector3 point)
    {
        return Active && Mathf.Abs(point.x) < BankEdge && !OnBridge(point);
    }

    /// <summary>整个人沉到水面下去了：脚底已经没到水面以下 0.9 米。</summary>
    public static bool FullySubmerged(Vector3 feet)
    {
        return Active
               && Mathf.Abs(feet.x) < WaterHalfWidth
               && feet.y < WaterSurfaceY - SubmergeDepth;
    }

    /// <summary>
    /// 把踩进近岸浅滩（水边到岸线之间那段没入水下的斜坡）的士兵推回干岸。
    /// 只在人已经低于水面、且还没离开河道范围时生效，桥上和水面上方都不动。
    /// </summary>
    public static Vector3 KeepFeetDry(Vector3 point)
    {
        if (!Active) return point;
        if (OnBridge(point)) return point;

        float ax = Mathf.Abs(point.x);
        if (ax < WaterHalfWidth || ax >= BankEdge) return point;
        if (point.y > WaterSurfaceY) return point;

        point.x = (point.x >= 0f ? 1f : -1f) * BankEdge;
        return point;
    }

    /// <summary>
    /// 一帧的落点越过岸线就夹回原来那一岸。
    /// 敌军是直接改 transform 走路的，不吃碰撞，这里给一条硬约束。
    /// 走在登记的桥面上时放行，只把 z 夹在两道栏杆之间。
    /// </summary>
    public static Vector3 Clamp(Vector3 from, Vector3 to)
    {
        if (!Active) return to;

        if (OnBridge(to) || OnBridge(from))
        {
            // 在桥上：栏杆挡住侧向，过河方向放行，掉到水里也不会被弹回岸边
            Corridor c = OnBridge(to) ? NearestCorridor(to) : NearestCorridor(from);
            to.z = Mathf.Clamp(to.z, c.Center.z - c.HalfZ, c.Center.z + c.HalfZ);
            return to;
        }

        if (Mathf.Abs(to.x) >= BankEdge) return to;

        float sign = Mathf.Abs(from.x) > 0.2f
            ? Mathf.Sign(from.x)
            : (to.x >= 0f ? 1f : -1f);

        to.x = sign * BankEdge;
        return to;
    }

    private static Corridor NearestCorridor(Vector3 point)
    {
        Corridor best = corridors[0];
        float bestGap = float.MaxValue;

        for (int i = 0; i < corridors.Count; i++)
        {
            Corridor c = corridors[i];
            float gap = Mathf.Abs(point.x - c.Center.x) + Mathf.Abs(point.z - c.Center.z);
            if (gap < bestGap)
            {
                bestGap = gap;
                best = c;
            }
        }
        return best;
    }

    /// <summary>生成点也夹到岸上，追兵不会凭空出现在河心。</summary>
    public static Vector3 ClampSpawn(Vector3 point)
    {
        if (!Active) return point;
        if (Mathf.Abs(point.x) >= SpawnBankEdge) return point;
        if (OnBridge(point)) return point;

        point.x = (point.x >= 0f ? 1f : -1f) * SpawnBankEdge;
        return point;
    }
}
