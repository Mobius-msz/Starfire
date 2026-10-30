using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 信仰系统（挂在 Player 身上）
///
/// 规则：
/// - 击杀敌人：信仰 +faithPerKill。
/// - 靠近 Tag = "Campfire" 的火堆：信仰持续增加。
/// - 信仰满值后按 F：清空信仰，进入 5 秒"信仰爆发"：
///   少量回血，并减免 50% 受到的伤害。
///
/// 依赖：
/// - 敌人根物体 Tag 设为 "Enemy"，并挂有 Health 组件。
/// - 火堆 Tag 设为 "Campfire"，Collider 勾选 Is Trigger。
/// - 步枪上的 SetFireRateMultiplier 已实现（见 Hanyang88Rifle）。
/// </summary>
[DisallowMultipleComponent]
public class FaithSystem : MonoBehaviour
{
    [Header("信仰值")]
    [Tooltip("信仰上限")]
    [SerializeField] private float maxFaith = 100f;

    [Tooltip("开局信仰值")]
    [SerializeField] private float startingFaith = 0f;

    [Tooltip("每次击杀获得的信仰")]
    [SerializeField] private float faithPerKill = 12f;

    [Tooltip("靠近火堆时每秒获得的信仰")]
    [SerializeField] private float faithPerSecondNearFire = 6f;

    [Tooltip("信仰自然衰减（0 = 不衰减）。策划案未要求，默认关闭")]
    [SerializeField] private float faithDecayPerSecond = 0f;

    [Header("增益（满值后按 F）")]
    [SerializeField] private KeyCode activateKey = KeyCode.F;

    [Tooltip("增益持续时间（策划案：5 秒）")]
    [SerializeField] private float buffDuration = 5f;

    [Tooltip("爆发时恢复的生命值（少量回血）")]
    [SerializeField] private float healOnBurst = 22f;

    [Tooltip("爆发期间的减伤比例：0.5 = 减免 50% 伤害")]
    [Range(0f, 0.9f)]
    [SerializeField] private float damageReduction = 0.5f;

    [Tooltip("增益期间移速倍率")]
    [SerializeField] private float buffMoveSpeedMultiplier = 1f;

    [Tooltip("增益期间射速倍率（拉栓速度）")]
    [SerializeField] private float buffFireRateMultiplier = 1f;

    [Header("引用")]
    [Tooltip("可留空，会在子物体中自动查找")]
    [SerializeField] private Hanyang88Rifle rifle;

    [Tooltip("可留空，会在自身/子物体中自动查找")]
    [SerializeField] private Health health;

    [Tooltip("可留空，会在子物体中自动查找")]
    [SerializeField] private Animator animator;

    [SerializeField] private string buffAnimatorTrigger = "FaithBuff";

    [Header("标签")]
    [SerializeField] private string enemyTag = "Enemy";
    [SerializeField] private string campfireTag = "Campfire";

    // ───────────────────────────── 对外状态 ─────────────────────────────

    public float Faith { get; private set; }
    public float MaxFaith => maxFaith;
    public float FaithNormalized => maxFaith <= 0f ? 0f : Faith / maxFaith;
    public bool IsFull => Faith >= maxFaith - 0.001f;
    public bool IsBuffActive { get; private set; }

    /// <summary>增益期间的移速倍率，由 PlayerMovement 读取。</summary>
    public float MovementSpeedMultiplier { get; private set; } = 1f;

    /// <summary>增益期间的射速倍率，已同步写入 Hanyang88Rifle。</summary>
    public float FireRateMultiplier { get; private set; } = 1f;

    public event Action<float, float> OnFaithChanged; // (当前, 上限)
    public event Action OnFaithFull;
    public event Action OnBuffStarted;
    public event Action OnBuffEnded;

    private readonly HashSet<Collider> fireZones = new HashSet<Collider>();

    // ───────────────────────────── 生命周期 ─────────────────────────────

    /// <summary>运行时初始化，程序化生成玩家时调用。</summary>
    public void Initialize(Hanyang88Rifle rifleRef)
    {
        rifle = rifleRef;
    }

    private void Awake()
    {
        Faith = Mathf.Clamp(startingFaith, 0f, maxFaith);

        if (rifle == null) rifle = GetComponentInChildren<Hanyang88Rifle>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (health == null) health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        // 全局死亡事件：任何 Health 死亡都会通知这里
        Health.AnyDied += HandleAnyDied;
    }

