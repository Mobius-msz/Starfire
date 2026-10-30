using UnityEngine;

/// <summary>
/// 武器槽管理：汉阳造（主武器）与大刀（副武器）。
///
/// - 鼠标滚轮切换武器，数字键 1 / 2 也可以直接选择。
/// - 步枪装备时按住鼠标右键进入机瞄：视野拉近、散布收紧、移速与视角灵敏度降低。
/// - 大刀装备时鼠标左键挥砍，不需要弹药。
/// - 只有当前武器会显示模型并接收输入，避免两把武器同时响应左键。
/// </summary>
[DisallowMultipleComponent]
public class WeaponController : MonoBehaviour
{
    public enum WeaponSlot
    {
        Rifle,
        Pistol,
        Knife
    }

    [SerializeField] private float aimFieldOfView = 40f;
    [SerializeField] private float pistolAimFieldOfView = 50f;
    [SerializeField] private float aimTransitionSpeed = 7f;
    [SerializeField] private float aimSensitivityMultiplier = 0.52f;
    [SerializeField] private float aimMovementMultiplier = 0.5f;
    [SerializeField] private float aimSpreadMultiplier = 0.06f;
    [SerializeField] private float switchCooldown = 0.28f;

    private Camera viewCamera;
    private Hanyang88Rifle rifle;
    private Pistol pistol;
    private PlayerMelee melee;
    private PlayerMovement movement;
    private MouseLook mouseLook;

    private Transform rifleView;
    private Transform pistolView;
    private Transform knifeView;
    private Renderer[] stockRenderers;
    private Transform handsView;
    private Transform boltBody;
    private Vector3 boltBodyHome;
    private Transform shellCase;
    private Vector3 shellHome;
    private Transform rightArm;
    private Transform rightHandPivot;
    private Vector3 rightArmHome;
    private Transform rightArmSwing;
    private Transform leftArmSwing;
    private Transform boltHand;
    private Transform pistolMagazine;
    private Vector3 pistolMagazineHome;
    private bool shellEjected;
    private float boltLift;
    private float boltPull;
    private float reloadPose;
    private float fireKick;
    private bool stockHidden;

    private Vector3 rifleHipPosition;
    private Quaternion rifleHipRotation;
    // 机瞄位：三点（照门两肩 + 准星珠）正好落在画面中心；枪模缩到 0.55，
    // 枪身压在准星下方，目标上方留出观察余量，不会只剩一个机匣挡住视线
    private static readonly Vector3 RifleAimPosition = new Vector3(0f, -0.048f, 0.36f);
    private static readonly Quaternion RifleAimRotation = Quaternion.identity;
    private const float AimViewScale = 0.55f;

    // 冲刺收枪：枪口压向地面，枪身往怀里带，手肘挡住下半屏
    private static readonly Vector3 RifleSprintOffset = new Vector3(0.07f, -0.2f, -0.01f);
    private static readonly Quaternion RifleSprintRotation = Quaternion.Euler(44f, 15f, -13f);

    // 手枪：腰射端在右下方；机瞄时把照门缺口抬到视线上，准星、照门、眼睛三点一线。
    // 枪模根节点缩放 0.85，照门中心在枪模本地 (±, 0.073, 0.075)，
    // 所以机瞄位的 y 要压到 -0.073 × 0.85，枪身才真正遮在准星下面而不是挡住目标。
    private static readonly Vector3 PistolHipPosition = new Vector3(0.19f, -0.17f, 0.62f);
    private static readonly Quaternion PistolHipRotation = Quaternion.Euler(-6f, -2f, 0f);
    private static readonly Vector3 PistolAimPosition = new Vector3(0f, -0.062f, 0.4f);
    private static readonly Quaternion PistolAimRotation = Quaternion.identity;
    private const float PistolAimSpreadMultiplier = 0.16f;

    // 拉栓：拉柄从压下 22° 转到抬起 48°（共 70°），整组枪机沿枪膛轴线后退 9 厘米
    private const float BoltLockedAngle = -22f;
    private const float BoltOpenAngle = 48f;
    private const float BoltTravel = 0.09f;

    // 拉栓时右手要够到的点：腕子在拉柄球头右后方的落点，拉柄球头在枪机上的位置
    private static readonly Vector3 BoltKnobLocal = new Vector3(0.098f, -0.002f, 0.005f);
    private static readonly Vector3 BoltWristLocal = new Vector3(0.058f, -0.024f, -0.046f);

    private bool aimOverride;
    private bool sprintOverride;
    private float baseFieldOfView = 64f;
    private float aimBlend;
    private float sprintBlend;
    private float aimTime;
    private float switchTimer;
    private float hitKick;

