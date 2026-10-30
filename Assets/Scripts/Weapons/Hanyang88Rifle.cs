using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 汉阳造 88 式步枪
///
/// 设计要点：
/// 1. 球形射线检测（HitScan）判定命中，不做子弹飞行体，保证"一发是一发"的精准手感。
/// 2. 栓动结构：每次射击后强制进入 1.5 秒拉栓状态，期间完全禁止开火 / 装填。
/// 3. 弹药管理：弹匣 30 发、初始备弹 30 发、每个补给包仅 +3 发。
/// 4. 伤害按命中部位结算：头部 100、躯干 40、四肢 20（可在 Inspector 调整）。
///
/// 使用方式：
/// - 挂在武器根节点（或玩家身上的武器管理器）上。
/// - shootOrigin 指向主摄像机（或枪口前方的空物体），射线从这里发出。
/// - Animator 需要三个 Trigger 参数："Fire"、"Bolt"、"Reload"。
/// - 拉栓动画长度必须为 1.5 秒；若美术给的动画时长不同，请看 BoltCycleRoutine 里的注释。
/// </summary>
[DisallowMultipleComponent]
public class Hanyang88Rifle : MonoBehaviour
{
    public enum WeaponState
    {
        Ready,      // 可射击
        BoltAction, // 拉栓中（1.5 秒，禁止开火）
        Reloading   // 装填中
    }

    // ───────────────────────────── 引用 ─────────────────────────────

    [Header("引用")]
    [Tooltip("射线起点，通常绑定到主摄像机的 transform")]
    [SerializeField] private Transform shootOrigin;

    [Tooltip("武器动画控制器，可留空，会在子物体中自动查找")]
    [SerializeField] private Animator animator;

    [Tooltip("音源，可留空，会自动查找同物体上的 AudioSource")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("枪口火焰粒子")]
    [SerializeField] private ParticleSystem muzzleFlash;

    [Tooltip("程序化枪口火光，第一人称枪模上用")]
    [SerializeField] private MuzzleFlash muzzleFlashFx;

    [Tooltip("弹着点特效预制体（尘土、血雾等），可留空")]
    [SerializeField] private GameObject impactEffectPrefab;

    // ───────────────────────────── 弹药 ─────────────────────────────

    [Header("弹药")]
    [Tooltip("弹匣容量：30 发")]
    [SerializeField] private int magazineCapacity = 30;

    [Tooltip("开局弹匣内已有子弹数：整局从 20 发起步，其余靠战场缴获")]
    [SerializeField] private int startingAmmoInMagazine = 20;

    [Tooltip("开局备弹数")]
    [SerializeField] private int startingReserveAmmo = 0;

    [Tooltip("备弹上限，防止后期无限囤积")]
    [SerializeField] private int maxReserveAmmo = 60;

    // ───────────────────────────── 射击 ─────────────────────────────

    [Header("射击")]
    [Tooltip("最大射程（米）")]
    [SerializeField] private float range = 120f;

    [Tooltip("腰射散布角度（度）。设为 0 则完全精准")]
    [SerializeField] private float hipSpread = 1.5f;

    [Tooltip("可被击中的层，建议排除玩家自身与 UI 层")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("伤害（按部位）")]
    [SerializeField] private float headDamage = 100f;
    [SerializeField] private float torsoDamage = 40f;
    [SerializeField] private float limbDamage = 20f;

    // ───────────────────────────── 动作时长 ─────────────────────────────

    [Header("栓动与装填")]
    [Tooltip("拉栓总时长（秒），期间无法开火。策划案固定为 1.5")]
    [SerializeField] private float boltDuration = 1.5f;

    [Tooltip("漏夹装填时长（秒）")]
    [SerializeField] private float reloadDuration = 3.0f;

    [Header("Animator 参数名")]
    [SerializeField] private string fireTrigger = "Fire";
    [SerializeField] private string boltTrigger = "Bolt";
    [SerializeField] private string reloadTrigger = "Reload";

    // ───────────────────────────── 音效 ─────────────────────────────

    [Header("音效")]
    [SerializeField] private AudioClip fireClip;   // 枪声
    [SerializeField] private AudioClip boltClip;   // 拉栓（抛壳 + 闭锁）
    [SerializeField] private AudioClip emptyClip;  // 空仓击锤
    [SerializeField] private AudioClip reloadClip; // 装填

    // ───────────────────────────── 行为 ─────────────────────────────

    [Header("行为")]
    [Tooltip("弹匣打空后再扣扳机，是否自动开始装填")]
    [SerializeField] private bool autoReloadWhenEmpty = false;

