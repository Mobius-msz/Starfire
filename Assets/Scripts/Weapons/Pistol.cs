using System;
using UnityEngine;

/// <summary>
/// 驳壳枪（副武器）：射速快、精度差。
///
/// 一个弹匣 6 发，开局带 4 个备用弹夹，按 R 换弹匣；
/// 和汉阳造相反：不用拉栓，按住左键就能连打，
/// 但散布大、单发伤害低，超过二十米基本靠运气。
/// </summary>
[DisallowMultipleComponent]
public class Pistol : MonoBehaviour
{
    [SerializeField] private float fireInterval = 0.145f;
    [SerializeField] private float damage = 17f;
    [SerializeField] private float range = 45f;
    [SerializeField] private int magazineCapacity = 6;
    [SerializeField] private float spread = 4.6f;
    [SerializeField] private float emptyClickCooldown = 0.35f;
    [SerializeField] private float reloadDuration = 1.35f;
    [SerializeField] private bool autoReloadWhenEmpty = true;

    /// <summary>开局的备用弹夹数。</summary>
    public const int StartMagazines = 4;

    private Camera viewCamera;
    private AudioSource audioSource;
    private AudioClip shotClip;
    private AudioClip emptyClip;
    private AudioClip reloadClip;
    private MuzzleFlash muzzleFlash;
    private LineRenderer tracer;

    private float cooldown;
    private float reloadEndTime;
    private float spreadMultiplier = 1f;

    public event Action OnFired;

    public int MagazineCapacity => magazineCapacity;
    public int AmmoInMagazine { get; private set; }
    public int ReserveAmmo { get; private set; }
    public bool IsReloading { get; private set; }

    /// <summary>换弹进度 0→1，枪模换弹动作读它。</summary>
    public float ReloadProgress
    {
        get
        {
            if (!IsReloading) return 0f;
            return Mathf.Clamp01(1f - (reloadEndTime - Time.time) / Mathf.Max(0.01f, reloadDuration));
        }
    }

    public void Initialize(Camera cameraRef, AudioSource source, AudioClip shot, AudioClip empty,
                           AudioClip reload = null)
    {
        viewCamera = cameraRef;
        audioSource = source;
        shotClip = shot;
        emptyClip = empty;
        reloadClip = reload;

        AmmoInMagazine = magazineCapacity;
        ReserveAmmo = magazineCapacity * StartMagazines;

        tracer = gameObject.AddComponent<LineRenderer>();
        tracer.positionCount = 2;
        tracer.startWidth = 0.018f;
        tracer.endWidth = 0.006f;
        tracer.useWorldSpace = true;
        tracer.material = LevelBuilder.MakeMaterial(
            new Color(1f, 0.85f, 0.55f), new Color(1f, 0.6f, 0.2f));
        tracer.enabled = false;
    }

    public void SetMuzzleFlash(MuzzleFlash flash)
    {
        muzzleFlash = flash;
    }

    /// <summary>机瞄时收紧散布。</summary>
    public void SetSpreadMultiplier(float multiplier)
    {
        spreadMultiplier = Mathf.Clamp(multiplier, 0.05f, 2f);
    }

    private void Update()
    {
        if (cooldown > 0f) cooldown -= Time.deltaTime;

        if (IsReloading)
        {
            if (Time.time >= reloadEndTime) FinishReload();
            return;
        }

        if (Input.GetKeyDown(KeyCode.R) && TryReload()) return;
        if (cooldown > 0f) return;

        if (Input.GetMouseButton(0)) TryFire();
    }

    public bool TryReload()
    {
        if (IsReloading) return false;
        if (ReserveAmmo <= 0 || AmmoInMagazine >= magazineCapacity) return false;

        IsReloading = true;
        reloadEndTime = Time.time + reloadDuration;
        cooldown = reloadDuration;

        if (audioSource != null && reloadClip != null)
        {
            audioSource.PlayOneShot(reloadClip, 0.55f);
        }
        return true;
    }

    private void FinishReload()
    {
        IsReloading = false;

        int needed = magazineCapacity - AmmoInMagazine;
        int take = Mathf.Min(needed, ReserveAmmo);
        AmmoInMagazine += take;
        ReserveAmmo -= take;
    }

    /// <summary>捡到弹夹：返回真正补进去的备弹数。</summary>
    public int AddReserveAmmo(int amount)
    {
        if (amount <= 0) return 0;

        int cap = magazineCapacity * (StartMagazines + 4);
        int added = Mathf.Min(amount, cap - ReserveAmmo);
        if (added <= 0) return 0;

        ReserveAmmo += added;
        return added;
    }

    public void ResetWeapon()
    {
        cooldown = 0f;
        IsReloading = false;
        if (tracer != null) tracer.enabled = false;
    }

    private void TryFire()
    {
        if (AmmoInMagazine <= 0)
        {
            cooldown = emptyClickCooldown;
            if (audioSource != null && emptyClip != null)
            {
                audioSource.PlayOneShot(emptyClip, 0.5f);
            }

            if (autoReloadWhenEmpty && ReserveAmmo > 0) TryReload();
            return;
        }

        if (viewCamera == null) return;

        AmmoInMagazine--;
        cooldown = fireInterval;

        Vector3 origin = viewCamera.transform.position;
        Vector3 forward = viewCamera.transform.forward;
        float cone = spread * spreadMultiplier;
        Vector3 direction = Quaternion.Euler(
            UnityEngine.Random.Range(-cone, cone),
            UnityEngine.Random.Range(-cone, cone), 0f) * forward;

        Vector3 end = origin + direction * range;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, range, ~0,
                            QueryTriggerInteraction.Ignore))
        {
            end = hit.point;

            if (!hit.collider.transform.IsChildOf(transform.root))
            {
                HitZone zone = hit.collider.GetComponent<HitZone>();
                if (zone == null) zone = hit.collider.GetComponentInParent<HitZone>();

                Health target = zone != null
                    ? zone.GetComponentInParent<Health>()
                    : hit.collider.GetComponentInParent<Health>();

                if (target != null && !target.IsDead && !target.CompareTag("Ally")
                    && !target.CompareTag("Player"))
                {
                    float multiplier = 1f;
                    if (zone != null)
                    {
                        switch (zone.Zone)
                        {
                            case BodyZone.Head: multiplier = 2f; break;
                            case BodyZone.Torso: multiplier = 1.1f; break;
                            default: multiplier = 0.85f; break;
                        }
                    }

                    target.TakeDamage(damage * multiplier, hit.point, hit.normal,
                        zone != null ? zone.Zone : BodyZone.Torso, transform.root.gameObject);
                }
            }
        }

        if (audioSource != null && shotClip != null)
        {
            audioSource.PlayOneShot(shotClip, 0.6f);
        }
        if (muzzleFlash != null) muzzleFlash.Play(0.8f);

        // 弹道从敌兵身边掠过时，他们会侧闪或缩回掩体
        EnemyAI.NotifyIncomingFire(origin, end);

        if (tracer != null) StartCoroutine(TracerRoutine(origin + forward * 0.4f, end));

        OnFired?.Invoke();

        if (autoReloadWhenEmpty && AmmoInMagazine <= 0 && ReserveAmmo > 0) TryReload();
    }

    private System.Collections.IEnumerator TracerRoutine(Vector3 from, Vector3 to)
    {
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        yield return new WaitForSeconds(0.045f);
        tracer.enabled = false;
    }

    /// <summary>开发截图用：把弹匣压到指定值。</summary>
    public void SetAmmoForCapture(int value)
    {
        AmmoInMagazine = Mathf.Clamp(value, 0, magazineCapacity);
    }
}
