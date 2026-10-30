using System.Collections;
using UnityEngine;

/// <summary>
/// 大刀近战：左键轻刀、右键重刀，背后贴近敌人时右键改为处决。
///
/// 手感参考 CS2 的小刀：
///   - 轻刀是横劈：手臂带动手腕，刀从一侧由外向内划出弧线，
///     刀刃横扫过身前，收招快，可以一刀接一刀地连；
///   - 重刀是直刺：先把刀收回胸前蓄力，再整条手臂直线捅出去，
///     最远处停一下才回抽，前后摇都长，冷却也更久。
///
/// 处决参考《艾尔登法环》的背刺：站在敌人正背后按右键，镜头压下去、
/// 刀从后腰捅进去再拧一下，处决期间玩家无敌，敌人必死。
///
/// 命中部位倍率：头 ×2、躯干 ×1.2、四肢 ×0.8。不会误伤战友。
/// </summary>
[DisallowMultipleComponent]
public class PlayerMelee : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private Transform blade;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip swingClip;
    [SerializeField] private AudioClip heavyClip;
    [SerializeField] private AudioClip executionClip;

    [Header("轻击：前后摇短、伤害低")]
    [SerializeField] private float lightDamage = 34f;
    [SerializeField] private float lightRange = 2.35f;
    [SerializeField] private float lightCooldown = 0.36f;

    [Header("重击：前后摇长、伤害高")]
    [SerializeField] private float heavyDamage = 92f;
    [SerializeField] private float heavyRange = 2.9f;
    [SerializeField] private float heavyCooldown = 1.05f;

    [Header("背刺处决")]
    [SerializeField] private float executionRange = 2.5f;
    [Tooltip("玩家必须落在敌人背后的夹角内：敌人前向与指向玩家方向的点积低于 -该值")]
    [SerializeField] private float executionBehindDot = 0.25f;
    [SerializeField] private float executionDuration = 1.3f;

    private float cooldownTimer;
    private Vector3 bladeRestPos;
    private Quaternion bladeRestRot = Quaternion.identity;
    private bool swinging;
    private bool executing;
    private bool forehand = true;
    private GameObject source;
    private Health playerHealth;

    private LineRenderer trail;
    private readonly Vector3[] trailPoints = new Vector3[10];

    /// <summary>处决演出中：UI 可以据此压暗画面边缘。</summary>
    public bool IsExecuting => executing;

    public void Initialize(Camera cam, AudioSource audioSrc, AudioClip lightSwing,
                           AudioClip heavySwing, AudioClip execution, Transform bladeTransform)
    {
        viewCamera = cam;
        audioSource = audioSrc;
        swingClip = lightSwing;
        heavyClip = heavySwing;
        executionClip = execution;
        blade = bladeTransform;
        source = transform.root.gameObject;
        playerHealth = transform.root.GetComponent<Health>();

        if (blade != null)
        {
            bladeRestPos = blade.localPosition;
            bladeRestRot = blade.localRotation;
            BuildTrail();
        }
    }

    /// <summary>刀光：挥砍时用一条逐渐消失的拖尾画出刀锋轨迹。</summary>
    private void BuildTrail()
    {
        GameObject trailGo = new GameObject("BladeTrail");
        trailGo.transform.SetParent(blade, false);

        trail = trailGo.AddComponent<LineRenderer>();
        trail.positionCount = trailPoints.Length;
        trail.useWorldSpace = true;
        trail.startWidth = 0.05f;
        trail.endWidth = 0.004f;
        trail.numCapVertices = 2;
        trail.material = LevelBuilder.MakeMaterial(
            new Color(0.86f, 0.9f, 0.94f), new Color(0.5f, 0.62f, 0.72f));
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.enabled = false;
    }

    private void SampleTrail()
    {
        if (trail == null || blade == null) return;

        for (int i = trailPoints.Length - 1; i > 0; i--)
        {
            trailPoints[i] = trailPoints[i - 1];
        }
        trailPoints[0] = blade.TransformPoint(new Vector3(0f, 0.2f, 0f));

        trail.SetPositions(trailPoints);
        trail.enabled = true;
    }

    private void Update()
    {
        if (executing) return;

        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer > 0f) return;

        if (Input.GetMouseButtonDown(1))
        {
            // 背后贴近敌人时右键直接处决，否则是重击
            if (!TryExecution()) TryHeavy();
        }
        else if (Input.GetMouseButtonDown(0))
        {
            TryLight();
        }
    }

    /// <summary>收刀时打断挥砍并复位，避免切回大刀时仍处于上一次动作。</summary>
    public void ResetMelee()
    {
        StopAllCoroutines();
        swinging = false;
        executing = false;
        cooldownTimer = 0f;

        if (blade != null)
        {
            blade.localPosition = bladeRestPos;
            blade.localRotation = bladeRestRot;
        }

        if (trail != null) trail.enabled = false;
    }

    /// <summary>轻击：左键。</summary>
    public bool TryLight()
    {
        if (cooldownTimer > 0f || executing) return false;
        cooldownTimer = lightCooldown;

        PlayClip(swingClip, 0.7f);

        if (blade != null && !swinging) StartCoroutine(LightRoutine());
        return true;
    }

    /// <summary>重击：右键。</summary>
    public bool TryHeavy()
    {
        if (cooldownTimer > 0f || executing) return false;
        cooldownTimer = heavyCooldown;

        PlayClip(heavyClip != null ? heavyClip : swingClip, 0.85f);

        if (blade != null && !swinging) StartCoroutine(HeavyRoutine());
        return true;
    }

    /// <summary>兼容旧调用：现在左键轻击。</summary>
    public bool TrySwing() => TryLight();

    /// <summary>兼容旧调用：现在右键重击。</summary>
    public bool TryStab() => TryHeavy();

    /// <summary>
    /// 背刺处决：玩家站在敌人正背后、距离足够近时按住右键触发。
    /// 找不到可处决目标就返回 false，交给重击。
    /// </summary>
    public bool TryExecution()
    {
        if (cooldownTimer > 0f || executing) return false;

        Health target = FindExecutionTarget(out Vector3 point);
        if (target == null) return false;

        StartCoroutine(ExecutionRoutine(target, point));
        return true;
    }

    private Health FindExecutionTarget(out Vector3 point)
    {
        point = Vector3.zero;
        if (viewCamera == null) return null;

        Vector3 origin = viewCamera.transform.position;
        Vector3 forward = viewCamera.transform.forward;

        RaycastHit[] hits = Physics.SphereCastAll(
            origin, 1.15f, forward, executionRange, ~0, QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform.root)) continue;

            Health health = hit.collider.GetComponentInParent<Health>();
            if (health == null || health.IsDead || !health.CompareTag("Enemy")) continue;

            // 玩家必须在敌人背后：敌人朝向与"敌人指向玩家"方向相反
            Vector3 toPlayer = origin - health.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) return null;
            toPlayer.Normalize();

            Vector3 facing = health.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f) return null;

            if (Vector3.Dot(facing.normalized, toPlayer) > -executionBehindDot) return null;

            point = hit.point;
            return health;
        }

        return null;
    }

    private void PlayClip(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
    }

    private IEnumerator ExecutionRoutine(Health target, Vector3 point)
    {
        executing = true;
        swinging = true;
        cooldownTimer = executionDuration;

        PlayClip(executionClip != null ? executionClip : heavyClip, 0.95f);

        // 处决期间玩家无敌：保存信仰等来源的倍率，结束后原样还回去
        float previousMultiplier = 1f;
        if (playerHealth != null)
        {
            previousMultiplier = playerHealth.IncomingDamageMultiplier;
            playerHealth.IncomingDamageMultiplier = 0f;
        }

        Vector3 restPos = bladeRestPos;
        Quaternion restRot = bladeRestRot;

        // 刀先横到身后蓄力，再顺着后腰捅进去，最后拧刀、抽刀
        Vector3 cockPos = restPos + new Vector3(-0.2f, -0.04f, -0.24f);
        Quaternion cockRot = restRot * Quaternion.Euler(-24f, 46f, -18f);
        Vector3 stabPos = restPos + new Vector3(-0.1f, 0.02f, 0.46f);
        Quaternion stabRot = restRot * Quaternion.Euler(78f, -6f, -6f);
        Vector3 twistPos = stabPos + new Vector3(0.02f, -0.01f, 0.02f);
        Quaternion twistRot = stabRot * Quaternion.Euler(0f, 0f, 26f);

        Vector3 cameraBase = viewCamera != null ? viewCamera.transform.localPosition : Vector3.zero;

        // 1) 压低身体、把刀横到身后
        float t = 0f;
        const float cockTime = 0.26f;
        while (t < cockTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / cockTime));
            blade.localPosition = Vector3.Lerp(restPos, cockPos, k);
            blade.localRotation = Quaternion.Slerp(restRot, cockRot, k);
            if (viewCamera != null)
                viewCamera.transform.localPosition = cameraBase + new Vector3(0f, -0.05f, 0.02f) * k;
            yield return null;
        }

        // 2) 捅进去：命中的瞬间结算必杀
        bool killed = false;
        t = 0f;
        const float thrustTime = 0.14f;
        while (t < thrustTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / thrustTime);
            float e = k * k;
            blade.localPosition = Vector3.Lerp(cockPos, stabPos, e);
            blade.localRotation = Quaternion.Slerp(cockRot, stabRot, e);
            SampleTrail();

            if (!killed && k >= 0.75f)
            {
                killed = true;
                if (target != null && !target.IsDead)
                {
                    target.TakeDamage(9999f, point, Vector3.back, BodyZone.Torso, source);
                }
            }

            yield return null;
        }

        if (trail != null) trail.enabled = false;

        // 3) 停住拧一下刀，这是处决最有分量的半秒
        t = 0f;
        const float twistTime = 0.3f;
        while (t < twistTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / twistTime));
            blade.localPosition = Vector3.Lerp(stabPos, twistPos, k);
            blade.localRotation = Quaternion.Slerp(stabRot, twistRot, k);
            yield return null;
        }

        // 4) 抽刀、直起身、归位
        t = 0f;
        const float pullTime = 0.34f;
        while (t < pullTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / pullTime));
            blade.localPosition = Vector3.Lerp(twistPos, restPos, k);
            blade.localRotation = Quaternion.Slerp(twistRot, restRot, k);
            if (viewCamera != null)
                viewCamera.transform.localPosition =
                    Vector3.Lerp(cameraBase + new Vector3(0f, -0.05f, 0.02f), cameraBase, k);
            yield return null;
        }

        if (viewCamera != null) viewCamera.transform.localPosition = cameraBase;
        blade.localPosition = restPos;
        blade.localRotation = restRot;

        if (playerHealth != null && !playerHealth.IsDead)
        {
            playerHealth.IncomingDamageMultiplier = previousMultiplier;
        }

        swinging = false;
        executing = false;
        cooldownTimer = Mathf.Max(cooldownTimer, 0.2f);
    }

    /// <summary>把一条拖尾重置到刀尖当前位置，避免上一刀的光带残留。</summary>
    private void ResetTrail()
    {
        if (trail == null || blade == null) return;

        Vector3 tip = blade.TransformPoint(new Vector3(0f, 0.2f, 0f));
        for (int i = 0; i < trailPoints.Length; i++) trailPoints[i] = tip;
        trail.SetPositions(trailPoints);
    }

    private IEnumerator LightRoutine()
    {
        swinging = true;
        ResetTrail();

        bool isForehand = forehand;
        forehand = !forehand;

        float dir = isForehand ? 1f : -1f;

        Vector3 restPos = bladeRestPos;
        Quaternion restRot = bladeRestRot;

        // 起挥：手腕先往外一翻，刀背朝里、刃口朝外，手臂小幅收在肩侧蓄力
        Vector3 cockPos = restPos + (isForehand
            ? new Vector3(0.10f, 0.08f, -0.10f)
            : new Vector3(-0.20f, 0.06f, -0.08f));
        Quaternion cockRot = restRot * Quaternion.Euler(-8f, -26f * dir, -40f * dir);

        // 刀身划过身前的正中：这一刻是击中帧，刀锋横着扫过目标
        Vector3 midPos = restPos + (isForehand
            ? new Vector3(-0.16f, 0.03f, 0.07f)
            : new Vector3(-0.02f, 0f, 0.08f));
        Quaternion midRot = restRot * Quaternion.Euler(6f, 8f * dir, 26f * dir);

        // 顺弧线甩到另一侧：手臂伸到最大幅度，刀接近水平
        Vector3 endPos = restPos + (isForehand
            ? new Vector3(-0.48f, -0.05f, -0.04f)
            : new Vector3(0.30f, -0.06f, -0.02f));
        Quaternion endRot = restRot * Quaternion.Euler(14f, 36f * dir, 78f * dir);

        float t = 0f;
        const float cockTime = 0.06f;
        while (t < cockTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / cockTime);
            float e = k * k;
            blade.localPosition = Vector3.Lerp(restPos, cockPos, e);
            blade.localRotation = Quaternion.Slerp(restRot, cockRot, e);
            yield return null;
        }

        // 横劈：前段加速送出去，后段沿弧线收住，不是一条直线
        const float slashTime = 0.12f;
        bool struck = false;
        t = 0f;

        while (t < slashTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / slashTime);

            if (k < 0.5f)
            {
                float e = (k / 0.5f);
                e = e * e;
                blade.localPosition = Vector3.Lerp(cockPos, midPos, e);
                blade.localRotation = Quaternion.Slerp(cockRot, midRot, e);
            }
            else
            {
                float e = (k - 0.5f) / 0.5f;
                e = 1f - (1f - e) * (1f - e);
                blade.localPosition = Vector3.Lerp(midPos, endPos, e);
                blade.localRotation = Quaternion.Slerp(midRot, endRot, e);
            }

            SampleTrail();

            if (!struck && k >= 0.48f)
            {
                struck = true;
                Strike(lightRange, 0.46f, lightDamage, 1f);
            }

            yield return null;
        }

        if (trail != null) trail.enabled = false;

        // 收刀：顺着挥砍的弧线把手腕收回来，回到待机位置
        t = 0f;
        const float recoverTime = 0.16f;
        while (t < recoverTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / recoverTime);
            float ease = 1f - (1f - k) * (1f - k);

            blade.localPosition = Vector3.Lerp(endPos, restPos, ease);
            blade.localRotation = Quaternion.Slerp(endRot, restRot, ease);
            yield return null;
        }

        blade.localPosition = restPos;
        blade.localRotation = restRot;
        swinging = false;
    }

    private IEnumerator HeavyRoutine()
    {
        swinging = true;
        ResetTrail();

        Vector3 restPos = bladeRestPos;
        Quaternion restRot = bladeRestRot;

        // 蓄力回拉：手臂向后收，手肘弯起来，刀收在胸前，刀尖对准正前方
        Vector3 cockPos = restPos + new Vector3(-0.05f, 0.14f, -0.18f);
        Quaternion cockRot = restRot * Quaternion.Euler(38f, 6f, -10f);

        // 直线捅出去：整条手臂猛地送出，刀尖指向目标。
        // 只前倾五十来度而不是完全放平——完全放平时刀身正对视线，
        // 屏幕上只剩一条边，握刀的袖口还会被转到镜头前面。
        Vector3 thrustPos = restPos + new Vector3(-0.18f, 0.10f, 0.30f);
        Quaternion thrustRot = restRot * Quaternion.Euler(52f, 0f, -6f);

        // 最远点再顶一下，这一顿是重刀"重"的来源
        Vector3 pushPos = thrustPos + new Vector3(0f, 0.01f, 0.05f);
        Quaternion pushRot = thrustRot * Quaternion.Euler(2f, 0f, 0f);

        // 1) 回拉：慢，给敌人看清的机会
        float t = 0f;
        const float cockTime = 0.18f;
        while (t < cockTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / cockTime));
            blade.localPosition = Vector3.Lerp(restPos, cockPos, k);
            blade.localRotation = Quaternion.Slerp(restRot, cockRot, k);
            yield return null;
        }

        // 2) 刺出：最快的一段，刀过 60% 判定
        const float thrustTime = 0.10f;
        bool struck = false;
        t = 0f;

        while (t < thrustTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / thrustTime);
            float e = k * k * k;
            blade.localPosition = Vector3.Lerp(cockPos, thrustPos, e);
            blade.localRotation = Quaternion.Slerp(cockRot, thrustRot, e);
            SampleTrail();

            if (!struck && k >= 0.6f)
            {
                struck = true;
                Strike(heavyRange, 0.5f, heavyDamage, 1.35f);
            }

            yield return null;
        }

        if (trail != null) trail.enabled = false;

        // 3) 停在最远点：刀尖顶着目标再送半寸
        t = 0f;
        const float holdTime = 0.10f;
        while (t < holdTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / holdTime));
            blade.localPosition = Vector3.Lerp(thrustPos, pushPos, k);
            blade.localRotation = Quaternion.Slerp(thrustRot, pushRot, k);
            yield return null;
        }

        // 4) 抽刀回位：慢，收完才知道有没有机会补下一刀
        t = 0f;
        const float recoverTime = 0.30f;
        while (t < recoverTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / recoverTime);
            float ease = 1f - (1f - k) * (1f - k);
            blade.localPosition = Vector3.Lerp(pushPos, restPos, ease);
            blade.localRotation = Quaternion.Slerp(pushRot, restRot, ease);
            yield return null;
        }

        blade.localPosition = restPos;
        blade.localRotation = restRot;
        swinging = false;
    }

    /// <summary>在刀锋经过的瞬间做一次球形扫掠判定。</summary>
    private bool Strike(float reach, float radius, float damage, float damageScale)
    {
        if (viewCamera == null) return false;

        Vector3 origin = viewCamera.transform.position;
        Vector3 forward = viewCamera.transform.forward;

        RaycastHit[] hits = Physics.SphereCastAll(
            origin, radius, forward, reach, ~0, QueryTriggerInteraction.Ignore);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            // 跳过自己身上的碰撞体
            if (hit.collider.transform.IsChildOf(transform.root)) continue;

            HitZone zone = hit.collider.GetComponent<HitZone>();
            if (zone == null) zone = hit.collider.GetComponentInParent<HitZone>();

            if (zone != null)
            {
                Health health = zone.GetComponentInParent<Health>();
                if (health != null && !health.IsDead)
                {
                    // 刀砍到自己人身上会被挡住，不结算伤害
                    if (health.CompareTag("Ally")) return false;

                    float multiplier;
                    switch (zone.Zone)
                    {
                        case BodyZone.Head:  multiplier = 2.0f; break;
                        case BodyZone.Torso: multiplier = 1.2f; break;
                        default:             multiplier = 0.8f; break;
                    }

                    health.TakeDamage(
                        damage * multiplier * damageScale, hit.point, hit.normal,
                        zone.Zone, source);
                    return true;
                }
            }

            // 第一面非自身的墙，不能穿过去砍人
            return false;
        }

        return false;
    }
}