    [Tooltip("显示弹药调试信息。正式版本请关闭")]
    [SerializeField] private bool showDebugHUD = false;

    // ───────────────────────────── 运行时状态 ─────────────────────────────

    public WeaponState State { get; private set; } = WeaponState.Ready;
    public int AmmoInMagazine => ammoInMagazine;
    public int ReserveAmmo => reserveAmmo;
    public int MagazineCapacity => magazineCapacity;
    public bool CanFire => State == WeaponState.Ready && ammoInMagazine > 0;

    /// <summary>本次拉栓进度 0→1，供第一人称枪模摆枪机。</summary>
    public float BoltProgress
    {
        get
        {
            if (State != WeaponState.BoltAction) return 0f;

            float duration = boltDuration / Mathf.Max(0.1f, fireRateMultiplier);
            return Mathf.Clamp01((Time.time - boltStartTime) / Mathf.Max(0.01f, duration));
        }
    }

    /// <summary>弹药变化事件：(弹匣内子弹, 备弹)。供 HUD / 音效系统订阅。</summary>
    public event Action<int, int> OnAmmoChanged;

    /// <summary>真正击发一发时触发（空仓、哑火不算），枪模后坐与镜头抖动挂在这里。</summary>
    public event Action OnFired;

    private int ammoInMagazine;
    private int reserveAmmo;

    // 外部临时射速倍率（信仰爆发等），1 = 正常
    private float fireRateMultiplier = 1f;

    // 外部散布倍率（机瞄收紧），1 = 正常
    private float spreadMultiplier = 1f;

    // 冲刺收枪期间枪还压在身侧，扣扳机打不出去，必须先停下或松开冲刺
    private bool sprintBlocked;

    // 本次拉栓开始的时间，用来算 BoltProgress
    private float boltStartTime;

    // ───────────────────────────── 生命周期 ─────────────────────────────

    private void Awake()
    {
        // 自动补齐引用，减少 Inspector 漏拖导致的空引用
        if (shootOrigin == null) shootOrigin = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        // 配置兜底：防止在 Inspector 里填出负数或超容量
        magazineCapacity = Mathf.Max(1, magazineCapacity);
        startingAmmoInMagazine = Mathf.Clamp(startingAmmoInMagazine, 0, magazineCapacity);
        startingReserveAmmo = Mathf.Clamp(startingReserveAmmo, 0, maxReserveAmmo);

        ammoInMagazine = startingAmmoInMagazine;
        reserveAmmo = startingReserveAmmo;

        NotifyAmmoChanged();
    }

    private void Update()
    {
        // 拉栓 / 装填期间，下面的 TryFire 会直接返回 false，
        // 但仍然允许玩家按 R 排队装填，所以输入检测保持常开。
        if (Input.GetButtonDown("Fire1"))
        {
            TryFire();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            TryReload();
        }
    }

    // ───────────────────────────── 开火 ─────────────────────────────

    /// <summary>
    /// 尝试开火。返回 true 表示成功击发。
    /// 任何非法状态（拉栓中、装填中、没子弹）都会返回 false。
    /// </summary>
    public bool TryFire()
    {
        // 1) 拉栓未完成、正在装填 —— 一律不能开火
        if (State != WeaponState.Ready)
        {
            return false;
        }

        // 1.5) 冲刺时枪还收在身侧，先停下来才举得起来
        if (sprintBlocked)
        {
            return false;
        }

        // 2) 严格判断弹匣容量：没有子弹就只响空仓击锤
        if (ammoInMagazine <= 0)
        {
            PlayOneShot(emptyClip);

            if (autoReloadWhenEmpty)
            {
                TryReload();
            }

            return false;
        }

        // 2.5) 冻雨让枪机受潮：偶尔哑火，仍要退弹、重新拉栓
        if (UnityEngine.Random.value > DynamicWeather.FirearmReliability)
        {
            ammoInMagazine--;
            NotifyAmmoChanged();
            PlayOneShot(emptyClip);
            StartCoroutine(BoltCycleRoutine());
            return false;
        }

        // 3) 消耗一发子弹（先扣弹，再结算命中，避免动画事件重复触发）
        ammoInMagazine--;
        NotifyAmmoChanged();

        // 4) 表现层：枪声、枪口火焰、开火动画
        PlayOneShot(fireClip, 1f);
        if (muzzleFlash != null) muzzleFlash.Play();
        if (muzzleFlashFx != null) muzzleFlashFx.Play();
        if (animator != null) animator.SetTrigger(fireTrigger);

        // 5) 命中结算
        PerformRaycast();

        // 5.5) 后坐表现：枪模后退上跳、镜头被顶一下
        if (OnFired != null) OnFired();

        // 枪声会惊动附近的敌人；雨声会缩短传播距离
        EnemyAI.AlertNearby(transform.position, 65f * DynamicWeather.HearingFactor);

        // 6) 强制进入拉栓状态（1.5 秒）
        StartCoroutine(BoltCycleRoutine());

        return true;
    }

