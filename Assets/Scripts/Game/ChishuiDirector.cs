using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 第二关「四渡赤水」的游击战导演。
///
/// 每渡一次河就是一个限时目标：抵达对岸渡口信标即可转入下一渡。
/// 每段一开始只放少量守军，逼玩家边打边跑；
/// 剩余时间不足 20 秒时敌军追兵与炮火一起压上来，
/// 把"原地对枪"变成最亏的选择。
/// </summary>
public class ChishuiDirector : MonoBehaviour
{
    private const float SegmentDuration = 60f;
    private const int MaxAliveEnemies = 12;

    private static readonly string[] SegmentNames = { "一", "二", "三", "四", "五" };
    private static readonly string[] SegmentPlaces = { "土城", "太平渡", "二郎滩", "茅台", "娄山关" };

    private Transform player;
    private Transform levelRoot;
    private Vector3[] crossings;
    private Transform[] guardSpawns;
    private int[] guardSegments;
    private AudioClip shotClip;
    private HUDController hud;
    private ArtillerySystem artillery;
    private Transform beacon;
    private Action onComplete;

    private int segment;
    private float timer;
    private float siegeTimer;
    private bool besieged;
    private bool active;

    /// <summary>当前第几渡（0 = 一渡），供地图指引使用。</summary>
    public int Segment => segment;
    public float TimeLeft => Mathf.Max(0f, timer);
    public bool IsActive => active;

    /// <summary>当前要去的渡口（到达后会自动换成下一个）。</summary>
    public Vector3 CurrentObjective
    {
        get
        {
            if (crossings == null || crossings.Length == 0) return Vector3.zero;
            return crossings[Mathf.Clamp(segment, 0, crossings.Length - 1)];
        }
    }

    public string ObjectiveLabel
    {
        get
        {
            switch (segment)
            {
                case 0: return "一渡 · 土城青杠坡";
                case 1: return "二渡 · 太平渡";
                case 2: return "三渡 · 二郎滩";
                case 3: return "四渡 · 茅台镇";
                default: return "娄山关关顶";
            }
        }
    }


    public void Initialize(Transform playerRef, Transform root, Vector3[] points,
                           Transform[] guards, int[] segments, Transform beaconRef,
                           AudioClip shot, HUDController hudRef, ArtillerySystem artilleryRef,
                           Action completeCallback)
    {
        player = playerRef;
        levelRoot = root;
        crossings = points;
        guardSpawns = guards;
        guardSegments = segments;
        beacon = beaconRef;
        shotClip = shot;
        hud = hudRef;
        artillery = artilleryRef;
        onComplete = completeCallback;
    }

    public void Begin()
    {
        active = true;
        segment = 0;
        timer = SegmentDuration;
        siegeTimer = 0f;
        besieged = false;

        MoveBeacon();
        ApplySegment();
        SpawnSegmentGuards(segment);

        hud.ShowMessage(
            "1935 年 · 赤水河谷\n土城 → 太平渡 → 二郎滩 → 茅台 → 娄山关。按住 M 展开行军地图，打完就走，不要恋战。", 7f);
    }

    public void Stop()
    {
        active = false;
    }

    /// <summary>截图 / 调试用：不跑前几渡，直接跳到指定渡口并放上该段守军。</summary>
    public void JumpToSegment(int index)
    {
        if (!active || crossings == null || crossings.Length == 0) return;

        segment = Mathf.Clamp(index, 0, crossings.Length - 1);
        timer = SegmentDuration;
        besieged = false;

        MoveBeacon();
        ApplySegment();
        SpawnSegmentGuards(segment);
    }

    public void Tick()
    {
        if (!active || player == null) return;

        timer -= Time.deltaTime;

        // 关顶在山上，必须把高度也算进去，否则走到崖底就会误判
        float toBeacon = (crossings[segment] - player.position).magnitude;

        if (toBeacon < 6f)
        {
            Advance();
            return;
        }

        if (!besieged && timer < 20f)
        {
            besieged = true;
            siegeTimer = 0f;
            hud.ShowMessage("追兵咬上来了 · 炮火覆盖", 3.5f);
            if (artillery != null) artillery.FireVolley(3);
            SpawnPursuers(3);
        }

        if (besieged)
        {
            siegeTimer -= Time.deltaTime;
            if (siegeTimer <= 0f)
            {
                siegeTimer = 7f;
                if (artillery != null) artillery.FireVolley(1);
                SpawnPursuers(2);
            }
        }

        // 时间耗尽不会直接判负，但压力会一直挂到渡河为止
        float shown = Mathf.Max(0f, timer);
        string timerLabel = segment >= 4
            ? "娄山关"
            : "第 " + SegmentNames[segment] + " 渡 · " + SegmentPlaces[segment];
        hud.SetTimerText(timerLabel + "　剩余 " +
                         Mathf.FloorToInt(shown / 60f).ToString("00") + ":" +
                         Mathf.FloorToInt(shown % 60f).ToString("00"));
    }

