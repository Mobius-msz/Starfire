using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第二关的行军地图（IMGUI，按住 M 展开）。
///
/// 图画的是整条赤水河谷：河流、四个渡口、我军与侦察到的敌军。
/// 玩家用金色箭头表示，下一个渡口用脉动的方框标出，
/// 底部给出到目标的直线距离与相对朝向，照着跑就不会走反。
/// </summary>
public class TacticalMap : MonoBehaviour
{
    private const float RefWidth = 1600f;
    private const float RefHeight = 900f;

    // 地图投影范围（世界坐标）
    private const float WorldXMin = -48f;
    private const float WorldXMax = 48f;
    private const float WorldZMin = -16f;
    private const float WorldZMax = 320f;

    private static readonly Vector3[] Route =
    {
        new Vector3(26f, 0f, 0f),
        new Vector3(-20f, 0f, 30f),
        new Vector3(20f, 0f, 96f),
        new Vector3(-20f, 0f, 162f),
        new Vector3(20f, 0f, 228f),
        new Vector3(30f, 0f, 302f)
    };

    private static readonly string[] CrossingNames =
    {
        "一渡 · 土城 · 跳石",
        "二渡 · 太平渡 · 索桥",
        "三渡 · 二郎滩 · 石堤",
        "四渡 · 茅台 · 断桥",
        "娄山关 · 关顶"
    };

    private static readonly Color Parchment = new Color(0.86f, 0.79f, 0.62f);
    private static readonly Color Ink = new Color(0.14f, 0.12f, 0.1f);
    private static readonly Color RiverInk = new Color(0.3f, 0.45f, 0.48f);
    private static readonly Color Gold = new Color(0.85f, 0.61f, 0.18f);
    private static readonly Color RedInk = new Color(0.58f, 0.14f, 0.1f);
    private static readonly Color AllyInk = new Color(0.13f, 0.3f, 0.47f);

    private Transform player;
    private Vector3[] crossings;
    private ChishuiDirector director;

    private Font uiFont;
    private GUIStyle titleStyle;
    private GUIStyle hintStyle;
    private GUIStyle labelStyle;
    private GUIStyle smallStyle;
    private GUIStyle guideStyle;
    private bool stylesReady;

    private bool held;
    private bool forcedOpen;
    private float enemyRefresh;
    private readonly List<Vector3> enemies = new List<Vector3>();

    public void Initialize(Transform playerRef, Vector3[] points, ChishuiDirector directorRef)
    {
        player = playerRef;
        crossings = points;
        director = directorRef;
    }

    /// <summary>截图 / 测试用：强制展开地图。</summary>
    public void SetOpen(bool open)
    {
        forcedOpen = open;
    }

    public bool IsOpen => forcedOpen || held;

    private void Update()
    {
        held = Input.GetKey(KeyCode.M);
        if (!IsOpen) return;

        enemyRefresh -= Time.deltaTime;
        if (enemyRefresh > 0f) return;

        enemyRefresh = 0.4f;
        enemies.Clear();

        foreach (EnemyAI ai in FindObjectsOfType<EnemyAI>())
        {
            if (ai == null || ai.IsDead) continue;
            if (player != null &&
                (ai.transform.position - player.position).sqrMagnitude > 80f * 80f) continue;

            enemies.Add(ai.transform.position);
            if (enemies.Count >= 40) break;
        }
    }

    private void OnGUI()
    {
        if (!IsOpen) return;
        if (director == null || crossings == null || player == null) return;

        // 地图压在 HUD 和准星之上
        GUI.depth = -1000;

        EnsureStyles();

        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(
            Screen.width / RefWidth, Screen.height / RefHeight, 1f));

        DrawMap();

        GUI.matrix = previous;
        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (stylesReady) return;

        uiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "DengXian", "SimSun", "Arial" }, 22);

        titleStyle = MakeStyle(30, TextAnchor.MiddleCenter, new Color(0.24f, 0.18f, 0.1f));
        titleStyle.fontStyle = FontStyle.Bold;
        hintStyle = MakeStyle(17, TextAnchor.MiddleCenter, new Color(0.34f, 0.28f, 0.2f));
        labelStyle = MakeStyle(18, TextAnchor.MiddleLeft, Ink);
        smallStyle = MakeStyle(15, TextAnchor.MiddleLeft, new Color(0.3f, 0.25f, 0.18f));
        guideStyle = MakeStyle(21, TextAnchor.MiddleCenter, new Color(0.55f, 0.24f, 0.08f));
        guideStyle.fontStyle = FontStyle.Bold;

        stylesReady = true;
    }

    private GUIStyle MakeStyle(int size, TextAnchor anchor, Color color)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.font = uiFont;
        style.fontSize = size;
        style.alignment = anchor;
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.padding = new RectOffset(0, 0, 0, 0);
        style.margin = new RectOffset(0, 0, 0, 0);
        return style;
    }

    // ───────────────────────────── 绘制 ─────────────────────────────

    private static readonly Rect Panel = new Rect(468f, 22f, 664f, 856f);
    private static readonly Rect MapRect = new Rect(500f, 116f, 600f, 596f);

    private void DrawMap()
    {
        // 背后压暗，收起地图之前看不到战场
        GUI.color = new Color(0.02f, 0.02f, 0.025f, 0.45f);
        GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), Texture2D.whiteTexture);

        // 纸面
        FillRect(Panel, Parchment);
        StrokeRect(Panel, new Color(0.32f, 0.24f, 0.14f), 2f);

        FillRect(MapRect, new Color(0.8f, 0.73f, 0.55f));
        StrokeRect(MapRect, new Color(0.42f, 0.34f, 0.2f), 1.5f);

        GUI.Label(new Rect(Panel.x, Panel.y + 14f, Panel.width, 44f), "赤水河谷 · 行军图", titleStyle);
        GUI.Label(new Rect(Panel.x, Panel.y + 58f, Panel.width, 26f),
            "按住 M 收起　·　沿虚线到下一个渡口，别在开阔地恋战", hintStyle);

        DrawTerrain();
        DrawRoute();
        DrawCrossings();
        DrawAllies();
        DrawEnemies();
        DrawPlayerAndGuide();
        DrawCompass();
        DrawLegend();
        DrawFooter();

        GUI.color = Color.white;
    }

    /// <summary>河谷：中间一条河，两岸用横向晕线表示山地。</summary>
    private void DrawTerrain()
    {
        Vector2 left = ToMap(new Vector3(-13f, 0f, 0f));
        Vector2 right = ToMap(new Vector3(13f, 0f, 0f));

        Rect river = Rect.MinMaxRect(left.x, MapRect.y + 2f, right.x, MapRect.yMax - 2f);
        FillRect(river, RiverInk);

        // 水流细线
        for (int i = 0; i < 9; i++)
        {
            float t = (i + 0.5f) / 9f;
            float x = Mathf.Lerp(river.x + 6f, river.xMax - 6f, t);
            FillRect(new Rect(x, river.y, 1f, river.height), new Color(0.55f, 0.7f, 0.72f, 0.28f));
        }

        // 两岸山脊晕线
        for (int i = 0; i < 14; i++)
        {
            float z = Mathf.Lerp(WorldZMin + 8f, WorldZMax - 8f, i / 13f);
            float y = ToMap(new Vector3(0f, 0f, z)).y;

            FillRect(new Rect(MapRect.x + 4f, y, left.x - MapRect.x - 10f, 1f),
                new Color(0.45f, 0.36f, 0.22f, 0.22f));
            FillRect(new Rect(right.x + 6f, y, MapRect.xMax - right.x - 10f, 1f),
                new Color(0.45f, 0.36f, 0.22f, 0.22f));
        }

        // 娄山关一带的山体
        Vector2 passTop = ToMap(new Vector3(0f, 0f, 260f));
        Vector2 passTopFar = ToMap(new Vector3(0f, 0f, 306f));
        FillRect(Rect.MinMaxRect(MapRect.x + 10f, passTopFar.y, MapRect.xMax - 10f,
            passTop.y), new Color(0.55f, 0.44f, 0.28f, 0.35f));

        // 河名，竖排在河心
        Rect nameRect = new Rect(river.center.x - 13f, MapRect.y + 150f, 26f, 150f);
        GUI.Label(nameRect, "赤\n水\n河", smallStyle);
    }

    private void DrawRoute()
    {
        for (int i = 0; i < Route.Length - 1; i++)
        {
            Vector2 a = ToMap(Route[i]);
            Vector2 b = ToMap(Route[i + 1]);
            DrawDashed(a, b, 1.6f, new Color(0.42f, 0.3f, 0.16f, 0.75f));
        }
    }

    private void DrawCrossings()
    {
        int current = director.Segment;

        for (int i = 0; i < crossings.Length; i++)
        {
            Vector2 p = ToMap(crossings[i]);
            bool done = i < current;
            bool target = i == current;

            Color color = done
                ? new Color(0.4f, 0.36f, 0.28f, 0.65f)
                : (target ? Gold : new Color(0.32f, 0.24f, 0.14f, 0.9f));

            // 渡口横杆
            float half = (i < 4) ? 8f : 5f;
            FillRect(new Rect(p.x - half, p.y - 2f, half * 2f, 4f), color);

            if (target)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4.5f);
                StrokeRect(new Rect(p.x - 13f, p.y - 11f, 26f, 22f),
                    new Color(0.85f, 0.61f, 0.18f, pulse), 2.5f);
            }
            else if (done)
            {
                FillRect(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), new Color(0.36f, 0.42f, 0.26f, 0.9f));
            }

            // 渡口名：左右交替，避开路线
            bool east = crossings[i].x >= 0f;
            Rect label = east
                ? new Rect(p.x + 16f, p.y - 12f, 190f, 24f)
                : new Rect(p.x - 16f - 190f, p.y - 12f, 190f, 24f);

            GUIStyle style = target ? labelStyle : smallStyle;
            Color before = style.normal.textColor;
            if (target) style.normal.textColor = new Color(0.52f, 0.3f, 0.05f);
            GUI.Label(label, CrossingNames[i], style);
            style.normal.textColor = before;
        }
    }

    private void DrawAllies()
    {
        for (int i = 0; i < AllyAI.All.Count; i++)
        {
            AllyAI ally = AllyAI.All[i];
            if (ally == null || ally.IsDead) continue;

            Vector2 p = ToMap(ally.transform.position);
            FillRect(new Rect(p.x - 2.5f, p.y - 2.5f, 5f, 5f), AllyInk);
        }
    }

    private void DrawEnemies()
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            Vector2 p = ToMap(enemies[i]);
            FillRect(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), new Color(0.66f, 0.16f, 0.12f, 0.8f));
        }
    }

    private void DrawPlayerAndGuide()
    {
        Vector2 me = ToMap(player.position);
        Vector3 forward = player.forward;

        // 视野扇形（简化成两条短线）
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 dir = Quaternion.Euler(0f, 30f * s, 0f) * forward;
            DrawLine(me, me + MapDirection(dir) * 26f, 1.5f, new Color(0.85f, 0.68f, 0.28f, 0.5f));
        }

        // 金色菱形 + 朝向线
        Matrix4x4 before = GUI.matrix;
        RotateGui(45f, me);
        FillRect(new Rect(me.x - 6f, me.y - 6f, 12f, 12f), Gold);
        GUI.matrix = before;

        DrawLine(me, me + MapDirection(forward) * 18f, 3f, new Color(0.55f, 0.3f, 0.05f));

        // 到目标的引导箭头
        Vector3 target = director.CurrentObjective;
        Vector2 tp = ToMap(target);
        Vector3 toTarget = target - player.position;
        float distance = toTarget.magnitude;
        Vector2 dir2 = MapDirection(toTarget);

        DrawLine(me + dir2 * 14f, me + dir2 * 46f, 4f, new Color(0.72f, 0.24f, 0.08f, 0.9f));
        DrawChevron(me + dir2 * 46f, dir2, new Color(0.72f, 0.24f, 0.08f, 0.9f));

        // 目标距离线
        DrawLine(me, tp, 1f, new Color(0.72f, 0.24f, 0.08f, 0.25f));

        string side = BearingText(forward, toTarget);
        guideStyle.normal.textColor = new Color(0.55f, 0.24f, 0.08f);
        GUI.Label(new Rect(Panel.x + 20f, Panel.y + Panel.height - 96f, Panel.width - 40f, 30f),
            "下一目标　" + director.ObjectiveLabel, guideStyle);
        GUI.Label(new Rect(Panel.x + 20f, Panel.y + Panel.height - 64f, Panel.width - 40f, 26f),
            "直线 " + Mathf.RoundToInt(distance) + " 米　·　" + side, labelStyle);

        // 目标名直接压在地图上
        GUI.Label(new Rect(tp.x + 10f, tp.y + 8f, 160f, 22f),
            director.ObjectiveLabel, smallStyle);
    }

    private void DrawCompass()
    {
        float x = MapRect.xMax - 44f;
        float y = MapRect.y + 18f;

        DrawLine(new Vector2(x, y + 22f), new Vector2(x, y), 2f, Ink);
        DrawChevron(new Vector2(x, y), new Vector2(0f, -1f), Ink);
        GUI.Label(new Rect(x - 20f, y + 22f, 40f, 20f), "北", smallStyle);
    }

    private void DrawLegend()
    {
        float y = Panel.y + Panel.height - 152f;
        float x = Panel.x + 30f;

        FillRect(new Rect(x, y, 12f, 12f), Gold);
        GUI.Label(new Rect(x + 18f, y - 7f, 130f, 24f), "你", smallStyle);

        FillRect(new Rect(x + 70f, y, 12f, 12f), AllyInk);
        GUI.Label(new Rect(x + 88f, y - 7f, 150f, 24f), "战友 " + AllyAI.AliveCount, smallStyle);

        FillRect(new Rect(x + 210f, y, 12f, 12f), new Color(0.66f, 0.16f, 0.12f));
        GUI.Label(new Rect(x + 228f, y - 7f, 180f, 24f), "敌军（80 米内）", smallStyle);

        FillRect(new Rect(x + 420f, y, 12f, 12f), RiverInk);
        GUI.Label(new Rect(x + 438f, y - 7f, 120f, 24f), "河面", smallStyle);
    }

    private void DrawFooter()
    {
        int done = Mathf.Clamp(director.Segment, 0, 5);
        GUI.Label(new Rect(Panel.x + 20f, Panel.y + Panel.height - 30f, Panel.width - 40f, 24f),
            "已渡 " + done + " / 5　·　剩余 " +
            Mathf.FloorToInt(director.TimeLeft / 60f).ToString("00") + ":" +
            Mathf.FloorToInt(director.TimeLeft % 60f).ToString("00"),
            hintStyle);
    }

    // ───────────────────────────── 投影与绘图工具 ─────────────────────────────

    private static Vector2 ToMap(Vector3 world)
    {
        float u = Mathf.InverseLerp(WorldXMin, WorldXMax, world.x);
        float v = Mathf.InverseLerp(WorldZMin, WorldZMax, world.z);

        // 世界 +z（前进方向）画在地图上方
        return new Vector2(MapRect.x + u * MapRect.width,
            MapRect.yMax - v * MapRect.height);
    }

    /// <summary>世界方向投到地图上：横纵缩放不同，箭头也不能直接用世界角度。</summary>
    private static Vector2 MapDirection(Vector3 world)
    {
        float sx = MapRect.width / (WorldXMax - WorldXMin);
        float sy = MapRect.height / (WorldZMax - WorldZMin);

        Vector2 flat = new Vector2(world.x * sx, -world.z * sy);
        return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector2.up;
    }

    private static string BearingText(Vector3 forward, Vector3 toTarget)
    {
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        if (flat.sqrMagnitude < 0.01f) return "就在脚下";

        float signed = Vector3.SignedAngle(forward, flat, Vector3.up);
        float angle = Mathf.Abs(signed);

        if (angle < 12f) return "正前方";
        if (angle > 150f) return "身后，掉头";

        return (signed > 0f ? "右前方 " : "左前方 ") + Mathf.RoundToInt(angle) + "°";
    }

    private static void FillRect(Rect rect, Color color)
    {
        Color before = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = before;
    }

    private static void StrokeRect(Rect rect, Color color, float thickness)
    {
        FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 delta = b - a;
        float length = delta.magnitude;
        if (length < 0.01f) return;

        Matrix4x4 before = GUI.matrix;
        RotateGui(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);

        FillRect(new Rect(a.x, a.y - width * 0.5f, length, width), color);

        GUI.matrix = before;
    }

    /// <summary>
    /// GUIUtility.RotateAroundPivot 的轴心按屏幕像素解释，地图却是按参考分辨率画的；
    /// 直接用会把线甩到别处，这里在参考坐标系里自己拼旋转。
    /// </summary>
    private static void RotateGui(float degrees, Vector2 pivot)
    {
        GUI.matrix = GUI.matrix
                     * Matrix4x4.TRS(new Vector3(pivot.x, pivot.y, 0f),
                                     Quaternion.Euler(0f, 0f, degrees), Vector3.one)
                     * Matrix4x4.TRS(new Vector3(-pivot.x, -pivot.y, 0f),
                                     Quaternion.identity, Vector3.one);
    }

    private static void DrawDashed(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 delta = b - a;
        float length = delta.magnitude;
        if (length < 0.01f) return;

        Vector2 dir = delta / length;
        const float dash = 9f;
        const float gap = 6f;

        for (float t = 0f; t < length; t += dash + gap)
        {
            float end = Mathf.Min(t + dash, length);
            DrawLine(a + dir * t, a + dir * end, width, color);
        }
    }

    private static void DrawChevron(Vector2 tip, Vector2 dir, Color color)
    {
        Vector2 back = -dir * 9f;
        DrawLine(tip, tip + back + new Vector2(-dir.y, dir.x) * 6f, 3f, color);
        DrawLine(tip, tip + back - new Vector2(-dir.y, dir.x) * 6f, 3f, color);
    }
}
