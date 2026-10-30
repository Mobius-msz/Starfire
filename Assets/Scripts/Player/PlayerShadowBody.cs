using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 玩家自己的身体（第一人称时只投影，第三人称时可见）。
///
/// 身体按关节搭：髋部 → 大腿 → 小腿，躯干 → 头 / 双臂。
/// 站立、蹲下、趴下不是简单地把模型压扁，而是真的折叠关节，
/// 所以第三人称下蹲、卧倒的姿势看得过去，雪地上的影子也会跟着变形。
/// </summary>
[DisallowMultipleComponent]
public class PlayerShadowBody : MonoBehaviour
{
    private Transform body;
    private Transform torsoPivot;
    private Transform thighL;
    private Transform thighR;
    private Transform shinL;
    private Transform shinR;
    private Transform armL;
    private Transform armR;
    private Transform forearmL;
    private Transform forearmR;
    private Transform footL;
    private Transform footR;
    private Transform head;
    private Transform slungRifle;
    private Transform heldRifle;
    private Transform heldPistol;
    private Transform heldKnife;
    private Transform heldBolt;
    private Vector3 heldBoltHome;
    private Transform waistTorch;
    private Transform heldTorch;
    private TorchSystem torch;
    private WeaponController weapon;
    private int heldSlot = -1;
    private float switchTimer;
    private float switchDip;
    private PlayerMovement movement;

    private const float SwitchDuration = 0.45f;

    private Renderer[] renderers;
    private bool visible;
    private float crouchWeight;
    private float kneelWeight;
    private float pronePhase;
    private float flinch;
    private float walkWeight;
    private float stride;
    private bool strideHold;

    /// <summary>开发截图用：把走路循环钉在某个相位，保证抓到的是一大步。</summary>
    public void SetWalkForCapture(float phase)
    {
        strideHold = true;
        stride = Mathf.Repeat(phase, Mathf.PI * 2f);
        walkWeight = 1f;
    }

    public bool IsVisible => visible;

    /// <summary>绑定武器：第三人称下手上拿什么、什么时候换，都从这里读。</summary>
    public void BindWeapon(WeaponController weaponRef)
    {
        weapon = weaponRef;
        heldSlot = weapon != null ? (int)weapon.Current : 0;
    }

    /// <summary>绑定火把：腰间 / 手里两个模型跟着火把开关换，左臂也要抬起来。</summary>
    public void BindTorch(TorchSystem torchRef)
    {
        torch = torchRef;
    }

    private void UpdateHeldWeapon()
    {
        int wanted = weapon != null ? (int)weapon.Current : 0;

        if (heldSlot < 0)
        {
            heldSlot = wanted;
        }
        else if (wanted != heldSlot)
        {
            heldSlot = wanted;
            switchTimer = SwitchDuration;
        }

        if (switchTimer > 0f)
        {
            switchTimer = Mathf.Max(0f, switchTimer - Time.deltaTime);
        }

        float progress = switchTimer > 0f ? 1f - switchTimer / SwitchDuration : 1f;
        switchDip = switchTimer > 0f ? Mathf.Sin(Mathf.PI * progress) : 0f;

        // 换到一半才换手：旧枪收起来，新枪随之抬起
        bool swapped = switchTimer <= 0f || progress >= 0.5f;
        bool showBody = visible;

        if (heldRifle != null)
            heldRifle.gameObject.SetActive(showBody && swapped && heldSlot == 0);
        if (heldPistol != null)
            heldPistol.gameObject.SetActive(showBody && swapped && heldSlot == 1);
        if (heldKnife != null)
            heldKnife.gameObject.SetActive(showBody && swapped && heldSlot == 2);

        // 步枪已经端在手里，背上那把就收起来
        if (slungRifle != null)
            slungRifle.gameObject.SetActive(!(showBody && heldSlot == 0));

        // 火把：别在腰上的和举在手里的，同一时刻只留一个
        float torchHand = torch != null ? torch.HandBlend : 0f;
        if (waistTorch != null)
            waistTorch.gameObject.SetActive(showBody && torchHand <= 0.5f);
        if (heldTorch != null)
            heldTorch.gameObject.SetActive(showBody && torchHand > 0.5f);
    }

