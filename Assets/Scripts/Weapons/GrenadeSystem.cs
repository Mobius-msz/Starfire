using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 手榴弹：从敌人尸体上捡，按 T（或 4）投掷。
///
/// 投掷物走真实刚体，2.6 秒引信；爆炸范围内按距离衰减伤害，
/// 目标与爆点之间被墙体挡住时伤害减半。不会伤到玩家和战友。
/// </summary>
[DisallowMultipleComponent]
public class GrenadeSystem : MonoBehaviour
{
    public const int MaxCount = 5;

    [SerializeField] private float fuseTime = 2.6f;
    [SerializeField] private float throwSpeed = 15f;
    [SerializeField] private float throwLift = 4.4f;
    [SerializeField] private float throwCooldown = 0.9f;
    [SerializeField] private float blastRadius = 6.5f;
    [SerializeField] private float blastDamage = 150f;

    private Camera viewCamera;
    private AudioSource audioSource;
    private AudioClip explosionClip;
    private AudioClip throwClip;
    private Transform ownerRoot;

    // ── 第一人称投弹动作 ──
    private GrenadeViewParts viewParts;
    private WeaponController weapons;
    private bool throwing;
    private static AudioClip fuseClipCache;

    private struct GrenadeViewParts
    {
        public Transform Root;
        public Transform RightArm;
        public Transform Grenade;
        public Transform LeftArm;
        public Transform PullCord;
        public GameObject PulledCord;
        public Renderer Spark;
        public Light SparkLight;
    }

    private static AudioClip FuseClip
    {
        get
        {
            if (fuseClipCache == null) fuseClipCache = ProceduralAudio.TorchFire();
            return fuseClipCache;
        }
    }

    /// <summary>绑定第一人称手模（GameBootstrap 建立）与武器控制器。</summary>
    public void BindGrenadeView(Transform view, WeaponController weaponController)
    {
        weapons = weaponController;

        if (view == null) return;

        GrenadeViewParts parts = new GrenadeViewParts();
        parts.Root = view;
        parts.RightArm = view.Find("RightArm");
        parts.Grenade = view.Find("RightArm/Grenade");
        parts.LeftArm = view.Find("LeftArm");
        parts.PullCord = view.Find("RightArm/Grenade/PullCord");

        Transform pulled = view.Find("LeftArm/PulledCord");
        if (pulled != null) parts.PulledCord = pulled.gameObject;

        Transform spark = view.Find("RightArm/Grenade/FuseSpark");
        if (spark != null)
        {
            parts.Spark = spark.GetComponent<Renderer>();
            parts.SparkLight = spark.GetComponentInChildren<Light>(true);
        }

        viewParts = parts;
        ResetViewPose();
        view.gameObject.SetActive(false);
    }

    private int count;
    private float cooldown;

    public int Count => count;
    public int Capacity => MaxCount;
    public event System.Action OnChanged;

    public void Initialize(Camera cam, AudioSource src, AudioClip explosion, AudioClip throwSfx,
                           Transform owner)
    {
        viewCamera = cam;
        audioSource = src;
        explosionClip = explosion;
        throwClip = throwSfx;
        ownerRoot = owner != null ? owner : transform;
    }

    public bool Add(int amount)
    {
        if (count >= MaxCount) return false;

        count = Mathf.Min(MaxCount, count + Mathf.Max(1, amount));
        if (OnChanged != null) OnChanged();
        return true;
    }