    // 左手举火把时，枪模收起左臂、整体往右让一点
    private readonly System.Collections.Generic.List<GameObject> leftHandParts =
        new System.Collections.Generic.List<GameObject>();
    private bool torchDrawn;
    private float torchBlend;

    // 掏手榴弹时整套枪模先收起来，投完再回到原来的武器槽
    private bool grenadePose;

    /// <summary>中弹时把枪口带偏一下，配合镜头震动让"挨了一枪"有分量。</summary>
    public void AddHitKick(float amount)
    {
        hitKick = Mathf.Clamp(hitKick + Mathf.Clamp01(amount), 0f, 1f);
    }

    public WeaponSlot Current { get; private set; } = WeaponSlot.Rifle;
    public bool IsRifle => Current == WeaponSlot.Rifle;
    public bool IsPistol => Current == WeaponSlot.Pistol;
    public bool IsKnife => Current == WeaponSlot.Knife;
    public bool IsAiming { get; private set; }

    /// <summary>拉柄抬起量：0 压在下位，1 抬到最高。第三人称右臂跟着抬。</summary>
    public float BoltLift => boltLift;

    /// <summary>枪机后拉量：0 闭锁，1 拉到最后。第三人称人跟着往后收手。</summary>
    public float BoltPull => boltPull;

    /// <summary>换刀时脚步更轻快：刀具不占双手，移速 ×1.15。</summary>
    public const float KnifeSpeedMultiplier = 1.15f;

    public string CurrentName
    {
        get
        {
            if (IsRifle) return "汉阳造 · 栓动步枪";
            return IsPistol ? "驳壳枪 · 副武器" : "大刀 · 近战";
        }
    }

    public string ShortName => IsRifle ? "汉阳造" : (IsPistol ? "驳壳枪" : "大刀");

    public void Initialize(Camera cameraRef, Hanyang88Rifle rifleRef, Pistol pistolRef,
                           PlayerMelee meleeRef, PlayerMovement movementRef, MouseLook lookRef,
                           Transform rifleViewRef, Transform pistolViewRef, Transform knifeViewRef)
    {
        viewCamera = cameraRef;
        rifle = rifleRef;
        pistol = pistolRef;
        melee = meleeRef;
        movement = movementRef;
        mouseLook = lookRef;
        rifleView = rifleViewRef;
        pistolView = pistolViewRef;
        knifeView = knifeViewRef;

        if (viewCamera != null) baseFieldOfView = viewCamera.fieldOfView;

        if (rifleView != null)
        {
            rifleHipPosition = rifleView.localPosition;
            rifleHipRotation = rifleView.localRotation;

            Transform stock = rifleView.Find("Stock");
            Transform butt = rifleView.Find("ButtPlate");
            Transform wrist = rifleView.Find("Wrist");
            handsView = rifleView.Find("Hands");
            boltBody = rifleView.Find("BoltBody");
            if (boltBody != null)
            {
                boltBodyHome = boltBody.localPosition;
                boltBody.localRotation = Quaternion.Euler(0f, 0f, BoltLockedAngle);
            }
            shellCase = rifleView.Find("ShellCase");
            if (shellCase != null) shellHome = shellCase.localPosition;
            stockRenderers = new Renderer[]
            {
                stock != null ? stock.GetComponent<Renderer>() : null,
                butt != null ? butt.GetComponent<Renderer>() : null,
                wrist != null ? wrist.GetComponent<Renderer>() : null
            };

            // 左手 / 左小臂单独记下来：举火把时要把它们收起来，画面里只剩一只右手握枪
            if (handsView != null)
            {
                foreach (Transform child in handsView)
                {
                    // "ArmL" 前缀也覆盖左小臂的腕子枢轴 ArmLSwing，整条小臂挂在它下面
                    if (child.name.StartsWith("HandL_") || child.name.StartsWith("ArmL"))
                    {
                        leftHandParts.Add(child.gameObject);
                    }
                }
            }

            // 右手连小臂挂在手腕上，拉栓时整条手臂跟着去够拉柄
            rightArm = handsView != null ? handsView.Find("RightArm") : null;
            rightHandPivot = rightArm != null ? rightArm.Find("HandR") : null;
            if (rightArm != null) rightArmHome = rightArm.localPosition;
            // 两条小臂的腕子枢轴：机瞄时往下转，让袖子从画面底缘滑出去
            rightArmSwing = rightArm != null ? rightArm.Find("ArmRSwing") : null;
            leftArmSwing = handsView != null ? handsView.Find("ArmLSwing") : null;
            boltHand = rifleView.Find("BoltHand");
        }

        if (pistolView != null)
        {
            pistolMagazine = pistolView.Find("PistolMagazine");
            if (pistolMagazine != null) pistolMagazineHome = pistolMagazine.localPosition;
        }

        if (rifle != null) rifle.OnFired += HandleRifleFired;
        if (pistol != null) pistol.OnFired += HandlePistolFired;

        ApplySlot(WeaponSlot.Rifle);
    }

