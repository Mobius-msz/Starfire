using System.Collections;
using UnityEngine;

/// <summary>
/// 敌军士兵 AI：推进、找掩体、蹲趴探头、射击。
///
/// 行为逻辑：
///   - 主动索敌：优先追玩家，跟丢后往最后看见的位置压上去；
///   - 玩家准星压到自己，或者中弹，就立刻找能挡住视线的落脚点；
///   - 到了掩体后按掩体高度决定蹲下还是趴下，躲一会儿再探头打一两枪；
///   - 跑动时左右晃动走位，接近交火距离会横向游走，不站着当靶子。
///
/// 人物模型的所有零件在 Awake 时被收进一个 Pose 空物体，
/// 蹲、趴、探头都只改 Pose 的位移和旋转，命中盒跟着一起动。
/// </summary>
public class EnemyAI : MonoBehaviour
{
    private enum Mind { Advance, SeekCover, Hide, Peek }

    private Transform player;
    private Transform target;
    private Health health;
    private AudioSource audioSource;
    private LineRenderer tracer;
    private AudioClip shotClip;
    private Camera viewCamera;

    private bool aiEnabled = true;
    private bool dead;
    private float fireTimer = 1.5f;
    private float retargetTimer;
    private float coverThinkTimer;
    private float coverCooldown;
    private float suppressTimer;
    private float hideTimer;
    private float peekTimer;

    private Transform pose;
    private Transform torso;
    private Transform head;
    private Transform hipL;
    private Transform hipR;
    private Transform kneeL;
    private Transform kneeR;
    private Transform shoulderL;
    private Transform shoulderR;
    private Transform rifle;
    private Transform boltHandle;
    private MuzzleFlash muzzleFlash;
    private Vector3 boltHomePos;
    private Quaternion boltHomeRot;
    private float boltTimer;
    private float rifleBlend;
    private const float BoltDuration = 0.62f;
    private static AudioClip boltClipCache;

    private static AudioClip BoltClip
    {
        get
        {
            if (boltClipCache == null) boltClipCache = ProceduralAudio.Bolt();
            return boltClipCache;
        }
    }

    private float walkPhase;
    private float walkBlend;
    private float runBlend;
    private float crouchBlend;
    private float proneBlend;
    private float leanBlend;
    private float flinch;
    private float seed;

    // ── 近战拼刀 ──
    // 贴到跟前就收起枪、换刀，脚步比平时更快、绕步更活，
    // 玩家想靠一把刀连杀就得瞄准每一刀的时机。
    [Header("近战拼刀")]
    [SerializeField] private float meleeRange = 2.8f;
    [SerializeField] private float meleeDamage = 18f;
    [SerializeField] private float meleeInterval = 0.95f;

    private Transform knife;
    private float meleeTimer;
    private float meleeBlend;
    private float meleeSwing;
    private const float MeleeSwingDuration = 0.42f;
    private static AudioClip meleeClipCache;

    private static AudioClip MeleeClip
    {
        get
        {
            if (meleeClipCache == null) meleeClipCache = ProceduralAudio.Swing();
            return meleeClipCache;
        }
    }

    private Mind mind = Mind.Advance;
    private Quaternion desiredRotation;
    private bool movingNow;
    private float moveSpeedNow;

    private Vector3 coverPosition;
    private Vector3 lastKnownPlayer;
    private bool hasCover;
    private bool proneCover;
    private float peekSide = 1f;

    // 弹道从身边掠过时的躲闪：短促一步侧移，或者干脆扑到掩体后
    private float dodgeTimer;
    private float dodgeCooldown;
    private Vector3 dodgeDir;

    // 大部分敌军从浮桥桥面推进，少数从河里涉水
    private bool bridgeLane;
    private float laneOffset;
    private bool columnFocus;

    // 炸桥时桥上残余敌军的溃逃与被气浪掀飞
    private bool fleeing;
    private Vector3 fleeTarget;
    private bool blastFlying;
    private const float SurfaceY = -0.07f;

    // 过桥与卡住检测
    private const float RopeBridgeZ = 88f;
    private Vector3 respawnPoint;
    private Vector3 stuckAnchor;
    private float stuckTimer;
    private float detourTimer;
    private Vector3 detourDir;
    private int stuckAttempts;
    private float stuckMemory;

    /// <summary>第一关的浮桥推进逻辑。第二关没有浮桥，生成前关掉。</summary>
    public static bool BridgeLanesEnabled = true;

    // 声东击西：假火堆点着后，附近的敌人会被引过去一阵子
    private static Vector3 lurePoint;
    private static float lureUntil;

    public static void Lure(Vector3 point, float duration)
    {
        lurePoint = point;
        lureUntil = Time.time + Mathf.Max(0.5f, duration);
    }

