using UnityEngine;

/// <summary>
/// 主角的火把：一件特殊装备，不占武器槽。
///
/// 不用的时候别在左腰上，按 Tab 抽出来举在左手，再按 Tab 收回腰间。
/// 抽出 / 收回是一段 0.35 秒的抬手动作，不是啪地切换；
/// 第一人称在画面左下角举着走，第三人称交给身体的左臂（见 PlayerShadowBody）。
/// 火把本身带一盏点光源和一层燃烧声，举着走路会跟着步幅轻晃。
/// </summary>
[DisallowMultipleComponent]
public class TorchSystem : MonoBehaviour
{
    [SerializeField] private float drawDuration = 0.35f;

    private static readonly Vector3 ViewRest = new Vector3(-0.4f, -0.72f, 0.9f);
    // 火把整体往上提：手抓在 3/5 处，柄尾要露在拳头下面，不能再贴着画面下缘
    private static readonly Vector3 ViewHeld = new Vector3(-0.44f, -0.2f, 0.82f);

    private static readonly Quaternion RotRest = Quaternion.Euler(32f, 12f, -14f);
    private static readonly Quaternion RotHeld = Quaternion.Euler(4f, -6f, -5f);

    private PlayerShadowBody body;
    private PlayerMovement movement;
    private WeaponController weapon;

    private Transform viewModel;
    private Transform flameView;
    private Vector3 flameBaseScale = Vector3.one;
    private AudioSource fireSource;

    private float raise;
    private bool lit;

    /// <summary>火把是否在手里点着。</summary>
    public bool IsLit => lit;

    /// <summary>抽出进度：0 别在腰间，1 举在手里。身体摆姿势时读这个值。</summary>
    public float HandBlend => raise;

    public void Initialize(PlayerShadowBody bodyRef, PlayerMovement movementRef,
                           WeaponController weaponRef, Transform viewModelRef, AudioClip fireClip)
    {
        body = bodyRef;
        movement = movementRef;
        weapon = weaponRef;
        viewModel = viewModelRef;

        if (viewModel != null)
        {
            viewModel.localScale = Vector3.one * 0.8f;
            flameView = viewModel.Find("Flame");
            if (flameView != null) flameBaseScale = flameView.localScale;
            viewModel.gameObject.SetActive(false);
        }

        if (fireClip != null)
        {
            fireSource = gameObject.AddComponent<AudioSource>();
            fireSource.clip = fireClip;
            fireSource.loop = true;
            fireSource.playOnAwake = false;
            fireSource.spatialBlend = 0f;
            fireSource.volume = 0f;
        }
    }

    /// <summary>标题、暂停、结算时把火把收起来，别带着一团火进菜单。</summary>
    public void SetActiveControl(bool value)
    {
        if (!value)
        {
            SetLit(false);
            raise = 0f;
            if (fireSource != null && fireSource.isPlaying) fireSource.Stop();
            if (viewModel != null) viewModel.gameObject.SetActive(false);
        }
        enabled = value;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            SetLit(!lit);
        }

        raise = Mathf.MoveTowards(raise, lit ? 1f : 0f,
            dt / Mathf.Max(0.05f, drawDuration));

        // 第三人称时身体自己举火把，第一人称的火把模型收起来，免得两支火把同框
        bool thirdPerson = body != null && body.IsVisible;
        if (viewModel != null)
        {
            bool show = !thirdPerson && raise > 0.001f;
            if (viewModel.gameObject.activeSelf != show) viewModel.gameObject.SetActive(show);
            if (show) UpdateViewModel();
        }

        UpdateFireSound(dt);
    }

    private void UpdateViewModel()
    {
        float t = raise * raise * (3f - 2f * raise);

        float speed = movement != null ? movement.PlanarSpeed : 0f;
        float walk = Mathf.Clamp01(speed / 3.2f);
        float phase = Time.time * (4.2f + speed * 0.8f);
        float breath = Mathf.Sin(Time.time * 1.3f);

        // 抽出时从画面下缘抬起来，走路跟着步子上下左右晃，站着只有呼吸
        Vector3 position = Vector3.Lerp(ViewRest, ViewHeld, t);
        position += new Vector3(
            0.013f * Mathf.Sin(phase) * walk,
            -0.010f * Mathf.Abs(Mathf.Sin(phase)) * walk + 0.0035f * breath * (1f - walk),
            0.006f * Mathf.Cos(phase) * walk);

        viewModel.localPosition = position;
        viewModel.localRotation = Quaternion.Slerp(RotRest, RotHeld, t)
            * Quaternion.Euler(1.1f * Mathf.Sin(phase) * walk,
                               1.8f * Mathf.Sin(phase * 0.5f) * walk,
                               1.2f * Mathf.Cos(phase) * walk);

        if (flameView != null)
        {
            float noise = Mathf.PerlinNoise(Time.time * 8.5f, 0.37f);
            flameView.localScale = flameBaseScale * (0.86f + noise * 0.3f);
        }
    }

    private void UpdateFireSound(float dt)
    {
        if (fireSource == null) return;

        bool thirdPerson = body != null && body.IsVisible;
        float target = raise * (thirdPerson ? 0.4f : 0.55f);
        fireSource.volume = Mathf.MoveTowards(fireSource.volume, target, dt * 1.8f);

        if (raise <= 0.001f)
        {
            if (fireSource.isPlaying) fireSource.Stop();
        }
        else if (!fireSource.isPlaying)
        {
            fireSource.Play();
        }
    }

    public void SetLit(bool value)
    {
        if (lit == value) return;

        lit = value;
        if (weapon != null) weapon.SetTorchHand(value);
    }

    /// <summary>开发截图用：不播抬手动作，直接把火把摆到举着 / 挂着的样子。</summary>
    public void SetLitForCapture(bool value)
    {
        SetLit(value);
        raise = value ? 1f : 0f;
        if (viewModel != null)
        {
            viewModel.gameObject.SetActive(value && (body == null || !body.IsVisible));
            if (value) UpdateViewModel();
        }
    }
}