    /// <summary>击发瞬间：枪模往怀里一坐，镜头被顶起来一点。</summary>
    private void HandleRifleFired()
    {
        fireKick = 1f;
        if (mouseLook != null) mouseLook.AddRecoil(IsAiming ? 1.1f : 1.7f);
    }

    /// <summary>手枪后坐更碎：连打时镜头被一下一下往上顶。</summary>
    private void HandlePistolFired()
    {
        fireKick = 0.55f;
        if (mouseLook != null) mouseLook.AddRecoil(0.75f);
    }

    /// <summary>左手去举火把了：画面里把步枪的左臂收掉，枪身往右让一点。</summary>
    public void SetTorchHand(bool drawn)
    {
        torchDrawn = drawn;
    }

    /// <summary>枪模被投弹动作 / 结算演出收起来了，镜头脚本不要再按武器槽把它放回来。</summary>
    public bool ViewModelHidden { get; private set; }

    /// <summary>
    /// 投弹姿态：枪（包括火把、手枪、大刀）全部收进画面外，
    /// 由 GrenadeSystem 的第一人称手模接管；投完恢复当前武器。
    /// </summary>
    public void SetGrenadePose(bool value)
    {
        grenadePose = value;

        if (!value)
        {
            ApplySlot(Current);
            return;
        }

        IsAiming = false;
        aimBlend = 0f;
        sprintBlend = 0f;
        aimTime = 0f;

        if (viewCamera != null) viewCamera.fieldOfView = baseFieldOfView;
        if (mouseLook != null)
        {
            mouseLook.SensitivityMultiplier = 1f;
            mouseLook.AimRoll = 0f;
        }
        if (movement != null)
        {
            movement.AimSpeedMultiplier = 1f;
            movement.AimOffset = Vector3.zero;
        }
        if (rifleView != null) rifleView.gameObject.SetActive(false);
        if (pistolView != null) pistolView.gameObject.SetActive(false);
        if (knifeView != null) knifeView.gameObject.SetActive(false);
        ViewModelHidden = true;
        if (rifle != null) rifle.enabled = false;
        if (pistol != null) pistol.enabled = false;
        if (melee != null) melee.enabled = false;
    }

    private void Update()
    {
        if (grenadePose) return;

        switchTimer -= Time.deltaTime;
        hitKick = Mathf.MoveTowards(hitKick, 0f, 2.8f * Time.deltaTime);

        torchBlend = Mathf.MoveTowards(torchBlend, torchDrawn ? 1f : 0f, 6f * Time.deltaTime);
        if (leftHandParts.Count > 0)
        {
            bool showLeft = torchBlend < 0.02f;
            for (int i = 0; i < leftHandParts.Count; i++)
            {
                if (leftHandParts[i].activeSelf != showLeft)
                {
                    leftHandParts[i].SetActive(showLeft);
                }
            }
        }

        if (switchTimer <= 0f)
        {
            float scroll = Input.mouseScrollDelta.y;

            if (Mathf.Abs(scroll) > 0.01f)
            {
                Select(scroll > 0f ? NextSlot() : PreviousSlot());
            }
            else if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Select(WeaponSlot.Rifle);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Select(WeaponSlot.Pistol);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Select(WeaponSlot.Knife);
            }
        }

