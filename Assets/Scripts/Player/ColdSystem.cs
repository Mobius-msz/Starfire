using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 严寒系统（挂在 Player 身上）
///
/// 规则：
/// - 进入 Tag = "Blizzard" 的触发器区域，体温持续下降。
/// - 体温低于 50 时，屏幕边缘冰冻特效按温度线性渐显（协程平滑）。
/// - 体温低于 0 时，移动速度开始下降；越冷越慢，最低由 minSpeedMultiplier 决定。
/// - 离开暴风雪缓慢回温；靠近 Tag = "Campfire" 的火堆快速回温。
///
/// 依赖：
/// - Player 需要有 Collider（CharacterController 也可以）才能接收 Trigger 事件。
/// - 暴风雪区域物体：Collider 勾选 Is Trigger，Tag 设为 "Blizzard"。
/// - 火堆物体：Collider 勾选 Is Trigger，Tag 设为 "Campfire"。
/// - 冰冻特效：可用 CanvasGroup，也可直接读取 FrostAlpha 交给任意 UI 绘制。
/// </summary>
[DisallowMultipleComponent]
public class ColdSystem : MonoBehaviour
{
    [Header("体温")]
    [Tooltip("体温上限（100 = 正常体温）")]
    [SerializeField] private float maxTemperature = 100f;

    [Tooltip("体温下限，允许低于 0，表示失温程度")]
    [SerializeField] private float minTemperature = -50f;

    [Tooltip("开局体温")]
    [SerializeField] private float startTemperature = 100f;

    [Tooltip("暴风雪中每秒流失的体温")]
    [SerializeField] private float coldPerSecond = 8f;

    [Tooltip("离开暴风雪、也不在火堆旁时，每秒恢复的体温")]
    [SerializeField] private float recoverPerSecond = 3f;

    [Tooltip("靠近火堆时每秒恢复的体温")]
    [SerializeField] private float fireWarmthPerSecond = 18f;

    [Header("阈值")]
    [Tooltip("低于该值开始出现冰冻特效（策划案：50）")]
    [SerializeField] private float frostThreshold = 50f;

    [Tooltip("低于该值开始减速（策划案：0）")]
    [SerializeField] private float speedPenaltyThreshold = 0f;

    [Tooltip("体温降到最低时的移速倍率")]
    [Range(0.1f, 1f)]
    [SerializeField] private float minSpeedMultiplier = 0.45f;

    [Header("表现")]
    [Tooltip("屏幕边缘冰冻特效的 CanvasGroup（alpha 控制浓度）")]
    [SerializeField] private CanvasGroup frostOverlay;

    [Tooltip("冰冻特效淡入淡出速度")]
    [SerializeField] private float frostFadeSpeed = 1.5f;

    [Tooltip("移速倍率的平滑速度")]
    [SerializeField] private float speedLerpSpeed = 2f;

    [Tooltip("可选：Animator 的 Float 参数名，用于播放寒冷颤抖")]
    [SerializeField] private string coldAnimatorParameter = "Cold";

    [Header("区域标签")]
    [SerializeField] private string blizzardTag = "Blizzard";
    [SerializeField] private string heatTag = "Campfire";

    // ───────────────────────────── 对外状态 ─────────────────────────────

    /// <summary>当前体温，范围 [minTemperature, maxTemperature]。</summary>
    public float Temperature { get; private set; }

    /// <summary>体温百分比：100 = 正常，0 = 临界，负数 = 失温。</summary>
    public float TemperaturePercent => maxTemperature <= 0f ? 0f : Temperature / maxTemperature * 100f;

    public bool IsInBlizzard => blizzardZones.Count > 0;
    public bool IsNearFire => heatZones.Count > 0;
    public bool IsFreezing => Temperature <= speedPenaltyThreshold;

    /// <summary>严寒造成的移速倍率，由 PlayerMovement 读取。</summary>
    public float MovementSpeedMultiplier { get; private set; } = 1f;

    /// <summary>冰冻特效浓度 0~1，供 HUD 直接绘制（无需 CanvasGroup）。</summary>
    public float FrostAlpha { get; private set; }

    public event Action<float> OnTemperatureChanged;
    public event Action<bool> OnBlizzardStateChanged;

    // ───────────────────────────── 内部 ─────────────────────────────

    private readonly HashSet<Collider> blizzardZones = new HashSet<Collider>();
    private readonly HashSet<Collider> heatZones = new HashSet<Collider>();

    private Animator animator;
    private bool wasInBlizzard;

    /// <summary>
    /// 运行时初始化。程序化生成关卡时用它注入冰冻特效与参数。
    /// </summary>
    public void Initialize(CanvasGroup overlay, float coldRate, float recoverRate, float fireWarmth)
    {
        frostOverlay = overlay;
        Initialize(coldRate, recoverRate, fireWarmth);

        if (frostOverlay != null)
        {
            frostOverlay.alpha = 0f;
            frostOverlay.interactable = false;
            frostOverlay.blocksRaycasts = false;
        }
    }

    /// <summary>不需要 CanvasGroup 的初始化：HUD 直接读取 FrostAlpha。</summary>
    public void Initialize(float coldRate, float recoverRate, float fireWarmth)
    {
        coldPerSecond = coldRate;
        recoverPerSecond = recoverRate;
        fireWarmthPerSecond = fireWarmth;
    }