    /// <summary>脚步 / 枪声惊动附近敌人；大雾会缩短视距，雨声会缩短听觉半径。</summary>
    public static void AlertNearby(Vector3 point, float radius)
    {
        if (radius <= 0.1f) return;

        float sqr = radius * radius;
        for (int i = 0; i < All.Count; i++)
        {
            EnemyAI ai = All[i];
            if (ai == null || ai.dead) continue;

            Vector3 delta = point - ai.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > sqr) continue;

            ai.lastKnownPlayer = point;
            if (ai.mind == Mind.Hide && Random.value < 0.55f)
            {
                ai.mind = Mind.Advance;
                ai.hideTimer = 0f;
            }
        }
    }

    /// <summary>
    /// 子弹从身边掠过：弹道两侧的敌军会侧闪一步，或者抢先缩回掩体。
    /// 由玩家与战友的枪在开火时调用。
    /// </summary>
    public static void NotifyIncomingFire(Vector3 origin, Vector3 impact)
    {
        for (int i = 0; i < All.Count; i++)
        {
            EnemyAI ai = All[i];
            if (ai == null || ai.IsDead) continue;
            ai.TryDodge(origin, impact);
        }
    }

    private void TryDodge(Vector3 origin, Vector3 impact)
    {
        if (dead || !aiEnabled || fleeing || dodgeCooldown > 0f) return;

        Vector3 a = new Vector3(origin.x, 0f, origin.z);
        Vector3 b = new Vector3(impact.x, 0f, impact.z);
        Vector3 me = new Vector3(transform.position.x, 0f, transform.position.z);

        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0.01f
            ? Mathf.Clamp01(Vector3.Dot(me - a, ab) / ab.sqrMagnitude)
            : 0f;

        if (Vector3.Distance(a + ab * t, me) > 2.2f) return;
        if (Vector3.Distance(a, me) > 45f) return;

        dodgeCooldown = Random.Range(1.4f, 2.8f);

        // 附近有掩体就先钻进去，比在空地上横跳稳妥
        if (hasCover && mind == Mind.Advance && coverCooldown <= 0f
            && Random.value < 0.45f)
        {
            StartSeekCover();
            return;
        }

        Vector3 side = Vector3.Cross(Vector3.up, ab.normalized);
        if (side.sqrMagnitude < 0.01f) side = transform.right;
        if (Random.value < 0.5f) side = -side;

        dodgeDir = (side + ab.normalized * 0.15f).normalized;
        dodgeTimer = Random.Range(0.22f, 0.42f);
    }

    // 相互推挤用的静态表：每帧对全场做 FindObjectsOfType 太贵
    private static readonly System.Collections.Generic.List<EnemyAI> All =
        new System.Collections.Generic.List<EnemyAI>();    [SerializeField] private float moveSpeed = 3.1f;
    [SerializeField] private float stopDistance = 14f;
    [SerializeField] private float fireInterval = 2.6f;
    [SerializeField] private float damage = 8f;
    [SerializeField] private float eyeHeight = 1.35f;
    [SerializeField] private float maxFireDistance = 55f;

    public bool IsDead => dead || (health != null && health.IsDead);

    public void Initialize(Transform playerTransform, AudioClip shot)
    {
        player = playerTransform;
        target = playerTransform;
        shotClip = shot;
        fireTimer = Random.Range(0.8f, 2.2f);
        retargetTimer = Random.Range(0.3f, 1.1f);
        coverThinkTimer = Random.Range(0.8f, 1.8f);
        lastKnownPlayer = playerTransform != null ? playerTransform.position : Vector3.zero;

        // 河面禁止通行时全员走桥，免得一队人堵在岸边下不来
        bridgeLane = BridgeLanesEnabled && (RiverGuard.Active || Random.value < 0.7f);
        laneOffset = RiverGuard.Active
            ? Random.Range(-4.1f, 4.1f)
            : Random.Range(-2.1f, 2.1f);

        // 第一关：四成敌军盯着浮桥上的大部队，剩下六成压制玩家
        columnFocus = !ChishuiWaterRule.Active && Random.value < 0.4f;

        respawnPoint = transform.position;
    }

    /// <summary>
    /// 整个人沉进赤水河：从水里捞起来送回出发的岸边，重整旗鼓再打。
    /// 玩家看不见这段，但也不会出现一队人贴着河床走路的怪样子。
    /// </summary>
    private void RespawnFromWater()
    {
        Vector3 point = ChishuiWaterRule.ClampSpawn(respawnPoint);
        point.y = ChishuiWaterRule.WaterSurfaceY + 0.35f;
        transform.position = point;

        mind = Mind.Peek;
        peekTimer = Random.Range(1.5f, 2.5f);
        movingNow = false;
        fleeing = false;
        detourTimer = 0f;
        stuckTimer = 0f;
        coverCooldown = 0.6f;
        target = player;

        FollowGround();
        if (ChishuiWaterRule.FullySubmerged(transform.position))
        {
            transform.position = new Vector3(point.x, ChishuiWaterRule.WaterSurfaceY + 0.35f, point.z);
        }
    }

    /// <summary>沿桥面推进的路径点：先上桥，再走完整座桥，最后下桥冲锋。</summary>
    private Vector3 LaneTarget()
    {
        Vector3 p = transform.position;

        if (p.z > 43.2f) return new Vector3(laneOffset, p.y, 42.0f);
        if (p.z > 16.0f) return new Vector3(laneOffset, p.y, 15.0f);

        return player != null ? player.position : p;
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        if (health != null) health.Died += HandleDeath;
        if (health != null) health.Damaged += HandleDamaged;

        hipL = transform.Find("HipL");
        hipR = transform.Find("HipR");
        kneeL = hipL != null ? hipL.Find("KneeL") : null;
        kneeR = hipR != null ? hipR.Find("KneeR") : null;
        shoulderL = transform.Find("ShoulderL");
        shoulderR = transform.Find("ShoulderR");

        BuildPose();

        seed = Random.value;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 4f;
        audioSource.maxDistance = 130f;

        tracer = gameObject.AddComponent<LineRenderer>();
        tracer.positionCount = 2;
        tracer.startWidth = 0.035f;
        tracer.endWidth = 0.012f;
        tracer.useWorldSpace = true;
        tracer.material = LevelBuilder.MakeMaterial(
            new Color(1f, 0.8f, 0.45f), new Color(1f, 0.55f, 0.15f));
        tracer.enabled = false;
    }

    /// <summary>把模型零件收进一个空物体，方便整体做蹲/趴/探头姿态。</summary>
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
        head = pose.Find("Head");
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

        // 拼刀用的大刀：平时收起，贴身时替换步枪
        knife = MeleeKnife.Create(pose);

        desiredRotation = transform.rotation;
    }

    private void OnEnable()
    {
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void OnDestroy()
    {
        All.Remove(this);

        if (health != null)
        {
            health.Died -= HandleDeath;
            health.Damaged -= HandleDamaged;
        }
    }

    private void Update()
    {
        if (dead || !aiEnabled || player == null) return;

        float dt = Time.deltaTime;

        // 掉进赤水河、整个人没到水面以下：捞回出发的岸边
        if (ChishuiWaterRule.FullySubmerged(transform.position))
        {
            RespawnFromWater();
            Animate(dt, 30f);
            return;
        }

        // 桥要炸了：枪也顾不上了，闷头往对岸桥头跑
        if (fleeing)
        {
            Vector3 to = fleeTarget - transform.position;
            to.y = 0f;

            movingNow = to.sqrMagnitude > 0.16f;
            moveSpeedNow = 5.4f;
            if (movingNow)
            {
                Vector3 dir = to.normalized;
                Vector3 fleeFrom = transform.position;
                MoveDelta(dir * moveSpeedNow * dt);
                transform.position = RiverGuard.Clamp(fleeFrom, transform.position);

                desiredRotation = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 9f * dt);
            }

            FollowGround();
            // 传一个大距离，让手臂收成行军姿态，不再端枪瞄准
            Animate(dt, 30f);
            return;
        }

        retargetTimer -= dt;
        if (retargetTimer <= 0f)
        {
            retargetTimer = Random.Range(0.7f, 1.2f);
            PickTarget();
        }

        if (target == null) target = player;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (CanSee(player)) lastKnownPlayer = player.position;

        Vector3 before = transform.position;

        UpdateTimers(dt, distance);
        TickMind(dt, distance);

        // 闪避比走位优先：抢一步横移，让子弹擦着过去
        if (dodgeTimer > 0f)
        {
            dodgeTimer -= dt;
            MoveDelta(dodgeDir * 4.4f * dt);
            movingNow = true;
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation,
            5f * dt);

        KeepSpacing(dt);
        TickStuck(dt);
        transform.position = RiverGuard.Clamp(before, transform.position);
        // 赤水河：敌军不下水，只在对岸据守射击
        transform.position = ChishuiWaterRule.Clamp(before, transform.position);
        FollowGround();
        Animate(dt, distance);
    }

    // ───────────────────────────── 决策 ─────────────────────────────

    private void UpdateTimers(float dt, float distance)
    {
        fireTimer -= dt;
        if (boltTimer > 0f) boltTimer -= dt;
        if (coverCooldown > 0f) coverCooldown -= dt;
        if (suppressTimer > 0f) suppressTimer -= dt;
        if (dodgeCooldown > 0f) dodgeCooldown -= dt;

        coverThinkTimer -= dt;
        if (coverThinkTimer > 0f) return;

        coverThinkTimer = Random.Range(1.2f, 2.2f);

        if (mind != Mind.Advance || coverCooldown > 0f || distance > 40f) return;

        // 准星压到自己身上就立刻进掩体；站在空地上挨过枪的也会变谨慎
        float seekChance = PlayerAimingAtMe() ? 0.92f : 0.34f;
        if (suppressTimer > 0f) seekChance = Mathf.Max(seekChance, 0.6f);

        if (Random.value < seekChance)
        {
            StartSeekCover();
        }
    }

    private void TickMind(float dt, float distance)
    {
        movingNow = false;
        moveSpeedNow = CurrentSpeed(distance);

        // 被假火堆牵着走：短时间内不开枪、不追玩家，直奔火堆
        if (Time.time < lureUntil)
        {
            Vector3 toLure = lurePoint - transform.position;
            toLure.y = 0f;

            if (toLure.sqrMagnitude < 70f * 70f)
            {
                desiredRotation = toLure.sqrMagnitude > 0.5f
                    ? Quaternion.LookRotation(toLure)
                    : desiredRotation;

                if (toLure.magnitude > 4f)
                {
                    movingNow = MoveToward(lurePoint, moveSpeedNow * 1.2f, dt);
                }

                // 假目标的火光在另一头，先别朝玩家扣扳机
                fireTimer = Mathf.Max(fireTimer, 0.5f);
                return;
            }
        }

        // 贴身就切刀拼刺：不再开枪，绕步贴上去砍
        Transform engageTarget = CombatTarget;
        if (engageTarget != null)
        {
            float engageDistance = FlatDistance(engageTarget.position);
            if (engageDistance <= meleeRange)
            {
                TickMelee(dt, engageTarget, engageDistance);
                return;
            }
        }

        switch (mind)
        {
            case Mind.SeekCover:
                movingNow = MoveToward(coverPosition, moveSpeedNow, dt);
                if (!movingNow) EnterHide();
                break;

            case Mind.Hide:
                desiredRotation = FacingTarget();
                if (distance < 6f)
                {
                    // 被摸到脸上了，别蹲着了
                    mind = Mind.Advance;
                    coverCooldown = 0f;
                    break;
                }
                hideTimer -= dt;
                if (hideTimer <= 0f)
                {
                    peekSide = Random.value < 0.5f ? -1f : 1f;
                    peekTimer = Random.Range(1.3f, 2.2f);
                    fireTimer = Mathf.Min(fireTimer, 0.3f);
                    mind = Mind.Peek;
                }
                break;

            case Mind.Peek:
                desiredRotation = FacingTarget();
                peekTimer -= dt;
                TryFire(distance);
                if (peekTimer <= 0f)
                {
                    if (hasCover && !BlocksPlayer(coverPosition, 1.0f))
                    {
                        // 掩体已经被绕开，起身换位置
                        mind = Mind.Advance;
                        coverCooldown = 0f;
                    }
                    else
                    {
                        hideTimer = Random.Range(0.8f, 1.7f);
                        mind = Mind.Hide;
                    }
                }
                break;

            default:
                Advance(dt, distance);
                break;
        }
    }

    private void Advance(float dt, float distance)
    {
        // 桥上推进：先走完整座桥，下桥再按正常逻辑打
        if (bridgeLane && transform.position.z > 15f)
        {
            Vector3 lane = LaneTarget() - transform.position;
            lane.y = 0f;

            if (lane.sqrMagnitude > 0.25f)
            {
                Vector3 dir = lane.normalized;
                MoveDelta(dir * (moveSpeedNow * 0.95f) * dt);
                movingNow = true;
                desiredRotation = lane.sqrMagnitude > 4f
                    ? Quaternion.LookRotation(dir)
                    : FacingTarget();
            }
            else
            {
                desiredRotation = FacingTarget();
            }

            TryFire(distance);
            return;
        }

        // 第二关：玩家在河对岸时走太平渡索桥过去，别挤在桥头被栏杆卡住
        if (TryCrossRopeBridge(dt, distance)) return;

        bool sees = CanSee(player);
        Vector3 goal = sees ? player.position : lastKnownPlayer;
        Vector3 toGoal = goal - transform.position;
        toGoal.y = 0f;

        // 跟丢了也往最后看见的位置压，而不是原地站着
        if (distance > stopDistance || !sees)
        {
            if (toGoal.sqrMagnitude < 0.04f)
            {
                desiredRotation = FacingTarget();
                return;
            }

            Vector3 dir = toGoal.normalized;
            float weave = Mathf.Sin(Time.time * 1.9f + seed * 12f) * 0.38f;
            dir = (dir + Vector3.Cross(Vector3.up, dir) * weave).normalized;

            MoveDelta(dir * moveSpeedNow * dt);
            movingNow = true;
            desiredRotation = Quaternion.LookRotation(dir);

            TryFire(distance);
            return;
        }

        // 进入交火距离：横向游走，一边走一边打
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        Vector3 flat = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
        float strafe = Mathf.Sin(Time.time * 1.15f + seed * 7f);
        Vector3 side = Vector3.Cross(Vector3.up, flat) * strafe;

        if (side.sqrMagnitude > 0.01f)
        {
            MoveDelta(side * (moveSpeed * 0.6f) * dt);
            movingNow = true;
        }

        desiredRotation = FacingTarget();
        TryFire(distance);
    }

    /// <summary>
    /// 赤水河上的过桥路线：玩家在河对岸、敌人又在太平渡附近时，
    /// 先跑到桥头，再沿桥板走到对岸；上了岸就交回普通追击逻辑。
    /// </summary>
    private bool TryCrossRopeBridge(float dt, float distance)
    {
        if (!ChishuiWaterRule.Active || player == null) return false;

        Vector3 pos = transform.position;
        float playerX = player.position.x;

        // 玩家还在河心，或者跟自己同岸，就没必要专门过桥
        if (Mathf.Abs(playerX) < 3f) return false;
        if (Mathf.Sign(playerX) == Mathf.Sign(pos.x)) return false;

        if (Mathf.Abs(pos.z - RopeBridgeZ) > 26f) return false;
        if (Mathf.Abs(player.position.z - RopeBridgeZ) > 42f) return false;

        float side = Mathf.Sign(pos.x);
        // 每人一条过桥车位：在 4 米宽的桥面上错开，不再几十号人抢同一个点
        float laneZ = RopeBridgeZ + Mathf.Lerp(-1.1f, 1.1f, seed);
        // 桥口判定比走廊略宽：站着等前面的人过桥时，不至于在桥口来回横跳
        bool atMouth = Mathf.Abs(pos.z - laneZ) <= 1.2f
                       && Mathf.Abs(pos.z - RopeBridgeZ) <= 1.5f;

        // 桥头没对齐就先沿自家岸边跑过去，对齐了再上桥
        Vector3 goal = atMouth
            ? new Vector3(-side * 17.2f, 0f, laneZ)
            : new Vector3(side * 15.9f, 0f, laneZ);

        Vector3 to = goal - pos;
        to.y = 0f;

        if (to.sqrMagnitude > 0.25f)
        {
            Vector3 dir = to.normalized;

            // 前面半步内有人就站住等：桥窄，硬挤只会把彼此卡在桥口
            if (atMouth && BlockedOnBridge(dir))
            {
                movingNow = false;
                desiredRotation = FacingTarget();
                TryFire(distance);
                return true;
            }

            MoveDelta(dir * moveSpeedNow * dt);
            movingNow = true;
            desiredRotation = Quaternion.LookRotation(dir);
        }
        else
        {
            desiredRotation = FacingTarget();
        }

        TryFire(distance);
        return true;
    }

    /// <summary>桥面上自己正前方是否已经有人：有人就排在他后面，别顶上去。</summary>
    private bool BlockedOnBridge(Vector3 dir)
    {
        Vector3 sideAxis = Vector3.Cross(Vector3.up, dir);

        for (int i = 0; i < All.Count; i++)
        {
            EnemyAI other = All[i];
            if (other == null || other == this || other.IsDead) continue;
            if (!ChishuiWaterRule.OnBridge(other.transform.position)) continue;

            Vector3 delta = other.transform.position - transform.position;
            delta.y = 0f;

            float ahead = Vector3.Dot(delta, dir);
            if (ahead <= 0.2f || ahead > 1.9f) continue;

            // 对方在侧向也占着自己的道，不是同一条线上的人
            if (Mathf.Abs(Vector3.Dot(delta, sideAxis)) > 1.0f) continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// 卡住检测：想走却一秒挪不出半米，就横着绕一步。
    /// 桥栏杆、沙袋缝、几个人挤在一起都会让人原地打转。
    /// </summary>
    private void TickStuck(float dt)
    {
        // 桥上两边都是扶手绳，绕障只会把人推下桥或者堵死桥口：桥上不绕障
        if (ChishuiWaterRule.Active && ChishuiWaterRule.OnBridge(transform.position))
        {
            detourTimer = 0f;
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        if (stuckMemory > 0f) stuckMemory -= dt;
        else stuckAttempts = 0;

        if (detourTimer > 0f)
        {
            detourTimer -= dt;
            MoveDelta(detourDir * (moveSpeed * 0.95f) * dt);
            movingNow = true;
            return;
        }

        if (!movingNow)
        {
            stuckTimer = 0f;
            stuckAnchor = transform.position;
            return;
        }

        stuckTimer += dt;
        if (stuckTimer < 0.9f) return;

        float moved = FlatDistance(stuckAnchor);
        stuckTimer = 0f;
        stuckAnchor = transform.position;

        // 完全没挪窝：多半是被挤进了沙袋堆内部，先找空地挪出去
        if (moved < 0.15f && TryEscapeWedge())
        {
            stuckAttempts = 0;
            stuckMemory = 0f;
            return;
        }

        if (moved > 0.5f) return;

        // 侧后方绕过障碍：左右交替，连续卡住就绕得更大一点
        stuckAttempts++;
        stuckMemory = 3f;

        Vector3 away = transform.position
                       - (player != null ? player.position : transform.position - transform.forward);
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();

        Vector3 sideDir = Vector3.Cross(Vector3.up, away);
        if ((stuckAttempts & 1) == 0) sideDir = -sideDir;

        float back = Mathf.Clamp(0.35f + stuckAttempts * 0.1f, 0.35f, 0.9f);
        detourDir = (sideDir + away * back).normalized;
        detourTimer = 0.5f + 0.12f * Mathf.Min(stuckAttempts, 4);
    }

    /// <summary>
    /// 被挤进沙袋 / 木箱内部时，胶囊检测已经推不出来，
    /// 就近找一个站得下人的空地挪出去。
    /// </summary>
    private bool TryEscapeWedge()
    {
        for (float radius = 0.8f; radius <= 2.4f; radius += 0.4f)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 candidate = transform.position + dir * radius;

                if (RiverGuard.Active && RiverGuard.InRiver(candidate)) continue;
                if (ChishuiWaterRule.Active && ChishuiWaterRule.NearWater(candidate)) continue;
                if (!IsFreeSpace(candidate)) continue;

                transform.position = candidate;
                return true;
            }
        }
        return false;
    }

    private bool IsFreeSpace(Vector3 point)
    {
        Vector3 bottom = point + Vector3.up * 0.6f;
        Vector3 top = point + Vector3.up * 1.2f;

        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, 0.3f, OverlapHits, ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = OverlapHits[i];
            if (hit == null) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.GetComponentInParent<Health>() != null) continue;
            return false;
        }
        return true;
    }

    private float CurrentSpeed(float distance)
    {
        if (mind != Mind.Advance) return moveSpeed * 1.3f;

        if (distance > 24f) return moveSpeed * 1.5f;
        if (distance > 10f) return moveSpeed * 1.15f;
        return moveSpeed * 0.8f;
    }

    /// <summary>当前追打的对象：优先已锁定的战友，否则就是玩家。</summary>
    private Transform CombatTarget
    {
        get
        {
            if (target != null && IsValidTarget(target)) return target;
            return player;
        }
    }

    private float FlatDistance(Vector3 point)
    {
        Vector3 delta = point - transform.position;
        delta.y = 0f;
        return delta.magnitude;
    }

    /// <summary>
    /// 近身拼刀：贴身时脚步比平时快近一倍，还不停横向绕步，
    /// 玩家想拉开距离或者一刀砍中都不容易。
    /// </summary>
    private void TickMelee(float dt, Transform engageTarget, float distance)
    {
        mind = Mind.Advance;

        movingNow = true;
        moveSpeedNow = moveSpeed * 1.9f;

        Vector3 to = engageTarget.position - transform.position;
        to.y = 0f;
        Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;

        // 左晃右晃地贴上来，不是直线冲
        float weave = Mathf.Sin(Time.time * 3.4f + seed * 11f) * 0.62f;
        Vector3 approach = (dir + Vector3.Cross(Vector3.up, dir) * weave).normalized;

        if (distance > 1.25f)
        {
            MoveDelta(approach * moveSpeedNow * dt);
        }

        // 近战转向比端枪快得多
        desiredRotation = Quaternion.LookRotation(dir);

        meleeTimer -= dt;
        if (meleeTimer <= 0f && distance < meleeRange * 0.92f
            && Vector3.Dot(transform.forward, dir) > 0.2f)
        {
            meleeTimer = meleeInterval * Random.Range(0.78f, 1.3f);
            meleeSwing = MeleeSwingDuration;
            DealMeleeDamage(engageTarget);
        }

        // 拼刀时枪不开火，但保持枪口不再抬起来
        fireTimer = Mathf.Max(fireTimer, 0.5f);
        boltTimer = Mathf.Min(boltTimer, 0.01f);
    }

    private void DealMeleeDamage(Transform victim)
    {
        if (victim == null) return;

        Health victimHealth = victim.GetComponent<Health>();
        if (victimHealth == null || victimHealth.IsDead) return;

        Vector3 point = victim.position + Vector3.up * 1.05f;
        victimHealth.TakeDamage(meleeDamage, point, -transform.forward, BodyZone.Torso,
                                gameObject);

        if (audioSource != null && MeleeClip != null)
        {
            audioSource.PlayOneShot(MeleeClip, 0.5f);
        }
    }

    private bool MoveToward(Vector3 point, float speed, float dt)
    {
        Vector3 delta = point - transform.position;
        delta.y = 0f;

        if (delta.sqrMagnitude < 0.36f) return false;

        Vector3 dir = delta.normalized;
        MoveDelta(dir * speed * dt);
        desiredRotation = Quaternion.LookRotation(dir);
        return true;
    }

    private static readonly RaycastHit[] MoveHits = new RaycastHit[8];
    private static readonly Collider[] OverlapHits = new Collider[12];

    /// <summary>
    /// 带碰撞的位移：撞上沙袋、岩壁、箱子就沿墙滑动，
    /// 不会再从掩体里直接穿过去。
    /// </summary>
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
            // 人挡人交给 KeepSpacing，别在这里又推一次
            if (hit.collider.GetComponentInParent<Health>() != null) continue;
            // 地面与缓坡可以走
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

    /// <summary>
    /// 保持间距：挤到别人身边就互相推开，免得一群人叠成一坨。
    /// 推进时推得狠一些，蹲守掩体时只是轻微挪开。
    /// </summary>
    private void KeepSpacing(float dt)
    {
        const float radius = 1.75f;
        Vector3 push = Vector3.zero;

        for (int i = 0; i < All.Count; i++)
        {
            EnemyAI other = All[i];
            if (other == null || other == this || other.IsDead) continue;

            Vector3 delta = transform.position - other.transform.position;
            delta.y = 0f;
            float gap = delta.magnitude;
            if (gap >= radius) continue;

            // 完全重叠时按各自 seed 给一个固定方向，避免原地抖
            Vector3 dir = gap > 0.01f ? delta / gap : Vector3.right * (seed < 0.5f ? -1f : 1f);
            push += dir * (radius - gap);
        }

        if (push.sqrMagnitude < 0.0001f) return;

        float strength = mind == Mind.Advance ? 2.4f : 0.8f;
        // 桥上横向空间小，推挤放轻，别把排队的同袍挤出桥面
        if (ChishuiWaterRule.Active && ChishuiWaterRule.OnBridge(transform.position))
        {
            strength *= 0.35f;
        }
        // 推挤也走碰撞滑动，别顺着人群把人挤进沙袋堆里
        MoveDelta(Vector3.ClampMagnitude(push, 1.2f) * (strength * dt));
    }

    private Quaternion FacingTarget()
    {
        Transform aim = target != null ? target : player;
        if (aim == null) return transform.rotation;

        Vector3 delta = aim.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude > 0.04f
            ? Quaternion.LookRotation(delta)
            : transform.rotation;
    }

    private void EnterHide()
    {
        mind = Mind.Hide;
        hideTimer = Random.Range(1.0f, 2.0f);

        // 半身高的掩体就趴下，高掩体蹲着就够了
        bool lowCover = BlocksPlayer(coverPosition, 0.8f) && !BlocksPlayer(coverPosition, 1.45f);
        proneCover = lowCover ? Random.value < 0.75f : Random.value < 0.18f;
    }

    private void StartSeekCover()
    {
        coverCooldown = Random.Range(2.8f, 4.5f);

        if (TryFindCover())
        {
            mind = Mind.SeekCover;
            return;
        }

        // 附近没有像样的掩体，就地卧倒减少暴露面积
        hasCover = false;
        coverPosition = transform.position;
        proneCover = true;
        hideTimer = Random.Range(1.4f, 2.4f);
        mind = Mind.Hide;
    }

    private bool PlayerAimingAtMe()
    {
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
            if (viewCamera == null) return false;
        }

        Vector3 toMe = transform.position + Vector3.up * 1.1f - viewCamera.transform.position;
        float distance = toMe.magnitude;
        if (distance > 42f) return false;

        return Vector3.Dot(viewCamera.transform.forward, toMe / distance) > 0.972f;
    }

    // ───────────────────────────── 掩体搜索 ─────────────────────────────

    private bool TryFindCover()
    {
        if (player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 1f) return false;

        Vector3 forward = toPlayer.normalized;
        float[] radii = { 2.0f, 3.3f, 4.6f };

        for (int r = 0; r < radii.Length; r++)
        {
            for (int i = 0; i < 9; i++)
            {
                float angle = Mathf.Lerp(-80f, 80f, i / 8f);
                Vector3 flat = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 candidate = transform.position + flat * radii[r];

                Vector3 grounded;
                if (!TryGroundPoint(candidate, out grounded)) continue;
                if (RiverGuard.InRiver(grounded)) continue;
                if (ChishuiWaterRule.NearWater(grounded)) continue;
                if (!IsOpen(grounded)) continue;
                if (!BlocksPlayer(grounded, 1.15f)) continue;

                coverPosition = grounded;
                hasCover = true;
                return true;
            }
        }

        return false;
    }

    private bool TryGroundPoint(Vector3 point, out Vector3 grounded)
    {
        grounded = point;

        Vector3 origin = point + Vector3.up * 3f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 9f, ~0,
                                               QueryTriggerInteraction.Ignore);

        // 沙袋顶、箱盖、房顶比人矮不了多少，不能当成落脚点，
        // 否则掩体点会选在沙袋上，人走到跟前被自己的掩体卡住。
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
            if (hits[i].normal.y < 0.6f) continue;
            if (hits[i].distance >= nearest) continue;

            nearest = hits[i].distance;
            grounded = hits[i].point;
            found = true;
        }

        return found;
    }

    private bool IsOpen(Vector3 point)
    {
        Collider[] hits = Physics.OverlapCapsule(point + Vector3.up * 0.35f,
            point + Vector3.up * 1.35f, 0.26f, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null) continue;
            if (hits[i].transform.IsChildOf(transform)) continue;
            if (hits[i].GetComponentInParent<Health>() != null) continue;
            return false;
        }

        return true;
    }

    private bool BlocksPlayer(Vector3 point, float height)
    {
        if (player == null) return false;

        Vector3 from = point + Vector3.up * height;
        Vector3 to = player.position + Vector3.up * 1.1f;
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance < 0.6f) return false;

        if (!Physics.Raycast(from, delta / distance, out RaycastHit hit, distance, ~0,
                             QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.collider.transform.IsChildOf(transform)) return false;

        Health hitHealth = hit.collider.GetComponentInParent<Health>();
        if (hitHealth != null && hitHealth.CompareTag("Player")) return false;

        return true;
    }

    // ───────────────────────────── 感知 ─────────────────────────────

    /// <summary>
    /// 在玩家与战友之间挑目标。玩家权重更高，跟丢目标才会换人，
    /// 不会像摇头电扇一样左右摆枪口。
    /// </summary>
    private void PickTarget()
    {
        if (target != null && IsValidTarget(target) && CanSee(target)) return;

        Transform best = player;
        float bestDistance = player != null
            ? Vector3.Distance(transform.position, player.position)
            : float.MaxValue;

        for (int i = 0; i < AllyAI.All.Count; i++)
        {
            AllyAI ally = AllyAI.All[i];
            if (ally == null || ally.IsDead) continue;

            float distance = Vector3.Distance(transform.position, ally.transform.position) + 12f;
            if (distance >= bestDistance) continue;
            if (!CanSee(ally.transform)) continue;

            best = ally.transform;
            bestDistance = distance;
        }

        target = best;
    }

    private static bool IsValidTarget(Transform candidate)
    {
        if (candidate == null) return false;
        Health candidateHealth = candidate.GetComponent<Health>();
        return candidateHealth != null && !candidateHealth.IsDead;
    }

    private bool CanSee(Transform candidate)
    {
        if (candidate == null) return false;

        Vector3 from = EyePosition();
        Vector3 to = candidate.position + Vector3.up * 1.05f;
        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance < 0.5f) return true;
        if (distance > 46f * DynamicWeather.EnemyVisionFactor) return false;

        if (Physics.Raycast(from, delta / distance, out RaycastHit hit,
                            distance + 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.transform.IsChildOf(candidate);
        }

        return true;
    }

    /// <summary>探头时眼睛跟着身体探出掩体，射击视线才不会被自己的掩体挡住。</summary>
    private Vector3 EyePosition()
    {
        float height = Mathf.Lerp(eyeHeight, 1.02f, crouchBlend);
        height = Mathf.Lerp(height, 0.5f, proneBlend);

        Vector3 position = transform.position + Vector3.up * height;
        if (mind == Mind.Peek) position += transform.right * (peekSide * 0.6f);
        return position;
    }

    /// <summary>
    /// 贴着地形走：下河床、上河岸都靠一条向下的射线取地面高度。
    /// 射线从身前 0.45 米处打下，避免打到自己头上的判定球。
    /// </summary>
    private void FollowGround()
    {
        Vector3 position = transform.position;

        // 桥面高度是已知的：直接锁死在桥板上，别从板缝掉到浮箱/河床里
        if (RiverGuard.OnBridge(position))
        {
            transform.position = new Vector3(position.x, RiverGuard.DeckY, position.z);
            return;
        }

        // 赤水河的索桥：桥板高度同样已知，锁死在桥面上，不靠射线去打栏杆和板缝
        if (ChishuiWaterRule.Active && ChishuiWaterRule.TryDeck(position, out float deck))
        {
            transform.position = new Vector3(position.x, deck, position.z);
            return;
        }

        // 赤水河：先看是不是飘在半空。松树冠、竹竿顶、房顶的碰撞体都在地面上方，
        // 生成或贴地时一旦打中它们，人就会站在几米高的树顶上"飞"。
        // 干地上以地形 / 关顶石阶的高度为准，高了就直接落回地面。
        if (ChishuiWaterRule.Active)
        {
            float walk = ChishuiLevelBuilder.WalkHeight(position.x, position.z);
            if (walk > ChishuiWaterRule.WaterSurfaceY + 0.05f && position.y > walk + 1.1f)
            {
                transform.position = new Vector3(position.x, walk, position.z);
                return;
            }
        }

        Vector3 probe = position + transform.forward * 0.45f + Vector3.up * 2.6f;

        // 同样按全部命中来挑地面：第一发常常先打中自己的爆头盒
        RaycastHit[] groundHits = Physics.RaycastAll(probe, Vector3.down, 7f, ~0,
                                                     QueryTriggerInteraction.Ignore);

        bool grounded = false;
        RaycastHit groundHit = default(RaycastHit);
        float bestY = float.MinValue;

        for (int i = 0; i < groundHits.Length; i++)
        {
            Collider c = groundHits[i].collider;
            if (c == null) continue;
            if (c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<Health>() != null) continue;

            // 只能迈上不高的台阶，避免踩着沙袋“跳”上去
            if (groundHits[i].point.y - position.y > 0.55f) continue;
            // 河床不是地面：不许往水里沉
            if (RiverGuard.Active && groundHits[i].point.y < -0.2f) continue;
            // 赤水河：水面以下同样不是地面，宁可留在岸上也不下去
            if (ChishuiWaterRule.Active
                && groundHits[i].point.y < ChishuiWaterRule.WaterSurfaceY - 0.05f
                && !ChishuiWaterRule.OnBridge(groundHits[i].point))
            {
                continue;
            }
            if (groundHits[i].point.y <= bestY) continue;

            bestY = groundHits[i].point.y;
            groundHit = groundHits[i];
            grounded = true;
        }

        if (!grounded) return;

        transform.position = new Vector3(position.x, groundHit.point.y, position.z);
    }

    // ───────────────────────────── 姿态与动画 ─────────────────────────────

    private void Animate(float dt, float distance)
    {
        float targetCrouch = 0f;
        float targetProne = 0f;
        float targetLean = 0f;

        switch (mind)
        {
            case Mind.Hide:
                if (proneCover) targetProne = 1f;
                else targetCrouch = 1f;
                break;

            case Mind.Peek:
                targetCrouch = 0.6f;
                targetLean = peekSide * 0.85f;
                break;

            case Mind.SeekCover:
                targetCrouch = 0.3f;
                break;

            default:
                if (distance < stopDistance) targetCrouch = 0.18f;
                break;
        }

        crouchBlend = Mathf.MoveTowards(crouchBlend, targetCrouch, 4.5f * dt);
        proneBlend = Mathf.MoveTowards(proneBlend, targetProne, 3.2f * dt);
        leanBlend = Mathf.MoveTowards(leanBlend, targetLean, 5f * dt);
        flinch = Mathf.MoveTowards(flinch, 0f, 3.2f * dt);

        walkBlend = Mathf.MoveTowards(walkBlend, movingNow ? 1f : 0f, 6f * dt);

        // 相对基准速度判断奔跑：1.5 倍速以上算全速冲刺
        float speedFrac = Mathf.Clamp01((moveSpeedNow / moveSpeed - 1f) / 0.5f);
        runBlend = Mathf.MoveTowards(runBlend, movingNow ? speedFrac : 0f, 5f * dt);

        if (movingNow)
        {
            walkPhase += dt * (6.5f + moveSpeedNow * 1.5f + runBlend * 7f);
        }

        bool aiming = mind != Mind.Advance || distance < 26f;

        float swing = Mathf.Sin(walkPhase)
                      * (24f + walkBlend * 15f + runBlend * 22f) * walkBlend;

        // 蹲姿：屈髋 + 屈膝，臀部坐下、上身基本立着；
        // 只屈髋不屈膝的话，整条腿会往后甩、上身再往前压，看着像鞠躬
        float hipBend = crouchBlend * -68f * (1f - proneBlend);
        float kneeBend = crouchBlend * 92f * (1f - proneBlend);
        SetPivot(hipL, swing + hipBend + proneBlend * 8f);
        SetPivot(hipR, -swing + hipBend + proneBlend * 10f);
        SetPivot(kneeL, kneeBend);
        SetPivot(kneeR, kneeBend);

        // 俯卧支撑：手臂伸到头前撑地，射击时枪口贴地
        float proneArmL = 164f;
        float proneArmR = 170f;

        float armL;
        float armR;
        float supportZ;
        if (aiming)
        {
            // 端枪：枪托抵肩，右手握把、左手托护木
            armL = -52f + Mathf.Sin(walkPhase) * 5f * walkBlend;
            armR = -74f + flinch * 22f;
            supportZ = 32f;
        }
        else
        {
            // 行军 / 推进：枪端在胸前低姿戒备，不是挂在腰边
            armL = -36f + Mathf.Sin(walkPhase) * 8f * walkBlend;
            armR = -50f + Mathf.Sin(walkPhase) * 5f * walkBlend;
            supportZ = 24f;
        }

        // 拉栓：右手离开握把去拨枪机
        if (boltTimer > 0f)
        {
            float work = Mathf.Sin(Mathf.Clamp01(1f - boltTimer / BoltDuration) * Mathf.PI);
            armR = Mathf.Lerp(armR, -54f, work);
            supportZ -= 12f * work;
        }

        UpdateKnife(dt);

        // 拼刀：右手举刀劈下，左手护在身前
        if (meleeBlend > 0.01f)
        {
            float slash = meleeSwing > 0f
                ? Mathf.Sin((1f - Mathf.Clamp01(meleeSwing / MeleeSwingDuration)) * Mathf.PI)
                : 0f;
            armR = Mathf.Lerp(armR, -74f + 48f * slash, meleeBlend);
            armL = Mathf.Lerp(armL, -34f - 16f * slash, meleeBlend);
            supportZ = Mathf.Lerp(supportZ, 10f, meleeBlend);
        }

        SetPivot(shoulderL, Mathf.Lerp(armL, proneArmL, proneBlend * 0.92f),
            supportZ * (1f - proneBlend));
        SetPivot(shoulderR, Mathf.Lerp(armR, proneArmR, proneBlend * 0.92f));

        UpdateRifle(aiming, dt);
        AnimateBolt();

        if (torso != null)
        {
            torso.localRotation = Quaternion.Euler(
                crouchBlend * 9f + walkBlend * 4f + runBlend * 9f
                + flinch * 12f - proneBlend * 18f,
                0f, 0f);
        }

        if (head != null)
        {
            // 站定时左右观察，交火时视线锁定目标；趴下时抬头看前方
            float scan = Mathf.Sin(Time.time * 0.9f + seed * 20f)
                         * (6f + (1f - walkBlend) * 9f)
                         * (mind == Mind.Advance ? 1f : 0.25f);
            head.localRotation = Quaternion.Euler(-flinch * 14f - proneBlend * 24f, scan, 0f);
        }

        if (pose == null) return;

        // 奔跑时步幅更大，身体上下起伏也更明显
        float bob = Mathf.Sin(walkPhase * 2f) * (0.035f + runBlend * 0.032f) * walkBlend;

        // 蹲下把身子压到膝盖高度（腿已经真正折起来，不用再往地里沉）
        float drop = crouchBlend * 0.3f - proneBlend * 0.2f;

        pose.localPosition = new Vector3(leanBlend * 0.34f, bob - drop,
                                          -flinch * 0.1f + proneBlend * 0.12f);
        pose.localRotation = Quaternion.Euler(
            proneBlend * 88f + flinch * 7f + runBlend * 6f,
            leanBlend * 8f,
            leanBlend * 16f + Mathf.Sin(walkPhase) * runBlend * 3f
            + proneBlend * 3f);
    }

    private static void SetPivot(Transform pivot, float angleX, float angleZ = 0f)
    {
        if (pivot != null) pivot.localRotation = Quaternion.Euler(angleX, 0f, angleZ);
    }

    /// <summary>切刀：近身时收起步枪、亮出大刀；劈砍动作由 meleeSwing 驱动。</summary>
    private void UpdateKnife(float dt)
    {
        Transform engageTarget = CombatTarget;
        float distance = engageTarget != null
            ? FlatDistance(engageTarget.position) : float.MaxValue;

        meleeBlend = Mathf.MoveTowards(meleeBlend,
            !dead && distance <= meleeRange * 1.55f ? 1f : 0f, 7f * dt);

        if (meleeSwing > 0f) meleeSwing -= dt;

        if (rifle != null)
        {
            bool show = meleeBlend < 0.5f;
            if (rifle.gameObject.activeSelf != show) rifle.gameObject.SetActive(show);
        }

        if (knife == null) return;

        bool knifeVisible = meleeBlend >= 0.5f;
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

    /// <summary>行军时低姿端枪，交火时把枪托抬到肩前。</summary>
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

        rifle.localRotation = rotation;
    }

    /// <summary>枪机：抬起拉柄 → 向后拉 → 推回 → 压下。</summary>
    private void AnimateBolt()
    {
        if (boltHandle == null) return;

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

    /// <summary>阶段切换时冻结 AI（例如浮桥炸毁后）。</summary>
    public void SetAIEnabled(bool value)
    {
        aiEnabled = value;
        if (!value && tracer != null) tracer.enabled = false;
    }

    // ───────────────────────── 炸桥：溃逃与气浪 ─────────────────────────

    /// <summary>炸桥前一刻：桥上残敌掉头往对岸跑。</summary>
    public static void RoutBridge(Vector3 farEnd)
    {
        for (int i = 0; i < All.Count; i++)
        {
            EnemyAI ai = All[i];
            if (ai == null || ai.dead || ai.blastFlying) continue;

            Vector3 p = ai.transform.position;
            if (Mathf.Abs(p.x) > RiverGuard.BridgeHalfWidth + 0.6f) continue;
            if (p.z < RiverGuard.BridgeMinZ - 0.5f || p.z > RiverGuard.BridgeMaxZ + 0.5f) continue;

            ai.fleeing = true;
            ai.aiEnabled = true;
            ai.fleeTarget = new Vector3(Mathf.Clamp(p.x, -4.6f, 4.6f), 0f, farEnd.z);

            // 枪也顾不上了，收起枪埋头跑
            ai.target = null;
            ai.mind = Mind.Advance;
        }
    }

    /// <summary>
    /// 炸点气浪：把附近的桥上敌兵掀起来。
    /// 起跳速度只有三四米每秒，落点一两米，是正常的抛物线，不做夸张的抛飞。
    /// </summary>
    public static void BlastBridgeEnemies(Vector3 point, float radius)
    {
        for (int i = 0; i < All.Count; i++)
        {
            EnemyAI ai = All[i];
            if (ai == null || ai.dead || ai.blastFlying) continue;

            Vector3 p = ai.transform.position;
            if (Mathf.Abs(p.x) > RiverGuard.BridgeHalfWidth + 1f) continue;
            if (p.z < RiverGuard.BridgeMinZ - 1f || p.z > RiverGuard.BridgeMaxZ + 2f) continue;

            Vector3 flat = p - point;
            flat.y = 0f;
            if (flat.magnitude > radius) continue;

            Vector3 push = flat.sqrMagnitude > 0.01f
                ? flat.normalized
                : new Vector3(Random.Range(-0.4f, 0.4f), 0f, 1f);

            Vector3 velocity = new Vector3(
                push.x * Random.Range(1.1f, 2.4f),
                Random.Range(3.0f, 4.4f),
                1.2f + push.z * Random.Range(0.8f, 1.8f));

            ai.BlastOffBridge(velocity);
        }
    }

    private void BlastOffBridge(Vector3 velocity)
    {
        if (dead || blastFlying) return;
        blastFlying = true;
        fleeing = false;
        aiEnabled = false;
        if (tracer != null) tracer.enabled = false;
        StartCoroutine(BlastFlightRoutine(velocity));
    }

    private IEnumerator BlastFlightRoutine(Vector3 velocity)
    {
        float spin = Random.Range(-180f, 180f);
        float elapsed = 0f;

        while (elapsed < 2.4f)
        {
            float dt = Time.deltaTime;
            velocity.y -= 14f * dt;              // 普通重力
            transform.position += velocity * dt;
            transform.Rotate(0f, spin * 0.25f * dt, spin * dt, Space.Self);
            elapsed += dt;

            // 落进江面就停住，让尸体在水上漂一小会儿
            if (transform.position.y <= SurfaceY + 0.02f)
            {
                transform.position = new Vector3(
                    transform.position.x, SurfaceY + 0.02f, transform.position.z);
                break;
            }

            yield return null;
        }

        blastFlying = false;

        // 落地后再结算阵亡：正常掉弹药、走尸体流程
        Health h = health != null ? health : GetComponent<Health>();
        if (h != null && !h.IsDead)
        {
            h.TakeDamage(9999f, transform.position, Vector3.up, BodyZone.Torso, null);
        }
    }

    // ───────────────────────────── 射击 ─────────────────────────────

    private void TryFire(float distance)
    {
        if (boltTimer > 0f) return;
        if (fireTimer > 0f || mind == Mind.Hide) return;

        // 盯着大部队的那几成敌军，优先打浮桥上的人；
        // 桥上的人全是触发器，射线看得穿，不能走 CanSee
        Transform aim;
        bool atColumn = false;
        if (columnFocus && CrossingColumn.Active != null
            && CrossingColumn.Active.TryGetTarget(transform.position, maxFireDistance, out aim))
        {
            atColumn = true;
            distance = Vector3.Distance(transform.position, aim.position);
        }
        else
        {
            aim = target != null ? target : player;
        }

        if (aim == null || distance > maxFireDistance) return;
        if (!atColumn && !CanSee(aim)) return;

        fireTimer = fireInterval * Random.Range(0.75f, 1.25f);
        if (mind == Mind.Peek) fireTimer -= 0.3f;

        // 偶尔连打两枪
        if (Random.value < 0.28f) fireTimer *= 0.45f;

        Fire(aim);
    }

    private void Fire(Transform aimTarget)
    {
        if (aimTarget == null) return;

        Vector3 muzzle = EyePosition() + transform.forward * 0.5f;
        Vector3 aim = aimTarget.position + Vector3.up * 1.1f;
        Vector3 direction = (aim - muzzle).normalized;

        float distance = Vector3.Distance(muzzle, aim);
        float spread = 1.2f + distance * 0.13f;
        // 暴风雪里枪口被风吹得发飘，敌人也打不准
        spread *= 1f + DynamicWeather.BlizzardLevel * 0.45f;

        if (mind == Mind.Peek) spread *= 0.75f;
        if (mind == Mind.Advance && distance < 14f) spread *= 1.35f;

        direction = Quaternion.Euler(
            Random.Range(-spread, spread),
            Random.Range(-spread, spread), 0f) * direction;

        Vector3 end = muzzle + direction * 60f;

        if (Physics.Raycast(muzzle, direction, out RaycastHit hit, 60f, ~0,
                            QueryTriggerInteraction.Ignore))
        {
            end = hit.point;

            if (!hit.collider.transform.IsChildOf(transform))
            {
                Health targetHealth = hit.collider.GetComponentInParent<Health>();
                if (targetHealth != null
                    && (targetHealth.CompareTag("Player") || targetHealth.CompareTag("Ally")))
                {
                    targetHealth.TakeDamage(damage, hit.point, hit.normal, BodyZone.Torso,
                                            gameObject);
                }
            }
        }

        // 子弹擦过时提醒附近的战友闪避
        AllyAI.NotifyIncomingFire(muzzle, end);

        if (audioSource != null && shotClip != null)
        {
            audioSource.PlayOneShot(shotClip, 0.72f);
        }
        if (muzzleFlash != null) muzzleFlash.Play(Random.Range(0.9f, 1.25f));

        // 栓动步枪：打完一发拉一次枪栓，拉栓期间打不出第二发
        boltTimer = BoltDuration;
        if (audioSource != null) audioSource.PlayOneShot(BoltClip, 0.45f);

        StartCoroutine(TracerRoutine(muzzle, end));
    }

    private IEnumerator TracerRoutine(Vector3 from, Vector3 to)
    {
        if (tracer == null) yield break;

        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;

        yield return new WaitForSeconds(0.055f);

        tracer.enabled = false;
    }

    // ───────────────────────────── 生死 ─────────────────────────────

    private void HandleDamaged(DamageInfo info)
    {
        if (dead) return;

        flinch = 1f;

        if (info.Source != null)
        {
            lastKnownPlayer = info.Source.transform.position;

            // 中弹就找掩体，这是敌军最主要的躲子弹方式；
            // 战友打过来的子弹同样会把敌人压回掩体后面
            if (mind == Mind.Advance && coverCooldown <= 0f)
            {
                StartSeekCover();
            }
        }

        suppressTimer = Mathf.Max(suppressTimer, 1.6f);
    }

    private void HandleDeath(DamageInfo info)
    {
        if (dead) return;
        dead = true;
        aiEnabled = false;

        if (tracer != null) tracer.enabled = false;

        // 尸体不再阻挡子弹
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        // 战利品：每人必掉一个手枪弹夹（6 发），另外再掉 2~4 发步枪弹；
        // 还有 20% 概率额外掉一枚手榴弹
        Vector3 lootPos = transform.position + Vector3.up * 0.45f;
        Vector3 magPos = lootPos + new Vector3(0.35f, 0.04f, Random.Range(-0.2f, 0.2f));
        AmmoPickup.SpawnPistolPickup(magPos, 6);
        AmmoPickup.SpawnPickup(lootPos, Random.Range(2, 5));

        if (Random.value < 0.2f)
        {
            Vector3 off = new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
            GrenadePickup.SpawnPickup(lootPos + off);
        }

        StartCoroutine(CorpseRoutine());
    }

    private IEnumerator CorpseRoutine()
    {
        if (pose != null)
        {
            pose.localPosition = Vector3.zero;
            pose.localRotation = Quaternion.identity;
        }

        Quaternion from = transform.rotation;
        Quaternion to = from * Quaternion.Euler(90f, 0f, 0f);

        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(from, to, t / 0.6f);
            yield return null;
        }

        yield return new WaitForSeconds(5f);

        Destroy(gameObject);
    }
}