        UpdateAim();
        UpdatePistolPose();
    }

    private WeaponSlot NextSlot()
    {
        if (Current == WeaponSlot.Rifle) return WeaponSlot.Pistol;
        return Current == WeaponSlot.Pistol ? WeaponSlot.Knife : WeaponSlot.Rifle;
    }

    private WeaponSlot PreviousSlot()
    {
        if (Current == WeaponSlot.Rifle) return WeaponSlot.Knife;
        return Current == WeaponSlot.Pistol ? WeaponSlot.Rifle : WeaponSlot.Pistol;
    }

    /// <summary>
    /// 手枪姿态：腰射端在右下方，按住右键把照门举到眼前。
    /// 换弹时整把枪沉下去、弹匣从握把前方掉出再推回新的。
    /// </summary>
    private void UpdatePistolPose()
    {
        if (pistolView == null || !IsPistol) return;

        float ready = aimBlend * aimBlend * (3f - 2f * aimBlend);
        float reload = pistol != null && pistol.IsReloading ? pistol.ReloadProgress : 0f;

        float bob = movement != null && movement.PlanarSpeed > 0.4f
            ? Mathf.Sin(Time.time * (movement.IsSprinting ? 11f : 7f)) : 0f;
        float walk = movement != null ? Mathf.Clamp01(movement.PlanarSpeed / 3.2f) : 0f;
        float breath = Mathf.Sin(Time.time * 1.3f);

        Vector3 hipPos = PistolHipPosition + new Vector3(
            0.006f * bob,
            0.008f * Mathf.Abs(bob) + 0.0022f * breath * (1f - walk),
            0f);
        Quaternion hipRot = PistolHipRotation * Quaternion.Euler(1.4f * bob, 0f, 0.9f * bob);

        // 举枪：过渡中先往前送一点，压到眼前时再稳住
        float raise = Mathf.Sin(Mathf.PI * aimBlend) * (1f - ready * 0.35f);
        Vector3 aimPos = PistolAimPosition + new Vector3(0f, 0.014f * raise, -0.018f * raise);

        Vector3 position = Vector3.Lerp(hipPos, aimPos, ready);
        Quaternion rotation = Quaternion.Slerp(hipRot, PistolAimRotation, ready);

        // 机瞄稳住后只剩呼吸带来的细微起伏，腰射时随脚步晃得更明显
        float carry = (1f - ready) * walk;
        position += new Vector3(
            0.009f * Mathf.Sin(Time.time * 7f) * carry,
            -0.007f * Mathf.Abs(bob) * carry,
            0f);
        rotation *= Quaternion.Euler(0f, 0f, 1.6f * bob * carry);

        // 换弹：枪口压低、枪身往左转，弹匣从握把前面落下去，再推上新匣
        if (reload > 0.001f)
        {
            float dip = Mathf.Sin(reload * Mathf.PI);
            position += new Vector3(-0.035f, -0.095f, -0.06f) * dip;
            rotation *= Quaternion.Euler(15f * dip, -10f * dip, -14f * dip);

            float outPhase = Mathf.InverseLerp(0.16f, 0.42f, reload)
                             * (1f - Mathf.InverseLerp(0.5f, 0.72f, reload));
            if (pistolMagazine != null)
            {
                pistolMagazine.localPosition =
                    pistolMagazineHome + Vector3.down * (0.14f * outPhase);
            }
        }
        else if (pistolMagazine != null)
        {
            pistolMagazine.localPosition = pistolMagazineHome;
        }

        // 开火后坐：枪口上跳、整把枪往后退一点
        float kick = fireKick;
        position += new Vector3(0f, 0.011f * kick, -0.045f * kick);
        rotation *= Quaternion.Euler(9f * kick, 0f, -1.5f * kick);

        // 中弹：枪被带偏，和镜头震动对上
        if (hitKick > 0.001f)
        {
            position += new Vector3(Mathf.Sin(Time.time * 70f) * 0.01f, -0.025f, -0.05f) * hitKick;
            rotation *= Quaternion.Euler(-7f * hitKick, 3f * hitKick, 5f * hitKick);
        }

        pistolView.localPosition = position;
        pistolView.localRotation = rotation;
    }

    private void UpdateAim()
    {
        // 换弹时枪已经放低了，右键压不出机瞄
        bool pistolBusy = IsPistol && pistol != null && pistol.IsReloading;
        IsAiming = (IsRifle || IsPistol) && !pistolBusy
                   && (aimOverride || Input.GetMouseButton(1));

        fireKick = Mathf.MoveTowards(fireKick, 0f, 6f * Time.deltaTime);

        aimBlend = Mathf.MoveTowards(
            aimBlend, IsAiming ? 1f : 0f, aimTransitionSpeed * Time.deltaTime);

        // 平滑曲线，进出机瞄都不会突兀
        float t = aimBlend * aimBlend * (3f - 2f * aimBlend);

        // 冲刺时端枪跑太累赘：枪口压低收在身前；一按右键立刻重新举枪
        bool sprinting = (sprintOverride || (movement != null && movement.IsSprinting))
                         && !IsAiming && IsRifle;
        sprintBlend = Mathf.MoveTowards(sprintBlend, sprinting ? 1f : 0f,
            7f * Time.deltaTime);

        if (viewCamera != null)
        {
            float aimFov = IsRifle ? aimFieldOfView : pistolAimFieldOfView;
            float fov = Mathf.Lerp(baseFieldOfView, aimFov, t);
            fov += 3.5f * sprintBlend;
            viewCamera.fieldOfView = fov;
        }

        if (mouseLook != null)
        {
            mouseLook.SensitivityMultiplier = Mathf.Lerp(1f, aimSensitivityMultiplier, t);
            // 头微微偏右，贴向枪托；手枪举在正中，不需要偏头
            mouseLook.AimRoll = -1.4f * t * (IsRifle ? 1f : 0f);
        }

        if (movement != null)
        {
            movement.AimSpeedMultiplier = Mathf.Lerp(1f, aimMovementMultiplier, t);
            // 步枪要眼窝下沉去凑照门；手枪是把枪举到眼前，位置几乎不动
            movement.AimOffset = IsRifle
                ? new Vector3(0.008f, -0.006f, 0.022f) * t
                : new Vector3(0f, 0f, 0.012f) * t;
        }

        if (rifle != null)
        {
            rifle.SetSpreadMultiplier(Mathf.Lerp(1f, aimSpreadMultiplier, t));
            // 枪还压着的时候扣扳机不响，避免"枪托朝下还能开枪"
            rifle.SetSprintBlocked(sprintBlend > 0.45f);
        }

        if (pistol != null)
        {
            pistol.SetSpreadMultiplier(Mathf.Lerp(1f, PistolAimSpreadMultiplier, t));
        }

        aimTime = aimBlend > 0.001f ? aimTime + Time.deltaTime : 0f;

        if (rifleView != null && IsRifle)
        {
            // 机瞄时整体缩小枪模，否则 1:1 的枪托会贴到脸上糊满屏幕
            float aimScale = Mathf.Lerp(1f, AimViewScale, t);
            // 枪托收到脸颊后面（照门、准星要露出来），但双手一直握着枪：
            // 机瞄时画面下缘仍能看到右手扣在枪颈、左手托着护木
            rifleView.localScale = new Vector3(aimScale, aimScale, aimScale);
            SetStockHidden(t > 0.55f);
            SetHandsHidden(false);

            // 小臂绕腕子往画面下方转：枪缩到眼前后小臂正对镜头，会把准星糊掉；
            // 转下去以后只剩手掌扣在枪上，袖子贴着画面底缘出画
            Quaternion armSwing = Quaternion.Euler(-30f * t, 0f, 0f);
            float armScale = Mathf.Lerp(1f, 0.85f, t);
            if (rightArmSwing != null)
            {
                rightArmSwing.localRotation = armSwing;
                rightArmSwing.localScale = new Vector3(armScale, armScale, armScale);
            }
            if (leftArmSwing != null)
            {
                leftArmSwing.localRotation = armSwing;
                leftArmSwing.localScale = new Vector3(armScale, armScale, armScale);
            }

            // 蹲下 / 卧倒时枪也压低，姿态变化不只体现在人物上
            Vector3 hip = rifleHipPosition;
            if (movement != null)
            {
                if (movement.CurrentStance == PlayerMovement.Stance.Prone)
                    hip += new Vector3(0.04f, -0.11f, -0.05f);
                else if (movement.CurrentStance == PlayerMovement.Stance.Crouching)
                    hip += new Vector3(0.02f, -0.055f, -0.02f);
            }

            // 站立 → 冲刺收枪 → 机瞄，三段姿态按权重叠加
            Vector3 sprintPos = hip + RifleSprintOffset;
            Quaternion sprintRot = rifleHipRotation * RifleSprintRotation;

            Vector3 targetPos = Vector3.Lerp(
                Vector3.Lerp(hip, sprintPos, sprintBlend), RifleAimPosition, t);
            Quaternion targetRot = Quaternion.Slerp(
                Quaternion.Slerp(rifleHipRotation, sprintRot, sprintBlend),
                RifleAimRotation, t);

            // ── 持枪动作：站着有呼吸，走起来随步子上下、左右晃 ──
            float speed = movement != null ? movement.PlanarSpeed : 0f;
            float walk = Mathf.Clamp01(speed / 3.2f);
            float phase = Time.time * (4.2f + speed * 0.9f);
            float breath = Mathf.Sin(Time.time * 1.35f);
            float carry = (1f - t) * (1f - sprintBlend);

            Vector3 sway = new Vector3(
                0.011f * Mathf.Sin(phase) * walk,
                -0.008f * Mathf.Abs(Mathf.Sin(phase)) * walk
                + 0.003f * breath * (1f - walk),
                0.005f * Mathf.Cos(phase) * walk);
            targetPos += sway * carry;
            targetRot *= Quaternion.Euler(
                0.9f * Mathf.Cos(phase) * walk * carry,
                1.1f * Mathf.Sin(phase) * walk * carry,
                1.3f * Mathf.Sin(phase) * walk * carry);

            // 举枪：过渡中枪口先向上抬一点，再压进照门，像真的抵上肩
            float raise = Mathf.Sin(Mathf.PI * aimBlend) * (1f - t * 0.35f);
            targetPos += new Vector3(0f, 0.022f * raise, -0.012f * raise);
            targetRot *= Quaternion.Euler(4.5f * raise, 0f, -1.2f * raise);

            // 冲刺时枪随步伐上下颠，避免"端着枪滑行"
            if (sprintBlend > 0.01f)
            {
                float bob = Mathf.Sin(Time.time * 11.5f);
                targetPos += new Vector3(0.012f * bob, 0.018f * Mathf.Abs(bob), 0f) * sprintBlend;
                targetRot *= Quaternion.Euler(2.2f * bob, 0f, 1.4f * bob * sprintBlend);
            }

            // 左手举着火把时只剩右手握枪：枪整体往右让开，别和火把叠在一起
            if (torchBlend > 0.001f)
            {
                float handOff = torchBlend * (1f - t);
                targetPos += new Vector3(0.075f, -0.012f, 0f) * handOff;
                targetRot *= Quaternion.Euler(0f, -5f * handOff, 3.5f * handOff);
            }

            rifleView.localPosition = targetPos;
            rifleView.localRotation = targetRot;

            if (aimBlend > 0.01f)
            {
                // 入瞄时枪托往脸颊上一送，再稳住；之后随呼吸轻晃
                float settle = Mathf.Sin(aimTime * 13f) * Mathf.Exp(-aimTime * 5.5f);
                float aimBreath = Mathf.Sin(Time.time * 1.6f);
                rifleView.localPosition += new Vector3(
                    0.003f * aimBreath,
                    -0.006f * Mathf.Abs(settle) + 0.0035f * aimBreath,
                    -0.03f * settle) * aimBlend;
                rifleView.localRotation *= Quaternion.Euler(
                    1.6f * settle, 0f, 0.5f * aimBreath * aimBlend);
            }

            if (hitKick > 0.001f)
            {
                rifleView.localPosition += new Vector3(
                    Mathf.Sin(Time.time * 70f) * 0.012f,
                    -0.035f, -0.085f) * hitKick;
                rifleView.localRotation *= Quaternion.Euler(
                    -9f * hitKick, 4f * hitKick, 6f * hitKick);
            }

            if (fireKick > 0.001f)
            {
                // 后坐：枪身后退、枪口上跳，再落回原来的位置
                rifleView.localPosition += new Vector3(0f, 0.008f, -0.05f) * fireKick;
                rifleView.localRotation *= Quaternion.Euler(-5f * fireKick, 0f, 0f);
            }

            ApplyActionPose();
        }
    }

    /// <summary>
    /// 拉栓与装填的枪模动作。
    ///
    /// 拉栓按 解锁 → 后拉 → 后止点抛壳 → 前推 → 闭锁 五段走：
    /// 拉柄先向上转出机匣缺口，再平直后拉到底，停一下抛出弹壳，
    /// 然后推回膛内，最后把拉柄压下去闭锁。进度读步枪状态，信仰爆发加速时整段跟着变快。
    /// </summary>
    private void ApplyActionPose()
    {
        float reloading = rifle != null && rifle.State == Hanyang88Rifle.WeaponState.Reloading
            ? 1f : 0f;
        reloadPose = Mathf.MoveTowards(reloadPose, reloading, 3.2f * Time.deltaTime);

        bool bolting = rifle != null && rifle.State == Hanyang88Rifle.WeaponState.BoltAction;
        float dip = 0f;
        float handEngage = 0f;
        boltLift = 0f;
        boltPull = 0f;

        if (bolting)
        {
            float progress = rifle.BoltProgress;

            float unlock = Phase(progress, 0f, 0.13f);      // 拉柄抬起
            float pullBack = Phase(progress, 0.13f, 0.40f); // 枪机后拉
            float pushBack = Phase(progress, 0.54f, 0.78f); // 枪机前推
            float lockDown = Phase(progress, 0.80f, 0.94f); // 拉柄压下

            boltLift = unlock * (1f - lockDown);
            boltPull = pullBack * (1f - pushBack);

            // 撞到后止点的那一下：轻微回弹，弹壳就在这时甩出去
            if (progress > 0.40f && progress < 0.56f && boltPull > 0.5f)
            {
                float ring = Mathf.Clamp01((progress - 0.40f) / 0.16f);
                boltPull -= Mathf.Sin(ring * Mathf.PI * 2f) * 0.06f * Mathf.Exp(-ring * 4f);
            }

            if (!shellEjected && progress >= 0.42f)
            {
                shellEjected = true;
                StartCoroutine(EjectShell());
            }

            // 拉栓时枪身往怀里一沉，手才够得着拉柄；推回去再抬起
            dip = Phase(progress, 0f, 0.10f) * (1f - Phase(progress, 0.84f, 1f));

            // 手比拉柄先动一步：抬栓前就离开枪颈去抓球头，闭锁完才回到握枪位
            handEngage = Phase(progress, 0f, 0.06f) * (1f - Phase(progress, 0.90f, 0.99f));
        }
        else
        {
            shellEjected = false;
        }

        if (dip > 0.002f)
        {
            // 拉栓时枪身往左滚一点：机匣顶转向镜头，拉柄连带球头升到机匣上方，
            // 手从右下正好够得着。滚转压到 26°，再大袖子就会横着扫过画面
            rifleView.localPosition += new Vector3(0.012f, -0.03f, -0.015f) * dip;
            rifleView.localRotation *= Quaternion.Euler(4f * dip, -5f * dip, 20f * dip);
        }

        if (reloadPose > 0.002f)
        {
            // 装填时整个枪放低、枪口朝左下，动作比拉栓大得多
            rifleView.localPosition += new Vector3(0.03f, -0.085f, -0.04f) * reloadPose;
            rifleView.localRotation *= Quaternion.Euler(16f * reloadPose, 0f, 9f * reloadPose);
        }

        if (boltBody != null)
        {
            boltBody.localPosition = boltBodyHome + Vector3.back * (BoltTravel * boltPull);
            boltBody.localRotation = Quaternion.Euler(
                0f, 0f, Mathf.Lerp(BoltLockedAngle, BoltOpenAngle, boltLift));
        }

        UpdateBoltHand(handEngage);
    }

    /// <summary>
    /// 拉栓时换手：普通握枪手收起，换成一只扣拳跟到拉柄球头上，
    /// 小臂从右下伸上来接住腕子，手背朝上、四指从近侧兜住球头——人手臂够得到的姿势。
    /// 球头抬到哪、退到哪，拳头就跟到哪，闭锁完再换回握枪手。
    /// </summary>
    private void UpdateBoltHand(float engage)
    {
        if (rightArm == null || boltHand == null) return;

        bool engaged = engage > 0.02f
                       && (handsView == null || handsView.gameObject.activeSelf);

        if (boltHand.gameObject.activeSelf != engaged)
        {
            boltHand.gameObject.SetActive(engaged);
        }
        if (rightHandPivot != null && rightHandPivot.gameObject.activeSelf == engaged)
        {
            rightHandPivot.gameObject.SetActive(!engaged);
        }

        if (!engaged)
        {
            rightArm.localPosition = rightArmHome;
            rightArm.localRotation = Quaternion.identity;
            return;
        }

        // 球头此刻的位置：跟着枪机抬升与后拉一起动
        Vector3 knob = boltBody != null
            ? boltBody.localPosition + boltBody.localRotation * BoltKnobLocal
            : new Vector3(0.098f, 0.028f, 0.06f);

        // 手腕跟着拉柄抬起的角度微微外翻；球头是个球，手在里面转一点就够，扭多了像断腕
        Quaternion handRot = Quaternion.Euler(-4f, -8f, -8f * boltLift * engage);
        Vector3 wristTarget = knob + handRot * BoltWristLocal;

        // 小臂从握枪位平滑挪到球头右后下方：先挪腕子，拳头再跟着腕子走，
        // 中间任何一帧拳头和袖口都连在一起，不会脱节
        rightArm.localPosition = Vector3.Lerp(rightArmHome, wristTarget, engage);
        rightArm.localRotation = Quaternion.Slerp(
            Quaternion.identity, Quaternion.Euler(0f, 0f, 12f), engage);

        boltHand.localRotation = handRot;
        boltHand.localPosition = rightArm.localPosition - handRot * BoltWristLocal;
    }

    /// <summary>把进度切成一段 0→1 的平滑曲线，超出区间就停在两端。</summary>
    private static float Phase(float progress, float from, float to)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, progress));
    }

    /// <summary>后止点抛壳：弹壳从右侧抛壳窗翻出来，划一小段弧线就收掉。</summary>
    private System.Collections.IEnumerator EjectShell()
    {
        if (shellCase == null) yield break;

        Transform shell = shellCase;
        shell.localPosition = shellHome;
        shell.localRotation = Quaternion.Euler(0f, 0f, 90f);
        shell.gameObject.SetActive(true);

        Vector3 position = shellHome;
        Vector3 velocity = new Vector3(0.62f, 0.5f, -0.18f);
        Vector3 spin = new Vector3(300f, 160f, 220f);

        float life = 0.8f;
        while (life > 0f)
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (dt > 0f)
            {
                velocity += new Vector3(0f, -3.6f, 0f) * dt;
                position += velocity * dt;
                shell.localPosition = position;
                shell.localRotation *= Quaternion.Euler(spin * dt);
            }
            yield return null;
        }

        shell.gameObject.SetActive(false);
    }

    /// <summary>机瞄时把枪托和托底板收起来，腾出视野。</summary>
    private void SetStockHidden(bool hidden)
    {
        if (stockHidden == hidden || stockRenderers == null) return;

        stockHidden = hidden;
        foreach (Renderer renderer in stockRenderers)
        {
            if (renderer != null) renderer.enabled = !hidden;
        }
    }

    /// <summary>切枪时整体启停双手；机瞄时不再收起，照门前后仍要看到握枪的手。</summary>
    private void SetHandsHidden(bool hidden)
    {
        if (handsView != null && handsView.gameObject.activeSelf == hidden)
        {
            handsView.gameObject.SetActive(!hidden);
        }
    }

    public void Select(WeaponSlot slot)
    {
        if (slot == Current || switchTimer > 0f) return;

        switchTimer = switchCooldown;
        ApplySlot(slot);
    }

    private void ApplySlot(WeaponSlot slot)
    {
        ViewModelHidden = false;
        Current = slot;
        IsAiming = false;
        aimBlend = 0f;
        aimTime = 0f;
        hitKick = 0f;

        if (viewCamera != null) viewCamera.fieldOfView = baseFieldOfView;

        if (mouseLook != null)
        {
            mouseLook.SensitivityMultiplier = 1f;
            mouseLook.AimRoll = 0f;
        }
        if (movement != null)
        {
            movement.AimSpeedMultiplier = 1f;
            movement.AimOffset = Vector3.zero;
            movement.WeaponSpeedMultiplier =
                slot == WeaponSlot.Knife ? KnifeSpeedMultiplier : 1f;
        }

        if (rifle != null)
        {
            rifle.ResetWeapon();
            rifle.SetSpreadMultiplier(1f);
            rifle.SetSprintBlocked(false);
            rifle.enabled = enabled && slot == WeaponSlot.Rifle;
        }

        if (pistol != null)
        {
            pistol.ResetWeapon();
            pistol.SetSpreadMultiplier(1f);
            pistol.enabled = enabled && slot == WeaponSlot.Pistol;
        }

        if (melee != null)
        {
            melee.ResetMelee();
            melee.enabled = enabled && slot == WeaponSlot.Knife;
        }

        if (rifleView != null)
        {
            rifleView.gameObject.SetActive(slot == WeaponSlot.Rifle);
            rifleView.localPosition = rifleHipPosition;
            rifleView.localRotation = rifleHipRotation;
            rifleView.localScale = Vector3.one;
            SetStockHidden(false);
            SetHandsHidden(false);
            reloadPose = 0f;
            fireKick = 0f;
            boltLift = 0f;
            boltPull = 0f;
            if (boltBody != null)
            {
                boltBody.localPosition = boltBodyHome;
                boltBody.localRotation = Quaternion.Euler(0f, 0f, BoltLockedAngle);
            }
            if (shellCase != null)
            {
                shellCase.localPosition = shellHome;
                shellCase.gameObject.SetActive(false);
            }
            if (rightArm != null)
            {
                rightArm.localPosition = rightArmHome;
                rightArm.localRotation = Quaternion.identity;
            }
            if (rightHandPivot != null)
            {
                rightHandPivot.localRotation = Quaternion.identity;
                rightHandPivot.gameObject.SetActive(true);
            }
            if (boltHand != null) boltHand.gameObject.SetActive(false);
        }

        if (pistolView != null)
        {
            pistolView.gameObject.SetActive(slot == WeaponSlot.Pistol);
            pistolView.localPosition = PistolHipPosition;
            pistolView.localRotation = PistolHipRotation;
            if (pistolMagazine != null) pistolMagazine.localPosition = pistolMagazineHome;
        }

        if (knifeView != null)
        {
            knifeView.gameObject.SetActive(slot == WeaponSlot.Knife);
        }
    }

    /// <summary>标题 / 结算界面时停用武器输入，恢复时回到原武器槽。</summary>
    public void SetActiveControl(bool value)
    {
        enabled = value;

        if (value)
        {
            ApplySlot(Current);
            return;
        }

        if (rifle != null) rifle.enabled = false;
        if (pistol != null) pistol.enabled = false;
        if (melee != null) melee.enabled = false;

        IsAiming = false;
        aimBlend = 0f;
        aimTime = 0f;

        if (viewCamera != null) viewCamera.fieldOfView = baseFieldOfView;
        if (mouseLook != null)
        {
            mouseLook.SensitivityMultiplier = 1f;
            mouseLook.AimRoll = 0f;
        }
        if (movement != null)
        {
            movement.AimSpeedMultiplier = 1f;
            movement.AimOffset = Vector3.zero;
        }
    }

    /// <summary>开发截图用：强制进入机瞄，检查举枪姿势。</summary>
    public void SetAimForCapture(bool value)
    {
        aimOverride = value;
    }

    /// <summary>开发截图用：强制进入冲刺收枪姿势。</summary>
    public void SetSprintForCapture(bool value)
    {
        sprintOverride = value;
    }

    /// <summary>结算演出时把第一人称枪模收起来，别挡住镜头。</summary>
    public void SetViewModelVisible(bool visible)
    {
        if (visible)
        {
            ApplySlot(Current);
            return;
        }

        if (rifleView != null) rifleView.gameObject.SetActive(false);
        if (pistolView != null) pistolView.gameObject.SetActive(false);
        if (knifeView != null) knifeView.gameObject.SetActive(false);
        ViewModelHidden = true;
    }
}