    private void Advance()
    {
        segment++;

        if (segment >= crossings.Length)
        {
            active = false;
            if (hud != null) hud.ClearWaypoint();
            if (onComplete != null) onComplete();
            return;
        }

        timer = SegmentDuration;
        besieged = false;

        MoveBeacon();
        ApplySegment();
        SpawnSegmentGuards(segment);

        if (segment >= 4)
        {
            hud.ShowMessage("娄山关 · 手脚并用冲上关顶，抢在追兵前面占住制高点", 5f);
        }
        else
        {
            hud.ShowMessage("第" + SegmentNames[segment] + "渡赤水 · " +
                            SegmentPlaces[segment] + " · 立刻转移", 4f);
        }
    }

    private void ApplySegment()
    {
        switch (segment)
        {
            case 0:
                hud.SetObjective("任务：一渡赤水 · 土城青杠坡突围");
                break;
            case 1:
                hud.SetObjective("任务：二渡赤水 · 太平渡声东击西");
                break;
            case 2:
                hud.SetObjective("任务：三渡赤水 · 二郎滩抢渡");
                break;
            case 3:
                hud.SetObjective("任务：四渡赤水 · 茅台镇穿插");
                break;
            default:
                hud.SetObjective("任务：抢占娄山关 · 冲上关顶");
                break;
        }

        // 屏幕上给个指路标：渡口在哪个方向、还有多远，跑起来不用来回翻地图
        if (hud != null && crossings != null && segment < crossings.Length)
            hud.SetWaypoint(crossings[segment], segment >= 4 ? "娄山关关顶" : "渡口");
    }

    private void MoveBeacon()
    {
        if (beacon != null)
        {
            beacon.position = crossings[segment] + Vector3.up * 0.1f;
        }
    }

    private void SpawnSegmentGuards(int seg)
    {
        if (guardSpawns == null || player == null) return;

        for (int i = 0; i < guardSpawns.Length; i++)
        {
            if (guardSegments[i] != seg) continue;

            Vector3 position = guardSpawns[i].position
                               + new Vector3(UnityEngine.Random.Range(-1.4f, 1.4f), 0f,
                                             UnityEngine.Random.Range(-1.4f, 1.4f));

            position = GroundedSpawn(position);
            EnemySpawner.SpawnAt(levelRoot, position, player, shotClip);
        }
    }

    /// <summary>
    /// 生成点贴地：岸上直接用地形（含娄山关石阶）的高度，不去打射线——
    /// 向下的射线常常先撞上松树冠、竹竿顶，守军就会站在几米高的空中。
    /// 只有落在桥廊里时才用射线取桥板高度。
    /// </summary>
    private static Vector3 GroundedSpawn(Vector3 position)
    {
        position = ChishuiWaterRule.ClampSpawn(position);

        if (ChishuiWaterRule.OnBridge(position))
        {
            if (Physics.Raycast(position + Vector3.up * 30f, Vector3.down,
                                out RaycastHit deckHit, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                position.y = deckHit.point.y + 0.02f;
            }
            return position;
        }

        position.y = ChishuiLevelBuilder.WalkHeight(position.x, position.z) + 0.02f;
        return position;
    }

    /// <summary>从玩家背后放追兵。找不到地面就沿着玩家当前高度放，由敌人自己贴地。</summary>
    private void SpawnPursuers(int count)
    {
        if (player == null) return;
        if (CountAlive() >= MaxAliveEnemies) return;

        for (int i = 0; i < count; i++)
        {
            // 从远处（30 米开外）追上来，不在玩家眼皮底下凭空出现
            float distance = UnityEngine.Random.Range(30f, 44f);
            float side = UnityEngine.Random.Range(-12f, 12f);

            Vector3 position = player.position
                               - player.forward * distance
                               + player.right * side;

            // 玩家可能正踩在跳石上，追兵不能跟着落进河心
            position = GroundedSpawn(position);
            EnemySpawner.SpawnAt(levelRoot, position, player, shotClip);
        }
    }

    private int CountAlive()
    {
        int count = 0;
        foreach (EnemyAI ai in FindObjectsOfType<EnemyAI>())
        {
            if (ai != null && !ai.IsDead) count++;
        }
        return count;
    }
}