    /// <summary>
    /// 射线检测命中判定。
    /// 从 shootOrigin 沿正前方发射一条射线，命中后根据 HitZone 判断部位。
    /// </summary>
    private void PerformRaycast()
    {
        Vector3 direction = shootOrigin.forward;

        // 散布：腰射较散，机瞄时倍率压到 0.06，几乎指哪打哪
        float spread = hipSpread * spreadMultiplier;
        if (spread > 0f)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * spread;
            direction = Quaternion.Euler(offset.y, offset.x, 0f) * direction;
        }

        // 起点前移，避免小球的初始重叠把玩家自己的胶囊体算成命中
        Vector3 origin = shootOrigin.position + direction * 0.45f;

        // 用小半径球体代替纯射线：瞄得差一点也能擦到头部判定盒
        if (Physics.SphereCast(origin, 0.05f, direction, out RaycastHit hit,
                                range, hitMask, QueryTriggerInteraction.Ignore))
        {
            // 忽略自己身上的碰撞体（第一人称视角下通常不会命中，但保险）
            if (shootOrigin.root != null && hit.collider.transform.IsChildOf(shootOrigin.root))
            {
                return;
            }

            // 部位判定：优先取碰撞体自身，其次向父级查找
            HitZone zone = hit.collider.GetComponent<HitZone>();
            if (zone == null)
            {
                zone = hit.collider.GetComponentInParent<HitZone>();
            }

            if (zone != null)
            {
                float damage = GetDamageForZone(zone.Zone);

                Health health = zone.GetComponentInParent<Health>();
                // 不打自己人：子弹仍会被战友挡住，但不结算伤害
                if (health != null && !health.CompareTag("Ally"))
                {
                    health.TakeDamage(damage, hit.point, hit.normal, zone.Zone, gameObject);
                }
            }
            else if (hit.rigidbody != null)
            {
                // 命中无生命值的物理物体：给一点冲量，作为环境反馈
                hit.rigidbody.AddForceAtPosition(direction * 15f, hit.point, ForceMode.Impulse);
            }

            SpawnImpactEffect(hit);

            // 弹道从旁边掠过时提醒敌兵闪避
            EnemyAI.NotifyIncomingFire(origin, hit.point);
            return;
        }

