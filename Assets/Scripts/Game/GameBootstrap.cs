using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏总控：程序化搭建场景、玩家、HUD、音效，并驱动关卡流程。
///
/// 流程：标题 → 坚守渡口（180 秒）→ 断后炸桥 → 胜利 / 阵亡 → 重开。
/// 所有内容运行时生成，不依赖任何美术、音频或 Prefab 资源。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    private enum GameState { Title, Briefing, Playing, Bridge, Guerrilla, Won, Lost }

    /// <summary>跨场景保留所选关卡：1 血战湘江，2 四渡赤水。</summary>
    private static int SelectedLevel = 1;
    private static bool pendingAutoStart;

    private struct Clips
    {
        public AudioClip gunshot;
        public AudioClip bolt;
        public AudioClip empty;
        public AudioClip reload;
        public AudioClip swing;
        public AudioClip wind;

        // 命中 / 击杀反馈
        public AudioClip hitTick;
        public AudioClip headshotTick;
        public AudioClip killConfirm;
        public AudioClip meleeHit;
        public AudioClip explosion;
        public AudioClip shellWhistle;
        public AudioClip pistolShot;
        public AudioClip heavySwing;
        public AudioClip execution;
        public AudioClip torchFire;
    }

    [SerializeField] private float defenseDuration = 180f;
    private const float HoldDuration = 3f;
    private const float BridgeInteractDistance = 4.5f;

    private GameState state = GameState.Title;

    // ESC 暂停菜单
    private bool paused;
    private RenderTexture pauseBackdrop;
    private static bool skipBriefingOnce;

    private Transform player;
    private Health playerHealth;
    private Hanyang88Rifle rifle;
    private Pistol pistol;
    private ColdSystem cold;
    private FaithSystem faith;
    private HUDController hud;
    private EnemySpawner spawner;
    private MouseLook mouseLook;
    private PlayerMovement movement;
    private PlayerLean lean;
    private PlayerMelee melee;
    private WeaponController weapon;
    private GrenadeSystem grenades;
    private TorchSystem torch;
    private CameraRig cameraRig;
    private CameraShake cameraShake;
    private PlayerShadowBody playerBody;
    private ArtillerySystem artillery;
    private ArtilleryBattery battery;
    private ChishuiDirector chishui;
    private TacticalMap tacticalMap;
    private CrossingColumn column;
    private Vector3 playerSpawn = new Vector3(0f, 0.1f, 6f);
    private float playerYaw;
    private bool isChishui;
    private Transform levelRoot;
    private Transform bridgePoint;
    private Transform bridgeRoot;
    private bool detonating;
    private float detonateTimer;
    private bool photoCapture;
    private AudioSource feedbackAudio;

    private Clips clips;
    private float defenseTimer;
    private float holdProgress;
    private int enemyKills;
    private float levelStartTime;
    private bool bugleBlown;
    private float killHealUntil;
    private const float BugleDelay = 90f;
    private const float RallyDuration = 15f;

    // ───────────────────────────── 启动 ─────────────────────────────

    private void Awake()
    {
        Time.timeScale = 1f;
        SelectedLevel = ParseLevelArgument();

        Health.AnyDied += HandleAnyDied;
        Health.AnyDamaged += HandleAnyDamaged;
        Health.AnyKilled += HandleAnyKilled;

        BuildLighting();
        BuildWorld();
        BuildHUD();
        BuildPlayer();
        BuildAmbience();
        BuildSpawner();
        BuildAllies();
        AllyAI.SetActive(false);

        hud.SetSelectedLevel(SelectedLevel);
        hud.SetHudVisible(false);
        hud.ShowTitlePanel();
        SetPlayerControl(false);

        string[] weatherArgs = System.Environment.GetCommandLineArgs();
        int forcedWeather = System.Array.IndexOf(weatherArgs, "-fog") >= 0 ? 1
            : (System.Array.IndexOf(weatherArgs, "-rain") >= 0 ? 2 : -1);
        float forcedPhase = -1f;
        if (System.Array.IndexOf(weatherArgs, "-night") >= 0) forcedPhase = 0.86f;
        int timeIndex = System.Array.IndexOf(weatherArgs, "-daytime");
        if (timeIndex >= 0 && timeIndex + 1 < weatherArgs.Length
            && float.TryParse(weatherArgs[timeIndex + 1], out float parsedPhase))
        {
            forcedPhase = parsedPhase;
        }

        DynamicWeather.Attach(player, hud, SelectedLevel == 2 ? 0.0085f : 0.0058f,
            forcedWeather, forcedPhase);

        Debug.Log("BOOT_OK level=" + SelectedLevel);
    }

    /// <summary>截图 / 测试用：命令行带 -level2 时直接进第二关。</summary>
    private static int ParseLevelArgument()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-level2") >= 0) return 2;
        if (System.Array.IndexOf(args, "-level1") >= 0) return 1;
        return SelectedLevel;
    }

    private void Start()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        PhotoRequest photo;
        if (TryParsePhoto(args, out photo))
        {
            StartCoroutine(PhotoRoutine(photo));
            return;
        }

        bool autoStart = System.Array.IndexOf(args, "-autostart") >= 0;
        bool autoQuit = System.Array.IndexOf(args, "-autoquit") >= 0;
        bool audit = System.Array.IndexOf(args, "-audit") >= 0;

        // 开发自检：-audit 每隔几秒扫描全场 NPC，记录离地高度与位移
        if (audit)
        {
            StartCoroutine(AuditRoutine(autoQuit));
        }

        if (pendingAutoStart)
        {
            pendingAutoStart = false;
            if (skipBriefingOnce)
            {
                skipBriefingOnce = false;
                StartGame();
            }
            else
            {
                ShowBriefing();
            }
            return;
        }

        if (autoStart) StartCoroutine(AutoStartRoutine(autoQuit && !audit));
    }

    // ───────────────────────────── 战斗自检 ─────────────────────────────
    // -audit：等战斗开始后，每隔五秒量一遍每个 NPC 的脚底离地高度，
    // 顺便记下位移。浮空、陷地、一枪不发也不动的人都会写进日志。

    private IEnumerator AuditRoutine(bool autoQuit)
    {
        float waited = 0f;
        while (state != GameState.Playing && state != GameState.Bridge
               && state != GameState.Guerrilla && waited < 20f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(3f);

        EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
        AllyAI[] allies = FindObjectsOfType<AllyAI>();
        Vector3[] first = new Vector3[enemies.Length + allies.Length];
        for (int i = 0; i < enemies.Length; i++)
            first[i] = enemies[i] != null ? enemies[i].transform.position : Vector3.zero;
        for (int i = 0; i < allies.Length; i++)
            first[enemies.Length + i] = allies[i] != null ? allies[i].transform.position : Vector3.zero;

        for (int pass = 0; pass < 5; pass++)
        {
            int floating = 0;
            int sunken = 0;
            int alive = 0;
            int allyCover = 0;
            int allyHiding = 0;
            int allyPeeking = 0;
            int allyHides = 0;
            System.Text.StringBuilder detail = new System.Text.StringBuilder();

            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyAI ai = enemies[i];
                if (ai == null || ai.IsDead) continue;
                alive++;
                detail.Append(CheckOne("enemy_" + i, ai.transform));
                float hover = GroundHover(ai.transform.position);
                if (hover > 0.45f) floating++;
                else if (hover < -0.45f) sunken++;
            }

            for (int i = 0; i < allies.Length; i++)
            {
                AllyAI ally = allies[i];
                if (ally == null || ally.IsDead) continue;
                alive++;
                detail.Append(CheckOne("ally_" + i, ally.transform));
                if (ally.InCover) allyCover++;
                if (ally.Hiding) allyHiding++;
                if (ally.Peeking) allyPeeking++;
                allyHides += ally.HideCount;
                float hover = GroundHover(ally.transform.position);
                if (hover > 0.45f) floating++;
                else if (hover < -0.45f) sunken++;
            }

            Debug.Log("AUDIT pass=" + pass + " alive=" + alive
                + " player=" + (player != null
                    ? player.position.x.ToString("0.0") + "," + player.position.z.ToString("0.0")
                    : "-")
                + " floating=" + floating + " sunken=" + sunken
                + " cover=" + allyCover + " hiding=" + allyHiding + " peeking=" + allyPeeking
                + " hides=" + allyHides
                + detail);

            if (pass < 4) yield return new WaitForSeconds(5f);
        }

        // 位移：三秒内挪动不足 0.2 米的人，多半是卡住了
        System.Text.StringBuilder moved = new System.Text.StringBuilder("AUDIT_MOVE");
        int stuck = 0;
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null || enemies[i].IsDead) continue;
            float d = Vector3.Distance(first[i], enemies[i].transform.position);
            if (d < 0.2f) stuck++;
            moved.Append(" e" + i + "=" + d.ToString("0.00"));
        }
        for (int i = 0; i < allies.Length; i++)
        {
            if (allies[i] == null || allies[i].IsDead) continue;
            float d = Vector3.Distance(first[enemies.Length + i], allies[i].transform.position);
            if (d < 0.2f) stuck++;
            moved.Append(" a" + i + "=" + d.ToString("0.00"));
        }
        moved.Append(" stuck=" + stuck);
        Debug.Log(moved.ToString());

        if (autoQuit)
        {
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }

    private string CheckOne(string name, Transform t)
    {
        Vector3 feet = t.position;
        float hover = GroundHover(feet);
        if (hover <= 0.45f && hover >= -0.45f) return string.Empty;

        string hitName = "none";
        float hitY = 0f;
        RaycastHit[] hits = Physics.RaycastAll(feet + Vector3.up * 0.3f, Vector3.down, 6f,
            ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null || c.GetComponentInParent<Health>() != null) continue;
            if (hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            hitName = c.name;
            hitY = hits[i].point.y;
        }

        // 模型脚底相对根节点的位置：-0.7 说明根在腰上，0 说明根就是脚底
        float meshMin = float.MaxValue;
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (renderers[i] is ParticleSystemRenderer) continue;
            meshMin = Mathf.Min(meshMin, renderers[i].bounds.min.y);
        }
        float meshOffset = meshMin == float.MaxValue ? 0f : meshMin - feet.y;

        string walk = isChishui
            ? " walk=" + (feet.y - ChishuiLevelBuilder.WalkHeight(feet.x, feet.z)).ToString("0.00")
            : string.Empty;

        return "\n  " + name + " pos=(" + feet.x.ToString("0.0") + ","
            + feet.y.ToString("0.0") + "," + feet.z.ToString("0.0")
            + ") hover=" + hover.ToString("0.00")
            + " hit=" + hitName + "@" + hitY.ToString("0.00")
            + " mesh=" + meshOffset.ToString("0.00") + walk;
    }

    /// <summary>脚底离地高度：从脚踝高度往下量，不会把胸高的沙袋顶当成地面。</summary>
    private static float GroundHover(Vector3 feet)
    {
        const float probe = 0.3f;
        RaycastHit[] hits = Physics.RaycastAll(feet + Vector3.up * probe, Vector3.down, 6f,
            ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<Health>() != null) continue;
            if (hits[i].distance < nearest) nearest = hits[i].distance;
        }

        if (nearest == float.MaxValue) return 99f;
        return nearest - probe;
    }

    // ───────────────────────────── 拍照模式 ─────────────────────────────
    // 仅用于开发截图：-photo x,y,z,yaw,pitch[,fov] [-warmup 秒] [-knife] [-swing] [-nohud]
    // 摆好姿势后冻结输入，画面完全由命令行决定，便于逐项检查地形与模型。

    private struct PhotoRequest
    {
        public Vector3 position;
        public float yaw;
        public float pitch;
        public float fov;
        public float warmup;
        public bool noHud;
        public bool knife;
        public bool swing;
        public bool heavy;
        public float swingWait;
        public bool allyCrouch;
        public bool allyProne;
        public float allyStanceWait;
        public bool warmupAtPose;
        public float hp;
        public bool victory;
        public float victoryWait;
        public bool factions;
        public bool muzzle;
        public bool aim;
        public bool sprintPose;
        public bool bolt;
        public float boltWait;
        public bool reload;
        public float stamina;
        public bool map;
        public bool demolish;
        public float demolishWait;
        public bool crouch;
        public bool prone;
        public float proneFrame;
        public int switchTo;
        public bool hasSwitch;
        public float switchFrame;
        public int cameraIndex;
        public bool hasCameraIndex;
        public bool thirdPerson;
        public bool figure;
        public bool figureSide;
        public bool bridgePhase;
        public bool pistol;
        public bool pistolReload;
        public float pistolReloadWait;
        public bool pause;
        public float walkPose;
        public bool torchLine;
        public bool torch;
        public bool bugle;
        public bool grenade;
        public float grenadeWait;
        public bool drown;
        public bool brawl;
        public bool execute;
        public int segment;
        public bool hasSegment;
        public bool killFeed;
        public bool blood;
    }

    private static bool TryParsePhoto(string[] args, out PhotoRequest request)
    {
        request = default(PhotoRequest);

        int index = System.Array.IndexOf(args, "-photo");
        if (index < 0 || index + 1 >= args.Length) return false;

        string[] parts = args[index + 1].Split(',');
        if (parts.Length < 5) return false;

        try
        {
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            request.position = new Vector3(
                float.Parse(parts[0], ci), float.Parse(parts[1], ci), float.Parse(parts[2], ci));
            request.yaw = float.Parse(parts[3], ci);
            request.pitch = float.Parse(parts[4], ci);
            request.fov = parts.Length > 5 ? float.Parse(parts[5], ci) : 0f;

            int warmup = System.Array.IndexOf(args, "-warmup");
            if (warmup >= 0 && warmup + 1 < args.Length)
            {
                request.warmup = float.Parse(args[warmup + 1], ci);
            }
        }
        catch (System.Exception)
        {
            return false;
        }

        request.noHud = System.Array.IndexOf(args, "-nohud") >= 0;
        request.knife = System.Array.IndexOf(args, "-knife") >= 0;
        request.swing = System.Array.IndexOf(args, "-swing") >= 0;
        // 截图用：右键重刀（直刺），配合 -swingwait N 停在动作的某个时刻
        request.heavy = System.Array.IndexOf(args, "-heavy") >= 0;
        int swingWait = System.Array.IndexOf(args, "-swingwait");
        if (swingWait >= 0 && swingWait + 1 < args.Length)
        {
            request.swingWait = float.Parse(args[swingWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        // 截图用：把战友强行摆成蹲姿 / 趴姿，检查动作链条
        request.allyCrouch = System.Array.IndexOf(args, "-allycrouch") >= 0;
        request.allyProne = System.Array.IndexOf(args, "-allyprone") >= 0;
        int allyWait = System.Array.IndexOf(args, "-allywait");
        if (allyWait >= 0 && allyWait + 1 < args.Length)
        {
            request.allyStanceWait = float.Parse(args[allyWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        // 先站到机位再快进：拍敌军行为时，敌人会朝镜头方向压上来
        request.warmupAtPose = System.Array.IndexOf(args, "-warmupat") >= 0;
        // 截图用：在镜头前排一列三种敌军，检查外观差异
        request.factions = System.Array.IndexOf(args, "-factions") >= 0;
        // 截图用：点亮枪口火光并保持，检查火光大小与位置
        request.muzzle = System.Array.IndexOf(args, "-muzzle") >= 0;
        // 截图用：强制机瞄，检查举枪与双手位置
        request.aim = System.Array.IndexOf(args, "-aim") >= 0;
        // 截图用：强制冲刺收枪
        request.sprintPose = System.Array.IndexOf(args, "-sprintpose") >= 0;
        // 截图用：打一枪后停在拉栓中途，检查枪机动作
        request.bolt = System.Array.IndexOf(args, "-bolt") >= 0;
        int boltWait = System.Array.IndexOf(args, "-boltwait");
        if (boltWait >= 0 && boltWait + 1 < args.Length)
        {
            request.boltWait = float.Parse(args[boltWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        // 截图用：补几发备弹后开始装填，检查装填动作
        request.reload = System.Array.IndexOf(args, "-reload") >= 0;
        // 截图用：展开行军地图
        request.map = System.Array.IndexOf(args, "-map") >= 0;
        // 截图用：引爆浮桥，等若干秒后开拍（看火球与桥体下沉）
        request.demolish = System.Array.IndexOf(args, "-demolish") >= 0;
        // 截图用：强行摆成蹲姿 / 卧姿，检查身体动作
        request.crouch = System.Array.IndexOf(args, "-crouch") >= 0;
        request.prone = System.Array.IndexOf(args, "-prone") >= 0;
        // 截图用：卧倒动作进行到第 N 秒时定住，检查动作链条的中间帧
        int proneFrame = System.Array.IndexOf(args, "-proneframe");
        if (proneFrame >= 0 && proneFrame + 1 < args.Length)
        {
            request.proneFrame = float.Parse(args[proneFrame + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        // 截图用：切到指定武器（0 步枪 / 1 驳壳枪 / 2 大刀），可停在切换中途
        int switchTo = System.Array.IndexOf(args, "-switch");
        if (switchTo >= 0 && switchTo + 1 < args.Length)
        {
            int.TryParse(args[switchTo + 1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out request.switchTo);
            request.hasSwitch = true;
        }
        int switchFrame = System.Array.IndexOf(args, "-switchframe");
        if (switchFrame >= 0 && switchFrame + 1 < args.Length)
        {
            request.switchFrame = float.Parse(args[switchFrame + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        request.thirdPerson = System.Array.IndexOf(args, "-third") >= 0;

        // 截图用：第三人称镜头距离档位（0 近 / 1 中 / 2 远）
        int cam = System.Array.IndexOf(args, "-cam");
        if (cam >= 0 && cam + 1 < args.Length)
        {
            request.hasCameraIndex = int.TryParse(args[cam + 1],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out request.cameraIndex);
        }
        // 截图用：镜头绕到主角正前方，检查人物模型细节
        request.figure = System.Array.IndexOf(args, "-figure") >= 0;
        request.figureSide = System.Array.IndexOf(args, "-figureside") >= 0;
        // 截图用：直接跳到第一关断桥阶段，检查桥头指引
        request.bridgePhase = System.Array.IndexOf(args, "-bridge") >= 0;
        // 截图用：切到副武器驳壳枪 / 打开 ESC 暂停菜单
        request.pistol = System.Array.IndexOf(args, "-pistol") >= 0;
        // 截图用：手枪换弹到一半定住（可指定秒数），检查换弹动作与弹匣位置
        request.pistolReload = System.Array.IndexOf(args, "-pistolreload") >= 0;
        int pistolReloadWait = System.Array.IndexOf(args, "-pistolreloadwait");
        if (pistolReloadWait >= 0 && pistolReloadWait + 1 < args.Length)
        {
            request.pistolReloadWait = float.Parse(args[pistolReloadWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        request.pause = System.Array.IndexOf(args, "-pause") >= 0;
        // 截图用：在镜头前喷一蓬血雾，检查粒子血迹的形态与落地
        request.blood = System.Array.IndexOf(args, "-blood") >= 0;

        // 截图用：停住移动后强行给个速度，检查第三人称走路循环（可带速度值）
        int walk = System.Array.IndexOf(args, "-walkpose");        if (walk >= 0)
        {
            request.walkPose = 3.2f;
            if (walk + 1 < args.Length
                && float.TryParse(args[walk + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float walkSpeed))
            {
                request.walkPose = walkSpeed;
            }
        }

        // 截图用：把第二关的战友排成一排，检查火把、驳壳枪与夜间照明
        request.torchLine = System.Array.IndexOf(args, "-torchline") >= 0;
        // 截图用：主角火把举在手里
        request.torch = System.Array.IndexOf(args, "-torch") >= 0;
        // 截图用：让号兵当场吹号，检查收枪、取号、举号的动作
        request.bugle = System.Array.IndexOf(args, "-bugle") >= 0;
        // 截图用：投一枚手榴弹，停在第 N 秒（默认 0.62 秒，拉火那一刻）
        request.grenade = System.Array.IndexOf(args, "-grenade") >= 0;
        int grenadeWait = System.Array.IndexOf(args, "-grenadewait");
        if (grenadeWait >= 0 && grenadeWait + 1 < args.Length)
        {
            request.grenadeWait = float.Parse(args[grenadeWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        // 截图用：把一个敌人丢进赤水河，验证落水后送回岸边
        request.drown = System.Array.IndexOf(args, "-drown") >= 0;
        // 截图用：在面前摆两个近身敌人，检查 AI 切刀拼刀
        request.brawl = System.Array.IndexOf(args, "-brawl") >= 0;
        // 截图用：敌人背对镜头，验证背刺处决的必杀与无敌
        request.execute = System.Array.IndexOf(args, "-execute") >= 0;
        // 截图用：往击杀信息里塞几条样例，检查排版（名字 / 枪图标 / 名字）
        request.killFeed = System.Array.IndexOf(args, "-killfeed") >= 0;
        // 截图用：直接跳到第二关某一渡，拍该段守军的行军路线
        int segment = System.Array.IndexOf(args, "-segment");
        if (segment >= 0 && segment + 1 < args.Length)
        {
            request.hasSegment = int.TryParse(args[segment + 1],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out request.segment);
        }
        int demolishWait = System.Array.IndexOf(args, "-demolishwait");
        if (demolishWait >= 0 && demolishWait + 1 < args.Length)
        {
            request.demolishWait = float.Parse(args[demolishWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        int stamina = System.Array.IndexOf(args, "-stamina");
        request.stamina = -1f;
        if (stamina >= 0 && stamina + 1 < args.Length)
        {
            request.stamina = float.Parse(args[stamina + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        // 截图用：把血量压到指定值，检查低血灰屏与血迹
        int hp = System.Array.IndexOf(args, "-hp");
        if (hp >= 0 && hp + 1 < args.Length)
        {
            request.hp = float.Parse(args[hp + 1], System.Globalization.CultureInfo.InvariantCulture);
        }

        // 截图用：直接进入胜利结算演出
        request.victory = System.Array.IndexOf(args, "-victory") >= 0;
        int victoryWait = System.Array.IndexOf(args, "-victorywait");
        if (victoryWait >= 0 && victoryWait + 1 < args.Length)
        {
            request.victoryWait = float.Parse(args[victoryWait + 1],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return true;
    }

    private IEnumerator PhotoRoutine(PhotoRequest request)
    {
        photoCapture = true;
        yield return new WaitForSeconds(0.4f);
        StartGame();
        yield return null;

        if (request.warmupAtPose) ApplyPhotoPose(request);

        if (request.bridgePhase && !isChishui) BeginBridgePhase();

        if (request.hasSegment && chishui != null) chishui.JumpToSegment(request.segment);

        // 快进若干游戏秒，让敌人走到河心后再定机位
        if (request.warmup > 0f)
        {
            Time.timeScale = 8f;
            yield return new WaitForSeconds(request.warmup);
            Time.timeScale = 1f;
            if (playerHealth != null) playerHealth.Restore(playerHealth.MaxHealth);
        }

        movement.enabled = false;
        mouseLook.enabled = false;

        if (request.knife || request.swing)
        {
            weapon.Select(WeaponController.WeaponSlot.Knife);
        }

        if (request.pistol)
        {
            weapon.Select(WeaponController.WeaponSlot.Pistol);
            yield return new WaitForSeconds(0.25f);
        }

        if (request.pistolReload)
        {
            if (!request.pistol)
            {
                weapon.Select(WeaponController.WeaponSlot.Pistol);
                yield return new WaitForSeconds(0.25f);
            }
            if (pistol != null)
            {
                // 弹匣不满才换得起来，先压到 2 发再按换弹
                pistol.SetAmmoForCapture(2);
                pistol.TryReload();
            }
            yield return new WaitForSeconds(
                request.pistolReloadWait > 0f ? request.pistolReloadWait : 0.62f);
        }

        if (request.aim)
        {
            weapon.SetAimForCapture(true);
            yield return new WaitForSeconds(0.5f);
        }

        if (request.sprintPose)
        {
            weapon.SetSprintForCapture(true);
            yield return new WaitForSeconds(0.4f);
        }

        if (request.bolt)
        {
            Hanyang88Rifle playerRifle = FindObjectOfType<Hanyang88Rifle>();
            if (playerRifle != null) playerRifle.TryFire();
            // 默认停在"枪机拉到底、还没推回"的这一段
            yield return new WaitForSeconds(request.boltWait > 0f ? request.boltWait : 0.33f);
        }

        if (request.reload)
        {
            Hanyang88Rifle playerRifle = FindObjectOfType<Hanyang88Rifle>();
            if (playerRifle != null)
            {
                playerRifle.AddReserveAmmo(10);
                playerRifle.TryReload();
            }
            yield return new WaitForSeconds(1.2f);
        }

        weapon.enabled = false;

        ApplyPhotoPose(request);

        if (request.stamina >= 0f) movement.SetStaminaForCapture(request.stamina);

        if (request.crouch) movement.ForceStance(PlayerMovement.Stance.Crouching);
        if (request.prone) movement.ForceStance(PlayerMovement.Stance.Prone);

        // 让卧倒动作走到第 N 秒，然后定住这一帧
        if (request.prone && request.proneFrame > 0f)
        {
            yield return new WaitForSeconds(request.proneFrame);
            Time.timeScale = 0f;
        }

        if (request.hasSwitch && weapon != null)
        {
            weapon.Select((WeaponController.WeaponSlot)Mathf.Clamp(request.switchTo, 0, 2));
            if (request.switchFrame > 0f)
            {
                yield return new WaitForSeconds(request.switchFrame);
                Time.timeScale = 0f;
            }
            else
            {
                yield return new WaitForSeconds(0.6f);
            }
        }
        if (request.thirdPerson && cameraRig != null) cameraRig.SetThirdPerson(true);
        if (request.hasCameraIndex && cameraRig != null)
        {
            cameraRig.SetThirdPerson(true);
            cameraRig.SetDistanceIndex(request.cameraIndex, false);
            // 截图不等镜头推拉，直接落到目标档位
            cameraRig.SnapDistance();
        }

        if (request.walkPose > 0f)
        {
            // 走路姿势：人物显形、相机退到第三人称，再喂一个假速度让腿摆起来
            if (cameraRig != null) cameraRig.SetThirdPerson(true);
            if (playerBody != null) playerBody.SetVisible(true);
            movement.SetPlanarSpeedForCapture(request.walkPose);
            yield return new WaitForSeconds(0.7f);
            // 抓图前把步幅钉在大跨步的相位，避免正好拍到双腿并拢
            if (playerBody != null) playerBody.SetWalkForCapture(Mathf.PI * 0.5f);
        }

        if (request.figure)
        {
            // 主角模型特写：镜头绕到人物正前方，第三人称身体显形
            if (cameraRig != null)
            {
                cameraRig.SetThirdPerson(true);
                cameraRig.enabled = false;
            }
            // 截图镜头要自己定机位，视角与探头脚本会在 LateUpdate 里把机位掰回去
            if (mouseLook != null) mouseLook.enabled = false;
            if (lean != null) lean.enabled = false;
            if (playerBody != null) playerBody.SetVisible(true);
            weapon.SetViewModelVisible(false);

            Camera figureCamera = Camera.main;
            if (figureCamera != null)
            {
                Vector3 target = player.position + Vector3.up * 0.8f;
                Vector3 spot = target + player.forward * 3.4f
                                      + player.right * 3.4f + Vector3.up * 0.5f;

                // 侧面机位：专门看卧倒 / 蹲跪这类前后折叠的姿势
                if (request.figureSide)
                {
                    target = player.position + player.forward * 0.45f + Vector3.up * 0.36f;
                    spot = target + player.right * 3.1f + Vector3.up * 0.32f;
                }

                figureCamera.transform.SetPositionAndRotation(
                    spot, Quaternion.LookRotation(target - spot, Vector3.up));
                figureCamera.fieldOfView = 40f;
            }
        }

        if (request.factions) SpawnFactionLineup(request);
        if (request.torchLine) SpawnTorchLineup(request);
        if (request.brawl)
        {
            SpawnBrawlLineup(request);
            // 等他们收起枪亮出大刀，抢在劈砍那一下定格
            yield return new WaitForSeconds(0.6f);
            // 快门有延迟：定住这一帧，免得抓图时两人已经糊在镜头上
            Time.timeScale = 0f;
        }

        if (request.execute)
        {
            // 敌人背对玩家站在正前方，右键应触发背刺处决
            Vector3 spot = request.position
                           + Quaternion.Euler(0f, request.yaw, 0f) * new Vector3(0.25f, 0f, 1.55f);
            spot.y = player.position.y + 0.02f;
            EnemyAI victim = EnemySpawner.SpawnAt(levelRoot, spot, player,
                clips.gunshot, EnemySpawner.Faction.CentralArmy);
            if (victim != null)
            {
                victim.transform.rotation = Quaternion.Euler(0f, request.yaw, 0f);
            }

            yield return null;
            bool triggered = melee.TryExecution();
            yield return new WaitForSeconds(0.5f);
            float midInvuln = playerHealth != null ? playerHealth.IncomingDamageMultiplier : -1f;
            yield return new WaitForSeconds(1.2f);
            Health victimHealth = victim != null ? victim.GetComponent<Health>() : null;
            Debug.Log("EXECUTE_TEST triggered=" + triggered
                + " invulnMid=" + midInvuln
                + " dead=" + (victimHealth != null && victimHealth.IsDead));
            Time.timeScale = 0f;
        }

        if (request.muzzle)
        {
            MuzzleFlash[] flashes = FindObjectsOfType<MuzzleFlash>();
            foreach (MuzzleFlash flash in flashes) flash.PlayHold(30f);
        }
        if (request.hp > 0f && playerHealth != null && playerHealth.Current > request.hp)
        {
            playerHealth.TakeDamage(playerHealth.Current - request.hp, player.position,
                Vector3.up, BodyZone.Torso, null);
        }

        // 火把：不播抬手动作，直接摆成举着的样子
        if (request.torch && torch != null)
        {
            torch.SetLitForCapture(true);
            yield return null;
        }

        // 号兵：当场吹号，等收枪、取号、举号的动作走完再定格
        if (request.bugle)
        {
            AllyAI.SoundBugle();
            yield return new WaitForSeconds(0.9f);
            Time.timeScale = 0f;
        }

        // 手榴弹：真人投一枚，停在第 N 秒的投掷动作上
        if (request.grenade)
        {
            Time.timeScale = 1f;
            if (grenades != null)
            {
                grenades.Add(1);
                grenades.Throw();
            }
            yield return new WaitForSeconds(
                request.grenadeWait > 0f ? request.grenadeWait : 0.62f);
            Time.timeScale = 0f;
        }

        // 落水测试：把一个敌人丢进河里，看他会不会被送回岸边
        if (request.drown)
        {
            Vector3 spot = new Vector3(2f, -1.2f, request.position.z + 6f);
            EnemyAI swimmer = EnemySpawner.SpawnAt(levelRoot, spot, player,
                clips.gunshot, EnemySpawner.Faction.Sichuan);
            Vector3 before = swimmer != null ? swimmer.transform.position : Vector3.zero;

            yield return new WaitForSeconds(0.8f);

            Vector3 after = swimmer != null ? swimmer.transform.position : Vector3.zero;
            Debug.Log("DROWN_TEST before=" + before.ToString("F2")
                + " after=" + after.ToString("F2")
                + " stillSubmerged=" + ChishuiWaterRule.FullySubmerged(after)
                + " onBank=" + (Mathf.Abs(after.x) > ChishuiWaterRule.WaterHalfWidth));
            Time.timeScale = 0f;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        hud.SetHudVisible(!request.noHud);

        // 截图用：塞几条击杀信息，检查"名字 · 枪图标 · 名字"的排版
        if (request.killFeed && hud != null)
        {
            hud.ReportAllyKill("赵铁柱", "汉阳造");
            hud.ReportKill(true, "汉阳造");
            hud.ReportAllyKill("王二小", "驳壳枪");
            hud.ReportKill(false, "大刀");
            hud.ReportAllyDown("李长顺");
        }

        if (request.map && tacticalMap != null) tacticalMap.SetOpen(true);

        if (request.demolish)
        {
            Detonate();
            yield return new WaitForSeconds(request.demolishWait > 0f ? request.demolishWait : 0.9f);
            // 定格在这一瞬间，抓图脚本的等待不会让桥继续下沉
            Time.timeScale = 0f;
        }

        if (request.victory)
        {
            state = isChishui ? GameState.Guerrilla : GameState.Playing;
            Win();
            if (request.victoryWait > 0f)
            {
                yield return new WaitForSeconds(request.victoryWait);
            }
        }

        if (request.blood)
        {
            // 定格前一秒连喷几股，抓图时血雾正好散开、血珠正在下坠
            Time.timeScale = 1f;
            Camera shotCam = Camera.main;
            Vector3 impact = shotCam.transform.position + shotCam.transform.forward * 2.6f
                             - shotCam.transform.up * 0.25f;
            Vector3 normal = -shotCam.transform.forward;

            for (int i = 0; i < 4; i++)
            {
                BloodEffects.Burst(impact, normal, 45f);
                yield return new WaitForSeconds(0.09f);
            }

            BloodEffects.Pool(impact - Vector3.up * 1.0f);
            yield return new WaitForSeconds(0.3f);
            Time.timeScale = 0f;
        }

        if (request.swing)
        {
            yield return new WaitForSeconds(0.05f);
            if (request.heavy) melee.TryHeavy();
            else melee.TrySwing();
            yield return new WaitForSeconds(request.swingWait > 0f
                ? request.swingWait : (request.heavy ? 0.34f : 0.125f));
            Time.timeScale = 0f;
        }

        if (request.allyCrouch || request.allyProne)
        {
            foreach (AllyAI ally in FindObjectsOfType<AllyAI>())
            {
                ally.SetStanceForCapture(request.allyProne ? 2 : 1);
            }
            yield return new WaitForSeconds(request.allyStanceWait > 0f
                ? request.allyStanceWait : 1.2f);
            Time.timeScale = 0f;
        }

        if (request.pause)
        {
            // 截图：定格画面 + 快门快照，检查左侧暂停菜单与背景虚化
            Time.timeScale = 1f;
            PauseGame();
        }

        Debug.Log("PHOTO_READY " + request.position + " yaw=" + request.yaw + " pitch=" + request.pitch);
    }

    private void SpawnFactionLineup(PhotoRequest request)
    {
        foreach (EnemyAI ai in FindObjectsOfType<EnemyAI>())
        {
            if (ai != null) Destroy(ai.gameObject);
        }

        Quaternion basis = Quaternion.Euler(0f, request.yaw, 0f);
        Vector3 forward = basis * Vector3.forward;
        Vector3 right = basis * Vector3.right;

        EnemySpawner.Faction[] factions =
        {
            EnemySpawner.Faction.CentralArmy, EnemySpawner.Faction.Sichuan,
            EnemySpawner.Faction.Guizhou,
            EnemySpawner.Faction.CentralArmy, EnemySpawner.Faction.Sichuan,
            EnemySpawner.Faction.Guizhou
        };

        for (int i = 0; i < factions.Length; i++)
        {
            int row = i / 3;
            float offset = (i % 3 - 1) * 2.2f;
            Vector3 position = request.position + forward * (4.6f + row * 2.8f)
                               + right * offset;
            EnemyAI ai = EnemySpawner.SpawnAt(levelRoot, position, player,
                clips.gunshot, factions[i]);
            if (ai == null) continue;

            ai.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            ai.SetAIEnabled(false);
        }
    }

    private void SpawnTorchLineup(PhotoRequest request)
    {
        AllyAI.CombatEnabled = false;

        Quaternion basis = Quaternion.Euler(0f, request.yaw, 0f);
        Vector3 forward = basis * Vector3.forward;
        Vector3 right = basis * Vector3.right;

        AllyAI[] allies = FindObjectsOfType<AllyAI>();
        int count = Mathf.Min(allies.Length, 5);
        for (int i = 0; i < count; i++)
        {
            AllyAI ally = allies[i];
            if (ally == null) continue;

            Vector3 position = request.position + forward * 4.6f
                               + right * ((i - (count - 1) * 0.5f) * 1.9f);
            position.y = isChishui
                ? ChishuiLevelBuilder.GroundHeight(position.x, position.z) + 0.05f
                : player.position.y - 0.05f;

            ally.transform.position = position;
            ally.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            ally.SetTorchBearer(i == 1);
            ally.SetBugler(i == count - 1);
        }
    }

    private void SpawnBrawlLineup(PhotoRequest request)
    {
        foreach (EnemyAI ai in FindObjectsOfType<EnemyAI>())
        {
            if (ai != null) Destroy(ai.gameObject);
        }

        Quaternion basis = Quaternion.Euler(0f, request.yaw, 0f);
        Vector3 forward = basis * Vector3.forward;
        Vector3 right = basis * Vector3.right;

        // 战友退到镜头后面停住，否则会挤到脸前挡住拼刀的两个人
        AllyAI.CombatEnabled = false;
        AllyAI[] allies = FindObjectsOfType<AllyAI>();
        for (int i = 0; i < allies.Length; i++)
        {
            if (allies[i] == null) continue;
            Vector3 spot = request.position - forward * (4.2f + i * 0.9f)
                           + right * ((i - 1f) * 1.6f);
            spot.y = ChishuiLevelBuilder.GroundHeight(spot.x, spot.z) + 0.05f;
            allies[i].transform.position = spot;
            allies[i].transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        for (int i = 0; i < 2; i++)
        {
            Vector3 position = request.position + forward * 4.0f
                               + right * ((i - 0.5f) * 1.6f);
            position.y = ChishuiLevelBuilder.GroundHeight(position.x, position.z) + 0.05f;
            EnemyAI ai = EnemySpawner.SpawnAt(levelRoot, position, player,
                clips.gunshot, EnemySpawner.Faction.CentralArmy);
            if (ai == null) continue;

            ai.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
        }

        // 截图期间免伤，别让拼刀的敌人两下就把镜头打成"阵地失守"
        if (playerHealth != null) playerHealth.IncomingDamageMultiplier = 0f;
    }

    private void ApplyPhotoPose(PhotoRequest request)
    {
        if (player != null)
        {
            player.position = request.position;
            player.rotation = Quaternion.Euler(0f, request.yaw, 0f);
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam.transform.localRotation = Quaternion.Euler(request.pitch, 0f, 0f);
        if (request.fov > 0f) cam.fieldOfView = request.fov;
    }

    private void OnDestroy()
    {
        Health.AnyDied -= HandleAnyDied;
        Health.AnyDamaged -= HandleAnyDamaged;
        Health.AnyKilled -= HandleAnyKilled;
    }

    private IEnumerator AutoStartRoutine(bool autoQuit)
    {
        yield return new WaitForSeconds(0.6f);
        StartGame();
        Debug.Log("GAME_STARTED");

        if (!autoQuit) yield break;

        yield return new WaitForSeconds(4f);

        Debug.Log("SMOKE_OK state=" + state
            + " hp=" + playerHealth.Current
            + " temp=" + cold.Temperature
            + " ammo=" + rifle.AmmoInMagazine + "/" + rifle.ReserveAmmo
            + " enemies=" + FindObjectsOfType<EnemyAI>().Length
            + " allies=" + AllyAI.AliveCount + "/" + AllyAI.SquadSize);

        yield return null;
        Application.Quit();
    }

    // ───────────────────────────── 场景构建 ─────────────────────────────

    private void BuildLighting()
    {
        // 画质：4x MSAA、最高分辨率阴影、各向异性过滤
        QualitySettings.antiAliasing = 4;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.shadowDistance = 140f;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.pixelLightCount = 8;
        QualitySettings.softParticles = true;

        // 三段式环境光：天空冷、地面暗，拉开明暗层次
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.34f, 0.42f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.22f, 0.25f, 0.28f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.08f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        // 赤水河谷纵深 340 米，雾浓一点把远山化成剪影
        RenderSettings.fogDensity = SelectedLevel == 2 ? 0.0085f : 0.0058f;
        RenderSettings.fogColor = new Color(0.4f, 0.46f, 0.54f);

        GameObject sunGo = new GameObject("Sun");
        Light sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.92f, 0.8f);
        sun.intensity = 1.05f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sunGo.transform.rotation = Quaternion.Euler(24f, -32f, 0f);
        RenderSettings.sun = sun;

        // 反向冷色补光，让雪地阴影不发死
        GameObject fillGo = new GameObject("FillLight");
        Light fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.45f, 0.6f, 0.8f);
        fill.intensity = 0.28f;
        fill.shadows = LightShadows.None;
        fillGo.transform.rotation = Quaternion.Euler(14f, 150f, 0f);

        // 程序化天空盒：阴云暮色，压低曝光避免天空过曝
        Shader skyShader = Shader.Find("Skybox/Procedural");
        if (skyShader != null)
        {
            Material sky = new Material(skyShader);
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.32f, 0.42f, 0.56f));
            if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.14f, 0.16f, 0.18f));
            if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 0.9f);
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 0.55f);
            if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.035f);
            if (sky.HasProperty("_SunSizeConvergence")) sky.SetFloat("_SunSizeConvergence", 4f);
            RenderSettings.skybox = sky;
        }
    }

    private void BuildWorld()
    {
        GameObject level = new GameObject("Level");
        levelRoot = level.transform;

        isChishui = SelectedLevel == 2;
        EnemyAI.BridgeLanesEnabled = !isChishui;

        // 第一关：AI 只能走浮桥，不许下水
        RiverGuard.Active = !isChishui;

        // 第二关：河面是一条南北向的带，敌军同样被挡在两岸
        ChishuiWaterRule.Active = isChishui;

        if (isChishui)
        {
            ChishuiLevelBuilder.Build(levelRoot, out playerSpawn, out playerYaw,
                out chishuiCrossings, out chishuiGuards, out chishuiSegments, out chishuiBeacon);
            cachedSpawnPoints = chishuiGuards;
        }
        else
        {
            LevelBuilder.BuildEnvironment(levelRoot, out bridgePoint, out bridgeRoot,
                out Transform[] spawnPoints);
            cachedSpawnPoints = spawnPoints;
            playerSpawn = new Vector3(0f, 0.1f, 6f);
            playerYaw = 0f;
        }
    }

    private Transform[] cachedSpawnPoints;
    private Vector3[] chishuiCrossings;
    private Transform[] chishuiGuards;
    private int[] chishuiSegments;
    private Transform chishuiBeacon;

    private void BuildHUD()
    {
        GameObject hudGo = new GameObject("HUD");
        hud = hudGo.AddComponent<HUDController>();
        hud.BuildShell();
    }

    private void BuildPlayer()
    {
        clips.gunshot = ProceduralAudio.Gunshot();
        clips.bolt = ProceduralAudio.Bolt();
        clips.empty = ProceduralAudio.Empty();
        clips.reload = ProceduralAudio.Reload();
        clips.swing = ProceduralAudio.Swing();
        clips.wind = ProceduralAudio.Wind();
        clips.hitTick = ProceduralAudio.HitTick();
        clips.headshotTick = ProceduralAudio.HeadshotTick();
        clips.killConfirm = ProceduralAudio.KillConfirm();
        clips.meleeHit = ProceduralAudio.MeleeHit();
        clips.explosion = ProceduralAudio.Explosion();
        clips.shellWhistle = ProceduralAudio.ShellWhistle();
        clips.pistolShot = ProceduralAudio.PistolShot();
        clips.heavySwing = ProceduralAudio.HeavySwing();
        clips.execution = ProceduralAudio.Execution();
        clips.torchFire = ProceduralAudio.TorchFire();

        GameObject playerGo = new GameObject("Player");
        playerGo.tag = "Player";
        player = playerGo.transform;
        player.position = playerSpawn;
        player.rotation = Quaternion.Euler(0f, playerYaw, 0f);

        CharacterController controller = playerGo.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.stepOffset = 0.4f;
        controller.slopeLimit = 45f;

        playerHealth = playerGo.AddComponent<Health>();
        playerHealth.Died += HandlePlayerDied;
        playerHealth.Damaged += HandlePlayerDamaged;

        AudioSource playerAudio = playerGo.AddComponent<AudioSource>();
        playerAudio.playOnAwake = false;
        playerAudio.spatialBlend = 0f;
        feedbackAudio = playerAudio;

        GameObject camGo = new GameObject("MainCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(player, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        Camera cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 64f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.backgroundColor = new Color(0.16f, 0.18f, 0.2f);
        cam.allowHDR = true;
        cam.allowMSAA = true;

        camGo.AddComponent<AudioListener>();

        mouseLook = camGo.AddComponent<MouseLook>();
        mouseLook.Initialize(player);

        BuildViewModel(cam, out Transform rifleView, out Transform pistolView,
            out Transform blade, out MuzzleFlash playerMuzzle, out MuzzleFlash pistolMuzzle);
        Transform torchView = BuildTorchView(cam.transform);
        Transform grenadeView = BuildGrenadeView(cam.transform);

        cold = playerGo.AddComponent<ColdSystem>();
        cold.Initialize(9f, 2f, 20f);

        rifle = playerGo.AddComponent<Hanyang88Rifle>();
        rifle.Initialize(cam.transform, playerAudio,
            clips.gunshot, clips.bolt, clips.empty, clips.reload, ~0);
        rifle.SetMuzzleFlash(playerMuzzle);

        faith = playerGo.AddComponent<FaithSystem>();
        faith.Initialize(rifle);

        // 副武器驳壳枪：一个弹匣 6 发，开局带 4 个备用弹夹，打空按 R 换匣
        pistol = playerGo.AddComponent<Pistol>();
        pistol.Initialize(cam, playerAudio, clips.pistolShot, clips.empty, clips.reload);
        pistol.SetMuzzleFlash(pistolMuzzle);

        movement = playerGo.AddComponent<PlayerMovement>();
        lean = playerGo.AddComponent<PlayerLean>();
        BuildPlayerShadow();

        melee = playerGo.AddComponent<PlayerMelee>();
        melee.Initialize(cam, playerAudio, clips.swing, clips.heavySwing, clips.execution, blade);

        weapon = playerGo.AddComponent<WeaponController>();
        weapon.Initialize(cam, rifle, pistol, melee, movement, mouseLook,
            rifleView, pistolView, blade);
        if (playerBody != null) playerBody.BindWeapon(weapon);

        grenades = playerGo.AddComponent<GrenadeSystem>();
        grenades.Initialize(cam, playerAudio, clips.explosion, clips.swing, player);
        grenades.BindGrenadeView(grenadeView, weapon);

        // 火把：特殊装备，不占武器槽；Tab 抽出 / 收回，别在腰上时只是一个道具
        torch = playerGo.AddComponent<TorchSystem>();
        torch.Initialize(playerBody, movement, weapon, torchView, clips.torchFire);
        if (playerBody != null) playerBody.BindTorch(torch);

        cameraRig = camGo.AddComponent<CameraRig>();
        cameraRig.Initialize(cam, movement, playerBody, weapon, rifleView, pistolView, blade, hud);

        cameraShake = camGo.AddComponent<CameraShake>();

        GameObject artilleryGo = new GameObject("Artillery");
        artilleryGo.transform.SetParent(levelRoot, true);
        artillery = artilleryGo.AddComponent<ArtillerySystem>();
        artillery.Initialize(player, levelRoot, clips.shellWhistle, clips.explosion, cameraShake);

        if (!isChishui)
        {
            // 敌军阵地（地图远端）的四门野炮，越过江面炮击西岸友军防线
            GameObject batteryGo = new GameObject("EnemyBattery");
            batteryGo.transform.SetParent(levelRoot, true);
            battery = batteryGo.AddComponent<ArtilleryBattery>();
            battery.Initialize(player, levelRoot, artillery, clips.explosion, clips.shellWhistle);
        }

        if (isChishui)
        {
            GameObject directorGo = new GameObject("ChishuiDirector");
            directorGo.transform.SetParent(levelRoot, true);
            chishui = directorGo.AddComponent<ChishuiDirector>();
            woundedRescued = 0;
            chishui.Initialize(player, levelRoot, chishuiCrossings, chishuiGuards, chishuiSegments,
                chishuiBeacon, clips.gunshot, hud, artillery, HandleChishuiComplete);

            // 青杠坡的伤员：救一名回一点血，三名都救回士气高涨
            Vector3[] woundedSpots =
            {
                new Vector3(23f, 0f, 12f),
                new Vector3(-22f, 0f, 45f),
                new Vector3(-25f, 0f, 62f)
            };
            for (int i = 0; i < woundedSpots.Length; i++)
            {
                Vector3 spot = woundedSpots[i];
                spot.y = ChishuiLevelBuilder.GroundHeight(spot.x, spot.z) + 0.02f;
                WoundedSoldier.Spawn(levelRoot, spot, player, hud, HandleWoundedRescued);
            }

            // 太平渡两岸的假火堆
            GameObject decoyGo = new GameObject("DecoyFires");
            decoyGo.transform.SetParent(levelRoot, true);
            DecoyFire decoys = decoyGo.AddComponent<DecoyFire>();
            Vector3[] decoySpots =
            {
                new Vector3(-24f, 0f, 78f),
                new Vector3(26f, 0f, 136f)
            };
            for (int i = 0; i < decoySpots.Length; i++)
            {
                decoySpots[i].y =
                    ChishuiLevelBuilder.GroundHeight(decoySpots[i].x, decoySpots[i].z) + 0.02f;
            }
            decoys.Initialize(player, hud, decoySpots, clips.explosion);

            directorGo.AddComponent<ChishuiWeather>().Initialize(player);

            // 行军地图：按住 M 展开，标出河谷、渡口与下一个目标
            GameObject mapGo = new GameObject("TacticalMap");
            mapGo.transform.SetParent(levelRoot, true);
            tacticalMap = mapGo.AddComponent<TacticalMap>();
            tacticalMap.Initialize(player, chishuiCrossings, chishui);
        }

        hud.Bind(playerHealth, cold, faith, rifle);
        hud.BindMovement(movement);
        hud.BindWeapon(weapon);
        hud.BindPistol(pistol);
        hud.BindGrenade(grenades);
        hud.BindCamera(cameraRig);

        Snowfall.Attach(player);
    }

    private void BuildViewModel(Camera cam, out Transform rifleView, out Transform pistolView,
                                out Transform blade, out MuzzleFlash rifleMuzzle,
                                out MuzzleFlash pistolMuzzle)
    {
        Transform camT = cam.transform;
        Color wood = new Color(0.31f, 0.18f, 0.09f);
        Color darkWood = new Color(0.22f, 0.12f, 0.06f);
        Color metal = new Color(0.13f, 0.13f, 0.14f);
        Color steel = new Color(0.64f, 0.67f, 0.7f);
        Color sleeve = new Color(0.33f, 0.37f, 0.4f);
        Color cuff = new Color(0.24f, 0.27f, 0.29f);
        Color skin = new Color(0.59f, 0.46f, 0.37f);

        // ── 汉阳造 88 式：枪托、机匣、枪管、护木、枪机拉柄、表尺、准星、背带环 ──
        GameObject rifleRoot = new GameObject("Hanyang88View");
        rifleRoot.transform.SetParent(camT, false);
        // 持枪视角：枪身抬高、枪口略向画面中心内收，枪托不再被屏幕下沿切掉
        rifleRoot.transform.localPosition = new Vector3(0.21f, -0.285f, 1f);
        // 轻微向左滚转，镜头能看到机匣顶面，而不是一整块枪托侧面
        rifleRoot.transform.localRotation = Quaternion.Euler(2f, -3.6f, 5f);
        Transform gun = rifleRoot.transform;

        // 枪托顶面压低到准星下方 2.5 厘米：机瞄时脸颊能贴上去，枪托不挡视线
        ModelPrim(PrimitiveType.Cube, "Stock", gun, new Vector3(-0.012f, -0.02f, -0.19f),
            new Vector3(0.056f, 0.09f, 0.28f), wood);
        GameObject butt = ModelPrim(PrimitiveType.Cube, "ButtPlate", gun,
            new Vector3(-0.012f, -0.02f, -0.335f),
            new Vector3(0.05f, 0.09f, 0.014f), metal);
        butt.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
        ModelPrim(PrimitiveType.Cube, "Wrist", gun, new Vector3(0f, -0.006f, -0.035f),
            new Vector3(0.048f, 0.062f, 0.16f), darkWood);
        ModelPrim(PrimitiveType.Cube, "Receiver", gun, new Vector3(0f, 0.018f, 0.085f),
            new Vector3(0.052f, 0.055f, 0.22f), metal);
        // 机匣侧面的螺钉与机匣下方的弹仓底板：机瞄时枪身贴到镜头前，
        // 近处多几道结构和明暗，才不会还是一整块黑铁
        GameObject screwA = ModelPrim(PrimitiveType.Cylinder, "ReceiverScrewA", gun,
            new Vector3(-0.027f, 0.028f, 0.02f), new Vector3(0.009f, 0.003f, 0.009f), steel);
        screwA.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        GameObject screwB = ModelPrim(PrimitiveType.Cylinder, "ReceiverScrewB", gun,
            new Vector3(-0.027f, 0.006f, 0.155f), new Vector3(0.009f, 0.003f, 0.009f), steel);
        screwB.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        ModelPrim(PrimitiveType.Cube, "MagazinePlate", gun,
            new Vector3(0f, -0.012f, 0.095f), new Vector3(0.046f, 0.012f, 0.15f),
            new Color(0.17f, 0.17f, 0.19f));

        ModelPrim(PrimitiveType.Cube, "Handguard", gun, new Vector3(0f, 0.002f, 0.285f),
            new Vector3(0.055f, 0.05f, 0.26f), wood);
        // 上护木压在枪管上方，机瞄时正好垫在准星与表尺之间
        ModelPrim(PrimitiveType.Cube, "UpperHandguard", gun, new Vector3(0f, 0.051f, 0.335f),
            new Vector3(0.042f, 0.022f, 0.29f), darkWood);

        // 枪管做成圆柱，带枪口箍与根部台阶，不再是方铁条
        GameObject barrel = ModelPrim(PrimitiveType.Cylinder, "Barrel", gun,
            new Vector3(0f, 0.03f, 0.45f), new Vector3(0.019f, 0.2f, 0.019f), metal);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject barrelStep = ModelPrim(PrimitiveType.Cylinder, "BarrelStep", gun,
            new Vector3(0f, 0.03f, 0.255f), new Vector3(0.024f, 0.014f, 0.024f), metal);
        barrelStep.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject muzzleRing = ModelPrim(PrimitiveType.Cylinder, "MuzzleRing", gun,
            new Vector3(0f, 0.03f, 0.64f), new Vector3(0.028f, 0.016f, 0.028f), steel);
        muzzleRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ModelPrim(PrimitiveType.Cube, "BandFront", gun, new Vector3(0f, 0.005f, 0.4f),
            new Vector3(0.06f, 0.058f, 0.014f), steel);
        ModelPrim(PrimitiveType.Cube, "BandRear", gun, new Vector3(0f, 0.005f, 0.19f),
            new Vector3(0.06f, 0.058f, 0.014f), steel);
        ModelPrim(PrimitiveType.Cube, "BandSpring", gun, new Vector3(-0.031f, 0.005f, 0.245f),
            new Vector3(0.004f, 0.014f, 0.09f), steel);

        // 枪机总成挂在一根空枢轴上：枢轴压在枪膛轴线上，整组零件绕它转（解锁 / 闭锁）、
        // 沿它平移（后拉 / 前推）。零件坐标都是相对枢轴的，动画只需要驱动这一根节点。
        GameObject boltRoot = new GameObject("BoltBody");
        boltRoot.transform.SetParent(gun, false);
        boltRoot.transform.localPosition = new Vector3(0f, 0.03f, 0.055f);
        Transform bolt = boltRoot.transform;

        Color boltSteel = new Color(0.4f, 0.42f, 0.44f);

        // 圆柱机尾 + 保险片 + 向右伸出的拉柄与球头
        GameObject boltSleeve = ModelPrim(PrimitiveType.Cylinder, "BoltSleeve", bolt,
            new Vector3(0f, 0f, -0.08f), new Vector3(0.03f, 0.016f, 0.03f), boltSteel);
        boltSleeve.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject safety = ModelPrim(PrimitiveType.Cube, "SafetyFlag", bolt,
            new Vector3(-0.03f, 0.012f, -0.067f), new Vector3(0.012f, 0.022f, 0.014f),
            boltSteel);
        safety.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
        GameObject boltHandle = ModelPrim(PrimitiveType.Cylinder, "BoltHandle", bolt,
            new Vector3(0.05f, -0.002f, 0.005f), new Vector3(0.018f, 0.048f, 0.018f),
            boltSteel);
        boltHandle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        ModelPrim(PrimitiveType.Sphere, "BoltKnob", bolt,
            new Vector3(0.098f, -0.002f, 0.005f), new Vector3(0.036f, 0.036f, 0.036f),
            boltSteel);

        // 抛壳窗里的黄铜弹壳：平时收着，拉栓到后止点时由 WeaponController 抛出来
        GameObject shellCase = ModelPrim(PrimitiveType.Cylinder, "ShellCase", gun,
            new Vector3(0.033f, 0.052f, 0.058f), new Vector3(0.013f, 0.017f, 0.013f),
            new Color(0.74f, 0.58f, 0.26f));
        shellCase.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        shellCase.SetActive(false);

        ModelPrim(PrimitiveType.Cube, "TriggerGuard", gun, new Vector3(0f, -0.045f, -0.02f),
            new Vector3(0.018f, 0.022f, 0.1f), metal);
        ModelPrim(PrimitiveType.Cube, "Trigger", gun, new Vector3(0f, -0.032f, -0.012f),
            new Vector3(0.008f, 0.022f, 0.008f), steel);

        // 表尺：底座 + 两侧护耳 + 后面的表尺座与游标，中间留缺口。
        // 只有左右两肩抬高，机瞄时视线仍能从缺口穿过去压住准星尖。
        ModelPrim(PrimitiveType.Cube, "RearSightBase", gun, new Vector3(0f, 0.064f, 0.16f),
            new Vector3(0.06f, 0.008f, 0.042f), metal);
        ModelPrim(PrimitiveType.Cube, "RearSightL", gun, new Vector3(-0.014f, 0.075f, 0.16f),
            new Vector3(0.012f, 0.016f, 0.02f), metal);
        ModelPrim(PrimitiveType.Cube, "RearSightR", gun, new Vector3(0.014f, 0.075f, 0.16f),
            new Vector3(0.012f, 0.016f, 0.02f), metal);
        ModelPrim(PrimitiveType.Cube, "RearSightEarL", gun, new Vector3(-0.027f, 0.076f, 0.16f),
            new Vector3(0.007f, 0.024f, 0.028f), metal);
        ModelPrim(PrimitiveType.Cube, "RearSightEarR", gun, new Vector3(0.027f, 0.076f, 0.16f),
            new Vector3(0.007f, 0.024f, 0.028f), metal);
        GameObject sightRamp = ModelPrim(PrimitiveType.Cube, "RearSightRamp", gun,
            new Vector3(0f, 0.057f, 0.19f), new Vector3(0.03f, 0.01f, 0.056f), steel);
        sightRamp.transform.localRotation = Quaternion.Euler(-9f, 0f, 0f);
        ModelPrim(PrimitiveType.Cube, "RearSightSlider", gun, new Vector3(0f, 0.068f, 0.176f),
            new Vector3(0.036f, 0.008f, 0.012f), steel);

        // 准星：底座 + 两侧护翼 + 中间的准星片
        ModelPrim(PrimitiveType.Cube, "FrontSightBase", gun, new Vector3(0f, 0.054f, 0.6f),
            new Vector3(0.034f, 0.016f, 0.032f), metal);
        ModelPrim(PrimitiveType.Cube, "FrontSightWingL", gun, new Vector3(-0.021f, 0.073f, 0.6f),
            new Vector3(0.006f, 0.032f, 0.028f), metal);
        ModelPrim(PrimitiveType.Cube, "FrontSightWingR", gun, new Vector3(0.021f, 0.073f, 0.6f),
            new Vector3(0.006f, 0.032f, 0.028f), metal);
        ModelPrim(PrimitiveType.Cube, "FrontSight", gun, new Vector3(0f, 0.071f, 0.6f),
            new Vector3(0.009f, 0.03f, 0.009f), metal);
        // 照门两肩与准星尖各点一颗暗黄铜小珠：机瞄时目标背影再暗也能看清枪口指向，
        // 颜色压得偏旧铜，不会亮得像两颗反光的眼睛
        Color bead = new Color(0.58f, 0.5f, 0.3f);
        ModelPrim(PrimitiveType.Cube, "RearBeadL", gun, new Vector3(-0.014f, 0.086f, 0.16f),
            new Vector3(0.006f, 0.006f, 0.006f), bead);
        ModelPrim(PrimitiveType.Cube, "RearBeadR", gun, new Vector3(0.014f, 0.086f, 0.16f),
            new Vector3(0.006f, 0.006f, 0.006f), bead);
        ModelPrim(PrimitiveType.Cube, "FrontBead", gun, new Vector3(0f, 0.089f, 0.6f),
            new Vector3(0.005f, 0.005f, 0.005f), bead);
        ModelPrim(PrimitiveType.Cube, "CleaningRod", gun, new Vector3(0f, 0.008f, 0.43f),
            new Vector3(0.007f, 0.007f, 0.3f), steel);

        // 背带环：前后各一个，圆环朝后，从持枪视角能看见枪身下缘挂着小铁环
        GameObject swivelFront = ModelPrim(PrimitiveType.Cylinder, "SlingSwivelFront", gun,
            new Vector3(0f, -0.036f, 0.415f), new Vector3(0.018f, 0.004f, 0.018f), steel);
        swivelFront.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject swivelFrontHole = ModelPrim(PrimitiveType.Cylinder, "SlingSwivelFrontHole", gun,
            new Vector3(0f, -0.036f, 0.418f), new Vector3(0.011f, 0.0045f, 0.011f),
            new Color(0.07f, 0.07f, 0.08f));
        swivelFrontHole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject swivelRear = ModelPrim(PrimitiveType.Cylinder, "SlingSwivelRear", gun,
            new Vector3(-0.012f, -0.072f, -0.275f), new Vector3(0.017f, 0.004f, 0.017f), steel);
        swivelRear.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject swivelRearHole = ModelPrim(PrimitiveType.Cylinder, "SlingSwivelRearHole", gun,
            new Vector3(-0.012f, -0.072f, -0.272f), new Vector3(0.01f, 0.0045f, 0.01f),
            new Color(0.07f, 0.07f, 0.08f));
        swivelRearHole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // ── 主角的双手：右手握枪颈扣扳机，左手托护木，小臂从画面下缘伸进来 ──
        BuildRifleHands(gun, sleeve, cuff, skin);

        // 枪口火光挂在枪管口，开火时由 Hanyang88Rifle 触发
        MuzzleFlash playerMuzzle = MuzzleFlash.Create(gun, new Vector3(0f, 0.03f, 0.68f), 0.3f);

        // 枪模专用补光：站在阴影里或背对太阳时，枪和手不会糊成一团黑。
        // 灯泡放在镜头后上方，照亮的正是朝向镜头的那些面；范围压得短，
        // 只罩住身前的枪，基本漏不到场景里
        GameObject viewLightGo = new GameObject("ViewModelLight");
        viewLightGo.transform.SetParent(camT, false);
        viewLightGo.transform.localPosition = new Vector3(0.12f, 0.18f, -0.15f);
        Light viewLight = viewLightGo.AddComponent<Light>();
        viewLight.type = LightType.Point;
        viewLight.color = new Color(1f, 0.95f, 0.88f);
        viewLight.intensity = 0.35f;
        viewLight.range = 2.2f;
        viewLight.shadows = LightShadows.None;

        // ── 大刀：宽刃、斜切刀尖、血槽、黄铜护手、缠绳刀柄、红缨 ──
        // 刀身是平面朝相机的大刀片（真人比例约 6 厘米宽），不再是细铁条。
        // 站位要让握刀的手和护手正好从画面下缘露出：
        // 只留一截刀尖悬在空中的话，看起来像刀自己飘在右下角
        GameObject dao = new GameObject("DadaoView");
        dao.transform.SetParent(camT, false);
        dao.transform.localPosition = new Vector3(0.3f, -0.24f, 0.7f);
        dao.transform.localRotation = Quaternion.Euler(6f, -14f, 18f);
        dao.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
        Transform bladeT = dao.transform;

        Color bladeSteel = new Color(0.12f, 0.12f, 0.13f);
        Color bladeEdge = new Color(0.55f, 0.57f, 0.6f);
        Color bladeBack = new Color(0.07f, 0.07f, 0.08f);
        Color brass = new Color(0.56f, 0.43f, 0.18f);
        Color crimson = new Color(0.66f, 0.1f, 0.08f);

        // 单边开刃的刀身：整块梯形——刀背一边竖直，刃口从刀身中段斜收到刀尖，
        // 顶边平齐，不再是从方块叠出来的、刃口那头缺一角的阶梯刀尖
        KnifeBlade.Create("Blade", bladeT,
            new Vector2(-0.0425f, -0.14f), new Vector2(0.0425f, -0.14f),
            new Vector2(0.0425f, 0.215f), new Vector2(0.0025f, 0.215f),
            0.008f, bladeSteel, false);

        // 刀刃：靠外的一线抛光，沿竖直段和斜收段各铺一条
        ModelPrim(PrimitiveType.Cube, "BladeEdge", bladeT, new Vector3(-0.041f, -0.04f, 0.001f),
            new Vector3(0.012f, 0.2f, 0.007f), bladeEdge);
        GameObject tipEdge = ModelPrim(PrimitiveType.Cube, "BladeTipEdge", bladeT,
            new Vector3(-0.02f, 0.1375f, 0.001f), new Vector3(0.012f, 0.168f, 0.007f),
            bladeEdge);
        tipEdge.transform.localRotation = Quaternion.Euler(0f, 0f, -16.2f);

        // 刀背加厚：贴着竖直的刀背一侧，一直到刀尖
        ModelPrim(PrimitiveType.Cube, "BladeSpine", bladeT, new Vector3(0.037f, 0.035f, 0f),
            new Vector3(0.012f, 0.35f, 0.011f), bladeBack);
        // 血槽
        ModelPrim(PrimitiveType.Cube, "Fuller", bladeT, new Vector3(0.006f, 0.035f, 0.005f),
            new Vector3(0.011f, 0.19f, 0.002f), new Color(0.2f, 0.2f, 0.22f));

        // 铁护手（带两侧小翼）
        ModelPrim(PrimitiveType.Cube, "Guard", bladeT, new Vector3(0f, -0.128f, 0f),
            new Vector3(0.14f, 0.017f, 0.032f), new Color(0.24f, 0.24f, 0.26f));
        ModelPrim(PrimitiveType.Cube, "GuardL", bladeT, new Vector3(-0.066f, -0.116f, 0f),
            new Vector3(0.026f, 0.026f, 0.03f), new Color(0.24f, 0.24f, 0.26f));
        ModelPrim(PrimitiveType.Cube, "GuardR", bladeT, new Vector3(0.066f, -0.116f, 0f),
            new Vector3(0.026f, 0.026f, 0.03f), new Color(0.24f, 0.24f, 0.26f));

        // 缠绳刀柄
        ModelPrim(PrimitiveType.Cube, "Handle", bladeT, new Vector3(0f, -0.2f, 0f),
            new Vector3(0.03f, 0.135f, 0.034f), new Color(0.24f, 0.15f, 0.08f));
        ModelPrim(PrimitiveType.Cube, "Wrap1", bladeT, new Vector3(0f, -0.155f, 0f),
            new Vector3(0.034f, 0.008f, 0.038f), new Color(0.16f, 0.1f, 0.05f));
        ModelPrim(PrimitiveType.Cube, "Wrap2", bladeT, new Vector3(0f, -0.19f, 0f),
            new Vector3(0.034f, 0.008f, 0.038f), new Color(0.16f, 0.1f, 0.05f));
        ModelPrim(PrimitiveType.Cube, "Wrap3", bladeT, new Vector3(0f, -0.225f, 0f),
            new Vector3(0.034f, 0.008f, 0.038f), new Color(0.16f, 0.1f, 0.05f));

        // 刀环与红缨
        ModelPrim(PrimitiveType.Cube, "Pommel", bladeT, new Vector3(0f, -0.272f, 0f),
            new Vector3(0.048f, 0.02f, 0.042f), brass);
        GameObject pommelRing = ModelPrim(PrimitiveType.Cylinder, "PommelRing", bladeT,
            new Vector3(0f, -0.292f, 0f), new Vector3(0.018f, 0.006f, 0.018f), brass);
        pommelRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // 环首中间压一块暗色，远看就是一个圆环，而不是一枚铜圆片
        GameObject ringHole = ModelPrim(PrimitiveType.Cylinder, "PommelRingHole", bladeT,
            new Vector3(0f, -0.292f, 0.002f), new Vector3(0.01f, 0.0065f, 0.01f),
            new Color(0.08f, 0.07f, 0.06f));
        ringHole.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ModelPrim(PrimitiveType.Cylinder, "Tassel", bladeT, new Vector3(0f, -0.332f, 0f),
            new Vector3(0.014f, 0.042f, 0.014f), crimson);
        GameObject tasselTip = ModelPrim(PrimitiveType.Cube, "TasselTip", bladeT,
            new Vector3(0f, -0.356f, 0f), new Vector3(0.02f, 0.02f, 0.02f), crimson);
        tasselTip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // 握刀的手与袖口：手掌包住刀柄，拇指从左侧绕出，袖口从画面下缘伸入
        ModelPrim(PrimitiveType.Cube, "KnifeSleeve", bladeT, new Vector3(0.012f, -0.335f, 0.005f),
            new Vector3(0.098f, 0.1f, 0.09f), sleeve);
        ModelPrim(PrimitiveType.Cube, "KnifeCuff", bladeT, new Vector3(0.01f, -0.288f, 0f),
            new Vector3(0.09f, 0.024f, 0.082f), new Color(0.24f, 0.26f, 0.21f));
        ModelPrim(PrimitiveType.Sphere, "KnifeHand", bladeT, new Vector3(0.002f, -0.198f, 0f),
            new Vector3(0.066f, 0.09f, 0.062f), skin);
        GameObject thumb = ModelPrim(PrimitiveType.Cube, "KnifeThumb", bladeT,
            new Vector3(-0.03f, -0.172f, 0.03f), new Vector3(0.022f, 0.05f, 0.022f), skin);
        thumb.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);

        // ── 驳壳枪（毛瑟 C96）：方形机匣、长枪管、木制握把、前置弹仓 ──
        // 端在胸前偏右，枪口朝画面中心；开火节奏快，所以握把和手都做得紧凑
        GameObject pistolRoot = new GameObject("MauserC96View");
        pistolRoot.transform.SetParent(camT, false);
        pistolRoot.transform.localPosition = new Vector3(0.19f, -0.17f, 0.62f);
        pistolRoot.transform.localRotation = Quaternion.Euler(-6f, -2f, 0f);
        pistolRoot.transform.localScale = new Vector3(0.85f, 0.85f, 0.85f);
        Transform pistolT = pistolRoot.transform;

        Color gunmetal = new Color(0.16f, 0.16f, 0.18f);
        Color gunSteel2 = new Color(0.5f, 0.52f, 0.55f);
        Color gunDarker = new Color(0.1f, 0.1f, 0.12f);
        Color boreDark = new Color(0.03f, 0.03f, 0.035f);
        Color gripWood = new Color(0.34f, 0.19f, 0.09f);
        Color gripBand = new Color(0.2f, 0.11f, 0.05f);

        // 机匣：方形主体 + 顶盖，顶盖略窄，折出枪身上沿的一道光
        ModelPrim(PrimitiveType.Cube, "PistolReceiver", pistolT, new Vector3(0f, 0.02f, 0.12f),
            new Vector3(0.05f, 0.075f, 0.24f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolReceiverTop", pistolT,
            new Vector3(0f, 0.0585f, 0.115f), new Vector3(0.044f, 0.007f, 0.225f), gunSteel2);
        ModelPrim(PrimitiveType.Cube, "PistolBoltSide", pistolT, new Vector3(0.026f, 0.028f, 0.1f),
            new Vector3(0.006f, 0.03f, 0.11f), gunSteel2);
        // 右侧抽壳窗：一块凹进去的暗面，开了枪能看出机件在里面
        ModelPrim(PrimitiveType.Cube, "PistolEjectionPort", pistolT,
            new Vector3(0.0255f, 0.044f, 0.148f), new Vector3(0.004f, 0.015f, 0.048f), boreDark);

        // 枪管 + 散热套筒：套筒上压三道箍、两侧各开一条加强筋
        ModelPrim(PrimitiveType.Cube, "PistolBarrel", pistolT, new Vector3(0f, 0.03f, 0.31f),
            new Vector3(0.028f, 0.028f, 0.2f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolJacket", pistolT, new Vector3(0f, 0.03f, 0.27f),
            new Vector3(0.044f, 0.044f, 0.13f), new Color(0.21f, 0.21f, 0.23f));
        ModelPrim(PrimitiveType.Cube, "PistolJacketRib", pistolT, new Vector3(0f, 0.054f, 0.27f),
            new Vector3(0.038f, 0.006f, 0.115f), gunSteel2);
        ModelPrim(PrimitiveType.Cube, "PistolJacketRibL", pistolT,
            new Vector3(-0.0235f, 0.03f, 0.27f), new Vector3(0.004f, 0.022f, 0.108f), gunSteel2);
        ModelPrim(PrimitiveType.Cube, "PistolJacketRibR", pistolT,
            new Vector3(0.0235f, 0.03f, 0.27f), new Vector3(0.004f, 0.022f, 0.108f), gunSteel2);
        for (int i = 0; i < 3; i++)
        {
            ModelPrim(PrimitiveType.Cube, "PistolJacketBand" + i, pistolT,
                new Vector3(0f, 0.03f, 0.222f + i * 0.048f),
                new Vector3(0.046f, 0.046f, 0.005f), gunDarker);
        }
        // 枪口：加厚的口部套环 + 中间一个黑洞，枪口不再是一块实心方铁
        ModelPrim(PrimitiveType.Cube, "PistolMuzzle", pistolT, new Vector3(0f, 0.03f, 0.408f),
            new Vector3(0.036f, 0.036f, 0.022f), gunSteel2);
        ModelPrim(PrimitiveType.Cube, "PistolMuzzleCrown", pistolT,
            new Vector3(0f, 0.03f, 0.4185f), new Vector3(0.03f, 0.03f, 0.006f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolBore", pistolT, new Vector3(0f, 0.03f, 0.4215f),
            new Vector3(0.013f, 0.013f, 0.004f), boreDark);

        // ── 准星：燕尾底座托着刀刃状准星，刃尖再点一颗小珠 ──
        ModelPrim(PrimitiveType.Cube, "PistolFrontSightBase", pistolT,
            new Vector3(0f, 0.055f, 0.385f), new Vector3(0.026f, 0.012f, 0.03f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolFrontSight", pistolT,
            new Vector3(0f, 0.0665f, 0.385f), new Vector3(0.006f, 0.019f, 0.007f), gunSteel2);
        ModelPrim(PrimitiveType.Sphere, "PistolFrontBead", pistolT,
            new Vector3(0f, 0.0745f, 0.385f), new Vector3(0.0062f, 0.0062f, 0.0062f), gunSteel2);

        // ── 照门：表尺座 + 可翻起的表尺片，末端两片立柱夹出缺口 ──
        ModelPrim(PrimitiveType.Cube, "PistolRearSightBase", pistolT,
            new Vector3(0f, 0.064f, 0.075f), new Vector3(0.036f, 0.01f, 0.026f), gunmetal);
        GameObject rearLeaf = ModelPrim(PrimitiveType.Cube, "PistolRearSightLeaf", pistolT,
            new Vector3(0f, 0.0775f, 0.063f), new Vector3(0.03f, 0.005f, 0.022f), gunSteel2);
        rearLeaf.transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
        ModelPrim(PrimitiveType.Cube, "PistolRearSightL", pistolT,
            new Vector3(-0.012f, 0.073f, 0.075f), new Vector3(0.01f, 0.014f, 0.015f), gunSteel2);
        ModelPrim(PrimitiveType.Cube, "PistolRearSightR", pistolT,
            new Vector3(0.012f, 0.073f, 0.075f), new Vector3(0.01f, 0.014f, 0.015f), gunSteel2);
        // 缺口底部留一道细缝，衬出"缺口里透光"的层次
        ModelPrim(PrimitiveType.Cube, "PistolRearSightNotch", pistolT,
            new Vector3(0f, 0.0665f, 0.075f), new Vector3(0.014f, 0.0025f, 0.016f), boreDark);

        // 机匣右侧的抽壳钩
        ModelPrim(PrimitiveType.Cube, "PistolExtractor", pistolT, new Vector3(0.027f, 0.05f, 0.16f),
            new Vector3(0.005f, 0.012f, 0.1f), gunSteel2);

        // ── 枪机：圆筒从机匣后部探出，两侧各一颗拉柄球头（驳壳枪最好认的特征）──
        GameObject pistolBolt = ModelPrim(PrimitiveType.Cylinder, "PistolBolt", pistolT,
            new Vector3(0f, 0.04f, -0.005f), new Vector3(0.03f, 0.03f, 0.03f), gunSteel2);
        pistolBolt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ModelPrim(PrimitiveType.Cylinder, "PistolBoltLugL", pistolT,
            new Vector3(-0.026f, 0.045f, -0.018f), new Vector3(0.009f, 0.012f, 0.009f), gunmetal);
        ModelPrim(PrimitiveType.Cylinder, "PistolBoltLugR", pistolT,
            new Vector3(0.026f, 0.045f, -0.018f), new Vector3(0.009f, 0.012f, 0.009f), gunmetal);

        // ── 击锤：带孔的环形锤，向后扳着待击 ──
        GameObject hammer = ModelPrim(PrimitiveType.Cube, "PistolHammer", pistolT,
            new Vector3(0f, 0.062f, -0.044f), new Vector3(0.014f, 0.034f, 0.014f), gunSteel2);
        hammer.transform.localRotation = Quaternion.Euler(-24f, 0f, 0f);
        GameObject hammerRing = ModelPrim(PrimitiveType.Cylinder, "PistolHammerRing", pistolT,
            new Vector3(0f, 0.079f, -0.05f), new Vector3(0.016f, 0.003f, 0.016f), gunSteel2);
        hammerRing.transform.localRotation = Quaternion.Euler(72f, 0f, 0f);

        // ── 扳机护圈：三段围成方框，中间空出来，能看见扳机在里面 ──
        ModelPrim(PrimitiveType.Cube, "PistolGuardFront", pistolT,
            new Vector3(0f, -0.044f, 0.025f), new Vector3(0.011f, 0.03f, 0.008f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolGuardBottom", pistolT,
            new Vector3(0f, -0.0575f, 0.006f), new Vector3(0.011f, 0.008f, 0.046f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolGuardRear", pistolT,
            new Vector3(0f, -0.043f, -0.014f), new Vector3(0.011f, 0.026f, 0.008f), gunmetal);
        ModelPrim(PrimitiveType.Cube, "PistolTrigger", pistolT, new Vector3(0f, -0.04f, 0.005f),
            new Vector3(0.007f, 0.022f, 0.007f), gunSteel2);

        // ── 前置弹仓：驳壳枪最醒目的特征，方方正正挂在扳机护圈前面 ──
        ModelPrim(PrimitiveType.Cube, "PistolMagazine", pistolT, new Vector3(0f, -0.05f, 0.052f),
            new Vector3(0.03f, 0.1f, 0.05f), new Color(0.19f, 0.19f, 0.21f));
        for (int i = 0; i < 2; i++)
        {
            ModelPrim(PrimitiveType.Cube, "PistolMagRib" + i, pistolT,
                new Vector3(0f, -0.078f + i * 0.044f, 0.052f),
                new Vector3(0.032f, 0.005f, 0.052f), gunDarker);
        }
        ModelPrim(PrimitiveType.Cube, "PistolMagFloor", pistolT,
            new Vector3(0f, -0.103f, 0.052f), new Vector3(0.035f, 0.008f, 0.055f), gunSteel2);

        // ── 木握把：两侧贴深色防滑护板，底部金属盖板下面挂一个枪绳环 ──
        GameObject grip = ModelPrim(PrimitiveType.Cube, "PistolGrip", pistolT,
            new Vector3(0f, -0.062f, -0.055f), new Vector3(0.04f, 0.135f, 0.058f), gripWood);
        grip.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject panel = ModelPrim(PrimitiveType.Cube, "PistolGripPanel" + (side < 0 ? "L" : "R"),
                pistolT, new Vector3(0.0205f * side, -0.062f, -0.055f),
                new Vector3(0.002f, 0.108f, 0.044f), gripBand);
            panel.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
        }
        GameObject gripCap = ModelPrim(PrimitiveType.Cube, "PistolGripCap", pistolT,
            new Vector3(0f, -0.128f, -0.073f), new Vector3(0.044f, 0.016f, 0.062f), gunSteel2);
        gripCap.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
        GameObject lanyard = ModelPrim(PrimitiveType.Cylinder, "PistolLanyard", pistolT,
            new Vector3(0f, -0.145f, -0.086f), new Vector3(0.012f, 0.004f, 0.012f), gunSteel2);
        lanyard.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // 握枪的手：手掌包住木把，四指从远侧绕到前面，袖口从画面下缘伸进来
        GameObject pistolHand = ModelPrim(PrimitiveType.Sphere, "PistolHand", pistolT,
            new Vector3(0.004f, -0.068f, -0.055f), new Vector3(0.062f, 0.09f, 0.07f), skin);
        pistolHand.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        for (int i = 0; i < 3; i++)
        {
            float z = -0.078f + i * 0.024f;
            GameObject finger = ModelPrim(PrimitiveType.Cube, "PistolFinger" + i, pistolT,
                new Vector3(-0.026f, -0.058f, z), new Vector3(0.022f, 0.05f, 0.024f), skin);
            finger.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        }
        ModelPrim(PrimitiveType.Cube, "PistolThumb", pistolT, new Vector3(0.026f, -0.05f, -0.04f),
            new Vector3(0.022f, 0.026f, 0.05f), skin);
        ModelPrim(PrimitiveType.Cube, "PistolSleeve", pistolT, new Vector3(0f, -0.16f, -0.085f),
            new Vector3(0.085f, 0.1f, 0.09f), sleeve);

        // 枪口火光挂在枪管口，开火由 Pistol 触发
        MuzzleFlash pistolFlash = MuzzleFlash.Create(
            pistolT, new Vector3(0f, 0.03f, 0.43f), 0.22f);

        rifleView = gun;
        pistolView = pistolT;
        blade = bladeT;
        rifleMuzzle = playerMuzzle;
        pistolMuzzle = pistolFlash;
    }

    /// <summary>
    /// 第一人称火把：左手握住木柄，火头裹着浸油的布、上面捆三道铁丝，
    /// 火苗两根十字交叉的方块叠出体积，火头自带一盏短程点光源照亮身前。
    /// 模型原点就是握把，TorchSystem 直接把整棵子树摆在画面左下角。
    /// </summary>
    private static Transform BuildTorchView(Transform camT)
    {
        Color wood = new Color(0.26f, 0.17f, 0.09f);
        Color cloth = new Color(0.16f, 0.12f, 0.09f);
        Color band = new Color(0.36f, 0.32f, 0.27f);
        Color flameOuter = new Color(1f, 0.52f, 0.12f);
        Color flameInner = new Color(1f, 0.86f, 0.45f);
        Color sleeve = new Color(0.33f, 0.37f, 0.4f);
        Color cuff = new Color(0.24f, 0.27f, 0.29f);
        Color skin = new Color(0.59f, 0.46f, 0.37f);

        GameObject root = new GameObject("TorchView");
        root.transform.SetParent(camT, false);
        root.transform.localPosition = new Vector3(-0.34f, -0.52f, 0.56f);
        root.transform.localRotation = Quaternion.Euler(32f, 12f, -14f);
        Transform torch = root.transform;

        // 木柄一直做到 -0.43，顶端顶到 0.135 与火头下沿（0.125）搭上，
        // 中间不留缝，火头不会像浮在柄上。手（原点上方 -0.02 处）落在 58% 的位置，
        // 柄尾从拳头下缘露出一大截。
        ModelPrim(PrimitiveType.Cylinder, "TorchHandle", torch,
            new Vector3(0f, -0.1475f, 0f), new Vector3(0.022f, 0.2825f, 0.022f), wood);
        ModelPrim(PrimitiveType.Cylinder, "TorchButt", torch,
            new Vector3(0f, -0.43f, 0f), new Vector3(0.028f, 0.012f, 0.028f), band);
        ModelPrim(PrimitiveType.Cube, "TorchHead", torch,
            new Vector3(0f, 0.2f, 0f), new Vector3(0.062f, 0.15f, 0.062f), cloth);
        for (int i = 0; i < 3; i++)
        {
            ModelPrim(PrimitiveType.Cube, "TorchBand" + i, torch,
                new Vector3(0f, 0.15f + i * 0.045f, 0f),
                new Vector3(0.072f, 0.014f, 0.072f), band);
        }

        // 火苗：TorchSystem 按名字找这个节点，会随噪声缩放
        Transform flame = new GameObject("Flame").transform;
        flame.SetParent(torch, false);
        flame.localPosition = new Vector3(0f, 0.32f, 0f);
        GameObject outer = ModelPrim(PrimitiveType.Cube, "TorchFlame", flame,
            new Vector3(0f, 0.05f, 0f), new Vector3(0.11f, 0.24f, 0.11f), flameOuter);
        outer.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        ModelPrim(PrimitiveType.Cube, "TorchFlameCore", flame,
            new Vector3(0f, 0.055f, 0f), new Vector3(0.055f, 0.15f, 0.055f), flameInner);

        // 握柄的左手：手掌包住木柄，四指从前面绕过来，袖口从画面下缘伸进
        ModelPrim(PrimitiveType.Sphere, "TorchPalm", torch,
            new Vector3(0.006f, -0.02f, -0.012f), new Vector3(0.074f, 0.09f, 0.068f), skin);
        // 四指从镜头这一侧（-z）绕过去，指腹压在木柄上，从画面里能看清是一把攥住的手
        for (int i = 0; i < 3; i++)
        {
            GameObject finger = ModelPrim(PrimitiveType.Cube, "TorchFinger" + i, torch,
                new Vector3(-0.014f, -0.055f + i * 0.034f, -0.024f),
                new Vector3(0.052f, 0.028f, 0.042f), skin);
            finger.transform.localRotation = Quaternion.Euler(0f, -14f, 6f);
        }
        ModelPrim(PrimitiveType.Cube, "TorchThumb", torch,
            new Vector3(0.034f, 0.008f, 0.012f), new Vector3(0.024f, 0.072f, 0.03f), skin);
        ModelPrim(PrimitiveType.Cube, "TorchCuff", torch,
            new Vector3(0f, -0.105f, -0.058f), new Vector3(0.088f, 0.05f, 0.086f), cuff);
        ModelPrim(PrimitiveType.Cube, "TorchSleeve", torch,
            new Vector3(0f, -0.23f, -0.085f), new Vector3(0.1f, 0.22f, 0.1f), sleeve);

        // 点光源：只照亮，不投影，免得自己在地上的影子乱跳
        GameObject lightGo = new GameObject("TorchLight");
        lightGo.transform.SetParent(torch, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        Light torchLight = lightGo.AddComponent<Light>();
        torchLight.type = LightType.Point;
        torchLight.color = new Color(1f, 0.72f, 0.4f);
        torchLight.range = 12f;
        torchLight.intensity = 2.1f;
        torchLight.shadows = LightShadows.None;
        lightGo.AddComponent<FireFlicker>();

        root.SetActive(false);
        return torch;
    }

    /// <summary>
    /// 第一人称投弹手模：右手攥着木柄手榴弹，左手从画面左侧伸进来拉火。
    /// 右臂枢轴在手腕，投掷时整条小臂带弹往后摆再甩出去；
    /// 拉火绳、引信火星都挂在弹头上，点燃后由 GrenadeSystem 显隐。
    /// </summary>
    private static Transform BuildGrenadeView(Transform camT)
    {
        Color sleeve = new Color(0.33f, 0.37f, 0.4f);
        Color cuff = new Color(0.24f, 0.27f, 0.29f);
        Color skin = new Color(0.59f, 0.46f, 0.37f);
        Color wood = new Color(0.35f, 0.21f, 0.1f);
        Color woodDark = new Color(0.24f, 0.14f, 0.07f);
        Color iron = new Color(0.2f, 0.23f, 0.17f);
        Color ironDark = new Color(0.13f, 0.14f, 0.11f);
        Color brass = new Color(0.6f, 0.48f, 0.2f);

        GameObject root = new GameObject("GrenadeView");
        root.transform.SetParent(camT, false);
        root.transform.localPosition = Vector3.zero;
        Transform view = root.transform;

        // ── 右臂：小臂从画面右下伸进来，拳头在腕关节上方 ──
        GameObject right = new GameObject("RightArm");
        right.transform.SetParent(view, false);
        right.transform.localPosition = new Vector3(0.28f, -0.66f, 0.56f);
        Transform r = right.transform;

        ModelPrim(PrimitiveType.Cube, "Sleeve", r, new Vector3(0f, -0.16f, -0.03f),
            new Vector3(0.11f, 0.32f, 0.12f), sleeve);
        ModelPrim(PrimitiveType.Cube, "Cuff", r, new Vector3(0f, -0.005f, 0.005f),
            new Vector3(0.1f, 0.05f, 0.11f), cuff);
        ModelPrim(PrimitiveType.Sphere, "Hand", r, new Vector3(0f, 0.085f, 0.01f),
            new Vector3(0.082f, 0.115f, 0.092f), skin);
        // 手指横着缠在木柄近镜头这一侧，从正面看得出是攥住的
        for (int i = 0; i < 3; i++)
        {
            GameObject finger = ModelPrim(PrimitiveType.Cube, "Finger" + i, r,
                new Vector3(-0.006f, 0.045f + i * 0.036f, -0.052f),
                new Vector3(0.088f, 0.026f, 0.044f), skin);
            finger.transform.localRotation = Quaternion.Euler(0f, 0f, i == 1 ? 0f : 4f);
        }
        ModelPrim(PrimitiveType.Cube, "Thumb", r, new Vector3(0.048f, 0.1f, -0.02f),
            new Vector3(0.032f, 0.078f, 0.04f), skin);

        // ── 木柄手榴弹：木柄在拳心，铸铁弹头朝上 ──
        GameObject grenade = new GameObject("Grenade");
        grenade.transform.SetParent(r, false);
        grenade.transform.localPosition = new Vector3(0f, 0.02f, 0.02f);
        grenade.transform.localScale = Vector3.one * 0.8f;
        Transform g = grenade.transform;

        ModelPrim(PrimitiveType.Cylinder, "Handle", g, new Vector3(0f, 0.115f, 0f),
            new Vector3(0.038f, 0.11f, 0.038f), wood);
        ModelPrim(PrimitiveType.Cylinder, "HandleBand", g, new Vector3(0f, 0.205f, 0f),
            new Vector3(0.043f, 0.007f, 0.043f), woodDark);
        ModelPrim(PrimitiveType.Sphere, "Head", g, new Vector3(0f, 0.28f, 0f),
            new Vector3(0.098f, 0.11f, 0.098f), iron);
        ModelPrim(PrimitiveType.Cylinder, "HeadBand", g, new Vector3(0f, 0.228f, 0f),
            new Vector3(0.1f, 0.008f, 0.1f), ironDark);
        ModelPrim(PrimitiveType.Cylinder, "HeadCollar", g, new Vector3(0f, 0.338f, 0f),
            new Vector3(0.042f, 0.01f, 0.042f), ironDark);
        ModelPrim(PrimitiveType.Cylinder, "FuseHead", g, new Vector3(0f, 0.352f, 0f),
            new Vector3(0.02f, 0.016f, 0.02f), brass);

        // 拉火绳：从引信头斜向下垂出，点燃后被扯掉
        GameObject cord = ModelPrim(PrimitiveType.Cylinder, "PullCord", g,
            new Vector3(-0.035f, 0.315f, 0f), new Vector3(0.005f, 0.042f, 0.005f),
            new Color(0.62f, 0.56f, 0.42f));
        cord.transform.localRotation = Quaternion.Euler(0f, 0f, 34f);
        ModelPrim(PrimitiveType.Cylinder, "PullRing", g,
            new Vector3(-0.072f, 0.278f, 0f), new Vector3(0.022f, 0.003f, 0.022f), brass);

        // 引信火星：一颗自发光小球 + 一小盏点光，点燃前不显示
        GameObject spark = ModelPrim(PrimitiveType.Sphere, "FuseSpark", g,
            new Vector3(0f, 0.37f, 0f), new Vector3(0.026f, 0.032f, 0.026f),
            new Color(1f, 0.82f, 0.38f));
        Renderer sparkRenderer = spark.GetComponent<Renderer>();
        if (sparkRenderer != null)
        {
            sparkRenderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(1f, 0.82f, 0.38f), new Color(1f, 0.58f, 0.14f));
            sparkRenderer.enabled = false;
        }
        GameObject sparkLightGo = new GameObject("SparkLight");
        sparkLightGo.transform.SetParent(spark.transform, false);
        Light sparkLight = sparkLightGo.AddComponent<Light>();
        sparkLight.type = LightType.Point;
        sparkLight.color = new Color(1f, 0.66f, 0.28f);
        sparkLight.range = 1.4f;
        sparkLight.intensity = 0f;
        sparkLight.shadows = LightShadows.None;

        // ── 左臂：从画面左侧伸过来，手指捏住拉火绳往下扯 ──
        GameObject left = new GameObject("LeftArm");
        left.transform.SetParent(view, false);
        left.transform.localPosition = new Vector3(-0.44f, -0.66f, 0.44f);
        left.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);
        Transform l = left.transform;

        ModelPrim(PrimitiveType.Cube, "Sleeve", l, new Vector3(0f, -0.17f, -0.02f),
            new Vector3(0.1f, 0.32f, 0.11f), sleeve);
        ModelPrim(PrimitiveType.Cube, "Cuff", l, new Vector3(0f, -0.01f, 0f),
            new Vector3(0.09f, 0.045f, 0.1f), cuff);
        ModelPrim(PrimitiveType.Sphere, "Hand", l, new Vector3(0f, 0.035f, 0.01f),
            new Vector3(0.08f, 0.1f, 0.085f), skin);
        ModelPrim(PrimitiveType.Cube, "Thumb", l, new Vector3(0.03f, 0.07f, 0.02f),
            new Vector3(0.026f, 0.05f, 0.03f), skin);

        // 扯下来的拉火绳：点火后挂在左手，跟着手往外一甩
        GameObject pulled = ModelPrim(PrimitiveType.Cylinder, "PulledCord", l,
            new Vector3(-0.01f, 0.11f, 0.03f), new Vector3(0.005f, 0.045f, 0.005f),
            new Color(0.62f, 0.56f, 0.42f));
        pulled.transform.localRotation = Quaternion.Euler(0f, 0f, 26f);
        ModelPrim(PrimitiveType.Cylinder, "PulledRing", pulled.transform,
            new Vector3(0f, 1.55f, 0f), new Vector3(4.8f, 0.18f, 4.8f), brass);
        pulled.SetActive(false);

        view.gameObject.SetActive(false);
        return view;
    }

    /// <summary>
    /// 第一人称持枪的双手：右手握枪颈扣扳机，左手托住护木，
    /// 小臂从画面下缘伸进来，掌心、手指都包在枪身上。
    /// </summary>
    private static void BuildRifleHands(Transform gun, Color sleeve, Color cuff, Color skin)
    {
        // 手和胳膊统一挂在一个空节点下，切枪 / 举火把时整体启停
        GameObject handsGo = new GameObject("Hands");
        handsGo.transform.SetParent(gun, false);
        Transform hands = handsGo.transform;

        // 镜头在枪身左后方，看到的是枪的左侧：右手掌在远侧，
        // 所以把手指、拇指和指节全部放到枪颈左面（x≈-0.05）才看得见。
        GameObject palmR = ModelPrim(PrimitiveType.Cube, "HandR_Palm", hands,
            new Vector3(0.038f, -0.028f, -0.005f),
            new Vector3(0.04f, 0.082f, 0.085f), skin);
        palmR.transform.localRotation = Quaternion.Euler(-6f, 0f, -16f);

        // 手背压在枪颈上方（压低到准星以下，机瞄时不挡视线）
        GameObject backR = ModelPrim(PrimitiveType.Cube, "HandR_Back", hands,
            new Vector3(0.008f, 0.02f, 0.005f),
            new Vector3(0.052f, 0.03f, 0.075f), skin);
        backR.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);

        // 拇指压在枪颈左上侧，指向前方
        GameObject thumbR = ModelPrim(PrimitiveType.Cube, "HandR_Thumb", hands,
            new Vector3(-0.038f, 0.022f, 0.03f),
            new Vector3(0.03f, 0.026f, 0.062f), skin);
        thumbR.transform.localRotation = Quaternion.Euler(-10f, -18f, 12f);

        // 食指沿枪身左侧伸向扳机护圈
        GameObject indexR = ModelPrim(PrimitiveType.Cube, "HandR_Index", hands,
            new Vector3(-0.044f, -0.006f, 0.052f),
            new Vector3(0.026f, 0.028f, 0.058f), skin);
        indexR.transform.localRotation = Quaternion.Euler(-8f, 0f, 4f);
        ModelPrim(PrimitiveType.Cube, "HandR_IndexTip", hands,
            new Vector3(-0.026f, -0.036f, 0.062f),
            new Vector3(0.024f, 0.028f, 0.024f), skin);

        // 中指、无名指、小指从枪颈左面绕下去，三根手指竖着扣住
        for (int i = 0; i < 3; i++)
        {
            float z = -0.052f + i * 0.028f;
            GameObject finger = ModelPrim(PrimitiveType.Cube, "HandR_Finger" + i, hands,
                new Vector3(-0.051f, -0.012f, z),
                new Vector3(0.026f, 0.054f, 0.028f), skin);
            finger.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);
        }

        // 左手：掌心托在护木下，手指从远侧翻上护木，拇指压在近侧
        GameObject palmL = ModelPrim(PrimitiveType.Cube, "HandL_Palm", hands,
            new Vector3(-0.016f, -0.048f, 0.3f),
            new Vector3(0.064f, 0.038f, 0.1f), skin);
        palmL.transform.localRotation = Quaternion.Euler(0f, 0f, 6f);

        for (int i = 0; i < 4; i++)
        {
            float z = 0.252f + i * 0.032f;
            GameObject finger = ModelPrim(PrimitiveType.Cube, "HandL_Finger" + i, hands,
                new Vector3(0.024f, 0.024f, z),
                new Vector3(0.024f, 0.052f, 0.024f), skin);
            finger.transform.localRotation = Quaternion.Euler(0f, 0f, -10f);
        }

        GameObject thumbL = ModelPrim(PrimitiveType.Cube, "HandL_Thumb", hands,
            new Vector3(-0.055f, -0.02f, 0.35f),
            new Vector3(0.026f, 0.032f, 0.085f), skin);
        thumbL.transform.localRotation = Quaternion.Euler(0f, -14f, 0f);

        // 右手和右小臂挂在手腕上，手掌再单独挂一层：
        // 拉栓时整条手臂平移去够拉柄（袖子不会从手腕断开），手掌绕腕子转着抓球头。
        GameObject rightArmGo = new GameObject("RightArm");
        rightArmGo.transform.SetParent(hands, false);
        rightArmGo.transform.localPosition = new Vector3(-0.046f, -0.056f, 0.005f);

        // 小臂从手掌底下伸出画面：皮肤手腕 → 深色袖口 → 军装袖子。
        // 袖子挂在一个腕子枢轴里：机瞄时枪整体缩到眼前，小臂正好正对镜头，
        // 会糊住大半个准星。WeaponController 入瞄时把枢轴往画面下方转，
        // 小臂顺着底缘滑出去，画面里只剩手掌扣在枪上。
        // 袖口起点塞进手掌里 3 厘米：腕子转下去以后，圆柱顶盖不会露在手外面
        GameObject armRSwing = new GameObject("ArmRSwing");
        armRSwing.transform.SetParent(rightArmGo.transform, false);
        BuildSleeve(armRSwing.transform, "ArmR", new Vector3(0f, 0.03f, -0.01f),
            new Vector3(0.1f, -1.2f, -0.42f) - new Vector3(-0.046f, -0.056f, 0.005f),
            0.032f, 0.046f, 0.041f, skin, cuff, sleeve);

        GameObject armLSwing = new GameObject("ArmLSwing");
        armLSwing.transform.SetParent(hands, false);
        armLSwing.transform.localPosition = new Vector3(-0.018f, -0.062f, 0.31f);
        BuildSleeve(armLSwing.transform, "ArmL", new Vector3(0f, 0.02f, -0.01f),
            new Vector3(-0.16f, -1.2f, -0.36f) - new Vector3(-0.018f, -0.062f, 0.31f),
            0.032f, 0.046f, 0.041f, skin, cuff, sleeve);

        GameObject rightHandGo = new GameObject("HandR");
        rightHandGo.transform.SetParent(rightArmGo.transform, false);

        System.Collections.Generic.List<Transform> rightParts =
            new System.Collections.Generic.List<Transform>();
        foreach (Transform child in hands)
        {
            if (child == rightArmGo.transform || child == rightHandGo.transform) continue;
            if (child.name.StartsWith("HandR_") || child.name.StartsWith("ArmR_"))
                rightParts.Add(child);
        }
        foreach (Transform part in rightParts)
        {
            part.SetParent(part.name.StartsWith("HandR_")
                ? rightHandGo.transform : rightArmGo.transform, true);
        }

        // ── 拉栓专用的握拳手 ──
        // 握枪那只手掌张开、手指朝前，直接扭到拉柄上会像手腕折断。
        // 这里另做一只扣拳：手背在上、四指从近侧兜下来包住球头、拇指压在顶上，
        // 原点就是拉柄球心，由 WeaponController 每帧摆到球头位置，普通握枪手同时收起。
        GameObject boltHandGo = new GameObject("BoltHand");
        boltHandGo.transform.SetParent(gun, false);
        boltHandGo.transform.localPosition = new Vector3(0.098f, 0.028f, 0.06f);
        Transform boltHandT = boltHandGo.transform;

        // 握拳：拳心就是球头所在的原点。手掌是一团圆的，四根手指各用三段圆柱
        // 绕着球头卷一圈，指腹正好压在球面上，拇指从上面别过去扣住——
        // 从镜头这边看到的是圆滚滚的拳头，不是一摞方块。
        GameObject back = ModelPrim(PrimitiveType.Sphere, "BoltHand_Back", boltHandT,
            new Vector3(0.038f, 0.000f, -0.008f), new Vector3(0.058f, 0.066f, 0.064f), skin);
        back.transform.localRotation = Quaternion.Euler(0f, -6f, 0f);

        const float arcRadius = 0.026f;
        float[] arcDeg = { 26f, 66f, 106f, 144f, 172f };
        float[] rowY = { -0.031f, -0.016f, -0.001f, 0.014f };

        for (int i = 0; i < 4; i++)
        {
            for (int s = 0; s < arcDeg.Length - 1; s++)
            {
                float a0 = arcDeg[s] * Mathf.Deg2Rad;
                float a1 = arcDeg[s + 1] * Mathf.Deg2Rad;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * arcRadius;
                Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * arcRadius;
                p0.y = rowY[i];
                p1.y = rowY[i];

                MakeLimb(boltHandT, "BoltHand_Finger" + i + "_" + s, p0, p1,
                    0.0078f, skin);
            }
        }

        // 拇指：从手背上沿横过来，压在球头顶上
        MakeLimb(boltHandT, "BoltHand_Thumb", new Vector3(0.034f, 0.030f, -0.022f),
            new Vector3(-0.004f, 0.028f, 0.006f), 0.0095f, skin);

        // 腕子：正好落在小臂皮肤那一截的起点上
        GameObject boltWrist = ModelPrim(PrimitiveType.Cube, "BoltHand_Wrist", boltHandT,
            new Vector3(0.058f, -0.022f, -0.046f), new Vector3(0.046f, 0.044f, 0.052f), skin);
        boltWrist.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);

        boltHandGo.SetActive(false);
    }

    private static void BuildSleeve(Transform parent, string name, Vector3 from, Vector3 to,
                                    float wristRadius, float cuffRadius, float sleeveRadius,
                                    Color skinColor, Color cuffColor, Color sleeveColor)
    {
        Vector3 dir = (to - from).normalized;
        float length = Vector3.Distance(from, to);

        Vector3 wristEnd = from + dir * Mathf.Min(0.085f, length * 0.18f);
        Vector3 cuffEnd = wristEnd + dir * Mathf.Min(0.075f, length * 0.16f);

        MakeLimb(parent, name + "_Wrist", from, wristEnd, wristRadius, skinColor);
        MakeLimb(parent, name + "_Cuff", wristEnd, cuffEnd, cuffRadius, cuffColor);
        MakeLimb(parent, name + "_Sleeve", cuffEnd, to, sleeveRadius, sleeveColor);
    }

    /// <summary>圆柱小臂：平头收口，不会在手腕处鼓出一个球。</summary>
    private static void MakeLimb(Transform parent, string name, Vector3 from, Vector3 to,
                                 float radius, Color color)
    {
        Vector3 dir = to - from;
        float length = dir.magnitude;
        if (length < 0.001f) return;

        GameObject go = ModelPrim(PrimitiveType.Cylinder, name, parent,
            (from + to) * 0.5f,
            new Vector3(radius * 2f, length * 0.5f, radius * 2f), color);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir / length);
    }

    /// <summary>
    /// 玩家自己的影子身体：只写阴影、不进相机，蹲下 / 趴下时跟着变矮。
    /// 没有它的话，第一人称下脚边永远是空的——雪地上很容易出戏。
    /// </summary>
    private void BuildPlayerShadow()
    {
        GameObject shadowGo = new GameObject("PlayerShadowBody");
        shadowGo.transform.SetParent(player, false);
        Transform root = shadowGo.transform;

        // 红一方面军的灰蓝粗布军装：八角帽红五星、红领章、红袖标、绑腿草鞋
        Color cloth = new Color(0.33f, 0.36f, 0.39f);
        Color clothDark = new Color(0.26f, 0.29f, 0.32f);
        Color clothLight = new Color(0.39f, 0.42f, 0.44f);
        Color seam = new Color(0.28f, 0.31f, 0.34f);
        Color skin = new Color(0.67f, 0.5f, 0.38f);
        Color leather = new Color(0.19f, 0.14f, 0.09f);
        Color straw = new Color(0.62f, 0.54f, 0.33f);
        Color strawDark = new Color(0.29f, 0.23f, 0.13f);
        Color red = new Color(0.62f, 0.13f, 0.1f);
        Color dark = new Color(0.1f, 0.09f, 0.08f);
        Color metal = new Color(0.32f, 0.32f, 0.3f);

        GameObject P(PrimitiveType type, string name, Transform parent,
                     Vector3 pos, Vector3 scale, Color color)
        {
            GameObject go = ShadowPart(type, name, parent, pos, scale);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = LevelBuilder.MakeMaterial(color);
            return go;
        }

        // ── 髋部与军装下摆 ──
        P(PrimitiveType.Cube, "Hips", root,
            new Vector3(0f, 0.84f, 0f), new Vector3(0.31f, 0.22f, 0.22f), cloth);

        Transform torsoPivot = new GameObject("TorsoPivot").transform;
        torsoPivot.SetParent(root, false);
        torsoPivot.localPosition = new Vector3(0f, 0.84f, 0f);

        // 躯干：肩宽腰窄，套在髋部上，不再是脖子埋在胸口的铁桶
        P(PrimitiveType.Capsule, "Torso", torsoPivot,
            new Vector3(0f, 0.33f, 0f), new Vector3(0.42f, 0.44f, 0.3f), cloth);
        P(PrimitiveType.Cube, "ChestYoke", torsoPivot,
            new Vector3(0f, 0.45f, 0f), new Vector3(0.36f, 0.14f, 0.28f), clothDark);
        P(PrimitiveType.Cube, "CoatHem", torsoPivot,
            new Vector3(0f, -0.02f, 0f), new Vector3(0.37f, 0.2f, 0.3f), cloth);
        P(PrimitiveType.Cube, "FrontSeam", torsoPivot,
            new Vector3(0f, 0.28f, 0.155f), new Vector3(0.02f, 0.4f, 0.012f), seam);
        // 补丁：粗布军装洗得发白，右胸一块补丁
        GameObject patch = P(PrimitiveType.Cube, "CoatPatch", torsoPivot,
            new Vector3(0.1f, 0.24f, 0.158f), new Vector3(0.09f, 0.1f, 0.008f), clothLight);
        patch.transform.localRotation = Quaternion.Euler(0f, 0f, -6f);

        // 领口：给脑袋垫一圈立领，脖子不会直接插进肩膀
        P(PrimitiveType.Cube, "CollarBand", torsoPivot,
            new Vector3(0f, 0.62f, 0f), new Vector3(0.22f, 0.1f, 0.3f), clothDark);
        // 红领章：贴在领根两侧、略向内倾，不是胸口上两块红补丁
        GameObject collarL = P(PrimitiveType.Cube, "CollarL", torsoPivot,
            new Vector3(-0.055f, 0.64f, 0.152f), new Vector3(0.07f, 0.06f, 0.018f), red);
        collarL.transform.localRotation = Quaternion.Euler(0f, 0f, -10f);
        GameObject collarR = P(PrimitiveType.Cube, "CollarR", torsoPivot,
            new Vector3(0.055f, 0.64f, 0.152f), new Vector3(0.07f, 0.06f, 0.018f), red);
        collarR.transform.localRotation = Quaternion.Euler(0f, 0f, 10f);

        // 胸袋：两个带盖的贴袋，粗布军装的常见样子
        for (int side = 0; side < 2; side++)
        {
            P(PrimitiveType.Cube, "Pocket", torsoPivot,
                new Vector3(side == 0 ? -0.13f : 0.13f, 0.27f, 0.152f),
                new Vector3(0.11f, 0.1f, 0.01f), clothLight);
            P(PrimitiveType.Cube, "PocketFlap", torsoPivot,
                new Vector3(side == 0 ? -0.13f : 0.13f, 0.33f, 0.156f),
                new Vector3(0.115f, 0.025f, 0.012f), seam);
        }

        // 斜挎的干粮袋背带：从左肩挂到右腰
        GameObject strap = P(PrimitiveType.Cube, "ChestStrap", torsoPivot,
            new Vector3(0f, 0.3f, 0.157f), new Vector3(0.05f, 0.58f, 0.012f), leather);
        strap.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
        P(PrimitiveType.Cube, "StrapBag", torsoPivot,
            new Vector3(0.21f, 0.06f, 0.12f), new Vector3(0.11f, 0.14f, 0.07f), leather);

        // 皮带 / 铜扣 / 弹药袋
        P(PrimitiveType.Cube, "Belt", torsoPivot,
            new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.065f, 0.31f), leather);
        P(PrimitiveType.Cube, "Buckle", torsoPivot,
            new Vector3(0f, 0.05f, 0.158f), new Vector3(0.06f, 0.05f, 0.015f), metal);
        P(PrimitiveType.Cube, "PouchL", torsoPivot,
            new Vector3(-0.13f, 0.03f, 0.16f), new Vector3(0.1f, 0.1f, 0.04f), clothDark);
        P(PrimitiveType.Cube, "PouchR", torsoPivot,
            new Vector3(0.13f, 0.03f, 0.16f), new Vector3(0.1f, 0.1f, 0.04f), clothDark);

        // 背包 / 铺盖卷 / 水壶
        P(PrimitiveType.Cube, "Backpack", torsoPivot,
            new Vector3(0f, 0.33f, -0.2f), new Vector3(0.3f, 0.32f, 0.15f), clothDark);
        GameObject bedroll = P(PrimitiveType.Cylinder, "Bedroll", torsoPivot,
            new Vector3(0f, 0.52f, -0.21f), new Vector3(0.085f, 0.17f, 0.085f), strawDark);
        bedroll.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        P(PrimitiveType.Cylinder, "Canteen", torsoPivot,
            new Vector3(0.19f, 0.0f, -0.15f), new Vector3(0.07f, 0.09f, 0.07f),
            new Color(0.27f, 0.31f, 0.24f));

        // ── 头：脸、五官、八角帽、红五星 ──
        Transform headPivot = new GameObject("HeadPivot").transform;
        headPivot.SetParent(torsoPivot, false);
        headPivot.localPosition = new Vector3(0f, 0.76f, 0f);

        P(PrimitiveType.Cylinder, "Neck", headPivot,
            new Vector3(0f, -0.055f, 0f), new Vector3(0.11f, 0.05f, 0.11f), skin);
        P(PrimitiveType.Sphere, "Head", headPivot,
            new Vector3(0f, 0.07f, 0f), new Vector3(0.27f, 0.3f, 0.26f), skin);
        P(PrimitiveType.Cube, "BrowL", headPivot,
            new Vector3(-0.052f, 0.1f, 0.124f), new Vector3(0.058f, 0.014f, 0.02f), dark);
        P(PrimitiveType.Cube, "BrowR", headPivot,
            new Vector3(0.052f, 0.1f, 0.124f), new Vector3(0.058f, 0.014f, 0.02f), dark);
        P(PrimitiveType.Cube, "EyeL", headPivot,
            new Vector3(-0.052f, 0.075f, 0.13f), new Vector3(0.032f, 0.02f, 0.012f), dark);
        P(PrimitiveType.Cube, "EyeR", headPivot,
            new Vector3(0.052f, 0.075f, 0.13f), new Vector3(0.032f, 0.02f, 0.012f), dark);
        P(PrimitiveType.Cube, "Nose", headPivot,
            new Vector3(0f, 0.04f, 0.138f), new Vector3(0.036f, 0.05f, 0.035f), skin);
        P(PrimitiveType.Cube, "Mouth", headPivot,
            new Vector3(0f, 0.005f, 0.132f), new Vector3(0.052f, 0.012f, 0.012f),
            new Color(0.36f, 0.22f, 0.18f));
        P(PrimitiveType.Cube, "EarL", headPivot,
            new Vector3(-0.135f, 0.06f, 0f), new Vector3(0.03f, 0.07f, 0.05f), skin);
        P(PrimitiveType.Cube, "EarR", headPivot,
            new Vector3(0.135f, 0.06f, 0f), new Vector3(0.03f, 0.07f, 0.05f), skin);

        // 八角帽：帽箍压到眉上，帽顶是两个错开 45° 的方块叠成的八角
        P(PrimitiveType.Cylinder, "Cap", headPivot,
            new Vector3(0f, 0.155f, 0f), new Vector3(0.29f, 0.055f, 0.29f), clothDark);
        GameObject crown = P(PrimitiveType.Cube, "CapCrown", headPivot,
            new Vector3(0f, 0.185f, 0f), new Vector3(0.235f, 0.06f, 0.235f), clothDark);
        crown.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        P(PrimitiveType.Cube, "CapTop", headPivot,
            new Vector3(0f, 0.21f, 0f), new Vector3(0.19f, 0.03f, 0.19f), clothDark);
        P(PrimitiveType.Cube, "CapBrim", headPivot,
            new Vector3(0f, 0.118f, 0.075f), new Vector3(0.28f, 0.025f, 0.14f), clothDark);
        // 帽徽五角星：真按十个角切出来的面片，比方块转 45° 像样
        GameObject star = new GameObject("RedStar");
        star.transform.SetParent(headPivot, false);
        star.transform.localPosition = new Vector3(0f, 0.15f, 0.168f);

        Mesh starMesh = new Mesh();
        Vector3[] starVerts = new Vector3[11];
        starVerts[0] = Vector3.zero;
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float r = (i % 2 == 0) ? 0.045f : 0.019f;
            starVerts[i + 1] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
        }
        int[] starTris = new int[30];
        for (int i = 0; i < 10; i++)
        {
            starTris[i * 3] = 0;
            starTris[i * 3 + 1] = i + 1;
            starTris[i * 3 + 2] = (i + 1) % 10 + 1;
        }
        starMesh.vertices = starVerts;
        starMesh.triangles = starTris;
        starMesh.RecalculateNormals();
        star.AddComponent<MeshFilter>().sharedMesh = starMesh;

        MeshRenderer starRenderer = star.AddComponent<MeshRenderer>();
        starRenderer.sharedMaterial = LevelBuilder.MakeMaterial(red);
        starRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        starRenderer.receiveShadows = false;

        // ── 双臂：军装 + 小臂 + 袖口 + 手；左臂扎红袖标 ──
        Transform rightHand = null;
        Transform leftHand = null;
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? -0.275f : 0.275f;
            Transform arm = new GameObject(s == 0 ? "ArmL" : "ArmR").transform;
            arm.SetParent(torsoPivot, false);
            arm.localPosition = new Vector3(x, 0.52f, 0f);

            P(PrimitiveType.Cube, "ArmPart", arm,
                new Vector3(0f, -0.125f, 0f), new Vector3(0.115f, 0.27f, 0.13f), cloth);

            // 小臂单独立一节：卧倒时前臂要能压在雪地上托住枪
            Transform forearm = new GameObject(s == 0 ? "ForearmL" : "ForearmR").transform;
            forearm.SetParent(arm, false);
            forearm.localPosition = new Vector3(0f, -0.26f, 0f);

            P(PrimitiveType.Cube, "ForearmPart", forearm,
                new Vector3(0f, -0.11f, 0f), new Vector3(0.108f, 0.22f, 0.122f), cloth);
            P(PrimitiveType.Cube, "Cuff", forearm,
                new Vector3(0f, -0.215f, 0f), new Vector3(0.116f, 0.05f, 0.13f), clothDark);
            P(PrimitiveType.Sphere, "Hand", forearm,
                new Vector3(0f, -0.255f, 0.01f), new Vector3(0.09f, 0.11f, 0.09f), skin);

            if (s == 0)
            {
                P(PrimitiveType.Cube, "Armband", arm,
                    new Vector3(0f, -0.08f, 0f), new Vector3(0.126f, 0.07f, 0.142f), red);

                // 左手持火把点：举火把时它挂在这只手上
                leftHand = new GameObject("LeftHand").transform;
                leftHand.SetParent(forearm, false);
                leftHand.localPosition = new Vector3(0f, -0.255f, 0.01f);
            }
            else
            {
                // 右手持枪点：第三人称下枪 / 刀挂在这只手上
                rightHand = new GameObject("RightHand").transform;
                rightHand.SetParent(forearm, false);
                rightHand.localPosition = new Vector3(0f, -0.255f, 0.01f);
            }
        }

        // ── 手里的家伙：步枪 / 驳壳枪 / 大刀，切枪时换的就是它们 ──
        void BuildHeldWeapons(Transform hand)
        {
            if (hand == null) return;

            Color steel = new Color(0.55f, 0.57f, 0.6f);

            // 枪身沿小臂方向伸出：绕 Z 转 180°，+Y 就指向手的前方
            Transform heldRifle = new GameObject("HeldRifle").transform;
            heldRifle.SetParent(hand, false);
            heldRifle.localPosition = new Vector3(0f, -0.02f, 0.055f);
            heldRifle.localRotation = Quaternion.Euler(0f, 0f, 180f);
            P(PrimitiveType.Cube, "Stock", heldRifle,
                new Vector3(0f, -0.3f, 0f), new Vector3(0.05f, 0.36f, 0.085f), leather);
            P(PrimitiveType.Cube, "Receiver", heldRifle,
                new Vector3(0f, -0.04f, 0f), new Vector3(0.045f, 0.24f, 0.07f), metal);
            P(PrimitiveType.Cube, "Barrel", heldRifle,
                new Vector3(0f, 0.36f, 0f), new Vector3(0.024f, 0.52f, 0.024f), metal);
            P(PrimitiveType.Cube, "Bolt", heldRifle,
                new Vector3(0.05f, -0.03f, 0f), new Vector3(0.075f, 0.02f, 0.02f), metal);
            P(PrimitiveType.Cube, "RearSight", heldRifle,
                new Vector3(0f, 0.05f, 0.04f), new Vector3(0.03f, 0.02f, 0.025f), dark);
            heldRifle.gameObject.SetActive(false);

            Transform heldPistol = new GameObject("HeldPistol").transform;
            heldPistol.SetParent(hand, false);
            heldPistol.localPosition = new Vector3(0f, -0.03f, 0.03f);
            heldPistol.localRotation = Quaternion.Euler(0f, 0f, 180f);
            P(PrimitiveType.Cube, "Slide", heldPistol,
                new Vector3(0f, 0.1f, 0f), new Vector3(0.05f, 0.3f, 0.06f), metal);
            P(PrimitiveType.Cube, "PistolGrip", heldPistol,
                new Vector3(0f, -0.09f, -0.08f), new Vector3(0.045f, 0.08f, 0.17f), leather);
            heldPistol.gameObject.SetActive(false);

            Transform heldKnife = new GameObject("HeldKnife").transform;
            heldKnife.SetParent(hand, false);
            heldKnife.localPosition = new Vector3(0f, -0.04f, 0.03f);
            heldKnife.localRotation = Quaternion.Euler(0f, 0f, 180f);
            P(PrimitiveType.Cube, "Handle", heldKnife,
                new Vector3(0f, -0.14f, 0f), new Vector3(0.038f, 0.17f, 0.045f), leather);
            P(PrimitiveType.Cube, "Guard", heldKnife,
                new Vector3(0f, -0.045f, 0f), new Vector3(0.12f, 0.022f, 0.035f), metal);
            // 梯形刀身：单边开刃，刃口斜收到平的刀尖
            KnifeBlade.Create("Blade", heldKnife,
                new Vector2(-0.0375f, -0.03f), new Vector2(0.0375f, -0.03f),
                new Vector2(0.0375f, 0.305f), new Vector2(0.002f, 0.305f),
                0.012f, steel, true);
            P(PrimitiveType.Cube, "BladeEdge", heldKnife,
                new Vector3(-0.036f, 0.0425f, 0.002f), new Vector3(0.01f, 0.145f, 0.01f),
                new Color(0.55f, 0.57f, 0.6f));
            GameObject heldTipEdge = P(PrimitiveType.Cube, "BladeTipEdge", heldKnife,
                new Vector3(-0.0178f, 0.21f, 0.002f), new Vector3(0.01f, 0.2f, 0.01f),
                new Color(0.55f, 0.57f, 0.6f));
            heldTipEdge.transform.localRotation = Quaternion.Euler(0f, 0f, -11.7f);
            P(PrimitiveType.Cube, "BladeBack", heldKnife,
                new Vector3(0.034f, 0.135f, 0f), new Vector3(0.011f, 0.34f, 0.014f),
                new Color(0.07f, 0.07f, 0.08f));
            heldKnife.gameObject.SetActive(false);
        }

        BuildHeldWeapons(rightHand);

        // ── 步枪：斜背在背后 ──
        Transform slung = new GameObject("SlungRifle").transform;
        slung.SetParent(torsoPivot, false);
        slung.localPosition = new Vector3(0.02f, 0.3f, -0.3f);
        slung.localRotation = Quaternion.Euler(24f, 0f, 28f);

        P(PrimitiveType.Cube, "RifleStock", slung,
            new Vector3(0f, -0.24f, 0f), new Vector3(0.045f, 0.32f, 0.075f), leather);
        P(PrimitiveType.Cube, "RifleReceiver", slung,
            new Vector3(0f, 0.02f, 0f), new Vector3(0.04f, 0.2f, 0.065f), metal);
        P(PrimitiveType.Cube, "RifleBarrel", slung,
            new Vector3(0f, 0.42f, 0f), new Vector3(0.022f, 0.62f, 0.022f), metal);
        P(PrimitiveType.Cube, "RifleSling", slung,
            new Vector3(0.045f, 0.12f, 0.05f), new Vector3(0.012f, 0.7f, 0.02f), strawDark);

        // ── 火把：不用时别在左腰上，按 Tab 抽到左手举起来 ──
        Color torchWood = new Color(0.24f, 0.15f, 0.08f);
        Color torchCloth = new Color(0.17f, 0.13f, 0.1f);
        Color flameOuter = new Color(1f, 0.52f, 0.12f);
        Color flameInner = new Color(1f, 0.86f, 0.45f);

        // 原点在握把上：手柄朝下，火头与火苗朝上，握在哪里都不会偏
        GameObject BuildTorchProp(string name, Transform parent, bool lit)
        {
            GameObject torchGo = new GameObject(name);
            torchGo.transform.SetParent(parent, false);

            // 柄尾做到 -0.405，让握住原点的位置落在整支火把的 3/5 处；
            // 柄顶顶到 0.13 与火头下沿接上，火头不会悬在柄上面
            P(PrimitiveType.Cube, "TorchHandle", torchGo.transform,
                new Vector3(0f, -0.1375f, 0f), new Vector3(0.036f, 0.535f, 0.036f), torchWood);
            P(PrimitiveType.Cube, "TorchGripWrap", torchGo.transform,
                new Vector3(0f, 0.01f, 0f), new Vector3(0.044f, 0.09f, 0.044f), strawDark);
            P(PrimitiveType.Cube, "TorchHead", torchGo.transform,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.072f, 0.14f, 0.072f), torchCloth);
            // 捆火头的三道铁丝圈
            for (int i = 0; i < 3; i++)
            {
                P(PrimitiveType.Cube, "TorchBand" + i, torchGo.transform,
                    new Vector3(0f, 0.16f + i * 0.04f, 0f),
                    new Vector3(0.082f, 0.014f, 0.082f), new Color(0.34f, 0.3f, 0.26f));
            }

            if (!lit) return torchGo;

            Transform flame = new GameObject("Flame").transform;
            flame.SetParent(torchGo.transform, false);
            flame.localPosition = new Vector3(0f, 0.31f, 0f);
            GameObject outer = P(PrimitiveType.Cube, "TorchFlame", flame,
                new Vector3(0f, 0.04f, 0f), new Vector3(0.13f, 0.24f, 0.13f), flameOuter);
            outer.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            P(PrimitiveType.Cube, "TorchFlameCore", flame,
                new Vector3(0f, 0.045f, 0f), new Vector3(0.062f, 0.14f, 0.062f), flameInner);

            GameObject lightGo = new GameObject("TorchLight");
            lightGo.transform.SetParent(torchGo.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.36f, 0f);
            Light torchLight = lightGo.AddComponent<Light>();
            torchLight.type = LightType.Point;
            torchLight.color = new Color(1f, 0.72f, 0.4f);
            torchLight.range = 11f;
            torchLight.intensity = 2f;
            torchLight.shadows = LightShadows.None;
            FireFlicker flicker = lightGo.AddComponent<FireFlicker>();
            flicker.flame = flame;

            return torchGo;
        }

        GameObject waistTorch = BuildTorchProp("WaistTorch", torsoPivot, false);
        waistTorch.transform.localPosition = new Vector3(-0.2f, -0.1f, 0.09f);
        waistTorch.transform.localRotation = Quaternion.Euler(14f, 0f, 20f);
        waistTorch.SetActive(false);

        if (leftHand != null)
        {
            GameObject heldTorch = BuildTorchProp("HeldTorch", leftHand, true);
            heldTorch.transform.localPosition = new Vector3(0f, -0.02f, 0.03f);
            heldTorch.transform.localRotation = Quaternion.Euler(80f, 0f, -8f);
            heldTorch.SetActive(false);
        }

        // ── 双腿：髋 → 大腿 → 膝 → 小腿 → 绑腿草鞋 ──
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? -0.1f : 0.1f;
            Transform thigh = new GameObject(s == 0 ? "ThighL" : "ThighR").transform;
            thigh.SetParent(root, false);
            thigh.localPosition = new Vector3(x, 0.86f, 0f);

            P(PrimitiveType.Cube, "ThighPart", thigh,
                new Vector3(0f, -0.22f, 0f), new Vector3(0.155f, 0.44f, 0.175f), cloth);

            Transform shin = new GameObject(s == 0 ? "ShinL" : "ShinR").transform;
            shin.SetParent(thigh, false);
            shin.localPosition = new Vector3(0f, -0.44f, 0f);

            P(PrimitiveType.Cube, "ShinPart", shin,
                new Vector3(0f, -0.2f, 0f), new Vector3(0.145f, 0.4f, 0.165f), cloth);
            // 绑腿：小腿缠成一圈圈，颜色比裤子浅
            P(PrimitiveType.Cube, "Puttee", shin,
                new Vector3(0f, -0.25f, 0f), new Vector3(0.155f, 0.19f, 0.175f),
                new Color(0.44f, 0.45f, 0.42f));
            P(PrimitiveType.Cube, "PutteeWrap", shin,
                new Vector3(0f, -0.3f, 0f), new Vector3(0.16f, 0.016f, 0.18f),
                new Color(0.36f, 0.37f, 0.35f));
            P(PrimitiveType.Cube, "PutteeWrap2", shin,
                new Vector3(0f, -0.36f, 0f), new Vector3(0.16f, 0.016f, 0.18f),
                new Color(0.36f, 0.37f, 0.35f));
            // 草鞋挂在脚踝上：卧倒时脚踝一转，脚背就能贴地
            Transform foot = new GameObject(s == 0 ? "FootL" : "FootR").transform;
            foot.SetParent(shin, false);
            foot.localPosition = new Vector3(0f, -0.44f, 0f);

            P(PrimitiveType.Cube, "StrawShoe", foot,
                new Vector3(0f, 0.025f, 0.045f), new Vector3(0.17f, 0.075f, 0.26f), straw);
            P(PrimitiveType.Cube, "ShoeSole", foot,
                new Vector3(0f, -0.015f, 0.045f), new Vector3(0.175f, 0.022f, 0.27f), strawDark);
        }

        playerBody = player.gameObject.AddComponent<PlayerShadowBody>();
        playerBody.Initialize(root, movement);
    }

    private static GameObject ShadowPart(PrimitiveType type, string name, Transform parent,
                                         Vector3 localPos, Vector3 localScale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            // 只投影不显示：相机看不到身体，但太阳会在地上画出完整人影
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            renderer.receiveShadows = false;
        }

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        return go;
    }

    private static GameObject ModelPrim(PrimitiveType type, string name, Transform parent,
                                        Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = LevelBuilder.MakeMaterial(color);
            // 第一人称视图模型不参与阴影，否则枪影会投在雪地/桥面上
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        return go;
    }

    private void BuildAmbience()
    {
        GameObject ambience = new GameObject("Ambience");
        AudioSource windSource = ambience.AddComponent<AudioSource>();
        windSource.clip = clips.wind;
        windSource.loop = true;
        windSource.volume = 0.3f;
        windSource.spatialBlend = 0f;
        windSource.Play();
    }

    private void BuildSpawner()
    {
        GameObject spawnerGo = new GameObject("EnemySpawner");
        spawner = spawnerGo.AddComponent<EnemySpawner>();
        // 第二关是黔北河谷，川军为主、黔军掺杂；第一关以中央军为主
        EnemySpawner.SetFactionPool(isChishui);
        spawner.Initialize(player, cachedSpawnPoints, clips.gunshot, levelRoot);
        spawner.SetActive(false);

        if (isChishui) return;

        GameObject columnGo = new GameObject("CrossingColumn");
        columnGo.transform.SetParent(levelRoot, true);
        column = columnGo.AddComponent<CrossingColumn>();
        column.Initialize(levelRoot, HandleBatchCrossed);
    }

    private void HandleBatchCrossed(int batches)
    {
        if (!InCombat) return;
        hud.SetObjective("任务：掩护主力渡江　已过 " + batches + "/" + CrossingColumn.Batches + " 批");
    }

    private static readonly string[] AllyNames =
    {
        "老赵", "小刘", "铁柱", "二娃", "石头",
        "大顺", "春生", "栓子", "满仓", "水生"
    };

    /// <summary>四名战友跟在玩家身边守渡口，出生点就在玩家四周几米内。</summary>
    private void BuildAllies()
    {
        if (isChishui)
        {
            BuildChishuiSquad();
            return;
        }

        // 两侧沙袋后各一名，身后两名，再加一个号兵：都在玩家 5 米内，又不挡桥口的射界
        Vector3 c = playerSpawn;
        Vector3[] posts =
        {
            c + new Vector3(-4.2f, -0.08f, 1.8f),
            c + new Vector3(4.2f, -0.08f, 1.8f),
            c + new Vector3(-2.2f, -0.08f, -1.6f),
            c + new Vector3(2.2f, -0.08f, -1.6f),
            c + new Vector3(0f, -0.08f, -3.8f)
        };

        AllyAI.SquadSize = posts.Length;

        for (int i = 0; i < posts.Length; i++)
        {
            string allyName = AllyNames[i % AllyNames.Length];

            GameObject go = new GameObject("Ally_" + allyName);
            go.tag = "Ally";
            go.transform.SetParent(levelRoot, true);
            go.transform.position = posts[i];
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(-10f, 10f), 0f);

            EnemySpawner.BuildBody(go.transform, true);
            go.AddComponent<Health>();

            AllyAI ally = go.AddComponent<AllyAI>();
            ally.Initialize(allyName, posts[i], clips.gunshot, clips.reload);

            // 桥上打得昏黑，留一个举火把的照明兵；最后一名是号兵
            ally.SetTorchBearer(i == 1);
            ally.SetBugler(i == posts.Length - 1);
        }
    }

    private const int ChishuiSquadSize = 10;

    /// <summary>
    /// 第二关：一个十人的排跟着玩家走完四次渡河。
    /// 他们不认路，靠玩家留下的足迹过河，初生点在玩家身后拉成两列纵队。
    /// </summary>
    private void BuildChishuiSquad()
    {
        AllyAI.SquadSize = ChishuiSquadSize;
        AllyAI.ClearTrail();

        Quaternion yaw = Quaternion.Euler(0f, playerYaw, 0f);

        for (int i = 0; i < ChishuiSquadSize; i++)
        {
            float behind = 3f + i * 1.45f;
            float side = (i % 2 == 0 ? -1f : 1f) * (1.3f + (i / 2 % 3) * 0.85f);

            Vector3 post = playerSpawn + yaw * new Vector3(side, 0f, -behind);
            float ground = ChishuiLevelBuilder.GroundHeight(post.x, post.z);
            post.y = Mathf.Max(ground, 0.15f) + 0.05f;

            string allyName = AllyNames[i % AllyNames.Length];

            GameObject go = new GameObject("Ally_" + allyName);
            go.tag = "Ally";
            go.transform.SetParent(levelRoot, true);
            go.transform.position = post;
            go.transform.rotation = Quaternion.Euler(0f, playerYaw + Random.Range(-8f, 8f), 0f);

            EnemySpawner.BuildBody(go.transform, true);
            go.AddComponent<Health>();

            AllyAI ally = go.AddComponent<AllyAI>();
            ally.Initialize(allyName, post, clips.gunshot, clips.reload);
            ally.SetFollow(3.2f + i * 1.55f, side);
            // 每个班留一个照明兵：火把 + 驳壳枪，夜里给全队照路
            ally.SetTorchBearer(i % 3 == 0);
            // 队伍中段跟一名号兵，第 90 秒吹冲锋号
            if (i == 2) ally.SetBugler(true);
        }
    }

    // ───────────────────────────── 主循环 ─────────────────────────────

    private void Update()
    {
        if (paused)
        {
            TickPauseMenu();
            return;
        }

        if (InCombat && Input.GetKeyDown(KeyCode.Escape))
        {
            PauseGame();
            return;
        }

        // 火堆旁缓慢恢复生命：让火堆成为真正的补给点
        if ((state == GameState.Playing || state == GameState.Bridge)
            && cold != null && cold.IsNearFire
            && playerHealth != null && !playerHealth.IsDead)
        {
            playerHealth.Restore(5f * Time.deltaTime);
        }

        // 第 90 秒吹冲锋号：全队振奋 15 秒，期间主角每杀一人回 5 滴血
        if ((state == GameState.Playing || state == GameState.Bridge || state == GameState.Guerrilla)
            && !bugleBlown && Time.time - levelStartTime >= BugleDelay)
        {
            bugleBlown = true;
            if (AllyAI.SoundBugle())
            {
                AllyAI.BeginRally(RallyDuration);
                killHealUntil = Time.time + RallyDuration;
                hud.ShowMessage("冲锋号响了！全排振奋 15 秒 —— 移动 +20%、射击更准、杀敌回血", 6f);
            }
        }

        switch (state)
        {
            case GameState.Title:
                if (ReturnPressed()) StartSelectedLevel();
                break;

            case GameState.Briefing:
                if (ReturnPressed())
                {
                    hud.HideBriefing();
                    StartGame();
                }
                break;

            case GameState.Playing:
                UpdateDefensePhase();
                break;

            case GameState.Bridge:
                UpdateBridgePhase();
                break;

            case GameState.Guerrilla:
                // 记下玩家的足迹，身后十个人跟着这条线过河
                if (player != null) AllyAI.RecordTrail(player.position);
                if (chishui != null) chishui.Tick();
                break;

            case GameState.Won:
            case GameState.Lost:
                if (ReturnPressed()) Restart();
                break;
        }
    }

    /// <summary>标题页选关：换关走整场景重载，关卡状态全部重新生成。</summary>
    private void StartSelectedLevel()
    {
        int chosen = hud != null ? hud.SelectedLevel : 1;

        if (chosen != SelectedLevel)
        {
            SelectedLevel = chosen;
            pendingAutoStart = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        ShowBriefing();
    }

    /// <summary>战役开始前的故事与真实历史背景。这里只做展示，按 Enter 才真正开局。</summary>
    private void ShowBriefing()
    {
        hud.HideTitlePanel();
        hud.SetHudVisible(false);
        SetPlayerControl(false);
        state = GameState.Briefing;

        if (isChishui)
        {
            hud.ShowBriefing(
                "战前 · 赤水河谷",
                "1935 年 1 月，红军进抵川黔滇边区的赤水河。四十万国民党军从四面压来，" +
                "身后是乌江，前面是长江。毛泽东决定不走直线——在土城、太平渡、二郎滩、茅台之间四次渡河，" +
                "把追兵拖进大山里来回打转。你是尖兵：不守阵地，只找缝隙。",
                "四渡赤水发生于 1935 年 1 月至 3 月。红军在三个多月里四次渡过赤水河，" +
                "声东击西、避实击虚，牵着国民党军在川黔滇边区兜圈子，是长征中最著名的一次运动战。",
                "红军由此跳出了四十万敌军的包围圈，重新掌握战略主动权，" +
                "为随后巧渡金沙江、强渡大渡河赢得了时间。四渡赤水被称为毛泽东军事指挥艺术的代表作。",
                "土城 → 太平渡 → 二郎滩 → 茅台 → 娄山关。救回青杠坡的伤员、点燃假火堆调动敌军、" +
                "在茅台镇补充弹药，最后抢在追兵前面冲上娄山关关顶。");
            return;
        }

        hud.ShowBriefing(
            "战前 · 湘江界首",
            "1934 年 11 月底，中央红军八万余人抵达湘江。三十万敌军在两岸布下口袋阵，" +
            "界首浮桥是全军唯一的生路。桥在，队伍就能过江；桥断，就是全军覆没。" +
            "你守在西岸阵地上，要做的只有一件事：守住，让战友一批批踏过江去。",
            "湘江战役是长征出发以来最惨烈的一仗。红军以巨大代价突破国民党军第四道封锁线，" +
            "粉碎了蒋介石“聚歼红军于湘江以东”的计划，但也付出了建军以来最惨重的损失。",
            "此役之后，红军由八万余人锐减至三万余人，却保住了党中央和革命的核心力量。" +
            "1935 年 1 月，遵义会议召开，确立了毛泽东在党和红军中的领导地位，长征迎来伟大转折。",
            "守住西岸防线，掩护主力 8 批渡江；最后前往桥头，按住 G 炸毁浮桥，切断追兵。");
    }

    private void StartGame()
    {
        hud.HideTitlePanel();
        hud.HideBriefing();
        hud.SetHudVisible(true);
        hud.ResetCombatFeedback();
        SetPlayerControl(true);
        holdProgress = 0f;
        levelStartTime = Time.time;
        enemyKills = 0;

        if (isChishui)
        {
            StartGuerrilla();
            return;
        }

        state = GameState.Playing;
        defenseTimer = defenseDuration;
        AllyAI.SetActive(true);

        // 第一关的炮击全部来自对岸炮兵阵地，不再由系统凭空落弹
        if (artillery != null) artillery.SetActive(false);
        if (battery != null) battery.SetActive(true);
        if (column != null) column.Begin();

        hud.SetObjective("任务：掩护主力渡江　已过 0/" + CrossingColumn.Batches + " 批");
        hud.SetTimerText("坚守 " + FormatTime(defenseTimer));
        hud.ShowMessage("1934 年冬 · 湘江界首渡口\n大部队正在过江。守住浮桥，别让追兵贴上桥面；弹药极少，从阵亡敌军身上搜刮", 8f);

        spawner.SetActive(true);
    }

    private void StartGuerrilla()
    {
        state = GameState.Guerrilla;

        AllyAI.SetActive(true);
        if (spawner != null) spawner.SetActive(false);
        if (artillery != null) artillery.SetActive(false);

        hud.SetObjective("四渡赤水：土城 → 太平渡 → 二郎滩 → 茅台 → 娄山关");
        hud.SetPrompt("", 0f);
        hud.SetEvacuation(-1f);

        if (chishui != null) chishui.Begin();
    }

    private int woundedRescued;

    private void HandleWoundedRescued()
    {
        woundedRescued++;

        if (woundedRescued >= 3 && hud != null)
        {
            hud.ShowMessage("伤员全部救回 · 全排士气高涨", 4f);
        }
    }

    private void HandleChishuiComplete()
    {
        if (state != GameState.Guerrilla) return;
        Win();
    }

    private void UpdateDefensePhase()
    {
        defenseTimer -= Time.deltaTime;

        if (defenseTimer <= 0f)
        {
            BeginBridgePhase();
            return;
        }

        hud.SetTimerText("坚守 " + FormatTime(defenseTimer));
        if (column != null) hud.SetEvacuation(column.EvacuationProgress);
    }

    private void BeginBridgePhase()
    {
        state = GameState.Bridge;
        holdProgress = 0f;

        spawner.SetActive(false);
        spawner.StopAllEnemies();
        AllyAI.SetActive(false);
        if (column != null) column.Stop();
        hud.SetEvacuation(-1f);

        hud.SetTimerText("断后 · 炸毁浮桥");
        hud.SetObjective("任务：前往桥头，炸毁浮桥，切断追兵");
        hud.ShowMessage("大部队已经过江。现在，把桥留给他们。", 5f);
        if (bridgePoint != null) hud.SetWaypoint(bridgePoint, "浮桥桥头");
    }

    private void UpdateBridgePhase()
    {
        if (detonating)
        {
            detonateTimer -= Time.deltaTime;

            // 等桥塌完再进结算，别让胜利画面盖住最要紧的几秒
            if (detonateTimer <= 0f) Win();
            return;
        }

        Vector3 playerFlat = new Vector3(player.position.x, 0f, player.position.z);
        Vector3 bridgeFlat = new Vector3(bridgePoint.position.x, 0f, bridgePoint.position.z);

        float distance = Vector3.Distance(playerFlat, bridgeFlat);

        if (distance > BridgeInteractDistance)
        {
            holdProgress = 0f;
            hud.SetPrompt("前往桥头安放炸药", 0f);
            return;
        }

        if (Input.GetKey(KeyCode.G))
        {
            holdProgress += Time.deltaTime;
            hud.SetPrompt("正在安放炸药…… 按住 G", holdProgress / HoldDuration);

            if (holdProgress >= HoldDuration) Detonate();
        }
        else
        {
            holdProgress = Mathf.Max(0f, holdProgress - Time.deltaTime * 1.5f);
            hud.SetPrompt("按住 G 炸毁浮桥", holdProgress / HoldDuration);
        }
    }

    /// <summary>桥头起爆：主角先退回友军阵地，再隔着整条江看浮桥断裂沉江。</summary>
    private void Detonate()
    {
        if (detonating) return;

        detonating = true;
        detonateTimer = 6.8f;
        holdProgress = 0f;

        hud.SetPrompt("", 0f);
        hud.SetObjective("任务：浮桥已断，追兵过不了江");
        hud.ClearWaypoint();

        StartCoroutine(DetonationSequence());
    }

    private IEnumerator DetonationSequence()
    {
        if (!photoCapture)
        {
            // 黑场换机位：安完炸药退回友军阵地，别让玩家站在会沉下去的桥板上
            SetPlayerControl(false);
            if (movement != null) movement.ForceStanding();
            if (cameraRig != null)
            {
                cameraRig.SetThirdPerson(false);
                cameraRig.enabled = false;
            }

            float fade = 0f;
            while (fade < 1f)
            {
                fade = Mathf.Min(1f, fade + Time.deltaTime * 3.4f);
                hud.SetFade(fade);
                yield return null;
            }

            Vector3 vantage = new Vector3(-2.2f, 0.4f, 6.6f);
            if (Physics.Raycast(new Vector3(vantage.x, vantage.y + 12f, vantage.z), Vector3.down,
                                out RaycastHit ground, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                vantage.y = ground.point.y + 0.05f;
            }

            player.position = vantage;
            if (mouseLook != null) mouseLook.SetLook(10f, 2f);

            yield return new WaitForSeconds(0.2f);

            while (fade > 0f)
            {
                fade = Mathf.Max(0f, fade - Time.deltaTime * 2.2f);
                hud.SetFade(fade);
                yield return null;
            }
        }

        BridgeDemolition.Play(bridgeRoot, player, null, feedbackAudio, clips.explosion);
        PlayFeedback(clips.explosion, 1f);
        hud.ShowMessage("炸药起爆 —— 浮桥从中间断开，桥板裹着浮箱沉进江里。", 6f);
    }

    /// <summary>结算演出切到白天：正午光照，风雪、冻雨和雪幕一起收掉。</summary>
    private void EnterVictoryDaylight()
    {
        DynamicWeather weather = DynamicWeather.Active;
        if (weather != null) weather.SetDaylight();

        ChishuiWeather storm = FindObjectOfType<ChishuiWeather>();
        if (storm != null) storm.enabled = false;

        if (player != null)
        {
            Transform snow = player.Find("Snowfall");
            if (snow != null) Destroy(snow.gameObject);
        }
        if (cold != null) cold.enabled = false;
    }

    private void Win()
    {
        if (state == GameState.Won) return;
        state = GameState.Won;

        // 结算镜头是独立演出，主角不能带着蹲 / 趴的姿态进场
        if (movement != null) movement.ForceStanding();

        if (spawner != null)
        {
            spawner.SetActive(false);
            spawner.StopAllEnemies();
        }
        AllyAI.SetActive(false);
        if (artillery != null) artillery.SetActive(false);
        if (battery != null) battery.SetActive(false);
        if (chishui != null) chishui.Stop();
        if (column != null) column.Stop();
        SetPlayerControl(false);

        hud.SetPrompt("", 0f);
        hud.ClearWaypoint();
        hud.SetHudVisible(false);

        // 结算演出：把第一人称枪模收起来，镜头交给战场中央的主角
        if (weapon != null) weapon.SetViewModelVisible(false);
        if (cameraRig != null) cameraRig.enabled = false;
        if (cameraShake != null) cameraShake.enabled = false;

        Camera cam = Camera.main;
        Vector3 stage;
        float stageYaw;

        if (isChishui)
        {
            // 关顶平台，朝向关门与石阶，主角从关门外走上来
            stage = new Vector3(30f, 11.35f, 306f);
            stageYaw = 180f;
        }
        else
        {
            // 桥头：浮桥已经炸断沉江，主角站在岸上回望
            stage = new Vector3(0f, 2.6f, 15.2f);
            stageYaw = 180f;
        }

        if (cam != null)
        {
            EnterVictoryDaylight();
            cam.transform.SetPositionAndRotation(stage, Quaternion.Euler(3f, stageYaw, 0f));
            cam.fieldOfView = 52f;
            VictoryScene.Play(cam.transform, stage, stageYaw);
        }

        int seconds = Mathf.Max(1, Mathf.RoundToInt(Time.time - levelStartTime));
        string stats = "战功 " + hud.Score + "　　歼敌 " + enemyKills
                       + "　　爆头 " + hud.HeadshotKills;
        if (isChishui) stats += "　　救回伤员 " + woundedRescued + "/3";
        stats += "　　用时 " + FormatTime(seconds);

        if (isChishui)
        {
            hud.ShowVictory("四渡赤水 · 娄山关已在我手", stats,
                "【历史】1935 年 1 至 3 月，中央红军四次渡过赤水河，在川黔滇边区的群山间来回穿插，\n" +
                "最终跳出四十万敌军的包围圈。四渡赤水，从此写进了世界军事史。",
                "按 Enter 重新开始");
            return;
        }

        hud.ShowVictory("主力已过江", stats,
            "【历史】湘江一战，红军由八万余人锐减至三万余人，却保住了党中央和革命的核心力量。\n" +
            "1935 年 1 月的遵义会议，成为中国革命由挫折走向胜利的转折点。",
            "按 Enter 重新开始");
    }

    private void Lose()
    {
        state = GameState.Lost;

        if (spawner != null)
        {
            spawner.SetActive(false);
            spawner.StopAllEnemies();
        }
        AllyAI.SetActive(false);
        if (artillery != null) artillery.SetActive(false);
        if (battery != null) battery.SetActive(false);
        if (chishui != null) chishui.Stop();
        if (column != null) column.Stop();
        SetPlayerControl(false);

        hud.SetPrompt("", 0f);
        hud.SetHudVisible(false);

        if (isChishui)
        {
            hud.ShowEnd(false, "倒在赤水河畔",
                "河水带走了你的体温。穿插失败的代价，就是被追兵咬住。\n\n"
                + "歼敌 " + enemyKills + " 人　爆头 " + hud.HeadshotKills + " 次\n"
                + "战功 " + hud.Score + "\n\n"
                + "按 Enter 重新开始");
            return;
        }

        hud.ShowEnd(false, "阵地失守",
            "你倒在了湘江边。\n\n"
            + "阻击歼敌 " + enemyKills + " 人　爆头 " + hud.HeadshotKills + " 次\n"
            + "战功 " + hud.Score + "\n\n"
            + "按 Enter 重新开始");
    }

    private void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ───────────────────────────── ESC 暂停 ─────────────────────────────

    private void PauseGame()
    {
        if (paused) return;

        paused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        SetPlayerControl(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        CapturePauseBackdrop();
        if (hud != null) hud.ShowPauseMenu(true, BuildPauseStats(), pauseBackdrop);
    }

    private void ResumeGame()
    {
        if (!paused) return;

        paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (hud != null) hud.ShowPauseMenu(false, "", null);
        ReleasePauseBackdrop();
        SetPlayerControl(true);
    }

    private void TickPauseMenu()
    {
        // 时间停了，鼠标必须解锁才点得到按钮
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        int action = hud != null ? hud.ConsumePauseAction() : 0;
        if (action == 0 && Input.GetKeyDown(KeyCode.Escape)) action = 1;

        switch (action)
        {
            case 1: ResumeGame(); break;
            case 2: RestartLevelNow(); break;
            case 3: ReturnToCampaignSelect(); break;
            case 4: QuitGame(); break;
        }
    }

    /// <summary>暂停背景：把主相机渲进一张很小的贴图，拉伸回全屏后天然就是虚化。</summary>
    private void CapturePauseBackdrop()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        ReleasePauseBackdrop();

        int width = 220;
        int height = Mathf.Max(1,
            Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width)));

        pauseBackdrop = new RenderTexture(width, height, 16, RenderTextureFormat.Default);
        pauseBackdrop.filterMode = FilterMode.Bilinear;
        pauseBackdrop.Create();

        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = pauseBackdrop;
        cam.Render();
        cam.targetTexture = previous;
    }

    private void ReleasePauseBackdrop()
    {
        if (pauseBackdrop == null) return;
        pauseBackdrop.Release();
        Destroy(pauseBackdrop);
        pauseBackdrop = null;
    }

    private string BuildPauseStats()
    {
        int seconds = Mathf.Max(1, Mathf.RoundToInt(Time.time - levelStartTime));
        string level = isChishui ? "四渡赤水 · 河谷穿插" : "湘江界守 · 西岸阻击";
        int score = hud != null ? hud.Score : 0;
        int heads = hud != null ? hud.HeadshotKills : 0;

        return "战役　" + level + "\n"
            + "战功　" + score + "\n"
            + "歼敌　" + enemyKills + " 人\n"
            + "爆头　" + heads + " 次\n"
            + "用时　" + FormatTime(seconds) + "\n"
            + "状态　" + (playerHealth != null && !playerHealth.IsDead ? "在场" : "倒下");
    }

    private void RestartLevelNow()
    {
        paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (hud != null) hud.ShowPauseMenu(false, "", null);
        ReleasePauseBackdrop();

        // 重开当前关：跳过战前简报，直接回到战斗
        pendingAutoStart = true;
        skipBriefingOnce = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ReturnToCampaignSelect()
    {
        paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (hud != null) hud.ShowPauseMenu(false, "", null);
        ReleasePauseBackdrop();

        // 回标题页，让玩家重新选战役
        pendingAutoStart = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void QuitGame()
    {
        paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }


    // ───────────────────────────── 事件 ─────────────────────────────

    private void HandleAnyDied(Health health)
    {
        if (health != null && health.CompareTag("Enemy")) enemyKills++;
    }

    /// <summary>玩家打中敌人：命中标记 + 命中音，爆头用更亮的音。</summary>
    private void HandleAnyDamaged(Health health, DamageInfo info)
    {
        if (health == null) return;

        // 中弹血花：玩家自己的血走屏幕血渍，不在地上撒血珠
        if (!health.CompareTag("Player"))
        {
            BloodEffects.Burst(info.Point, info.Normal, info.Amount);
        }

        if (!health.CompareTag("Enemy")) return;
        if (info.Source == null || !info.Source.CompareTag("Player")) return;
        if (!InCombat) return;

        bool head = info.Zone == BodyZone.Head;
        hud.ReportHit(head);

        if (weapon != null && weapon.IsKnife)
        {
            PlayFeedback(clips.meleeHit, 0.5f);
        }
        else
        {
            PlayFeedback(head ? clips.headshotTick : clips.hitTick, 0.5f);
        }
    }

    /// <summary>击杀归属：自己击杀 / 战友击杀 / 战友倒下。</summary>
    private void HandleAnyKilled(Health health, DamageInfo info)
    {
        if (health == null) return;

        if (!health.CompareTag("Player"))
        {
            BloodEffects.Pool(health.transform.position);
        }

        if (health.CompareTag("Ally"))
        {
            hud.ReportAllyDown(AllyAI.NameOf(health.gameObject));
            PlayFeedback(clips.meleeHit, 0.35f);
            return;
        }

        if (!health.CompareTag("Enemy") || info.Source == null) return;
        if (!InCombat) return;

        if (info.Source.CompareTag("Player"))
        {
            hud.ReportKill(info.Zone == BodyZone.Head,
                weapon != null ? weapon.ShortName : "汉阳造");
            PlayFeedback(clips.killConfirm, 0.62f);

            // 冲锋号振奋期间：主角每杀一人回 5 滴血
            if (Time.time < killHealUntil && playerHealth != null && !playerHealth.IsDead)
            {
                playerHealth.Restore(5f);
                hud.ReportHeal(5f);
            }
        }
        else if (info.Source.CompareTag("Ally"))
        {
            hud.ReportAllyKill(AllyAI.NameOf(info.Source), "汉阳造");
        }
    }

    private void PlayFeedback(AudioClip clip, float volume)
    {
        if (clip != null && feedbackAudio != null)
        {
            feedbackAudio.PlayOneShot(clip, volume);
        }
    }

    /// <summary>玩家中弹：镜头一震，身体一顿，枪口被带偏。</summary>
    private void HandlePlayerDamaged(DamageInfo info)
    {
        if (!InCombat) return;

        float severity = Mathf.Clamp(info.Amount * 0.016f, 0.22f, 0.9f);

        if (cameraShake != null) cameraShake.Shake(severity);
        if (playerBody != null) playerBody.Flinch(severity);
        if (weapon != null) weapon.AddHitKick(severity);
    }

    private void HandlePlayerDied(DamageInfo info)
    {
        if (InCombat) Lose();
    }

    private bool InCombat
    {
        get
        {
            return state == GameState.Playing || state == GameState.Bridge
                   || state == GameState.Guerrilla;
        }
    }

    private void SetPlayerControl(bool enabled)
    {
        if (movement != null) movement.enabled = enabled;
        if (lean != null) lean.SetActiveControl(enabled);
        if (mouseLook != null) mouseLook.enabled = enabled;
        if (faith != null) faith.enabled = enabled;
        if (grenades != null) grenades.enabled = enabled;
        if (torch != null) torch.SetActiveControl(enabled);

        // 武器输入由武器槽统一管理：只启用当前武器，避免步枪和大刀抢左键
        if (weapon != null)
        {
            weapon.SetActiveControl(enabled);
        }
        else
        {
            if (melee != null) melee.enabled = enabled;
            if (rifle != null) rifle.enabled = enabled;
        }

        if (enabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static bool ReturnPressed()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
    }

    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        return minutes.ToString("00") + ":" + secs.ToString("00");
    }
}