    private void OnDisable()
    {
        Health.AnyDied -= HandleAnyDied;
        StopAllCoroutines();

        // 退出时确保增益状态完全清除
        IsBuffActive = false;
        ApplyMultipliers(1f, 1f);
        if (health != null) health.IncomingDamageMultiplier = 1f;
    }

    private void Update()
    {
        // 自然衰减（默认 0，不启用）
        if (faithDecayPerSecond > 0f && !IsBuffActive && Faith > 0f)
        {
            AddFaith(-faithDecayPerSecond * Time.deltaTime);
        }

        if (Input.GetKeyDown(activateKey))
        {
            TryActivateBuff();
        }

        // 站在火堆旁持续获得信仰（用进入/离开事件记录，兼容 CharacterController）
        if (fireZones.Count > 0)
        {
            AddFaith(faithPerSecondNearFire * Time.deltaTime);
        }
    }

    // ───────────────────────────── 获得信仰 ─────────────────────────────

    private void HandleAnyDied(Health health)
    {
        // 只统计敌人，避免误吃自己或友军的死亡
        if (health == null || !health.CompareTag(enemyTag)) return;

        AddFaith(faithPerKill);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(campfireTag)) fireZones.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        fireZones.Remove(other);
    }

    /// <summary>增加信仰。传负数即为扣除。</summary>
    public void AddFaith(float amount)
    {
        if (Mathf.Approximately(amount, 0f)) return;

        bool wasFull = IsFull;
        Faith = Mathf.Clamp(Faith + amount, 0f, maxFaith);

        OnFaithChanged?.Invoke(Faith, maxFaith);

        if (!wasFull && IsFull)
        {
            OnFaithFull?.Invoke();
        }
    }

    /// <summary>在 duration 秒内平滑增加信仰（协程），适合剧情奖励或火堆演出。</summary>
    public Coroutine AddFaithOverTime(float totalAmount, float duration)
    {
        return StartCoroutine(FaithGainRoutine(totalAmount, duration));
    }

    private IEnumerator FaithGainRoutine(float totalAmount, float duration)
    {
        if (duration <= 0f)
        {
            AddFaith(totalAmount);
            yield break;
        }

        float elapsed = 0f;
        float applied = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float target = totalAmount * t;

            AddFaith(target - applied);
            applied = target;

            yield return null;
        }
    }

    // ───────────────────────────── 增益 ─────────────────────────────

    /// <summary>信仰满值时按 F 调用。返回 true 表示成功触发。</summary>
    public bool TryActivateBuff()
    {
        if (IsBuffActive) return false;
        if (!IsFull) return false;

        // 一次性清空信仰，作为增益的代价
        AddFaith(-maxFaith);

        StartCoroutine(BuffRoutine());
        return true;
    }

    private IEnumerator BuffRoutine()
    {
        IsBuffActive = true;

        // 少量回血 + 50% 减伤
        if (health != null)
        {
            health.Restore(healOnBurst);
            health.IncomingDamageMultiplier = Mathf.Clamp(1f - damageReduction, 0.1f, 1f);
        }

        ApplyMultipliers(buffMoveSpeedMultiplier, buffFireRateMultiplier);

        if (animator != null && !string.IsNullOrEmpty(buffAnimatorTrigger))
        {
            animator.SetTrigger(buffAnimatorTrigger);
        }

        OnBuffStarted?.Invoke();

        // 策划案要求：持续时间严格 5 秒
        yield return new WaitForSeconds(buffDuration);

        if (health != null) health.IncomingDamageMultiplier = 1f;

        ApplyMultipliers(1f, 1f);
        IsBuffActive = false;

        OnBuffEnded?.Invoke();
    }

    /// <summary>爆发期间的减伤比例，供 HUD 显示。</summary>
    public float ActiveDamageReduction => IsBuffActive ? damageReduction : 0f;

    private void ApplyMultipliers(float moveMultiplier, float fireRateMultiplier)
    {
        MovementSpeedMultiplier = moveMultiplier;
        FireRateMultiplier = fireRateMultiplier;

        // 步枪的下一次拉栓会按该倍率缩短
        if (rifle != null)
        {
            rifle.SetFireRateMultiplier(fireRateMultiplier);
        }
    }
}
