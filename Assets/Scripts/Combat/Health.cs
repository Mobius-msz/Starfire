using System;
using UnityEngine;

/// <summary>一次伤害结算的完整信息，供受击表现、音效、UI 订阅。</summary>
public struct DamageInfo
{
    public float Amount;        // 本次伤害
    public float Remaining;     // 剩余生命
    public Vector3 Point;       // 命中点
    public Vector3 Normal;      // 命中面法线
    public BodyZone Zone;       // 命中部位
    public GameObject Source;   // 伤害来源（玩家 / 战友 / 敌军），用于击杀归属

    public DamageInfo(float amount, float remaining, Vector3 point, Vector3 normal,
                      BodyZone zone, GameObject source)
    {
        Amount = amount;
        Remaining = remaining;
        Point = point;
        Normal = normal;
        Zone = zone;
        Source = source;
    }
}

/// <summary>
/// 通用生命值组件。敌人、友军、可破坏物都可以挂。
///
/// 默认最大生命 100：
///   - 爆头 100 → 一枪毙命
///   - 躯干 40  → 三枪毙命
/// 这个数值刻意让"每一发子弹"都有重量。
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("生命值")]
    [SerializeField] private float maxHealth = 100f;

    [Tooltip("死亡后延迟多久触发事件，用于播放倒地动画")]
    [SerializeField] private float deathDelay = 0f;

    public float MaxHealth => maxHealth;
    public float Current { get; private set; }
    public bool IsDead { get; private set; }

    /// <summary>受伤倍率。1 = 正常，0.5 = 减伤 50%（信仰爆发等）。</summary>
    public float IncomingDamageMultiplier { get; set; } = 1f;

    /// <summary>每次受伤触发（未死亡也会触发）。</summary>
    public event Action<DamageInfo> Damaged;

    /// <summary>死亡时触发一次。</summary>
    public event Action<DamageInfo> Died;

    /// <summary>
    /// 任意 Health 死亡时触发（全局静态事件）。
    /// 信仰系统用它统计击杀，订阅者必须在 OnDisable 中取消订阅。
    /// </summary>
    public static event Action<Health> AnyDied;

    /// <summary>任意 Health 受伤（含致死一击）时触发，带伤害来源。</summary>
    public static event Action<Health, DamageInfo> AnyDamaged;

    /// <summary>任意 Health 被击杀时触发，带致死一击的信息（部位与来源）。</summary>
    public static event Action<Health, DamageInfo> AnyKilled;

    private void Awake()
    {
        Current = maxHealth;
    }

    /// <summary>
    /// 受到伤害。已死亡或伤害非正数时直接忽略。
    /// </summary>
    public void TakeDamage(float amount, Vector3 point, Vector3 normal, BodyZone zone,
                           GameObject source = null)
    {
        if (IsDead || amount <= 0f) return;

        amount *= Mathf.Max(0f, IncomingDamageMultiplier);
        if (amount <= 0f) return;

        Current = Mathf.Max(0f, Current - amount);
        DamageInfo info = new DamageInfo(amount, Current, point, normal, zone, source);

        Damaged?.Invoke(info);
        AnyDamaged?.Invoke(this, info);

        if (Current <= 0f && !IsDead)
        {
            IsDead = true;

            // 击杀统计即时触发，不受倒地动画延迟影响
            AnyDied?.Invoke(this);
            AnyKilled?.Invoke(this, info);

            if (deathDelay > 0f)
            {
                Invoke(nameof(NotifyDeath), deathDelay);
                cachedDeathInfo = info;
            }
            else
            {
                Died?.Invoke(info);
            }
        }
    }

    private DamageInfo cachedDeathInfo;

    private void NotifyDeath()
    {
        Died?.Invoke(cachedDeathInfo);
    }

    /// <summary>恢复生命值（火堆等恢复点调用）。</summary>
    public void Restore(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Min(maxHealth, Current + amount);
    }

    /// <summary>重置生命值（对象池复用时调用）。</summary>
    public void ResetHealth()
    {
        CancelInvoke();
        Current = maxHealth;
        IsDead = false;
    }

    /// <summary>生成时按兵种设置血量。</summary>
    public void ConfigureMaxHealth(float value)
    {
        maxHealth = Mathf.Max(1f, value);
        Current = maxHealth;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
    }
}