    private void Awake()
    {
        Temperature = Mathf.Clamp(startTemperature, minTemperature, maxTemperature);
        animator = GetComponentInChildren<Animator>();

        if (frostOverlay != null)
        {
            frostOverlay.alpha = 0f;
            frostOverlay.interactable = false;
            frostOverlay.blocksRaycasts = false;
        }
    }

    private void OnEnable()
    {
        // 用协程持续做平滑过渡：特效浓度、移速倍率、动画参数
        StartCoroutine(EffectsRoutine());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    private void Update()
    {
        UpdateTemperature();
    }

    // ───────────────────────────── 温度 ─────────────────────────────

    private void UpdateTemperature()
    {
        float delta = 0f;
        float weather = DynamicWeather.ColdSeverity;

        // 暴风雪与火堆可以同时生效（暴风雪里的火堆依然能取暖）
        if (IsInBlizzard) delta -= coldPerSecond * (1f + weather * 0.5f);
        // 冻雨会把体温一点点带走，就算不在暴风雪里也一样
        if (weather > 0f) delta -= 1.8f * weather;
        if (IsNearFire) delta += fireWarmthPerSecond;
        else if (!IsInBlizzard) delta += recoverPerSecond * Mathf.Lerp(1f, 0.35f, weather);

        if (!Mathf.Approximately(delta, 0f))
        {
            SetTemperature(Temperature + delta * Time.deltaTime);
        }

        if (wasInBlizzard != IsInBlizzard)
        {
            wasInBlizzard = IsInBlizzard;
            OnBlizzardStateChanged?.Invoke(IsInBlizzard);
        }
    }

    /// <summary>直接设置体温（会做上下限裁剪）。</summary>
    public void SetTemperature(float value)
    {
        float clamped = Mathf.Clamp(value, minTemperature, maxTemperature);
        if (Mathf.Approximately(clamped, Temperature)) return;

        Temperature = clamped;
        OnTemperatureChanged?.Invoke(Temperature);
    }

    /// <summary>立刻增加体温（喝热水、进室内等）。</summary>
    public void AddWarmth(float amount)
    {
        SetTemperature(Temperature + amount);
    }

    /// <summary>在 duration 秒内平滑增加体温（协程）。</summary>
    public Coroutine WarmOverTime(float amount, float duration)
    {
        return StartCoroutine(TemperatureChangeRoutine(amount, duration));
    }

    /// <summary>在 duration 秒内平滑降低体温，例如掉进冰河。</summary>
    public Coroutine ChillOverTime(float amount, float duration)
    {
        return StartCoroutine(TemperatureChangeRoutine(-Mathf.Abs(amount), duration));
    }

    private IEnumerator TemperatureChangeRoutine(float totalDelta, float duration)
    {
        if (duration <= 0f)
        {
            SetTemperature(Temperature + totalDelta);
            yield break;
        }

        float elapsed = 0f;
        float applied = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // SmoothStep 让变化有两端缓入缓出的手感
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float target = totalDelta * t;

            SetTemperature(Temperature + (target - applied));
            applied = target;

            yield return null;
        }
    }

    // ───────────────────────────── 视觉与速度 ─────────────────────────────

    /// <summary>
    /// 常驻协程：每帧把特效浓度、移速倍率、动画参数平滑地推向目标值。
    /// 用 MoveTowards 而不是一次性赋值，避免温度抖动导致画面闪烁。
    /// </summary>
    private IEnumerator EffectsRoutine()
    {
        while (true)
        {
            // 1) 冰冻特效：50% 开始出现，0% 时达到最浓，低于 0 保持最浓
            float targetAlpha = Mathf.Clamp01(
                Mathf.InverseLerp(frostThreshold, 0f, Temperature));

            float currentAlpha = frostOverlay != null ? frostOverlay.alpha : FrostAlpha;
            float nextAlpha = Mathf.MoveTowards(
                currentAlpha, targetAlpha, frostFadeSpeed * Time.deltaTime);

            FrostAlpha = nextAlpha;
            if (frostOverlay != null) frostOverlay.alpha = nextAlpha;

            // 2) 移速：低于 0% 后线性下降，最低 minSpeedMultiplier
            float targetSpeed = EvaluateSpeedMultiplier(Temperature);
            MovementSpeedMultiplier = Mathf.MoveTowards(
                MovementSpeedMultiplier, targetSpeed, speedLerpSpeed * Time.deltaTime);

            // 3) 可选：寒冷颤抖动画参数，0 = 正常，1 = 极寒
            if (animator != null && !string.IsNullOrEmpty(coldAnimatorParameter))
            {
                float coldValue = Mathf.InverseLerp(maxTemperature, minTemperature, Temperature);
                animator.SetFloat(coldAnimatorParameter, coldValue);
            }

            yield return null;
        }
    }

    private float EvaluateSpeedMultiplier(float temp)
    {
        if (temp >= speedPenaltyThreshold) return 1f;

        float t = Mathf.InverseLerp(speedPenaltyThreshold, minTemperature, temp);
        return Mathf.Lerp(1f, minSpeedMultiplier, t);
    }

    // ───────────────────────────── 区域检测 ─────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(blizzardTag)) blizzardZones.Add(other);
        if (other.CompareTag(heatTag)) heatZones.Add(other);
    }

    private void OnTriggerExit(Collider other)
    {
        blizzardZones.Remove(other);
        heatZones.Remove(other);
    }
}