    /// <summary>中弹抽搐：第三人称 / 雪地影子里能看到身体被打得一顿。</summary>
    public void Flinch(float amount)
    {
        flinch = Mathf.Clamp(flinch + Mathf.Clamp(amount, 0.15f, 1f), 0f, 1.3f);
    }

    public void Initialize(Transform bodyTransform, PlayerMovement movementRef)
    {
        body = bodyTransform;
        movement = movementRef;

        if (body == null) return;

        torsoPivot = body.Find("TorsoPivot");
        thighL = body.Find("ThighL");
        thighR = body.Find("ThighR");
        shinL = thighL != null ? thighL.Find("ShinL") : null;
        shinR = thighR != null ? thighR.Find("ShinR") : null;
        armL = torsoPivot != null ? torsoPivot.Find("ArmL") : null;
        armR = torsoPivot != null ? torsoPivot.Find("ArmR") : null;
        forearmL = armL != null ? armL.Find("ForearmL") : null;
        forearmR = armR != null ? armR.Find("ForearmR") : null;
        footL = shinL != null ? shinL.Find("FootL") : null;
        footR = shinR != null ? shinR.Find("FootR") : null;
        head = torsoPivot != null ? torsoPivot.Find("HeadPivot") : null;

        slungRifle = torsoPivot != null ? torsoPivot.Find("SlungRifle") : null;

        Transform rightHand = body.Find("TorsoPivot/ArmR/ForearmR/RightHand");
        if (rightHand != null)
        {
            heldRifle = rightHand.Find("HeldRifle");
            heldPistol = rightHand.Find("HeldPistol");
            heldKnife = rightHand.Find("HeldKnife");
            heldBolt = heldRifle != null ? heldRifle.Find("Bolt") : null;
            if (heldBolt != null) heldBoltHome = heldBolt.localPosition;
        }

        waistTorch = torsoPivot != null ? torsoPivot.Find("WaistTorch") : null;
        Transform leftHand = body.Find("TorsoPivot/ArmL/ForearmL/LeftHand");
        heldTorch = leftHand != null ? leftHand.Find("HeldTorch") : null;
        if (waistTorch != null) waistTorch.gameObject.SetActive(false);
        if (heldTorch != null) heldTorch.gameObject.SetActive(false);

        renderers = body.GetComponentsInChildren<Renderer>(true);
        body.localScale = Vector3.one;
        body.localPosition = Vector3.zero;
    }