        // 打空：子弹打到射程尽头，沿途的敌人照样会躲
        EnemyAI.NotifyIncomingFire(origin, origin + direction * range);
    }

    /// <summary>按命中部位返回伤害值。</summary>
    private float GetDamageForZone(BodyZone zone)
    {
        switch (zone)
        {
            case BodyZone.Head:  return headDamage;
            case BodyZone.Torso: return torsoDamage;
            case BodyZone.Limb:  return limbDamage;
            default:             return torsoDamage;
        }
    }

    private void SpawnImpactEffect(RaycastHit hit)
    {
        if (impactEffectPrefab == null) return;

        Quaternion rotation = Quaternion.LookRotation(hit.normal);
        GameObject effect = Instantiate(impactEffectPrefab, hit.point, rotation);
        Destroy(effect, 2f); // 防止场景里堆积特效
    }

    // ───────────────────────────── 拉栓 ─────────────────────────────

    /// <summary>
    /// 拉栓协程：整段 1.5 秒内锁定开火。
    ///
    /// 注意：如果美术给的拉栓动画长度不是 1.5 秒，有两种处理方式——
    ///   A. 在 Animator 里把该 State 的 Speed 设为 clipLength / 1.5；
    ///   B. 在动画末尾添加 Animation Event，由事件驱动状态切换。
    /// 当前实现采用固定时长，保证策划数值绝对可控。
    /// </summary>
    private IEnumerator BoltCycleRoutine()
    {
        State = WeaponState.BoltAction;
        boltStartTime = Time.time;

        // 信仰爆发等外部增益会抬高射速倍率：拉栓时长按倍率缩短
        float duration = boltDuration / Mathf.Max(0.1f, fireRateMultiplier);

        if (animator != null)
        {
            animator.SetTrigger(boltTrigger);
            animator.speed = fireRateMultiplier; // 动画与逻辑同步加速
        }
        PlayOneShot(boltClip);

        yield return new WaitForSeconds(duration);

        if (animator != null) animator.speed = 1f;

        State = WeaponState.Ready;

        // 打空后若开启了自动装填，拉完栓立刻补弹
        if (ammoInMagazine <= 0 && autoReloadWhenEmpty && reserveAmmo > 0)
        {
            TryReload();
        }
    }

    // ───────────────────────────── 装填 ─────────────────────────────

    /// <summary>
    /// 尝试装填。拉栓中或装填中调用会失败。
    /// 只能从备弹中取弹，且不会超过弹匣容量。
    /// </summary>
    public bool TryReload()
    {
        if (State != WeaponState.Ready) return false;
        if (reserveAmmo <= 0) return false;
        if (ammoInMagazine >= magazineCapacity) return false;

        StartCoroutine(ReloadRoutine());
        return true;
    }

    private IEnumerator ReloadRoutine()
    {
        State = WeaponState.Reloading;

        if (animator != null) animator.SetTrigger(reloadTrigger);
        PlayOneShot(reloadClip);

        yield return new WaitForSeconds(reloadDuration);

        // 严格按弹匣容量补弹，多余的留在备弹里
        int needed = magazineCapacity - ammoInMagazine;
        int taken = Mathf.Min(needed, reserveAmmo);

        ammoInMagazine += taken;
        reserveAmmo -= taken;

        NotifyAmmoChanged();

        State = WeaponState.Ready;
    }

    // ───────────────────────────── 外部接口 ─────────────────────────────

    /// <summary>
    /// 增加备弹。补给包调用此方法。
    /// 返回实际增加的数量（可能因上限而被截断，也可能为 0）。
    /// </summary>
    public int AddReserveAmmo(int amount)
    {
        if (amount <= 0) return 0;

        int space = maxReserveAmmo - reserveAmmo;
        int added = Mathf.Min(amount, space);
        if (added <= 0) return 0;

        reserveAmmo += added;
        NotifyAmmoChanged();
        return added;
    }

    /// <summary>
    /// 设置临时射速倍率：1 = 正常，&gt;1 更快。
    /// 只影响下一次拉栓（已经开始的拉栓不会被中途改变）。
    /// 信仰系统在增益开始 / 结束时调用。
    /// </summary>
    public void SetFireRateMultiplier(float multiplier)
    {
        fireRateMultiplier = Mathf.Clamp(multiplier, 0.1f, 3f);
    }

    /// <summary>设置散布倍率：机瞄时由武器控制器收紧。</summary>
    public void SetSpreadMultiplier(float multiplier)
    {
        spreadMultiplier = Mathf.Clamp(multiplier, 0.02f, 4f);
    }

    /// <summary>冲刺收枪时由武器控制器置位，枪放下期间禁止击发。</summary>
    public void SetSprintBlocked(bool blocked)
    {
        sprintBlocked = blocked;
    }

    /// <summary>
    /// 运行时初始化。程序化生成武器与音效时调用。
    /// </summary>
    public void Initialize(Transform origin, AudioSource source,
        AudioClip fire, AudioClip bolt, AudioClip empty, AudioClip reload, LayerMask mask)
    {
        shootOrigin = origin;
        audioSource = source;
        fireClip = fire;
        boltClip = bolt;
        emptyClip = empty;
        reloadClip = reload;
        hitMask = mask;
    }

    /// <summary>绑定第一人称枪口的火光（枪模由 GameBootstrap 程序化拼装）。</summary>
    public void SetMuzzleFlash(MuzzleFlash flash)
    {
        muzzleFlashFx = flash;
    }

    /// <summary>清空武器状态（切枪、重生时调用）。</summary>
    public void ResetWeapon()
    {
        StopAllCoroutines();
        State = WeaponState.Ready;
    }

    private void NotifyAmmoChanged()
    {
        OnAmmoChanged?.Invoke(ammoInMagazine, reserveAmmo);
    }

    private void PlayOneShot(AudioClip clip)
    {
        PlayOneShot(clip, 1f);
    }

    private void PlayOneShot(AudioClip clip, float volume)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }

    // ───────────────────────────── 调试 ─────────────────────────────

    private void OnGUI()
    {
        if (!showDebugHUD) return;

        GUILayout.BeginArea(new Rect(20, 20, 260, 120));
        GUILayout.Label($"状态：{State}");
        GUILayout.Label($"弹匣：{ammoInMagazine} / {magazineCapacity}");
        GUILayout.Label($"备弹：{reserveAmmo} / {maxReserveAmmo}");
        GUILayout.Label(CanFire ? "可以开火" : "无法开火");
        GUILayout.EndArea();
    }
}