    private void Update()
    {
        cooldown -= Time.deltaTime;

        // G 键留给断后炸桥，这里用 T / 4
        if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.Alpha4))
        {
            Throw();
        }
    }

    /// <summary>投出一枚手榴弹；没有存货或正在冷却时返回 false。</summary>
    public bool Throw()
    {
        if (count <= 0 || cooldown > 0f || throwing || viewCamera == null) return false;

        count--;
        cooldown = Mathf.Max(throwCooldown, 1.4f);
        throwing = true;
        if (OnChanged != null) OnChanged();

        StartCoroutine(ThrowRoutine());
        return true;
    }

    /// <summary>
    /// 第一人称投弹：抬手 → 左手拉火点燃 → 引信火星 → 后摆 → 掷出。
    /// 手模只是演出，真正的投掷物在掷出那一拍才生成。
    /// </summary>
    private IEnumerator ThrowRoutine()
    {
        if (weapons != null) weapons.SetGrenadePose(true);

        Transform view = viewParts.Root;
        if (view != null)
        {
            ResetViewPose();
            view.gameObject.SetActive(true);
        }

        Vector3 armStart = new Vector3(0.28f, -0.66f, 0.56f);
        Vector3 armUp = new Vector3(0.16f, -0.38f, 0.56f);
        Vector3 armWind = new Vector3(0.24f, -0.52f, 0.44f);
        Vector3 armEnd = new Vector3(0.08f, -0.16f, 0.74f);
        Quaternion rotStart = Quaternion.identity;
        Quaternion rotUp = Quaternion.Euler(-6f, 18f, -6f);
        Quaternion rotWind = Quaternion.Euler(14f, 22f, 8f);
        Quaternion rotEnd = Quaternion.Euler(-40f, 8f, -12f);

        Vector3 leftStart = new Vector3(-0.44f, -0.66f, 0.44f);
        Vector3 leftGrab = new Vector3(0.10f, -0.175f, 0.59f);
        Vector3 leftPull = new Vector3(-0.14f, -0.3f, 0.5f);

        bool ignited = false;
        bool spawned = false;
        float t = 0f;

        while (t < 1.25f)
        {
            t += Time.deltaTime;

            if (viewParts.RightArm != null)
            {
                Vector3 pos;
                Quaternion rot;

                if (t < 0.3f)
                {
                    float k = Smooth(t / 0.3f);
                    pos = Vector3.Lerp(armStart, armUp, k);
                    rot = Quaternion.Slerp(rotStart, rotUp, k);
                }
                else if (t < 0.55f)
                {
                    pos = armUp;
                    rot = rotUp;
                }
                else if (t < 0.8f)
                {
                    float k = Smooth((t - 0.55f) / 0.25f);
                    pos = Vector3.Lerp(armUp, armWind, k);
                    rot = Quaternion.Slerp(rotUp, rotWind, k);
                }
                else if (t < 0.95f)
                {
                    float k = Smooth((t - 0.8f) / 0.15f);
                    pos = Vector3.Lerp(armWind, armEnd, k);
                    rot = Quaternion.Slerp(rotWind, rotEnd, k);
                }
                else
                {
                    pos = armEnd;
                    rot = rotEnd;
                }

                viewParts.RightArm.localPosition = pos;
                viewParts.RightArm.localRotation = rot;
            }

            if (viewParts.LeftArm != null)
            {
                if (t < 0.24f)
                {
                    float k = Smooth(t / 0.24f);
                    viewParts.LeftArm.localPosition = Vector3.Lerp(leftStart, leftGrab, k);
                }
                else if (t < 0.44f)
                {
                    float k = Smooth((t - 0.24f) / 0.2f);
                    viewParts.LeftArm.localPosition = Vector3.Lerp(leftGrab, leftPull, k);
                }
                else
                {
                    float k = Smooth(Mathf.Clamp01((t - 0.44f) / 0.32f));
                    viewParts.LeftArm.localPosition = Vector3.Lerp(leftPull, leftStart, k);
                }
            }

            if (!ignited && t >= 0.4f)
            {
                ignited = true;
                IgniteInHand();
            }

            if (viewParts.SparkLight != null && ignited)
            {
                viewParts.SparkLight.intensity = 0.9f + Random.value * 1.6f;
            }

            if (!spawned && t >= 0.9f)
            {
                spawned = true;
                if (viewParts.Grenade != null) viewParts.Grenade.gameObject.SetActive(false);
                SpawnThrown();
            }

            yield return null;
        }

        if (!spawned) SpawnThrown();

        if (view != null) view.gameObject.SetActive(false);
        if (weapons != null) weapons.SetGrenadePose(false);
        throwing = false;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void ResetViewPose()
    {
        if (viewParts.RightArm != null)
        {
            viewParts.RightArm.localPosition = new Vector3(0.28f, -0.66f, 0.56f);
            viewParts.RightArm.localRotation = Quaternion.identity;
        }
        if (viewParts.LeftArm != null)
        {
            viewParts.LeftArm.localPosition = new Vector3(-0.44f, -0.66f, 0.44f);
            viewParts.LeftArm.localRotation = Quaternion.Euler(0f, 0f, -18f);
        }
        if (viewParts.Grenade != null) viewParts.Grenade.gameObject.SetActive(true);
        if (viewParts.PullCord != null) viewParts.PullCord.gameObject.SetActive(true);
        if (viewParts.PulledCord != null) viewParts.PulledCord.SetActive(false);
        if (viewParts.Spark != null) viewParts.Spark.enabled = false;
        if (viewParts.SparkLight != null)
        {
            viewParts.SparkLight.enabled = true;
            viewParts.SparkLight.intensity = 0f;
        }
    }

    /// <summary>拉火：引信头冒火星、擦出几颗火花，手跟着往外一扯。</summary>
    private void IgniteInHand()
    {
        if (viewParts.Spark != null) viewParts.Spark.enabled = true;

        // 拉火绳从弹体上脱开，挂到左手手里——左手这时正好在外扯
        if (viewParts.PullCord != null) viewParts.PullCord.gameObject.SetActive(false);
        if (viewParts.PulledCord != null) viewParts.PulledCord.SetActive(true);

        if (audioSource != null) audioSource.PlayOneShot(FuseClip, 0.7f);

        if (viewParts.Spark == null) return;

        Vector3 origin = viewParts.Spark.transform.position;
        for (int i = 0; i < 7; i++)
        {
            GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spark.name = "IgniteSpark";
            spark.transform.position = origin;
            float s = Random.Range(0.006f, 0.014f);
            spark.transform.localScale = new Vector3(s, s, s);
            Renderer renderer = spark.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = LevelBuilder.MakeMaterial(
                    new Color(1f, 0.85f, 0.42f), new Color(1f, 0.6f, 0.16f));
            }
            Collider c = spark.GetComponent<Collider>();
            if (c != null) Destroy(c);

            Rigidbody rb = spark.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.AddForce(Random.insideUnitSphere * 1.6f, ForceMode.VelocityChange);

            Destroy(spark, 0.35f);
        }
    }

    /// <summary>真正生成投掷物；从镜头前抛出，和手模的掷出时机对齐。</summary>
    private void SpawnThrown()
    {
        Transform cam = viewCamera.transform;
        GameObject grenade = GrenadePickup.CreateModel("ThrownGrenade");
        grenade.transform.position = cam.position + cam.forward * 0.55f
                                    + cam.right * 0.18f - cam.up * 0.12f;
        grenade.transform.rotation = Random.rotation;

        Rigidbody body = grenade.AddComponent<Rigidbody>();
        body.mass = 0.35f;
        body.drag = 0.05f;
        body.angularDrag = 0.08f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SphereCollider collider = grenade.AddComponent<SphereCollider>();
        collider.radius = 0.1f;

        body.AddForce(cam.forward * throwSpeed + Vector3.up * throwLift, ForceMode.VelocityChange);
        body.AddTorque(new Vector3(Random.Range(-6f, 6f), Random.Range(-6f, 6f),
                                   Random.Range(-6f, 6f)), ForceMode.VelocityChange);

        if (audioSource != null && throwClip != null)
        {
            audioSource.PlayOneShot(throwClip, 0.5f);
        }

        AttachFuseFx(grenade);
        StartCoroutine(Fuse(grenade));
    }

    /// <summary>飞出去的手榴弹引信口一直冒火星和细烟，落地滚动时也看得见。</summary>
    private void AttachFuseFx(GameObject grenade)
    {
        GameObject spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        spark.name = "FuseSpark";
        spark.transform.SetParent(grenade.transform, false);
        spark.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        spark.transform.localScale = Vector3.one * 0.05f;

        Renderer renderer = spark.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(1f, 0.8f, 0.36f), new Color(1f, 0.55f, 0.12f));
        }
        Collider c = spark.GetComponent<Collider>();
        if (c != null) Destroy(c);

        GameObject lightGo = new GameObject("FuseLight");
        lightGo.transform.SetParent(spark.transform, false);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.62f, 0.25f);
        light.range = 3.5f;
        light.intensity = 1.4f;
        light.shadows = LightShadows.None;

        StartCoroutine(FuseSmoke(grenade, spark.transform, light));
    }

    private IEnumerator FuseSmoke(GameObject grenade, Transform spark, Light light)
    {
        while (grenade != null)
        {
            light.intensity = 0.9f + Random.value * 1.2f;
            spark.localScale = Vector3.one * Random.Range(0.035f, 0.06f);

            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "FuseSmoke";
            puff.transform.position = spark.position;
            puff.transform.localScale = Vector3.one * 0.07f;
            Renderer renderer = puff.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = LevelBuilder.MakeTransparentMaterial(
                    new Color(0.6f, 0.6f, 0.58f, 0.4f));
            }
            Collider c = puff.GetComponent<Collider>();
            if (c != null) Destroy(c);
            Destroy(puff, 0.9f);

            yield return new WaitForSeconds(0.22f);
        }
    }

    private IEnumerator Fuse(GameObject grenade)
    {
        yield return new WaitForSeconds(fuseTime);

        if (grenade == null) yield break;

        Vector3 position = grenade.transform.position;
        Destroy(grenade);
        Explode(position);
    }

    private void Explode(Vector3 position)
    {
        if (explosionClip != null)
        {
            AudioSource.PlayClipAtPoint(explosionClip, position, 1f);
        }

        SpawnFlash(position);
        SpawnDebris(position);
        DamageAround(position);
    }

    private void DamageAround(Vector3 position)
    {
        Collider[] hits = Physics.OverlapSphere(position, blastRadius, ~0,
                                                QueryTriggerInteraction.Ignore);
        HashSet<Health> damaged = new HashSet<Health>();

        foreach (Collider hit in hits)
        {
            Health health = hit.GetComponentInParent<Health>();
            if (health == null || health.IsDead) continue;
            if (!health.CompareTag("Enemy")) continue;
            if (!damaged.Add(health)) continue;

            Vector3 target = health.transform.position + Vector3.up * 1.0f;
            float distance = Vector3.Distance(position, target);
            if (distance > blastRadius) continue;

            float falloff = Mathf.Clamp01(1f - distance / blastRadius);

            bool blocked = false;
            Vector3 from = position + Vector3.up * 0.25f;
            Vector3 delta = target - from;
            if (Physics.Raycast(from, delta.normalized, out RaycastHit obstacle,
                                delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!obstacle.collider.transform.IsChildOf(health.transform))
                {
                    blocked = true;
                }
            }

            float damage = blastDamage * Mathf.Pow(falloff, 1.4f) * (blocked ? 0.45f : 1f);
            if (damage < 6f) continue;

            health.TakeDamage(damage, target, Vector3.up, BodyZone.Torso,
                              ownerRoot != null ? ownerRoot.gameObject : gameObject);
        }
    }

    private static void SpawnFlash(Vector3 position)
    {
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "GrenadeFlash";
        flash.transform.position = position;
        flash.transform.localScale = Vector3.one * 1.2f;

        Renderer renderer = flash.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(1f, 0.72f, 0.28f), new Color(1f, 0.55f, 0.18f));
        }
        Collider collider = flash.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);

        GameObject lightGo = new GameObject("GrenadeFlashLight");
        lightGo.transform.SetParent(flash.transform, false);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.68f, 0.35f);
        light.intensity = 7f;
        light.range = 16f;

        Object.Destroy(flash, 0.16f);
    }

    private static void SpawnDebris(Vector3 position)
    {
        Color dirt = new Color(0.24f, 0.21f, 0.17f);
        Color smoke = new Color(0.32f, 0.3f, 0.27f);

        for (int i = 0; i < 14; i++)
        {
            GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = "BlastDebris";
            chunk.transform.position = position + Random.insideUnitSphere * 0.5f;
            float s = Random.Range(0.07f, 0.2f);
            chunk.transform.localScale = new Vector3(s, s * 0.7f, s);

            Renderer renderer = chunk.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = LevelBuilder.MakeMaterial(dirt);

            Rigidbody rb = chunk.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.AddExplosionForce(4f, position, 6f, 1.6f);
            rb.AddTorque(Random.insideUnitSphere * 12f, ForceMode.VelocityChange);

            Object.Destroy(chunk, 2.6f);
        }

        for (int i = 0; i < 7; i++)
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "BlastSmoke";
            puff.transform.position = position + Random.insideUnitSphere * 0.9f;
            puff.transform.localScale = Vector3.one * Random.Range(0.5f, 1.1f);

            Renderer renderer = puff.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = LevelBuilder.MakeTransparentMaterial(
                    new Color(smoke.r, smoke.g, smoke.b, 0.55f));
            }
            Collider collider = puff.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            Object.Destroy(puff, Random.Range(0.9f, 1.6f));
        }
    }
}