    /// <summary>第三人称下显示身体，第一人称下切回"只投影"。</summary>
    public void SetVisible(bool value)
    {
        visible = value;
        if (renderers == null) return;

        ShadowCastingMode mode = value ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].shadowCastingMode = mode;
            renderers[i].receiveShadows = value;
        }
    }

    private void LateUpdate()
    {
        if (body == null || movement == null) return;

        bool crouching = movement.CurrentStance == PlayerMovement.Stance.Crouching;
        bool prone = movement.CurrentStance == PlayerMovement.Stance.Prone;

        crouchWeight = Mathf.MoveTowards(crouchWeight, crouching ? 1f : 0f,
            StancePose.CrouchRate * Time.deltaTime);
        // 卧倒整条动作链约 0.7 秒，起身略快，倒放同一套关键帧
        pronePhase = Mathf.MoveTowards(pronePhase, prone ? 1f : 0f,
            (prone ? StancePose.ProneDownRate : StancePose.ProneUpRate) * Time.deltaTime);
        flinch = Mathf.MoveTowards(flinch, 0f, 3.4f * Time.deltaTime);

        // 蹲着且几乎不动才跪，一迈步就收回成蹲走
        kneelWeight = Mathf.MoveTowards(kneelWeight,
            crouchWeight * (1f - pronePhase)
            * Mathf.Clamp01(1f - movement.PlanarSpeed / 1.2f),
            StancePose.KneelRate * Time.deltaTime);

        StancePose.Key proneKey = StancePose.SampleProne(pronePhase);
        float crouchBase = crouchWeight * (1f - pronePhase);

        UpdateHeldWeapon();

        float lean = crouchBase * StancePose.CrouchLean + proneKey.lean;

        // 蹲姿：站着不动时单膝跪地，一迈步就过渡回低姿蹚步。
        StancePose.SampleCrouch(kneelWeight,
            out float thighFront, out float shinFront,
            out float thighBack, out float shinBack, out float crouchDrop);

        float thighLBase = crouchBase * thighFront + proneKey.thighL;
        float shinLBase = crouchBase * shinFront + proneKey.shinL;
        float thighRBase = crouchBase * thighBack + proneKey.thighR;
        float shinRBase = crouchBase * shinBack + proneKey.shinR;
        float arm = crouchBase * 12f;

        // 趴着时的呼吸起伏：胸口和枪口一起缓慢上落
        float proneBreath = Mathf.Sin(Time.time * 1.35f) * pronePhase;

        // ── 走路 / 奔跑循环 ──
        // 速度直接读 CharacterController 的实际水平速度，撞墙、被卡住时腿也会停；
        // 蹲着只摆小步，趴下完全不摆。
        float speed = movement.PlanarSpeed;
        float targetWeight = speed > 0.3f ? Mathf.Clamp01(speed / 3.2f) : 0f;
        targetWeight *= 1f - pronePhase;
        targetWeight *= Mathf.Lerp(1f, 0.45f, crouchWeight);
        walkWeight = strideHold ? 1f : Mathf.MoveTowards(walkWeight, targetWeight, 5f * Time.deltaTime);

        bool running = movement.IsSprinting;

        if (!strideHold && walkWeight > 0.001f)
        {
            // 步频跟着速度走：走得越快步子越密
            float cadence = running ? 10.5f : 4.2f + speed * 0.9f;
            stride += Time.deltaTime * cadence;
            if (stride > Mathf.PI * 2f) stride -= Mathf.PI * 2f;
        }

        float amp = (movement.IsSprinting ? 46f : 26f) * walkWeight;
        float swing = Mathf.Sin(stride) * amp;
        float swingCos = Mathf.Cos(stride);

        // 腿：前摆时伸直，后摆时小腿折起来
        float thighSwing = swing;
        float shinBend = Mathf.Max(0f, -Mathf.Sin(stride)) * amp * 1.35f;
        float thighSwingR = -swing;
        float shinBendR = Mathf.Max(0f, Mathf.Sin(stride)) * amp * 1.35f;

        // 手臂反向摆；奔跑时上身略前倾
        float armSwing = -swing * 0.72f;
        lean += (running ? 6f : 2.2f) * walkWeight;

        // 步子落地的上下颠簸
        float bob = Mathf.Abs(Mathf.Sin(stride)) * 0.045f * walkWeight;

        // 中弹时上身向后一顿，头跟着偏
        float jolt = flinch * 12f;

        body.localPosition = new Vector3(
            0f,
            crouchBase * crouchDrop
            + proneKey.bodyY
            + proneBreath * 0.005f
            - flinch * 0.03f + bob,
            0f);

        if (torsoPivot != null)
        {
            // 肩胯反向扭转，走起来不是一块板子在平移
            torsoPivot.localRotation = Quaternion.Euler(
                lean - jolt,
                flinch * 8f - swingCos * 2.6f * walkWeight - switchDip * 7f,
                flinch * 5f + kneelWeight * 4f + pronePhase * 3f);
        }
        if (thighL != null) thighL.localRotation = Quaternion.Euler(thighLBase + thighSwing, 0f, 0f);
        if (thighR != null) thighR.localRotation = Quaternion.Euler(thighRBase + thighSwingR, 0f, 0f);
        if (shinL != null) shinL.localRotation = Quaternion.Euler(shinLBase + shinBend, 0f, 0f);
        if (shinR != null) shinR.localRotation = Quaternion.Euler(shinRBase + shinBendR, 0f, 0f);
        // 第三人称持枪：右手抬到胸前，左臂托枪；换枪时整条手臂先落下去
        float hold = visible ? (1f - pronePhase) * (1f - 0.85f * switchDip) : 0f;
        // 冲刺收枪：手垂下来，跟着跑
        if (movement.IsSprinting) hold *= 0.35f;
        float holdArmR, holdForeR, holdArmL, holdForeL;
        if (heldSlot == 0)
        {
            holdArmR = -38f; holdForeR = -47f; holdArmL = -30f; holdForeL = -52f;
        }
        else if (heldSlot == 1)
        {
            holdArmR = -36f; holdForeR = -44f; holdArmL = -14f; holdForeL = -22f;
        }
        else
        {
            holdArmR = -30f; holdForeR = -38f; holdArmL = -10f; holdForeL = -16f;
        }

        // 端着枪走路时手臂不再大幅前后摆，枪才不至于乱甩
        float heldArmSwing = armSwing * (1f - 0.75f * hold);

        // 举火把：大臂略抬、肘弯起来把火把举到身前，走起来也跟着步子轻晃
        float torchBlend = torch != null ? torch.HandBlend * (1f - pronePhase) : 0f;
        float armLAngle = arm + proneKey.armL + holdArmL * hold - jolt * 0.4f + heldArmSwing;
        float foreLAngle = proneKey.forearmL + holdForeL * hold - jolt * 0.2f;
        if (torchBlend > 0.001f)
        {
            float torchArm = -18f + Mathf.Sin(stride) * 3f * walkWeight;
            float torchFore = -100f + Mathf.Cos(stride) * 3f * walkWeight;
            armLAngle = Mathf.Lerp(armLAngle, torchArm, torchBlend);
            foreLAngle = Mathf.Lerp(foreLAngle, torchFore, torchBlend);
        }

        if (armL != null)
            armL.localRotation = Quaternion.Euler(armLAngle, 0f, 0f);

        // 拉栓：右手离开枪颈去掰拉柄，大臂抬一点、肘弯起来，拉到底再送回去
        float boltLift = heldSlot == 0 && weapon != null ? weapon.BoltLift : 0f;
        float boltPull = heldSlot == 0 && weapon != null ? weapon.BoltPull : 0f;
        float armRAngle = arm * 0.85f + proneKey.armR + holdArmR * hold - jolt * 0.4f
                          - heldArmSwing - 10f * boltLift - 4f * boltPull;
        float foreRAngle = proneKey.forearmR + holdForeR * hold - jolt * 0.2f
                           - 26f * boltLift - 7f * boltPull;

        if (armR != null)
        {
            armR.localRotation = Quaternion.Euler(armRAngle, 0f, 0f);
            // 拔刀时右臂往外一甩
            if (heldSlot == 2 && switchDip > 0.001f)
                armR.localRotation *= Quaternion.Euler(0f, 0f, -24f * switchDip);
        }
        if (forearmL != null)
            forearmL.localRotation = Quaternion.Euler(foreLAngle, 0f, 0f);
        if (forearmR != null)
            forearmR.localRotation = Quaternion.Euler(foreRAngle, 0f, 0f);

        // 第三人称枪上的拉柄也跟着抬起、后拉，远看也能看出在拉栓
        if (heldBolt != null)
        {
            heldBolt.localPosition = heldBoltHome
                + new Vector3(0.014f * boltLift, -0.032f * boltPull, 0f);
            heldBolt.localRotation = Quaternion.Euler(0f, 52f * boltLift, 0f);
        }

        // 手随小臂转，火把得靠手腕掰回来才立得起来
        if (heldTorch != null)
        {
            heldTorch.localRotation = Quaternion.Euler(113f * torchBlend, 0f, -8f * torchBlend);
        }
        if (footL != null) footL.localRotation = Quaternion.Euler(proneKey.footL, 0f, 0f);
        if (footR != null) footR.localRotation = Quaternion.Euler(proneKey.footR, 0f, 0f);
        if (head != null)
        {
            head.localRotation = Quaternion.Euler(
                proneKey.head - crouchBase * 4f - jolt * 0.7f
                - proneBreath * 1.4f, flinch * 6f, 0f);
        }
    }
}
