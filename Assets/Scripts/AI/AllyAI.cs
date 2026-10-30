using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 红军战友 AI：守在渡口沙袋后，替玩家分担火力。
///
/// 行为刻意做得“不像机器”：
///   - 发现目标后有 0.3～0.7 秒的反应时间，不会瞬间开枪；
///   - 命中率随持续瞄准提高，刚开火时明显打偏；
///   - 打 2～4 发就停顿、换弹，偶尔缩回沙袋后换位；
///   - 中弹会下意识蹲低，躲几秒再探头；
///   - 久未中弹会慢慢缓过来。
/// 战友不会误伤玩家，玩家也不会误伤战友。
/// </summary>
[DisallowMultipleComponent]
public class AllyAI : MonoBehaviour
{
    public static readonly List<AllyAI> All = new List<AllyAI>();

    /// <summary>本局战友编制人数。</summary>
    public static int SquadSize { get; set; }

    /// <summary>是否允许开火与换位（标题 / 炸桥阶段关闭）。</summary>
    public static bool CombatEnabled { get; set; }

    public static int AliveCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] != null && !All[i].IsDead) count++;
            }
            return count;
        }
    }

    public static string NameOf(GameObject source)
    {
        if (source == null) return "战友";
        AllyAI ally = source.GetComponent<AllyAI>();
        return ally != null ? ally.AllyName : "战友";
    }

    public static void SetActive(bool value)
    {
        CombatEnabled = value;
    }

    [Header("身份")]
    [SerializeField] private string allyName = "战友";

    [Header("射击")]
    [SerializeField] private float fireRange = 65f;
    [SerializeField] private float damageTorso = 22f;
    [SerializeField] private float damageHead = 45f;
    [SerializeField] private int magazineSize = 8;

    [Header("性格（越小越准）")]
    [SerializeField] private float skill = 1f;

    [Header("近战拼刀")]
    [SerializeField] private float meleeRange = 2.7f;
    [SerializeField] private float meleeDamage = 20f;
    [SerializeField] private float meleeInterval = 1.0f;

    private Transform knife;
    private float meleeTimer;
    private float meleeBlend;
    private float meleeSwing;
    private const float MeleeSwingDuration = 0.4f;
    private static AudioClip meleeClipCache;

    // ── 火把与手枪（第二关夜行军照明） ──
    private Transform torch;
    private Light torchLight;
    private Transform pistolProp;
    private bool torchBearer;
    private MuzzleFlash pistolFlash;
    private float pistolKick;
    private float torchSeed;

    // ── 号兵与振奋 ──
    private Transform bugle;
    private bool bugler;
    private float bugleTimer;
    private float bugleBlend;
    private static AudioClip bugleClipCache;

    // ── 掩体节奏：躲 → 起身/探头打一梭子 → 再躲 ──
    private bool inCover;
    private float coverHideTimer;
    private float coverPeekTimer;
    private float peekBlend;
    private float peekSide = 1f;

    private static float rallyUntil;

    /// <summary>号声响起：15 秒内战友移速 +20%、射击散布减半。</summary>
    public static void BeginRally(float duration)
    {
        rallyUntil = Time.time + Mathf.Max(0.1f, duration);
    }

    public static bool Rallied => Time.time < rallyUntil;

    public bool IsBugler => bugler;

    // 体检用：看战友是不是真的在掩体后躲、蹲、趴
    public bool InCover => inCover;
    public bool Hiding => inCover && coverHideTimer > 0f;
    public bool Peeking => inCover && coverHideTimer <= 0f && proneTimer <= 0f;
    public int HideCount => hideCount;
    private int hideCount;

    /// <summary>让编制里还活着的号兵吹号；号兵阵亡了就没人吹。</summary>
    public static bool SoundBugle()
    {
        for (int i = 0; i < All.Count; i++)
        {
            AllyAI ally = All[i];
            if (ally != null && ally.bugler && !ally.IsDead)
            {
                ally.BlowBugle();
                return true;
            }
        }
        return false;
    }

    private static AudioClip BugleClip
    {
        get
        {
            if (bugleClipCache == null) bugleClipCache = ProceduralAudio.Bugle();
            return bugleClipCache;
        }
    }

    private static AudioClip MeleeClip
    {
        get
        {
            if (meleeClipCache == null) meleeClipCache = ProceduralAudio.Swing();
            return meleeClipCache;
        }
    }

    public string AllyName => allyName;
    public bool IsDead => dead || (health != null && health.IsDead);

    private Health health;
    private AudioSource audioSource;
    private LineRenderer tracer;
    private AudioClip shotClip;
    private AudioClip reloadClip;

    private Transform pose;
    private Transform torso;
    private Transform hipL;
    private Transform hipR;
    private Transform kneeL;
    private Transform kneeR;
    private Transform shoulderL;
    private Transform shoulderR;
    private Transform elbowL;
    private Transform elbowR;
    private Transform rifle;
    private Transform boltHandle;
    private MuzzleFlash muzzleFlash;
    private Vector3 boltHomePos;
    private Quaternion boltHomeRot;
    private float boltTimer;
    private float rifleBlend;
    private const float BoltDuration = 0.95f;
    private static AudioClip boltClipCache;

    private static AudioClip BoltClip
    {
        get
        {
            if (boltClipCache == null) boltClipCache = ProceduralAudio.Bolt();
            return boltClipCache;
        }
    }

    private float crouchBlend;
    private float proneBlend;
    private float kneelWeight;
    // 本帧实际挪动的水平速度，蹲着不动时才跪下去
    private float planarSpeed;
    private Vector3 lastGroundPos;
    // 开发截图用：1 蹲、2 趴、0 照常
    private int captureStance;

    private Vector3 home;
    private Vector3 moveTarget;
    private float homeYaw;
    private bool moving;
    private float moveSpeed = 1.6f;

    // 卡住检测：想走却挪不动时横着绕半步
    private Vector3 moveAnchor;
    private float moveStuckTimer;
    private float detourTimer;
    private Vector3 detourDir;
    private int detourFlips;

    // 战斗走位与闪避
    private float combatMoveTimer;
    private float dodgeTimer;
    private Vector3 dodgeTarget;
    private float proneTimer;
    private float proneCooldown;
    private float trenchTimer;

    private Transform target;
    private float retargetTimer;
    private float reactionTimer;
    private float trackTime;
    private float fireTimer;
    private int burstLeft;
    private int magazine;
    private float reloadTimer;
    private bool reloading;
    private float crouchTimer;
    private float lastHurtTime = -99f;
    private float repositionTimer;
    private float healTimer;

    private float walkPhase;
    private float walkBlend;
    private float idlePhase;
    private bool dead;

    // ── 行军跟随（第二关） ──
    // 队友不认路，只跟着玩家走过的足迹过河：跳石、索桥、石堤、断桥
    // 都是一次只容一人通过的窄路，直接朝玩家直线走会掉进河里。
    private bool following;
    private float followGap;
    private float followSide;
    private Vector3 followPoint;
    private float safeY;
    private bool haveSafeY;
    private Vector3 lastDryPoint;
    private bool hasDryPoint;

    private static readonly List<Vector3> Trail = new List<Vector3>();
    private const float TrailSpacing = 0.7f;
    private const int TrailCapacity = 900;

    /// <summary>记录玩家足迹，供队友跟随。GameBootstrap 每帧调用。</summary>
    public static void RecordTrail(Vector3 position)
    {
        if (Trail.Count == 0)
        {
            Trail.Add(position);
            return;
        }

        if ((Trail[Trail.Count - 1] - position).sqrMagnitude < TrailSpacing * TrailSpacing) return;

        Trail.Add(position);
        if (Trail.Count > TrailCapacity) Trail.RemoveAt(0);
    }

    public static void ClearTrail()
    {
        Trail.Clear();
    }

    /// <summary>沿足迹往回量 gap 米，得到队形里自己的位置。</summary>
    private static Vector3 TrailPointBehind(float gap)
    {
        if (Trail.Count == 0)
        {
            Transform p = PlayerTransform;
            return p != null ? p.position : Vector3.zero;
        }

        Vector3 current = Trail[Trail.Count - 1];
        float remaining = gap;

        for (int i = Trail.Count - 1; i > 0; i--)
        {
            Vector3 previous = Trail[i - 1];
            float segment = Vector3.Distance(current, previous);
            if (segment >= remaining)
            {
                return Vector3.Lerp(current, previous, remaining / Mathf.Max(0.01f, segment));
            }

            remaining -= segment;
            current = previous;
        }

        return Trail[0];
    }

    /// <summary>第二关：编入行军纵队，隔 gap 米跟在玩家后面，侧向错开 followSide 米。</summary>
    public void SetFollow(float gap, float side)
    {
        following = true;
        followGap = Mathf.Max(1.5f, gap);
        followSide = side;
        home = transform.position;
        moveTarget = transform.position;

        // 别一开局就抢跑，按人数拉开跟车间距
        repositionTimer = 99f;
    }

    public void Initialize(string displayName, Vector3 post,
                           AudioClip shot, AudioClip reload)
    {
        allyName = displayName;
        home = post;
        moveTarget = post;
        homeYaw = transform.eulerAngles.y;
        shotClip = shot;
        reloadClip = reload;
        skill = Random.Range(0.82f, 1.18f);
        burstLeft = Random.Range(2, 5);
        repositionTimer = Random.Range(6f, 11f);
        combatMoveTimer = Random.Range(2f, 5f);
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        if (health != null)
        {
            health.Damaged += HandleDamaged;
            health.Died += HandleDeath;
        }

        BuildPose();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 5f;
        audioSource.maxDistance = 130f;

        tracer = gameObject.AddComponent<LineRenderer>();
        tracer.positionCount = 2;
        tracer.startWidth = 0.03f;
        tracer.endWidth = 0.01f;
        tracer.useWorldSpace = true;
        tracer.material = LevelBuilder.MakeMaterial(
            new Color(0.85f, 0.92f, 1f), new Color(0.6f, 0.75f, 1f));
        tracer.enabled = false;

        magazine = magazineSize;
        idlePhase = Random.Range(0f, 10f);
    }

    /// <summary>把模型零件收进 Pose 空物体，蹲、趴都靠整体姿态而不是把人物压扁。</summary>
    private void BuildPose()
    {
        GameObject poseGo = new GameObject("Pose");
        pose = poseGo.transform;
        pose.SetParent(transform, false);

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == pose) continue;
            child.SetParent(pose, true);
        }

        pose.localPosition = Vector3.zero;
        pose.localRotation = Quaternion.identity;

        torso = pose.Find("Torso");
        hipL = pose.Find("HipL");
        hipR = pose.Find("HipR");
        kneeL = hipL != null ? hipL.Find("KneeL") : null;
        kneeR = hipR != null ? hipR.Find("KneeR") : null;
        shoulderL = pose.Find("ShoulderL");
        shoulderR = pose.Find("ShoulderR");
        elbowL = shoulderL != null ? shoulderL.Find("ElbowL") : null;
        elbowR = shoulderR != null ? shoulderR.Find("ElbowR") : null;
        rifle = pose.Find("Rifle");
        if (rifle != null)
        {
            boltHandle = rifle.Find("BoltHandle");
            if (boltHandle != null)
            {
                boltHomePos = boltHandle.localPosition;
                boltHomeRot = boltHandle.localRotation;
            }
            muzzleFlash = MuzzleFlash.Create(rifle, new Vector3(0f, 0.07f, 0.8f), 0.3f);
        }

        // 贴脸拼刀用的大刀
        knife = MeleeKnife.Create(pose);
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDestroy()
    {
        All.Remove(this);
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
            health.Died -= HandleDeath;
        }
    }

    // ───────────────────────── 火把与手枪 ─────────────────────────

    /// <summary>
    /// 第二关的照明兵：左手举火把、右手提驳壳枪，夜里走在队伍里给全排照路。
    /// 火把挂在左肩的骨架上，跟着手臂一起摆；步枪收起来，枪口火光挂到手枪上。
    /// </summary>
    public void SetTorchBearer(bool value)
    {
        torchBearer = value;
        if (value) BuildTorchGear();

        if (torch != null) torch.gameObject.SetActive(value);
        if (pistolProp != null) pistolProp.gameObject.SetActive(value);

        if (value)
        {
            if (rifle != null) rifle.gameObject.SetActive(false);
            if (pistolFlash != null) muzzleFlash = pistolFlash;
        }
        else
        {
            // 换回步枪：不再举火把时枪要重新露出来
            if (rifle != null && !bugler) rifle.gameObject.SetActive(true);
            if (muzzleFlash != null && pistolFlash != null && muzzleFlash == pistolFlash)
            {
                muzzleFlash = null;
            }
        }
    }

    public bool IsTorchBearer => torchBearer;

    /// <summary>
    /// 号兵：平时小号挂在右胯，双手照常持枪；听到号令先把枪斜挎到后背，
    /// 再把号端到嘴边吹冲锋号。第 90 秒由 GameBootstrap 喊他吹号。
    /// </summary>
    public void SetBugler(bool value)
    {
        bugler = value;
        if (value) BuildBugle();

        if (bugle != null) bugle.gameObject.SetActive(value);
        if (value)
        {
            bugleBlend = 0f;
            if (rifle != null) rifle.gameObject.SetActive(true);
        }
        else
        {
            bugleBlend = 0f;
            if (!torchBearer && rifle != null) rifle.gameObject.SetActive(true);
        }
    }

    /// <summary>吹号：放音效、抬起号角，并给全队挂上振奋。</summary>
    public void BlowBugle()
    {
        if (!bugler || dead || bugleTimer > 0f) return;

        bugleTimer = 3.2f;
        if (audioSource != null) audioSource.PlayOneShot(BugleClip, 1f);
    }

    /// <summary>小号在腰胯与嘴边之间移动；吹号时步枪同时斜挎到背后。</summary>
    private void UpdateBugle(float dt)
    {
        if (bugle == null) return;

        float want = bugler && bugleTimer > 0f ? 1f : 0f;
        bugleBlend = Mathf.MoveTowards(bugleBlend, want, 4.5f * dt);

        bugle.localPosition = Vector3.Lerp(BugleHipPosition, BugleMouthPosition, bugleBlend);
        bugle.localRotation = Quaternion.Slerp(BugleHipRotation, BugleMouthRotation, bugleBlend);
    }

    private static readonly Vector3 BugleHipPosition = new Vector3(0.27f, 1.0f, 0.08f);
    private static readonly Quaternion BugleHipRotation = Quaternion.Euler(86f, 0f, -14f);
    private static readonly Vector3 BugleMouthPosition = new Vector3(0.1f, 1.46f, 0.24f);
    private static readonly Quaternion BugleMouthRotation = Quaternion.Euler(-8f, 8f, 0f);

    private void BuildBugle()
    {
        if (bugle != null || pose == null) return;

        Color brass = new Color(0.86f, 0.7f, 0.34f);
        Color brassDark = new Color(0.55f, 0.42f, 0.17f);
        Color bore = new Color(0.18f, 0.14f, 0.07f);
        Color cloth = new Color(0.62f, 0.12f, 0.1f);
        Color cordColor = new Color(0.45f, 0.09f, 0.07f);

        GameObject go = new GameObject("Bugle");
        go.transform.SetParent(pose, false);
        // 腰带右胯的挂位：号管竖直、喇叭口朝下，走路时不会穿进大腿
        go.transform.localPosition = BugleHipPosition;
        go.transform.localRotation = BugleHipRotation;
        bugle = go.transform;

        // 号管沿本地 +Z：号嘴在后（贴着嘴边），喇叭口朝前
        Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f);

        // 号嘴：小圆杯 + 一圈压边
        GameObject mouth = TorchPart(PrimitiveType.Cylinder, "BugleMouth", bugle,
            new Vector3(0f, 0f, -0.075f), new Vector3(0.04f, 0.018f, 0.04f), brassDark);
        mouth.transform.localRotation = alongZ;
        GameObject mouthFlange = TorchPart(PrimitiveType.Cylinder, "BugleMouthFlange", bugle,
            new Vector3(0f, 0f, -0.095f), new Vector3(0.052f, 0.007f, 0.052f), brassDark);
        mouthFlange.transform.localRotation = alongZ;

        // 号管：四节渐粗，口越小管越长，看着像拉出来的锥管
        GameObject lead = TorchPart(PrimitiveType.Cylinder, "BugleLead", bugle,
            new Vector3(0f, 0f, 0.03f), new Vector3(0.03f, 0.04f, 0.03f), brass);
        lead.transform.localRotation = alongZ;
        GameObject pipe2 = TorchPart(PrimitiveType.Cylinder, "BuglePipe2", bugle,
            new Vector3(0f, 0f, 0.165f), new Vector3(0.042f, 0.075f, 0.042f), brass);
        pipe2.transform.localRotation = alongZ;
        GameObject pipe3 = TorchPart(PrimitiveType.Cylinder, "BuglePipe3", bugle,
            new Vector3(0f, 0f, 0.33f), new Vector3(0.058f, 0.07f, 0.058f), brass);
        pipe3.transform.localRotation = alongZ;
        GameObject throat = TorchPart(PrimitiveType.Cylinder, "BugleThroat", bugle,
            new Vector3(0f, 0f, 0.43f), new Vector3(0.085f, 0.04f, 0.085f), brass);
        throat.transform.localRotation = alongZ;

        // 两道加强箍，让细长号管有几个亮暗分节
        for (int i = 0; i < 2; i++)
        {
            GameObject ring = TorchPart(PrimitiveType.Cylinder, "BugleRing" + i, bugle,
                new Vector3(0f, 0f, 0.08f + i * 0.17f),
                new Vector3(0.048f + i * 0.012f, 0.008f, 0.048f + i * 0.012f), brassDark);
            ring.transform.localRotation = alongZ;
        }

        // 喇叭口：两节渐扩 + 外翻压边 + 暗色内壁
        GameObject bellA = TorchPart(PrimitiveType.Cylinder, "BugleBellA", bugle,
            new Vector3(0f, 0f, 0.4925f), new Vector3(0.13f, 0.0275f, 0.13f), brass);
        bellA.transform.localRotation = alongZ;
        GameObject bellB = TorchPart(PrimitiveType.Cylinder, "BugleBellB", bugle,
            new Vector3(0f, 0f, 0.535f), new Vector3(0.175f, 0.02f, 0.175f), brass);
        bellB.transform.localRotation = alongZ;
        GameObject rim = TorchPart(PrimitiveType.Cylinder, "BugleBellRim", bugle,
            new Vector3(0f, 0f, 0.557f), new Vector3(0.19f, 0.009f, 0.19f), brassDark);
        rim.transform.localRotation = alongZ;
        GameObject inner = TorchPart(PrimitiveType.Cylinder, "BugleBellInner", bugle,
            new Vector3(0f, 0f, 0.5675f), new Vector3(0.176f, 0.004f, 0.176f), bore);
        inner.transform.localRotation = alongZ;

        // 号管下面的护环：一根横杆加两根立柱，号嘴那头正好当握把
        TorchPart(PrimitiveType.Cube, "BugleGuardBar", bugle,
            new Vector3(0f, -0.085f, 0.195f), new Vector3(0.014f, 0.014f, 0.21f), brassDark);
        for (int i = 0; i < 2; i++)
        {
            TorchPart(PrimitiveType.Cylinder, "BugleGuardStrut" + i, bugle,
                new Vector3(0f, -0.05f, 0.11f + i * 0.17f),
                new Vector3(0.012f, 0.028f, 0.012f), brassDark);
        }

        // 红缨：喇叭口根部缠一圈红布，外侧打个结——挂在胯上时朝外，不穿进身体
        GameObject ribbon = TorchPart(PrimitiveType.Cylinder, "BugleRibbon", bugle,
            new Vector3(0f, 0f, 0.425f), new Vector3(0.092f, 0.016f, 0.092f), cloth);
        ribbon.transform.localRotation = alongZ;
        TorchPart(PrimitiveType.Cube, "BugleKnot", bugle,
            new Vector3(0.088f, 0f, 0.425f), new Vector3(0.022f, 0.036f, 0.03f), cordColor);
        for (int i = 0; i < 2; i++)
        {
            GameObject strand = TorchPart(PrimitiveType.Cube, "BugleTassel" + i, bugle,
                new Vector3(0.1f + i * 0.012f, -0.055f, 0.425f),
                new Vector3(0.014f, 0.075f, 0.014f), cloth);
            strand.transform.localRotation = Quaternion.Euler(0f, 0f, 8f + i * 12f);
        }
    }

    private void BuildTorchGear()
    {
        if (torch != null || pose == null) return;

        Color wood = new Color(0.24f, 0.15f, 0.08f);
        Color cloth = new Color(0.12f, 0.1f, 0.08f);
        Color flameOuter = new Color(1f, 0.52f, 0.12f);
        Color flameInner = new Color(1f, 0.86f, 0.45f);

        // 火把：左手
        GameObject torchGo = new GameObject("Torch");
        torchGo.transform.SetParent(shoulderL != null ? shoulderL : pose, false);
        torchGo.transform.localPosition = new Vector3(0f, -0.62f, 0.08f);
        torchGo.transform.localRotation = Quaternion.Euler(-6f, 0f, 8f);
        torch = torchGo.transform;

        TorchPart(PrimitiveType.Cube, "TorchHandle", torch,
            new Vector3(0f, 0.16f, 0f), new Vector3(0.035f, 0.42f, 0.035f), wood);
        TorchPart(PrimitiveType.Cube, "TorchHead", torch,
            new Vector3(0f, 0.4f, 0f), new Vector3(0.075f, 0.12f, 0.075f), cloth);
        GameObject flameGo = TorchPart(PrimitiveType.Cube, "TorchFlame", torch,
            new Vector3(0f, 0.53f, 0f), new Vector3(0.15f, 0.26f, 0.15f), flameOuter);
        flameGo.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        TorchPart(PrimitiveType.Cube, "TorchFlameCore", torch,
            new Vector3(0f, 0.55f, 0f), new Vector3(0.075f, 0.15f, 0.075f), flameInner);

        GameObject lightGo = new GameObject("TorchLight");
        lightGo.transform.SetParent(torch, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.58f, 0f);
        torchLight = lightGo.AddComponent<Light>();
        torchLight.type = LightType.Point;
        torchLight.color = new Color(1f, 0.72f, 0.4f);
        torchLight.range = 13f;
        torchLight.intensity = 2.2f;
        torchLight.shadows = LightShadows.None;
        torchSeed = Random.Range(0f, 40f);

        // 驳壳枪：右手
        Color gunmetal = new Color(0.17f, 0.17f, 0.19f);
        Color gripWood = new Color(0.34f, 0.19f, 0.09f);

        GameObject pistolGo = new GameObject("PistolProp");
        pistolGo.transform.SetParent(shoulderR != null ? shoulderR : pose, false);
        pistolGo.transform.localPosition = new Vector3(0f, -0.6f, 0.06f);
        pistolGo.transform.localRotation = Quaternion.Euler(64f, 0f, 0f);
        pistolProp = pistolGo.transform;

        TorchPart(PrimitiveType.Cube, "PistolReceiver", pistolProp,
            new Vector3(0f, 0f, 0.12f), new Vector3(0.045f, 0.07f, 0.24f), gunmetal);
        TorchPart(PrimitiveType.Cube, "PistolBarrel", pistolProp,
            new Vector3(0f, 0.01f, 0.3f), new Vector3(0.026f, 0.026f, 0.16f), gunmetal);
        TorchPart(PrimitiveType.Cube, "PistolGrip", pistolProp,
            new Vector3(0f, -0.06f, -0.01f), new Vector3(0.038f, 0.12f, 0.05f), gripWood);
        pistolFlash = MuzzleFlash.Create(pistolProp, new Vector3(0f, 0.01f, 0.39f), 0.16f);
    }

    private static GameObject TorchPart(PrimitiveType type, string name, Transform parent,
                                        Vector3 pos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = LevelBuilder.MakeMaterial(color);

        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        return go;
    }

    private void Update()
    {
        if (dead) return;

        float dt = Time.deltaTime;

        // 实际挪动速度：蹲着不动时用来决定要不要单膝跪下去
        Vector3 groundStep = transform.position - lastGroundPos;
        groundStep.y = 0f;
        lastGroundPos = transform.position;
        planarSpeed = Mathf.Lerp(planarSpeed, groundStep.magnitude / Mathf.Max(0.0001f, dt),
            10f * dt);

        retargetTimer -= dt;
        if (retargetTimer <= 0f)
        {
            retargetTimer = Random.Range(0.45f, 0.7f);
            PickTarget();
        }

        if (crouchTimer > 0f) crouchTimer -= dt;
        if (boltTimer > 0f) boltTimer -= dt;
        if (dodgeTimer > 0f) dodgeTimer -= dt;
        if (proneTimer > 0f) proneTimer -= dt;
        if (proneCooldown > 0f) proneCooldown -= dt;
        if (trenchTimer > 0f) trenchTimer -= dt;
        if (bugleTimer > 0f) bugleTimer -= dt;

        UpdatePosture(dt);
        FollowGround();

        // 赤水河：整个人沉到水面以下就爬回最近的干岸，别在河床里走
        if (ChishuiWaterRule.FullySubmerged(transform.position))
        {
            RespawnFromWater();
        }
        else
        {
            transform.position = ChishuiWaterRule.KeepFeetDry(transform.position);
        }

        KeepSpacing(dt);
        UpdateTorch(dt);
        UpdateBugle(dt);

        if (!CombatEnabled)
        {
            UpdateIdle(dt);
            return;
        }

        // 号兵不参战：跟着队伍走，听到号令就停下吹号
        if (bugler)
        {
            if (following) UpdateFollow();
            if (bugleTimer > 0f) StopMoving();
            UpdateMoving(dt, moveSpeed);
            if (!moving) UpdateIdle(dt);
            Animate(dt);
            return;
        }

        // 行军纵队：目标点一直在变，走位由足迹决定
        if (following) UpdateFollow();

        if (target != null && !IsTargetValid(target))
        {
            target = null;
        }

        UpdateCover(dt);

        // 闪避：沿垂直弹道方向抢步，优先级高于一切走位
        if (dodgeTimer > 0f) UpdateMoving(dt, 3.4f);

        if (target != null)
        {
            trackTime += dt;
            reactionTimer -= dt;

            FaceTarget(target, 2.4f, dt);

            bool brawling = FlatGapTo(target) <= meleeRange;

            if (brawling)
            {
                // 贴脸了：收枪换刀，跟敌人拼刺
                UpdateBrawl(dt, target);
                UpdateMoving(dt, moveSpeed);
            }
            else
            {
                // 缩在掩体后、或者整个人趴着时先不开枪，等起身/探头那一拍再打
                bool hiding = coverHideTimer > 0f || proneTimer > 0f;
                if (!hiding && !reloading && boltTimer <= 0f && magazine > 0
                    && reactionTimer <= 0f)
                {
                    fireTimer -= dt;
                    if (fireTimer <= 0f) FireOnce(target);
                }
                else if (!reloading && magazine <= 0)
                {
                    StartReload();
                }

                // 对射时也会换位置，不再站桩
                if (dodgeTimer <= 0f)
                {
                    if (!following) HandleCombatMovement(dt);
                    UpdateMoving(dt, moveSpeed);
                }
            }
        }
        else
        {
            trackTime = 0f;
            UpdateMoving(dt, moveSpeed);
            if (!moving) UpdateIdle(dt);
        }

        HandleReload(dt);
        if (!following) HandleReposition(dt);
        Recover(dt);
        Animate(dt);
    }

    /// <summary>姿态：蹲下、卧倒全部走和主角同一套关键帧。</summary>
    private void UpdatePosture(float dt)
    {
        float targetCrouch = 0f;
        float targetProne = 0f;

        if (proneTimer > 0f) targetProne = 1f;
        else if (crouchTimer > 0f) targetCrouch = 1f;

        // 开发截图：强行摆成蹲姿 / 趴姿
        if (captureStance == 1) { targetCrouch = 1f; targetProne = 0f; }
        else if (captureStance == 2) { targetCrouch = 0f; targetProne = 1f; }

        crouchBlend = Mathf.MoveTowards(crouchBlend, targetCrouch, StancePose.CrouchRate * dt);
        proneBlend = Mathf.MoveTowards(proneBlend, targetProne,
            (targetProne > proneBlend ? StancePose.ProneDownRate : StancePose.ProneUpRate) * dt);

        // 蹲着且站着不动才单膝跪地，和主角一样一迈步就收回低姿蹚步
        kneelWeight = Mathf.MoveTowards(kneelWeight,
            crouchBlend * (1f - proneBlend) * Mathf.Clamp01(1f - planarSpeed / 1.2f),
            StancePose.KneelRate * dt);
    }

    /// <summary>开发截图用：1 蹲、2 趴、0 恢复。</summary>
    public void SetStanceForCapture(int stance)
    {
        captureStance = stance;
    }

    /// <summary>
    /// 掩体节奏：躲 1～2 秒，再起身/探头打一梭子，打完再缩回去。
    /// 只在第一关（不跟队、不是号兵）用；没有掩体时照常站在空地上对射。
    /// </summary>
    private void UpdateCover(float dt)
    {
        if (following || bugler || dead)
        {
            inCover = false;
            coverHideTimer = 0f;
            coverPeekTimer = 0f;
            peekBlend = Mathf.MoveTowards(peekBlend, 0f, 4f * dt);
            return;
        }

        bool hasCover = target != null && BlocksFromTarget(transform.position, 0.95f);

        if (!hasCover)
        {
            inCover = false;
            coverHideTimer = 0f;
            coverPeekTimer = 0f;
        }
        else if (!inCover)
        {
            inCover = true;
            coverPeekTimer = Random.Range(1.4f, 2.6f);
            coverHideTimer = 0f;
            peekSide = Random.value < 0.5f ? -1f : 1f;
        }
        else if (coverHideTimer > 0f)
        {
            coverHideTimer -= dt;
            if (coverHideTimer <= 0f && proneTimer <= 0f)
            {
                coverPeekTimer = Random.Range(2.0f, 3.4f);
                peekSide = -peekSide;
            }
        }
        else if (coverPeekTimer > 0f)
        {
            coverPeekTimer -= dt;
            if (coverPeekTimer <= 0f) BeginCoverHide();
        }
        else if (proneTimer <= 0f)
        {
            // 趴伏结束、掩体还在：重新抬头找下一个射击窗口
            coverPeekTimer = Random.Range(1.6f, 2.8f);
            peekSide = -peekSide;
        }

        float lean = inCover && coverHideTimer <= 0f && proneTimer <= 0f ? peekSide : 0f;
        peekBlend = Mathf.MoveTowards(peekBlend, lean, 3.2f * dt);
    }

    /// <summary>缩回掩体：多数时候蹲下，伤得重、火力猛时干脆整个人趴下。</summary>
    private void BeginCoverHide()
    {
        if (proneTimer > 0f) return;

        coverPeekTimer = 0f;
        coverHideTimer = Random.Range(1.0f, 1.9f);
        hideCount++;

        bool hurt = health != null && health.Current < health.MaxHealth * 0.6f;
        if (hurt && proneCooldown <= 0f && Random.value < 0.4f)
        {
            proneTimer = Random.Range(1.4f, 2.2f);
            proneCooldown = Random.Range(4f, 7f);
            coverHideTimer = proneTimer;
            return;
        }

        crouchTimer = Mathf.Max(crouchTimer, coverHideTimer + 0.15f);
    }

    /// <summary>
    /// 沿玩家的足迹前进。河上的跳石、索桥、独木梁一次只容一人，
    /// 所以踏进水面范围就收成一路纵队，不再横向错开。
    /// </summary>
    private void UpdateFollow()
    {
        // 闪避途中别改目标点，否则会原地打转
        if (dodgeTimer > 0f) return;

        followPoint = TrailPointBehind(followGap);

        bool onCrossing = ChishuiWaterRule.InWater(followPoint);
        Vector3 goal = followPoint;
        if (!onCrossing) goal.x += followSide;

        home = goal;

        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        float gap = flat.magnitude;

        if (moving)
        {
            moveTarget = goal;
            if (gap < 1.1f) StopMoving();
        }
        else if (gap > 2.6f)
        {
            moveTarget = goal;
            moving = true;
        }

        if (!moving) return;

        // 落远了要小跑追上，别让队伍断成两截
        moveSpeed = gap > 11f ? 5.6f : (gap > 4.5f ? 4.4f : 2.9f);
    }

    /// <summary>战友之间保持横向间距，几个人的身位不会叠成一团。</summary>
    private void KeepSpacing(float dt)
    {
        if (proneBlend > 0.6f) return;

        // 水上通道窄，互相推挤只会把人挤下桥，过河时各走各的
        if (ChishuiWaterRule.InWater(transform.position)) return;

        Vector3 push = Vector3.zero;
        for (int i = 0; i < All.Count; i++)
        {
            AllyAI other = All[i];
            if (other == null || other == this || other.IsDead) continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance > 1.15f || distance < 0.001f) continue;

            push += away / distance * (1.15f - distance);
        }

        // 别贴着玩家站，更别挡在枪口前面
        Transform playerT = PlayerTransform;
        if (playerT != null)
        {
            Vector3 awayPlayer = transform.position - playerT.position;
            awayPlayer.y = 0f;
            float distance = awayPlayer.magnitude;
            if (distance < 2.4f && distance > 0.001f)
            {
                push += awayPlayer / distance * (2.4f - distance) * 1.4f;
            }
        }

        if (push.sqrMagnitude < 1e-5f) return;

        Vector3 from = transform.position;
        MoveDelta(Vector3.ClampMagnitude(push, 1f) * 1.4f * dt);
        transform.position = RiverGuard.Clamp(from, transform.position);
    }

    private static Transform playerCache;

    private static Transform PlayerTransform
    {
        get
        {
            if (playerCache == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) playerCache = go.transform;
            }
            return playerCache;
        }
    }

    /// <summary>贴地：让队友能走下战壕、越过河岸，不会悬在半空。</summary>
    private void FollowGround()
    {
        Vector3 position = transform.position;

        if (RiverGuard.OnBridge(position))
        {
            transform.position = new Vector3(position.x, RiverGuard.DeckY, position.z);
            return;
        }

        if (RiverGuard.Active)
        {
            // 第一关岸面是一整块平地，桥面上面已经单独处理过。
            // 沙袋、木箱、板车都只是掩体，不是可以踩上去的台阶：
            // 直接把脚锁在地面高度，人就不会再借着向下射线爬到掩体顶上卡住。
            transform.position = new Vector3(position.x, 0f, position.z);
            safeY = 0f;
            haveSafeY = true;
            lastDryPoint = transform.position;
            hasDryPoint = true;
            return;
        }

        // 赤水河的索桥：桥板高度已知，直接踩在桥面上
        if (ChishuiWaterRule.Active && ChishuiWaterRule.TryDeck(position, out float deck))
        {
            transform.position = new Vector3(position.x, deck, position.z);
            safeY = deck;
            haveSafeY = true;
            return;
        }

        // 赤水河：飘在半空（站在树冠、房顶的碰撞体上）就直接落回地形面
        if (ChishuiWaterRule.Active)
        {
            float walk = ChishuiLevelBuilder.WalkHeight(position.x, position.z);
            if (walk > ChishuiWaterRule.WaterSurfaceY + 0.05f && position.y > walk + 1.1f)
            {
                transform.position = new Vector3(position.x, walk, position.z);
                safeY = walk;
                haveSafeY = true;
                return;
            }
        }

        Vector3 probe = position + Vector3.up * 2.6f;

        // 单发射线会先打中自己的爆头盒，接下来整段贴地逻辑就被跳过，
        // 人停在下坡前的高度上，看上去悬空。这里把所有命中都过一遍。
        RaycastHit[] groundHits = Physics.RaycastAll(probe, Vector3.down, 6f, ~0,
                                                     QueryTriggerInteraction.Ignore);

        bool grounded = false;
        RaycastHit hit = default(RaycastHit);
        float bestY = float.MinValue;

        for (int i = 0; i < groundHits.Length; i++)
        {
            Collider c = groundHits[i].collider;
            if (c == null) continue;
            if (c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<Health>() != null) continue;

            // 能爬出战壕（0.85 米），但不会踩到沙袋顶
            if (groundHits[i].point.y - position.y > 0.85f) continue;
            // 河床不算地面，队友不下水
            if (groundHits[i].point.y < -0.2f) continue;
            if (groundHits[i].point.y <= bestY) continue;

            bestY = groundHits[i].point.y;
            hit = groundHits[i];
            grounded = true;
        }

        if (grounded)
        {
            transform.position = new Vector3(position.x, hit.point.y, position.z);
            safeY = hit.point.y;
            haveSafeY = true;

            if (hit.point.y > ChishuiWaterRule.WaterSurfaceY + 0.05f)
            {
                lastDryPoint = transform.position;
                hasDryPoint = true;
            }
            return;
        }

        // 赤水河上：跳石之间的缝、断桥的缺口，下面都是河床。
        // 队伍走的是玩家走过的路，这里按上一块立足面的高度托住，不会掉进河心。
        // safeY 可能是在岸边高地上记下的，过河时不能拿来用，最多托到桥面那么高。
        if (ChishuiWaterRule.InWater(position) && haveSafeY)
        {
            float hold = Mathf.Min(safeY, ChishuiWaterRule.WaterSurfaceY + 0.75f);
            transform.position = new Vector3(position.x,
                Mathf.Max(hold, ChishuiWaterRule.WaterSurfaceY + 0.02f), position.z);
        }
    }

    /// <summary>整个人沉进赤水河：回到最近一次站稳的干地，重新跟队。</summary>
    private void RespawnFromWater()
    {
        Vector3 point = hasDryPoint ? lastDryPoint : transform.position;
        point.y = ChishuiWaterRule.WaterSurfaceY + 0.4f;
        transform.position = point;

        // 落地高度作废，让 FollowGround 重新找一次地面
        haveSafeY = false;
        moving = false;
        FollowGround();
    }

    /// <summary>敌方子弹擦身而过时触发：侧移躲开，或者在原地扑倒。</summary>
    public static void NotifyIncomingFire(Vector3 origin, Vector3 impact)
    {
        for (int i = 0; i < All.Count; i++)
        {
            AllyAI ally = All[i];
            if (ally == null || ally.IsDead) continue;
            ally.TryDodge(origin, impact);
        }
    }

    private void TryDodge(Vector3 origin, Vector3 impact)
    {
        if (dead || !CombatEnabled || dodgeTimer > 0f) return;

        // 桥上闪避一步就下河了，过河时宁可挨枪也不侧移
        if (ChishuiWaterRule.InWater(transform.position)) return;

        Vector3 a = new Vector3(origin.x, 0f, origin.z);
        Vector3 b = new Vector3(impact.x, 0f, impact.z);
        Vector3 me = new Vector3(transform.position.x, 0f, transform.position.z);

        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0.01f
            ? Mathf.Clamp01(Vector3.Dot(me - a, ab) / ab.sqrMagnitude)
            : 0f;
        Vector3 closest = a + ab * t;

        if (Vector3.Distance(closest, me) > 2.4f) return;
        if (Vector3.Distance(a, me) > 45f) return;

        // 已经在掩体后：不往外跑，直接缩下去，必要时整个人趴平
        if (inCover)
        {
            BeginCoverHide();
            return;
        }

        // 来不及跑就整人扑倒
        if (proneCooldown <= 0f && proneTimer <= 0f && Random.value < 0.45f)
        {
            proneTimer = Random.Range(1.0f, 1.9f);
            proneCooldown = Random.Range(3.5f, 6.5f);
            crouchTimer = 0f;
            StopMoving();
            return;
        }

        Vector3 side = Vector3.Cross(Vector3.up, ab.normalized);
        if (side.sqrMagnitude < 0.01f) side = transform.right;
        if (Random.value < 0.5f) side = -side;

        Vector3 candidate = transform.position + side * Random.Range(1.5f, 2.6f);
        Vector3 bottom = candidate + Vector3.up * 0.35f;
        Vector3 top = candidate + Vector3.up * 1.0f;

        if (Physics.CheckCapsule(bottom, top, 0.28f, ~0, QueryTriggerInteraction.Ignore))
        {
            // 侧面被挡就原地蹲下
            crouchTimer = Mathf.Max(crouchTimer, Random.Range(1.2f, 2f));
            return;
        }

        dodgeTarget = candidate;
        moveTarget = candidate;
        moving = true;
        moveSpeed = 3.4f;
        dodgeTimer = Random.Range(0.5f, 0.9f);
        crouchTimer = Mathf.Max(crouchTimer, 0.7f);
    }

    /// <summary>有目标时的走位：隔几秒换个射击位，被压制时往战壕里撤。</summary>
    private void HandleCombatMovement(float dt)
    {
        if (moving || dodgeTimer > 0f) return;

        combatMoveTimer -= dt;
        if (combatMoveTimer > 0f) return;

        combatMoveTimer = Random.Range(2.8f, 5.5f);
        if (Random.value < 0.25f) return;

        Vector3 point = PickCombatPoint();
        if (Vector3.Distance(point, transform.position) < 0.7f) return;

        moveTarget = point;
        moving = true;

        bool toTrench = Mathf.Abs(point.x) > 10f;
        moveSpeed = toTrench ? 2.4f : 1.9f;

        if (toTrench)
        {
            trenchTimer = Random.Range(7f, 12f);
            crouchTimer = 0f;
            proneTimer = 0f;
        }
    }

    private Vector3 PickCombatPoint()
    {
        // 挂彩且被压制时，撤进东侧战壕
        if (health != null && health.Current < health.MaxHealth * 0.6f
            && trenchTimer <= 0f && Mathf.Abs(home.x) < 10f && Random.value < 0.55f)
        {
            return new Vector3(Random.Range(12f, 18.5f), 0f, Random.Range(8.2f, 8.8f));
        }

        // 优先挪到能挡住敌人视线的沙袋 / 墙后：蹲下探头能打，缩回去就安全
        if (target != null)
        {
            Vector3 toEnemy = target.position - transform.position;
            toEnemy.y = 0f;
            Vector3 forward = toEnemy.sqrMagnitude > 0.01f
                ? toEnemy.normalized : transform.forward;

            for (int i = 0; i < 12; i++)
            {
                float angle = Random.Range(-75f, 75f);
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 probe = transform.position + dir * Random.Range(2f, 5f);
                probe.y = home.y;

                Vector3 grounded;
                if (!TryGroundPoint(probe, out grounded)) continue;
                if (ChishuiWaterRule.InWater(grounded)) continue;
                if (!BlocksFromTarget(grounded, 0.95f)) continue;
                if (!OpenAt(grounded)) continue;

                return grounded;
            }
        }

        return home + new Vector3(Random.Range(-2.8f, 2.8f), 0f, Random.Range(-1.4f, 1.2f));
    }

    private bool TryGroundPoint(Vector3 point, out Vector3 grounded)
    {
        grounded = point;

        Vector3 origin = point + Vector3.up * 3f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 8f, ~0,
                                               QueryTriggerInteraction.Ignore);

        // 只认脚下的实地。沙袋顶、木箱盖比地面高出一大截，
        // 选成落脚点会让人贴着掩体顶不动。
        float ceiling = transform.position.y + 0.55f;
        float nearest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<Health>() != null) continue;
            if (hits[i].point.y > ceiling) continue;
            if (hits[i].normal.y < 0.55f) continue;
            if (hits[i].distance >= nearest) continue;

            nearest = hits[i].distance;
            grounded = hits[i].point;
            found = true;
        }

        return found;
    }

    /// <summary>从敌人眼睛看过去，这个落脚点是否被沙袋、墙或者地形挡住。</summary>
    private bool BlocksFromTarget(Vector3 point, float height)
    {
        if (target == null) return false;

        Vector3 from = target.position + Vector3.up * 1.15f;
        Vector3 to = point + Vector3.up * height;
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.6f) return false;

        if (!Physics.Raycast(from, delta / distance, out RaycastHit hit, distance, ~0,
                             QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.collider.transform.IsChildOf(transform)) return false;
        return true;
    }

    private bool OpenAt(Vector3 point)
    {
        Collider[] hits = Physics.OverlapCapsule(point + Vector3.up * 0.35f,
            point + Vector3.up * 1.2f, 0.24f, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;
            if (hits[i].transform.IsChildOf(transform)) continue;
            // 人身上的碰撞体不算障碍，不然整片场地都"站不下人"
            if (hits[i].GetComponentInParent<Health>() != null) continue;
            return false;
        }

        return true;
    }

    private void UpdateMoving(float dt, float speed)
    {
        if (!moving) return;

        // 冲锋号一响，全队脚步加快两成
        float boost = Rallied ? 1.2f : 1f;

        Vector3 flat = moveTarget - transform.position;
        flat.y = 0f;

        if (flat.sqrMagnitude < 0.09f)
        {
            StopMoving();
            return;
        }

        // 卡住绕行：先横着走半步，别硬顶着栏杆
        if (detourTimer > 0f)
        {
            detourTimer -= dt;
            MoveDelta(detourDir * speed * boost * dt);
            walkBlend = Mathf.MoveTowards(walkBlend, 1f, 6f * dt);
            walkPhase += dt * 9f * boost;
            return;
        }

        Vector3 from = transform.position;
        MoveDelta(flat.normalized * speed * boost * dt);
        transform.position = RiverGuard.Clamp(from, transform.position);
        walkBlend = Mathf.MoveTowards(walkBlend, 1f, 6f * dt);
        walkPhase += dt * 9f * boost;

        // 想走却一秒挪不出半米：判定被卡，下一帧横着绕
        if (flat.sqrMagnitude > 1.5f)
        {
            moveStuckTimer += dt;
            if (moveStuckTimer >= 1f)
            {
                if (Vector3.Distance(transform.position, moveAnchor) < 0.4f)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, flat.normalized);
                    if ((++detourFlips & 1) == 0) side = -side;
                    detourDir = (side + flat.normalized * 0.2f).normalized;
                    detourTimer = 0.55f;
                }
                moveStuckTimer = 0f;
                moveAnchor = transform.position;
            }
        }
        else
        {
            moveStuckTimer = 0f;
            moveAnchor = transform.position;
        }
    }

    private float FlatGapTo(Transform other)
    {
        if (other == null) return float.MaxValue;
        Vector3 delta = other.position - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    /// <summary>贴脸拼刀：侧步绕上去，隔一会儿劈一刀。</summary>
    private void UpdateBrawl(float dt, Transform enemy)
    {
        Vector3 to = enemy.position - transform.position;
        to.y = 0f;
        float gap = to.magnitude;
        Vector3 dir = gap > 0.01f ? to.normalized : transform.forward;

        moveSpeed = 3.5f;

        if (gap > 1.2f)
        {
            float weave = Mathf.Sin(Time.time * 2.8f + idlePhase) * 0.42f;
            Vector3 approach = (dir + Vector3.Cross(Vector3.up, dir) * weave).normalized;
            moveTarget = transform.position + approach * 1.4f;
            moving = true;
        }

        meleeTimer -= dt;
        if (meleeTimer <= 0f && gap < meleeRange * 0.95f)
        {
            meleeTimer = meleeInterval * Random.Range(0.8f, 1.25f);
            meleeSwing = MeleeSwingDuration;
            StrikeWithKnife(enemy);
        }
    }

    private void StrikeWithKnife(Transform victim)
    {
        if (victim == null) return;

        Health victimHealth = victim.GetComponent<Health>();
        if (victimHealth == null || victimHealth.IsDead) return;

        Vector3 to = victim.position - transform.position;
        to.y = 0f;
        if (to.magnitude > meleeRange) return;
        if (Vector3.Dot(transform.forward, to.normalized) < 0.1f) return;

        Vector3 point = victim.position + Vector3.up * 1.05f;
        victimHealth.TakeDamage(meleeDamage, point, -transform.forward, BodyZone.Torso,
                                gameObject);

        if (audioSource != null && MeleeClip != null)
        {
            audioSource.PlayOneShot(MeleeClip, 0.5f);
        }
    }

    private static readonly RaycastHit[] MoveHits = new RaycastHit[8];

    /// <summary>撞到沙袋、战壕壁就沿墙滑动，队友不会直接穿过去。</summary>
    private void MoveDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-8f) return;

        float distance = delta.magnitude;
        Vector3 dir = delta / distance;
        Vector3 bottom = transform.position + Vector3.up * 0.55f;
        Vector3 top = transform.position + Vector3.up * 1.25f;

        int count = Physics.CapsuleCastNonAlloc(top, bottom, 0.32f, dir, MoveHits,
            distance + 0.06f, ~0, QueryTriggerInteraction.Ignore);

        Vector3 normal = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = MoveHits[i];
            if (hit.collider == null) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<Health>() != null) continue;
            if (hit.normal.y > 0.55f) continue;

            normal = hit.normal;
            break;
        }

        if (normal.sqrMagnitude < 0.001f)
        {
            transform.position += delta;
            return;
        }

        Vector3 slide = Vector3.ProjectOnPlane(delta, normal);
        if (slide.sqrMagnitude > 1e-6f) transform.position += slide * 0.85f;
    }

    private void HandleDamaged(DamageInfo info)
    {
        crouchTimer = Mathf.Max(crouchTimer, Random.Range(1.2f, 2.0f));
        lastHurtTime = Time.time;

        if (inCover)
        {
            BeginCoverHide();
        }
        else if (proneCooldown <= 0f && proneTimer <= 0f && Random.value < 0.55f)
        {
            // 中弹后有时干脆整人扑倒，等火力过去再起来
            proneTimer = Random.Range(0.9f, 1.7f);
            proneCooldown = Random.Range(3f, 5.5f);
            StopMoving();
        }
    }

    // ───────────────────────────── 目标 ─────────────────────────────

    private void PickTarget()
    {
        if (!CombatEnabled) return;

        EnemyAI best = null;
        float bestDistance = fireRange;

        EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyAI enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;

            float distance = Vector3.Distance(transform.position, enemy.transform.position);
            if (distance >= bestDistance) continue;
            if (!CanSee(enemy.transform, distance)) continue;

            best = enemy;
            bestDistance = distance;
        }

        if (best != null)
        {
            if (target != best.transform)
            {
                target = best.transform;
                trackTime = 0f;
                fireTimer = Random.Range(0.3f, 0.75f);
            }
            reactionTimer = Random.Range(0.28f, 0.7f);
        }
        else
        {
            target = null;
        }
    }

    private bool IsTargetValid(Transform candidate)
    {
        if (candidate == null) return false;
        EnemyAI enemy = candidate.GetComponent<EnemyAI>();
        return enemy != null && !enemy.IsDead;
    }

    private bool CanSee(Transform candidate, float distance)
    {
        Vector3 from = EyePosition();
        Vector3 to = candidate.position + Vector3.up * 1.05f;
        Vector3 delta = to - from;

        if (delta.magnitude > distance + 1f) return false;

        if (Physics.Raycast(from, delta.normalized, out RaycastHit hit,
                            delta.magnitude + 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.transform.IsChildOf(candidate);
        }

        return true;
    }

    private Vector3 EyePosition()
    {
        float height = Mathf.Lerp(1.45f, 1.12f, crouchBlend);
        height = Mathf.Lerp(height, 0.38f, proneBlend);
        return transform.position + Vector3.up * height
               + transform.right * (peekBlend * 0.3f);
    }

    private void FaceTarget(Transform candidate, float speed, float dt)
    {
        Vector3 flat = candidate.position - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(flat), speed * dt);
    }

    // ───────────────────────────── 射击 ─────────────────────────────

    private void FireOnce(Transform aimTarget)
    {
        if (magazine <= 0) return;

        magazine--;
        burstLeft--;

        // 刚接手目标时手生，瞄久了越来越稳
        float settle = Mathf.Clamp01(trackTime / 2.2f);
        float spread = Mathf.Lerp(3.2f, 0.55f, settle) / Mathf.Max(0.5f, skill);
        spread += Random.Range(-0.18f, 0.35f);
        // 号声在耳：精神头上来，散布减半（命中精度翻倍）
        if (Rallied) spread *= 0.5f;

        float muzzleHeight = Mathf.Lerp(1.32f, 1.0f, crouchBlend);
        muzzleHeight = Mathf.Lerp(muzzleHeight, 0.36f, proneBlend);

        // 探头时枪也从掩体侧面探出去，否则子弹全打在沙袋上
        Vector3 muzzle = transform.position
                         + Vector3.up * muzzleHeight
                         + transform.right * (peekBlend * 0.3f)
                         + transform.forward * 0.5f;
        Vector3 aim = aimTarget.position + Vector3.up * 1.05f;
        Vector3 direction = (aim - muzzle).normalized;

        direction = Quaternion.Euler(
            Random.Range(-spread, spread),
            Random.Range(-spread, spread), 0f) * direction;

        Vector3 end = muzzle + direction * fireRange;

        if (Physics.Raycast(muzzle, direction, out RaycastHit hit, fireRange, ~0,
                            QueryTriggerInteraction.Ignore))
        {
            end = hit.point;

            if (!hit.collider.transform.IsChildOf(transform))
            {
                Health hitHealth = hit.collider.GetComponentInParent<Health>();
                HitZone zone = hit.collider.GetComponent<HitZone>();

                if (hitHealth != null && hitHealth.CompareTag("Enemy") && !hitHealth.IsDead)
                {
                    bool head = zone != null && zone.Zone == BodyZone.Head;
                    hitHealth.TakeDamage(head ? damageHead : damageTorso,
                        hit.point, hit.normal, head ? BodyZone.Head : BodyZone.Torso,
                        gameObject);
                }
            }
        }

        if (audioSource != null && shotClip != null)
        {
            audioSource.PlayOneShot(shotClip, 0.65f);
        }
        if (muzzleFlash != null) muzzleFlash.Play(Random.Range(0.9f, 1.25f));

        // 战友的子弹同样会追着敌人躲：尽量不让他们站在空地上对射
        EnemyAI.NotifyIncomingFire(muzzle, end);

        if (torchBearer)
        {
            // 照明兵拿的是驳壳枪：半自动连打，不拉栓
            pistolKick = 1f;
            fireTimer = Random.Range(0.2f, 0.34f);
            return;
        }

        // 汉阳造是栓动枪：每打一发都要拉栓，不能像半自动那样连打
        boltTimer = BoltDuration;
        if (audioSource != null) audioSource.PlayOneShot(BoltClip, 0.4f);

        StartCoroutine(TracerRoutine(muzzle, end));

        if (burstLeft <= 0)
        {
            burstLeft = Random.Range(2, 5);
            fireTimer = Random.Range(1.6f, 2.8f);

            // 在掩体后打完一梭子就缩回去换弹/躲子弹，不站在空地上当靶子
            if (inCover && Random.value < 0.85f)
            {
                BeginCoverHide();
            }
            else if (Random.value < 0.4f)
            {
                crouchTimer = Mathf.Max(crouchTimer, Random.Range(0.9f, 1.8f));
            }
        }
        else
        {
            fireTimer = Random.Range(0.9f, 1.2f);
        }
    }

    private IEnumerator TracerRoutine(Vector3 from, Vector3 to)
    {
        if (tracer == null) yield break;

        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;

        yield return new WaitForSeconds(0.05f);

        tracer.enabled = false;
    }

    private void StartReload()
    {
        if (reloading) return;

        reloading = true;
        reloadTimer = Random.Range(1.8f, 2.4f);

        // 换弹时缩到掩体后面：站着压弹是给对面送靶子
        if (target != null && !ChishuiWaterRule.InWater(transform.position))
        {
            crouchTimer = Mathf.Max(crouchTimer, reloadTimer + 0.4f);
        }

        if (audioSource != null && reloadClip != null)
        {
            audioSource.PlayOneShot(reloadClip, 0.35f);
        }
    }

    private void HandleReload(float dt)
    {
        if (!reloading) return;

        reloadTimer -= dt;
        if (reloadTimer > 0f) return;

        reloading = false;
        magazine = magazineSize;
    }

    // ───────────────────────────── 走位 ─────────────────────────────

    private void HandleReposition(float dt)
    {
        // 交火时由 HandleCombatMovement 负责走位
        if (target != null) return;

        repositionTimer -= dt;
        if (moving || repositionTimer > 0f) return;

        repositionTimer = Random.Range(5f, 10f);
        moveTarget = PickCoverPoint();
        moving = Vector3.Distance(transform.position, moveTarget) > 0.4f;
        moveSpeed = 1.5f;
    }

    private Vector3 PickCoverPoint()
    {
        for (int i = 0; i < 6; i++)
        {
            Vector3 candidate = home + new Vector3(
                Random.Range(-2.6f, 2.6f), 0f, Random.Range(-1.2f, 1.2f));

            Vector3 bottom = candidate + Vector3.up * 0.4f;
            Vector3 top = candidate + Vector3.up * 1.2f;

            Collider[] blocked = Physics.OverlapCapsule(bottom, top, 0.24f, ~0,
                                                        QueryTriggerInteraction.Ignore);
            bool clear = true;
            for (int b = 0; b < blocked.Length; b++)
            {
                if (blocked[b] == null || blocked[b].transform.IsChildOf(transform)) continue;
                clear = false;
                break;
            }

            if (!clear) continue;

            return candidate;
        }

        return home;
    }

    private void StopMoving()
    {
        moving = false;
        walkBlend = 0f;
        moveStuckTimer = 0f;
        detourTimer = 0f;
    }

    private void Recover(float dt)
    {
        if (health == null || health.IsDead) return;
        if (Time.time - lastHurtTime < 6f) { healTimer = 0f; return; }

        healTimer += dt;
        if (healTimer < 1f) return;

        healTimer = 0f;
        if (health.Current < health.MaxHealth)
        {
            health.Restore(4f);
        }
    }

    // ───────────────────────────── 动画 ─────────────────────────────

    private void UpdateIdle(float dt)
    {
        walkBlend = Mathf.MoveTowards(walkBlend, 0f, 5f * dt);
        idlePhase += dt;

        // 站岗时缓慢左右观察，不是木头人
        if (!moving)
        {
            float scan = Mathf.Sin(idlePhase * 0.55f) * 7f;
            Quaternion look = Quaternion.Euler(0f, homeYaw + scan, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 2.2f * dt);
        }

        Animate(dt);
    }

    private void Animate(float dt)
    {
        float swing = moving ? Mathf.Sin(walkPhase) * 24f * walkBlend : 0f;

        float breathe = Mathf.Sin(idlePhase * 1.6f) * (1f - walkBlend) * 1.2f;

        // 蹲、趴和主角共用一套关键帧：先取卧倒链，再叠蹲姿的跪地 / 蹚步
        StancePose.Key proneKey = StancePose.SampleProne(proneBlend);
        float crouchBase = crouchBlend * (1f - proneBlend);

        StancePose.SampleCrouch(kneelWeight,
            out float thighFront, out float shinFront,
            out float thighBack, out float shinBack, out float crouchDrop);

        float lean = crouchBase * StancePose.CrouchLean + proneKey.lean;

        float thighLBase = crouchBase * thighFront + proneKey.thighL;
        float shinLBase = crouchBase * shinFront + proneKey.shinL;
        float thighRBase = crouchBase * thighBack + proneKey.thighR;
        float shinRBase = crouchBase * shinBack + proneKey.shinR;

        // 整个人绕髋部翻下去，腿不能跟着翻：减掉整体前倾，腿只认自己的关键帧
        SetPivot(hipL, swing + thighLBase - lean);
        SetPivot(hipR, -swing + thighRBase - lean);
        SetPivot(kneeL, shinLBase);
        SetPivot(kneeR, shinRBase);

        // 有目标时端枪压肩；平时枪端在胸前低姿戒备；趴下时两臂向前撑地
        bool aiming = target != null;
        float armL;
        float armR;
        float supportZ;

        if (aiming)
        {
            armL = -54f + Mathf.Sin(walkPhase) * 5f * walkBlend;
            armR = -76f + breathe * 0.5f;
            supportZ = 32f;
        }
        else
        {
            armL = -36f + breathe + Mathf.Sin(walkPhase) * 8f * walkBlend;
            armR = -50f + breathe * 0.5f + Mathf.Sin(walkPhase) * 5f * walkBlend;
            supportZ = 24f;
        }

        // 号兵：不吹号时斜端枪——右手在枪托后握把、左手托住护木，
        // 两臂夹到胸前端枪（枪本身在 LateUpdate 里贴着两只手摆）
        if (bugler && bugleBlend < 0.5f && !aiming)
        {
            armR = -40f + breathe * 0.5f + Mathf.Sin(walkPhase) * 4f * walkBlend;
            armL = -52f + breathe + Mathf.Sin(walkPhase) * 5f * walkBlend;
            supportZ = 30f;
        }

        // 号兵：吹号时右臂抬到嘴边，左手自然垂在身侧
        if (bugler && bugleBlend > 0.01f)
        {
            armR = Mathf.Lerp(armR, -104f, bugleBlend);
            armL = Mathf.Lerp(armL, -20f, bugleBlend);
            supportZ = Mathf.Lerp(supportZ, 2f, bugleBlend);
        }

        // 拉栓：右手去拨枪机
        if (boltTimer > 0f)
        {
            float work = Mathf.Sin(Mathf.Clamp01(1f - boltTimer / BoltDuration) * Mathf.PI);
            armR = Mathf.Lerp(armR, -54f, work);
            supportZ -= 12f * work;
        }

        UpdateKnife(dt);

        if (meleeBlend > 0.01f)
        {
            float slash = meleeSwing > 0f
                ? Mathf.Sin((1f - Mathf.Clamp01(meleeSwing / MeleeSwingDuration)) * Mathf.PI)
                : 0f;
            armR = Mathf.Lerp(armR, -74f + 48f * slash, meleeBlend);
            armL = Mathf.Lerp(armL, -34f - 16f * slash, meleeBlend);
            supportZ = Mathf.Lerp(supportZ, 10f, meleeBlend);
        }

        // 卧倒：两臂按关键帧伸到地上撑住，小臂再折下来
        // （上身的前倾已经由 Pose 整体旋转承担，这里只给关节自己的角度）
        armL = Mathf.Lerp(armL, proneKey.armL, proneBlend);
        armR = Mathf.Lerp(armR, proneKey.armR, proneBlend);

        SetPivot(shoulderL, armL, Mathf.Lerp(supportZ, 0f, proneBlend));
        SetPivot(shoulderR, armR);
        SetPivot(elbowL, proneKey.forearmL * proneBlend);
        SetPivot(elbowR, proneKey.forearmR * proneBlend);

        UpdateRifle(aiming, dt);
        AnimateBolt();

        // 上身整体前倾由 Pose 承担，躯干零件不再单独弯腰
        if (torso != null) torso.localRotation = Quaternion.identity;

        if (pose == null) return;

        // 绕髋部翻下去：Pose 的原点在脚底，直接转会像一块板子从脚踝倒下，
        // 这里补偿位置，让转动中心落在髋部（和主角的 TorsoPivot 同一个点）
        float bodyDrop = crouchBase * crouchDrop + proneKey.bodyY
                         + Mathf.Sin(Time.time * 1.35f) * proneBlend * 0.005f;
        Vector3 hipPoint = new Vector3(0f, 0.8f, 0f);
        Quaternion bodyRot = Quaternion.Euler(lean, 0f, proneBlend * 3f - peekBlend * 13f);

        pose.localRotation = bodyRot;
        // 探头：整个人从掩体侧缘探出去一点，不是原地站着平移
        pose.localPosition = hipPoint - bodyRot * hipPoint
                             + new Vector3(peekBlend * 0.2f, bodyDrop, 0f);
    }

    private static void SetPivot(Transform pivot, float angleX, float angleZ = 0f)
    {
        if (pivot != null) pivot.localRotation = Quaternion.Euler(angleX, 0f, angleZ);
    }

    /// <summary>
    /// 火把要正握：手臂摆动会把火把带得前倾，这里在动画之后再扳回竖直，
    /// 火头始终朝上，跟人走路的节奏无关。
    /// </summary>
    private void LateUpdate()
    {
        if (dead) return;

        if (torchBearer && torch != null)
        {
            torch.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        FitBuglerGrip();
    }

    /// <summary>
    /// 号兵抱枪：单节胳膊够不到胸前，固定摆枪总会脱手。
    /// 这里反过来，用两只手的实际位置反推枪的位置和朝向，
    /// 枪托落在右手后方、枪管顺着左右手连线指出去，呼吸和走路时都黏在手上。
    /// </summary>
    private void FitBuglerGrip()
    {
        if (!bugler) return;
        if (bugleBlend > 0.5f || target != null || proneBlend > 0.01f) return;
        if (meleeBlend > 0.01f || boltTimer > 0f || torchBearer) return;
        if (rifle == null || !rifle.gameObject.activeSelf) return;
        if (pose == null || shoulderL == null || shoulderR == null) return;

        Vector3 rightHand = shoulderR.TransformPoint(0f, -0.6f, 0.01f);
        Vector3 leftHand = shoulderL.TransformPoint(0f, -0.6f, 0.01f);

        Vector3 forward = leftHand - rightHand;
        if (forward.sqrMagnitude < 0.0004f) return;
        forward.Normalize();

        Vector3 stock = rightHand - forward * 0.12f;

        Vector3 localForward = pose.InverseTransformDirection(forward);
        Vector3 localUp = pose.InverseTransformDirection(Vector3.up);
        if (Vector3.Cross(localForward, localUp).sqrMagnitude < 1e-4f) return;

        rifle.localPosition = pose.InverseTransformPoint(stock);
        rifle.localRotation = Quaternion.LookRotation(localForward, localUp);
    }

    /// <summary>火把的光舌：强度按噪声抖动，风雪里忽明忽暗。</summary>
    private void UpdateTorch(float dt)
    {
        if (torchLight == null) return;

        bool lit = torchBearer && !dead;
        if (torchLight.enabled != lit) torchLight.enabled = lit;
        if (!lit) return;

        float t = Time.time * 9f + torchSeed;
        float flicker = 0.74f + Mathf.PerlinNoise(t, torchSeed) * 0.52f;

        // 全图暴风雪把火苗压下去一点
        float storm = DynamicWeather.BlizzardLevel;
        flicker *= Mathf.Lerp(1f, 0.72f, storm);

        torchLight.intensity = 2.3f * flicker;
        torchLight.range = 11f + 2.2f * flicker;
    }

    /// <summary>贴脸时收起枪、亮出大刀。</summary>
    private void UpdateKnife(float dt)
    {
        bool want = !dead && target != null && FlatGapTo(target) <= meleeRange * 1.55f;
        meleeBlend = Mathf.MoveTowards(meleeBlend, want ? 1f : 0f, 7f * dt);
        if (meleeSwing > 0f) meleeSwing -= dt;

        bool meleeing = meleeBlend >= 0.5f;

        if (torchBearer)
        {
            // 照明兵：平时左手火把、右手驳壳枪，贴脸才收枪换刀
            if (pistolProp != null && pistolProp.gameObject.activeSelf == meleeing)
            {
                pistolProp.gameObject.SetActive(!meleeing);
            }
            if (rifle != null && rifle.gameObject.activeSelf) rifle.gameObject.SetActive(false);
            if (pistolProp != null)
            {
                pistolKick = Mathf.MoveTowards(pistolKick, 0f, dt * 8f);
                float kick = pistolKick * pistolKick;
                pistolProp.localPosition = new Vector3(0f, -0.6f + 0.018f * kick, 0.06f - 0.05f * kick);
                pistolProp.localRotation = Quaternion.Euler(64f - 18f * kick, 0f, 0f);
            }
        }
        else if (rifle != null)
        {
            bool show = !meleeing;
            if (rifle.gameObject.activeSelf != show) rifle.gameObject.SetActive(show);
        }

        if (knife == null) return;

        bool knifeVisible = meleeing;
        if (knife.gameObject.activeSelf != knifeVisible)
        {
            knife.gameObject.SetActive(knifeVisible);
        }

        if (!knifeVisible) return;

        float k = meleeSwing > 0f
            ? 1f - Mathf.Clamp01(meleeSwing / MeleeSwingDuration) : 0f;
        float swing = Mathf.Sin(k * Mathf.PI);

        knife.localPosition = new Vector3(0.36f - 0.18f * swing,
                                          1.0f + 0.12f * swing,
                                          0.34f + 0.2f * swing);
        knife.localRotation = Quaternion.Euler(72f - 52f * swing, 0f, -12f - 32f * swing);
    }

    private void UpdateRifle(bool aiming, float dt)
    {
        if (rifle == null) return;

        rifleBlend = Mathf.MoveTowards(rifleBlend, aiming ? 1f : 0f, 5f * dt);

        Vector3 carryPos = new Vector3(0.18f, 1.03f, 0.26f);
        Vector3 aimPos = new Vector3(0.24f, 1.2f, 0.46f);
        rifle.localPosition = Vector3.Lerp(carryPos, aimPos, rifleBlend);

        Quaternion carryRot = Quaternion.Euler(-16f, 0f, -8f);
        Quaternion aimRot = Quaternion.Euler(-2f, 0f, 0f);
        Quaternion rotation = Quaternion.Slerp(carryRot, aimRot, rifleBlend);

        if (boltTimer > 0f)
        {
            float work = Mathf.Sin(Mathf.Clamp01(1f - boltTimer / BoltDuration) * Mathf.PI);
            rotation *= Quaternion.Euler(0f, 0f, -9f * work);
        }

        // 吹号时步枪从手里挪到后背：枪托在左肩、枪管斜指向右胯。
        // 枢轴在枪托上，所以先绕 X 放平再绕 Z 斜过来，不会横着戳进身体
        if (bugler)
        {
            Vector3 backPos = new Vector3(0.02f, 1.3f, -0.42f);
            Quaternion backRot = Quaternion.Euler(90f, 0f, 30f);
            rifle.localPosition = Vector3.Lerp(rifle.localPosition, backPos, bugleBlend);
            rotation = Quaternion.Slerp(rotation, backRot, bugleBlend);
        }

        // 卧倒时枪从胸前挪到身侧，枪管顺着地面朝前，不往雪地里戳
        if (proneBlend > 0.001f)
        {
            rifle.localPosition = Vector3.Lerp(rifle.localPosition,
                new Vector3(0.36f, 0.95f, 0.02f), proneBlend);
            rotation = Quaternion.Slerp(rotation, Quaternion.Euler(-90f, 0f, -8f), proneBlend);
        }

        rifle.localRotation = rotation;
    }

    private void AnimateBolt()
    {        if (boltHandle == null) return;

        if (boltTimer <= 0f)
        {
            boltHandle.localPosition = boltHomePos;
            boltHandle.localRotation = boltHomeRot;
            return;
        }

        float t = 1f - boltTimer / BoltDuration;

        float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.15f))
                     - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.55f) / 0.2f));
        float pull = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.2f))
                     - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.38f) / 0.2f));

        boltHandle.localRotation = boltHomeRot * Quaternion.Euler(0f, 0f, 62f * lift);
        boltHandle.localPosition = boltHomePos + new Vector3(0f, 0.018f * lift, -0.09f * pull);
    }

    // ───────────────────────────── 死亡 ─────────────────────────────

    private void HandleDeath(DamageInfo info)
    {
        if (dead) return;

        dead = true;
        target = null;
        moving = false;

        if (tracer != null) tracer.enabled = false;

        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        StartCoroutine(CorpseRoutine());
    }

    private IEnumerator CorpseRoutine()
    {
        Quaternion from = transform.rotation;
        Quaternion to = from * Quaternion.Euler(88f, 0f, 14f);

        float t = 0f;
        while (t < 0.7f)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(from, to, t / 0.7f);
            yield return null;
        }

        yield return new WaitForSeconds(25f);
        Destroy(gameObject);
    }
}
