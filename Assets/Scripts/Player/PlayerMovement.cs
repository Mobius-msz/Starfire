using UnityEngine;

/// <summary>
/// 第一人称移动：行走 / 奔跑 / 跳跃 / 蹲下 / 趴下，并消费严寒与信仰的移速倍率。
///
/// 姿态：
///   Space —— 跳跃（站立或蹲下；卧姿不可跳）
///   C —— 站立 ⇄ 蹲下（趴下时按 C 先回到蹲下）
///   Z —— 蹲下 / 站立 ⇄ 趴下
/// 姿态会改变 CharacterController 的高度、摄像机眼高与移动速度。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public enum Stance
    {
        Standing,
        Crouching,
        Prone
    }

    [Header("速度")]
    [SerializeField] private float walkSpeed = 3.2f;
    [SerializeField] private float sprintSpeed = 5.6f;

    [Header("姿态速度倍率")]
    [SerializeField] private float crouchSpeedMultiplier = 0.55f;
    [SerializeField] private float proneSpeedMultiplier = 0.28f;

    [Header("姿态尺寸")]
    [SerializeField] private float crouchHeight = 1.25f;
    [SerializeField] private float crouchEyeHeight = 1.0f;
    [SerializeField] private float proneHeight = 0.75f;
    [SerializeField] private float proneEyeHeight = 0.55f;

    [Tooltip("姿态切换的平滑速度")]
    [SerializeField] private float stanceTransitionSpeed = 9f;

    [Header("按键")]
    [SerializeField] private KeyCode crouchKey = KeyCode.C;
    [SerializeField] private KeyCode proneKey = KeyCode.Z;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;

    [Header("跳跃")]
    [Tooltip("跳跃最高点（米）")]
    [SerializeField] private float jumpHeight = 1.15f;

    [Header("重力")]
    [SerializeField] private float gravity = -20f;

    [Header("体力")]
    [Tooltip("满体力能连续奔跑多少秒")]
    [SerializeField] private float sprintSeconds = 5.5f;
    [Tooltip("力竭后恢复到多少比例才能再次奔跑")]
    [SerializeField] private float sprintUnlockRatio = 0.25f;
    [Tooltip("停下后多久开始回体力")]
    [SerializeField] private float staminaRecoverDelay = 0.8f;

    public Stance CurrentStance { get; private set; } = Stance.Standing;

    /// <summary>平滑后的眼高，第三人称相机用它定位。</summary>
    public float EyeHeight { get; private set; } = 1.6f;

    /// <summary>机瞄时由武器控制器降低，1 = 正常移速。</summary>
    public float AimSpeedMultiplier { get; set; } = 1f;

    /// <summary>机瞄时头部凑向枪托的位移，由武器控制器注入。</summary>
    public Vector3 AimOffset { get; set; }

    /// <summary>当前武器的移速倍率：换刀时更轻快。</summary>
    public float WeaponSpeedMultiplier { get; set; } = 1f;

    /// <summary>水平移动速度（米/秒），第三人称走路动画与枪模晃动读它。</summary>
    public float PlanarSpeed
    {
        get
        {
            if (captureSpeed >= 0f) return captureSpeed;
            if (controller == null) return 0f;
            Vector3 v = controller.velocity;
            return new Vector2(v.x, v.z).magnitude;
        }
    }

    private float captureSpeed = -1f;

    /// <summary>开发截图用：停掉移动后强行给出一个速度，让第三人称走路循环还动。</summary>
    public void SetPlanarSpeedForCapture(float speed)
    {
        captureSpeed = speed;
    }

    /// <summary>体力条容量：比原来的 100 多出 50%，能连着跑更久。</summary>
    public const float MaxStamina = 150f;

    // 消耗 / 恢复仍按原来的 100 计量，所以条变长之后奔跑时长也跟着多 50%
    private const float StaminaUnit = 100f;

    /// <summary>当前体力，HUD 与喘息音量都读它。</summary>
    public float Stamina { get; private set; } = MaxStamina;

    public float StaminaPercent => Mathf.Clamp01(Stamina / MaxStamina) * 100f;

    /// <summary>力竭：必须回一点体力才能再跑。</summary>
    public bool IsExhausted { get; private set; }

    /// <summary>本帧是否真的在冲刺（体力见底会被掐掉）。</summary>
    public bool IsSprinting { get; private set; }

    private CharacterController controller;
    private ColdSystem coldSystem;
    private FaithSystem faithSystem;
    private Transform cameraTransform;
    private Vector3 cameraBaseLocal;

    private float standingHeight;
    private float standingEyeHeight;
    private float currentHeight;
    private float targetHeight;

    private float verticalVelocity;
    private float stepTimer;

    private AudioSource breathSource;
    private float breathVolume;
    private float staminaRecoverTimer;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        coldSystem = GetComponent<ColdSystem>();
        faithSystem = GetComponent<FaithSystem>();

        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cameraTransform = cam.transform;
        if (cameraTransform != null) cameraBaseLocal = cameraTransform.localPosition;

        standingHeight = controller.height;
        standingEyeHeight = cameraTransform != null ? cameraTransform.localPosition.y : 1.6f;
        currentHeight = standingHeight;
        targetHeight = standingHeight;
        EyeHeight = standingEyeHeight;

        // 喘息：体力越低音量越大，喘得越沉
        breathSource = gameObject.AddComponent<AudioSource>();
        breathSource.clip = ProceduralAudio.Breath();
        breathSource.loop = true;
        breathSource.spatialBlend = 0f;
        breathSource.playOnAwake = false;
        breathSource.volume = 0f;
    }

    private void Update()
    {
        HandleStanceInput();
        UpdateStanceGeometry();

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 input = new Vector3(h, 0f, v);
        if (input.sqrMagnitude > 1f) input.Normalize();

        // 基础速度 → 姿态 → 严寒惩罚 → 信仰增益
        bool moving = input.sqrMagnitude > 0.01f;
        bool faithBurst = faithSystem != null && faithSystem.IsBuffActive;

        bool wantSprint = CurrentStance == Stance.Standing
                          && Input.GetKey(KeyCode.LeftShift) && moving;
        UpdateStamina(wantSprint);

        // 信仰爆发期间无视体力：力竭也能接着跑
        bool sprinting = wantSprint && (faithBurst || (!IsExhausted && Stamina > 0f));
        IsSprinting = sprinting;
        float speed = sprinting ? sprintSpeed : walkSpeed;
        speed *= WeaponSpeedMultiplier;

        if (CurrentStance == Stance.Crouching) speed *= crouchSpeedMultiplier;
        else if (CurrentStance == Stance.Prone) speed *= proneSpeedMultiplier;

        if (coldSystem != null) speed *= coldSystem.MovementSpeedMultiplier;
        if (faithSystem != null) speed *= faithSystem.MovementSpeedMultiplier;
        speed *= AimSpeedMultiplier;

        Vector3 move = (transform.right * input.x + transform.forward * input.z) * speed;

        // 跳跃：卧姿先起身才能跳
        if (Input.GetKeyDown(jumpKey) && controller.isGrounded && CurrentStance != Stance.Prone)
        {
            if (CurrentStance == Stance.Crouching) SetStance(Stance.Standing);
            verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
        }

        // 重力
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);

        EmitFootstepNoise(sprinting);
    }

    /// <summary>奔跑消耗体力；停下片刻后回体力，力竭时喘得最凶。</summary>
    private void UpdateStamina(bool wantSprint)
    {
        float drain = StaminaUnit / Mathf.Max(1f, sprintSeconds);
        float recover = StaminaUnit / 6.5f;

        if (wantSprint && !IsExhausted)
        {
            Stamina = Mathf.Max(0f, Stamina - drain * Time.deltaTime);
            staminaRecoverTimer = staminaRecoverDelay;
            if (Stamina <= 0f) IsExhausted = true;
        }
        else
        {
            if (staminaRecoverTimer > 0f) staminaRecoverTimer -= Time.deltaTime;
            else Stamina = Mathf.Min(MaxStamina, Stamina + recover * Time.deltaTime);

            if (IsExhausted && Stamina >= MaxStamina * sprintUnlockRatio) IsExhausted = false;
        }

        UpdateBreathing();
    }

    private void UpdateBreathing()
    {
        // 体力 65% 以下开始能听见喘息，越低头越响
        float tired = Mathf.Clamp01(1f - Stamina / (MaxStamina * 0.65f));
        float target = tired * tired * 0.95f;

        breathVolume = Mathf.MoveTowards(breathVolume, target,
            Time.deltaTime * (target > breathVolume ? 2.2f : 0.75f));

        if (breathSource == null) return;

        if (breathVolume <= 0.01f)
        {
            if (breathSource.isPlaying) breathSource.Stop();
            breathSource.volume = 0f;
            return;
        }

        breathSource.volume = breathVolume;
        breathSource.pitch = Mathf.Lerp(1.25f, 0.9f, tired);
        if (!breathSource.isPlaying) breathSource.Play();
    }

    /// <summary>开发截图用：把体力压到指定值，检查体力条与喘息状态。</summary>
    public void SetStaminaForCapture(float value)
    {
        Stamina = Mathf.Clamp(value, 0f, MaxStamina);
        IsExhausted = Stamina <= 0f;
        staminaRecoverTimer = staminaRecoverDelay;
    }

    /// <summary>脚步会引来附近敌人；雨声把这个半径压下去，蹲行更安静。</summary>
    private void EmitFootstepNoise(bool sprinting)    {
        if (controller == null || !controller.isGrounded || CurrentStance == Stance.Prone)
        {
            stepTimer = 0f;
            return;
        }

        Vector3 velocity = controller.velocity;
        if (new Vector2(velocity.x, velocity.z).magnitude < 0.5f)
        {
            stepTimer = 0f;
            return;
        }

        stepTimer -= Time.deltaTime;
        if (stepTimer > 0f) return;

        float radius = sprinting ? 24f : 12f;
        if (CurrentStance == Stance.Crouching) radius *= 0.4f;
        radius *= DynamicWeather.HearingFactor;

        stepTimer = sprinting ? 0.3f : (CurrentStance == Stance.Crouching ? 0.85f : 0.5f);
        EnemyAI.AlertNearby(transform.position, radius);
    }

    private void HandleStanceInput()
    {
        if (Input.GetKeyDown(crouchKey))
        {
            if (CurrentStance == Stance.Prone) SetStance(Stance.Crouching);
            else if (CurrentStance == Stance.Crouching) SetStance(Stance.Standing);
            else SetStance(Stance.Crouching);
        }

        if (Input.GetKeyDown(proneKey))
        {
            SetStance(CurrentStance == Stance.Prone ? Stance.Crouching : Stance.Prone);
        }
    }

    public void SetStance(Stance stance)
    {
        CurrentStance = stance;

        switch (stance)
        {
            case Stance.Crouching:
                targetHeight = crouchHeight;
                break;
            case Stance.Prone:
                targetHeight = proneHeight;
                break;
            default:
                targetHeight = standingHeight;
                break;
        }
    }

    /// <summary>演出 / 结算用：立刻站直，不受输入和插值影响。</summary>
    public void ForceStanding()
    {
        ForceStance(Stance.Standing);
    }

    /// <summary>截图 / 演出用：不经插值，立刻摆成指定姿态。</summary>
    public void ForceStance(Stance stance)
    {
        CurrentStance = stance;

        float height = standingHeight;
        float eye = standingEyeHeight;
        if (stance == Stance.Crouching)
        {
            height = crouchHeight;
            eye = crouchEyeHeight;
        }
        else if (stance == Stance.Prone)
        {
            height = proneHeight;
            eye = proneEyeHeight;
        }

        targetHeight = height;
        currentHeight = height;
        controller.height = height;
        controller.center = new Vector3(0f, height * 0.5f, 0f);

        EyeHeight = eye;
        if (cameraTransform != null)
        {
            cameraTransform.localPosition = new Vector3(cameraBaseLocal.x, eye, cameraBaseLocal.z);
        }
    }

    private void UpdateStanceGeometry()
    {
        // 蹲下快、卧倒慢、起身略慢：动作有轻重，不是一条直线插值
        float speed = stanceTransitionSpeed;
        if (CurrentStance == Stance.Prone) speed = stanceTransitionSpeed * 0.7f;
        else if (CurrentStance == Stance.Crouching && targetHeight < currentHeight)
            speed = stanceTransitionSpeed * 1.35f;

        if (Mathf.Abs(currentHeight - targetHeight) > 0.001f)
        {
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            currentHeight = Mathf.Lerp(currentHeight, targetHeight, t);

            if (Mathf.Abs(currentHeight - targetHeight) < 0.002f) currentHeight = targetHeight;

            controller.height = currentHeight;
            controller.center = new Vector3(0f, currentHeight * 0.5f, 0f);
        }

        if (cameraTransform == null) return;

        float targetEye = standingEyeHeight;
        if (CurrentStance == Stance.Crouching) targetEye = crouchEyeHeight;
        else if (CurrentStance == Stance.Prone) targetEye = proneEyeHeight;

        float eyeT = 1f - Mathf.Exp(-speed * Time.deltaTime);
        EyeHeight = Mathf.Lerp(EyeHeight, targetEye, eyeT);
        if (Mathf.Abs(EyeHeight - targetEye) < 0.0015f) EyeHeight = targetEye;

        Vector3 localPos = cameraTransform.localPosition;
        localPos.x = cameraBaseLocal.x + AimOffset.x;
        localPos.y = EyeHeight + AimOffset.y;
        localPos.z = cameraBaseLocal.z + AimOffset.z;
        cameraTransform.localPosition = localPos;
    }
}
