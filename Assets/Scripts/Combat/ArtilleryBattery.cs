using System.Collections;
using UnityEngine;

/// <summary>
/// 敌军炮兵阵地：地图远端的四门野炮，按各自节奏向友军阵地（玩家一侧河岸）射击。
///
/// 每门炮都有完整的炮身细节——防盾、分体大架、辐条炮轮、驻锄、弹药箱、沙袋胸墙；
/// 开火时炮口焰、后坐、烟尘一起出现，弹丸沿抛物线越过江面，
/// 落地后交给 ArtillerySystem 结算爆炸伤害。
/// </summary>
public class ArtilleryBattery : MonoBehaviour
{
    private struct GunSlot
    {
        public Vector3 position;
        public float yawOffset;
    }

    // 敌军阵地全部在主地图后端（z 61～68），公路两侧各两门，正对西岸友军防线
    private static readonly GunSlot[] Slots =
    {
        new GunSlot { position = new Vector3(-20f, 0f, 62f), yawOffset = 6f },
        new GunSlot { position = new Vector3(-12f, 0f, 68f), yawOffset = 2f },
        new GunSlot { position = new Vector3(12f, 0f, 67f), yawOffset = -2f },
        new GunSlot { position = new Vector3(20f, 0f, 61f), yawOffset = -6f }
    };

    private struct Gun
    {
        public Transform barrel;
        public Vector3 barrelRest;
        public Transform muzzle;
        public float timer;
        public float recoil;
    }

    private Gun[] guns;
    private Transform player;
    private Transform levelRoot;
    private ArtillerySystem artillery;
    private AudioClip boomClip;
    private AudioClip whistleClip;
    private bool active;

    private static Material shellMaterial;
    private static Material smokeMaterial;

    public void Initialize(Transform playerRef, Transform root, ArtillerySystem artilleryRef,
                           AudioClip boom, AudioClip whistle)
    {
        player = playerRef;
        levelRoot = root;
        artillery = artilleryRef;
        boomClip = boom;
        whistleClip = whistle;

        Material iron = LevelBuilder.MakeMaterial(new Color(0.23f, 0.24f, 0.23f));
        Material ironDark = LevelBuilder.MakeMaterial(new Color(0.13f, 0.14f, 0.13f));
        Material wood = LevelBuilder.MakeMaterial(new Color(0.36f, 0.27f, 0.16f));
        Material woodDark = LevelBuilder.MakeMaterial(new Color(0.23f, 0.17f, 0.1f));
        Material brass = LevelBuilder.MakeMaterial(new Color(0.52f, 0.41f, 0.19f));
        Material bag = LevelBuilder.MakeMaterial(new Color(0.44f, 0.39f, 0.27f));
        Material canvas = LevelBuilder.MakeMaterial(new Color(0.3f, 0.28f, 0.2f));
        Material dirt = LevelBuilder.MakeMaterial(new Color(0.2f, 0.17f, 0.13f));

        guns = new Gun[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
        {
            guns[i] = BuildGun(root, Slots[i], i, iron, ironDark, wood, woodDark,
                               brass, bag, canvas, dirt);
        }
    }

    public void SetActive(bool value)
    {
        active = value;
        if (!value || guns == null) return;

        for (int i = 0; i < guns.Length; i++)
        {
            Gun gun = guns[i];
            gun.timer = Random.Range(7f, 13f) + i * 2.6f;
            guns[i] = gun;
        }
    }

    private void Update()
    {
        if (!active || player == null || guns == null) return;

        float dt = Time.deltaTime;
        for (int i = 0; i < guns.Length; i++)
        {
            Gun gun = guns[i];

            if (gun.recoil > 0f)
            {
                gun.recoil = Mathf.MoveTowards(gun.recoil, 0f, 2.8f * dt);
                gun.barrel.localPosition = gun.barrelRest
                    + new Vector3(0f, 0f, -0.42f * gun.recoil * gun.recoil);
                guns[i] = gun;
            }

            gun.timer -= dt;
            if (gun.timer > 0f)
            {
                guns[i] = gun;
                continue;
            }

            gun.timer = Random.Range(18f, 30f);
            gun.recoil = 1f;
            guns[i] = gun;
            Fire(gun);
        }
    }

    // ───────────────────────────── 射击 ─────────────────────────────

    private void Fire(Gun gun)
    {
        Vector3 muzzle = gun.muzzle.position;

        if (boomClip != null) AudioSource.PlayClipAtPoint(boomClip, muzzle, 0.55f);
        SpawnMuzzleEffects(muzzle);

        Vector3 target = PickTarget();
        if (whistleClip != null) AudioSource.PlayClipAtPoint(whistleClip, target, 0.75f);
        if (artillery != null) artillery.MarkImpact(target);
        StartCoroutine(ShellRoutine(muzzle, target));
    }

    /// <summary>落点只落在西岸友军阵地上，不会砸到浮桥或江面。</summary>
    private Vector3 PickTarget()
    {
        Vector3 point;
        AllyAI[] allies = FindObjectsOfType<AllyAI>();

        if (allies.Length > 0 && Random.value < 0.45f)
        {
            point = allies[Random.Range(0, allies.Length)].transform.position
                + new Vector3(Random.Range(-5.5f, 5.5f), 0f, Random.Range(-5.5f, 5.5f));
        }
        else
        {
            point = new Vector3(Random.Range(-16f, 16f), 0f, Random.Range(0f, 14f));
        }

        point.x = Mathf.Clamp(point.x, -30f, 30f);
        point.z = Mathf.Clamp(point.z, -26f, 13.5f);

        if (Physics.Raycast(point + Vector3.up * 30f, Vector3.down, out RaycastHit hit,
                            60f, ~0, QueryTriggerInteraction.Ignore))
        {
            point.y = hit.point.y;
        }

        return point;
    }

    private IEnumerator ShellRoutine(Vector3 from, Vector3 to)
    {
        float distance = Vector3.Distance(from, to);
        float flight = Mathf.Clamp(distance / 48f, 1f, 2.6f);

        if (shellMaterial == null)
        {
            shellMaterial = LevelBuilder.MakeMaterial(new Color(1f, 0.8f, 0.45f),
                new Color(2.6f, 1.2f, 0.3f));
        }

        GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "ShellInFlight";
        shell.transform.SetParent(levelRoot, true);
        shell.transform.localScale = Vector3.one * 0.26f;
        Destroy(shell.GetComponent<Collider>());

        Renderer shellRenderer = shell.GetComponent<Renderer>();
        shellRenderer.sharedMaterial = shellMaterial;
        shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        TrailRenderer trail = shell.AddComponent<TrailRenderer>();
        trail.time = 0.55f;
        trail.startWidth = 0.2f;
        trail.endWidth = 0.02f;
        trail.numCapVertices = 2;
        trail.sharedMaterial = shellMaterial;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / flight;
            float e = Mathf.Clamp01(t);
            Vector3 point = Vector3.Lerp(from, to, e);
            point.y += Mathf.Sin(e * Mathf.PI) * (distance * 0.12f + 4f);
            shell.transform.position = point;
            yield return null;
        }

        Destroy(shell);
        if (artillery != null) artillery.FireAt(to);
    }

    private void SpawnMuzzleEffects(Vector3 point)
    {
        if (shellMaterial == null)
        {
            shellMaterial = LevelBuilder.MakeMaterial(new Color(1f, 0.8f, 0.45f),
                new Color(2.6f, 1.2f, 0.3f));
        }
        if (smokeMaterial == null)
        {
            smokeMaterial = LevelBuilder.MakeTransparentMaterial(
                new Color(0.5f, 0.49f, 0.47f, 0.5f));
        }

        GameObject lightGo = new GameObject("GunFlashLight");
        lightGo.transform.SetParent(levelRoot, true);
        lightGo.transform.position = point;
        Light flash = lightGo.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.68f, 0.32f);
        flash.intensity = 11f;
        flash.range = 20f;
        Destroy(lightGo, 0.16f);

        GameObject flare = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flare.name = "GunFlash";
        flare.transform.SetParent(levelRoot, true);
        flare.transform.position = point;
        flare.transform.localScale = new Vector3(1.1f, 1.1f, 1.6f);
        Destroy(flare.GetComponent<Collider>());
        Renderer flareRenderer = flare.GetComponent<Renderer>();
        flareRenderer.sharedMaterial = shellMaterial;
        flareRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(flare, 0.11f);

        for (int i = 0; i < 4; i++)
        {
            GameObject smoke = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            smoke.name = "GunSmoke";
            smoke.transform.SetParent(levelRoot, true);
            smoke.transform.position = point + Random.insideUnitSphere * 0.5f;
            smoke.transform.localScale = Vector3.one * Random.Range(0.8f, 1.6f);
            Destroy(smoke.GetComponent<Collider>());
            Renderer renderer = smoke.GetComponent<Renderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(smoke, Random.Range(1.6f, 2.6f));
        }
    }

    // ───────────────────────────── 炮位建模 ─────────────────────────────

    private static Gun BuildGun(Transform root, GunSlot slot, int index, Material iron,
                                Material ironDark, Material wood, Material woodDark,
                                Material brass, Material bag, Material canvas, Material dirt)
    {
        Vector3 toPlayer = new Vector3(0f, 0f, 6f) - slot.position;
        float yaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg + slot.yawOffset;

        GameObject gunGo = new GameObject("EnemyGun_" + index);
        gunGo.transform.SetParent(root, true);
        gunGo.transform.position = slot.position;
        gunGo.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform g = gunGo.transform;

        // 炮位土台
        Part(PrimitiveType.Cube, "Emplacement", g, new Vector3(0f, 0.035f, 0f),
            new Vector3(5.6f, 0.08f, 6.4f), dirt);

        // 分体大架 + 驻锄
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject trail = Part(PrimitiveType.Cube, side < 0 ? "TrailL" : "TrailR", g,
                new Vector3(side * 0.44f, 0.36f, -1.5f), new Vector3(0.2f, 0.2f, 2.7f), wood);
            trail.transform.localRotation = Quaternion.Euler(-5f, side * 4f, 0f);

            GameObject spade = Part(PrimitiveType.Cube, "Spade", g,
                new Vector3(side * 0.66f, 0.14f, -2.95f), new Vector3(0.36f, 0.1f, 0.6f), ironDark);
            spade.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);

            Part(PrimitiveType.Cube, "TrailBrace", g,
                new Vector3(side * 0.44f, 0.6f, -0.55f), new Vector3(0.08f, 0.55f, 0.08f), ironDark);
        }

        // 炮轴与炮轮
        Part(PrimitiveType.Cube, "Axle", g, new Vector3(0f, 0.72f, 0f),
            new Vector3(2.1f, 0.14f, 0.15f), ironDark);

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject wheelGo = new GameObject(side < 0 ? "WheelL" : "WheelR");
            wheelGo.transform.SetParent(g, false);
            wheelGo.transform.localPosition = new Vector3(side * 1.0f, 0.72f, 0f);
            wheelGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            BuildWheel(wheelGo.transform, wood, woodDark, iron);
        }

        // 炮身：摇架 → 后坐的炮身组（炮管、炮闩、加强箍、炮口）
        GameObject cradleGo = new GameObject("Cradle");
        cradleGo.transform.SetParent(g, false);
        cradleGo.transform.localPosition = new Vector3(0f, 0.98f, 0.1f);
        cradleGo.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
        Transform cradle = cradleGo.transform;

        GameObject barrelGroupGo = new GameObject("BarrelGroup");
        barrelGroupGo.transform.SetParent(cradle, false);
        barrelGroupGo.transform.localPosition = new Vector3(0f, 0f, 0.62f);
        Transform barrelGroup = barrelGroupGo.transform;

        GameObject barrel = Part(PrimitiveType.Cylinder, "Barrel", barrelGroup, Vector3.zero,
            new Vector3(0.24f, 1.2f, 0.24f), iron);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        for (int i = 0; i < 3; i++)
        {
            GameObject band = Part(PrimitiveType.Cylinder, "BarrelBand", barrelGroup,
                new Vector3(0f, 0f, 0.28f + i * 0.38f), new Vector3(0.275f, 0.035f, 0.275f),
                ironDark);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        Part(PrimitiveType.Cube, "Breech", barrelGroup, new Vector3(0f, 0f, -1.06f),
            new Vector3(0.34f, 0.34f, 0.36f), ironDark);
        Part(PrimitiveType.Cube, "BreechLever", barrelGroup,
            new Vector3(0.26f, 0.03f, -0.92f), new Vector3(0.3f, 0.06f, 0.06f), iron);
        Part(PrimitiveType.Cylinder, "MuzzleRing", barrelGroup,
            new Vector3(0f, 0f, 1.17f), new Vector3(0.29f, 0.05f, 0.29f), ironDark)
            .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        GameObject muzzleGo = new GameObject("Muzzle");
        muzzleGo.transform.SetParent(barrelGroup, false);
        muzzleGo.transform.localPosition = new Vector3(0f, 0f, 1.28f);

        // 防盾：左右两块留出炮管缺口，带铆钉
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject plate = Part(PrimitiveType.Cube, "GunShield", g,
                new Vector3(side * 0.54f, 0.98f, -0.03f), new Vector3(0.72f, 1.02f, 0.07f), iron);
            plate.transform.localRotation = Quaternion.Euler(9f, 0f, side * 2f);

            for (int r = 0; r < 3; r++)
            {
                Part(PrimitiveType.Sphere, "Rivet", g,
                    new Vector3(side * 0.34f, 0.62f + r * 0.31f, 0.03f),
                    Vector3.one * 0.055f, ironDark);
            }
        }

        // 后坐筒、高低机手轮、瞄准具
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject cylinder = Part(PrimitiveType.Cylinder, "RecoilCylinder", cradle,
                new Vector3(side * 0.16f, -0.01f, 0.42f), new Vector3(0.13f, 0.55f, 0.13f),
                ironDark);
            cylinder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        GameObject handwheel = Part(PrimitiveType.Cylinder, "ElevationWheel", g,
            new Vector3(0.62f, 0.92f, -0.42f), new Vector3(0.4f, 0.05f, 0.4f), brass);
        handwheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        Part(PrimitiveType.Cube, "SightPost", cradle, new Vector3(-0.3f, 0.2f, -0.35f),
            new Vector3(0.05f, 0.32f, 0.05f), ironDark);
        Part(PrimitiveType.Cube, "SightArm", cradle, new Vector3(-0.24f, 0.35f, -0.35f),
            new Vector3(0.2f, 0.04f, 0.04f), ironDark);

        // 弹药：木箱、散放炮弹
        Part(PrimitiveType.Cube, "ShellCrate", g, new Vector3(1.85f, 0.24f, -0.75f),
            new Vector3(0.95f, 0.48f, 0.62f), wood);
        Part(PrimitiveType.Cube, "ShellCrateLid", g, new Vector3(1.85f, 0.5f, -0.75f),
            new Vector3(1.0f, 0.05f, 0.66f), woodDark);
        Part(PrimitiveType.Cube, "ShellCrateBand", g, new Vector3(1.85f, 0.25f, -0.75f),
            new Vector3(1.0f, 0.06f, 0.66f), ironDark);

        for (int i = 0; i < 4; i++)
        {
            Part(PrimitiveType.Cylinder, "Shell", g,
                new Vector3(-1.72f + (i % 2) * 0.24f, 0.34f, -0.5f - (i / 2) * 0.3f),
                new Vector3(0.13f, 0.34f, 0.13f), brass);
        }

        // 沙袋胸墙：两层错缝，围出炮位
        for (int layer = 0; layer < 2; layer++)
        {
            int count = layer == 0 ? 11 : 10;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Lerp(-64f, 64f, i / (count - 1f)) + (layer == 0 ? 0f : 3.2f);
                float rad = 3f - layer * 0.16f;
                GameObject sandbag = Part(PrimitiveType.Cube, "Sandbag", g,
                    new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * rad,
                                0.18f + layer * 0.3f,
                                Mathf.Cos(a * Mathf.Deg2Rad) * rad),
                    new Vector3(0.66f, 0.3f, 0.42f), bag);
                sandbag.transform.localRotation =
                    Quaternion.Euler(0f, -a + Random.Range(-4f, 4f), 0f);
            }
        }

        // 奇数炮位搭伪装网，偶数炮位摆空药筒，避免四个炮位长得一模一样
        if (index % 2 == 0)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -2.05f : 2.05f;
                float z = i % 2 == 0 ? -1.5f : 2.3f;
                Part(PrimitiveType.Cube, "NetPole", g, new Vector3(x, 1.15f, z),
                    new Vector3(0.07f, 2.3f, 0.07f), woodDark);
            }

            GameObject net = Part(PrimitiveType.Cube, "CamouflageNet", g,
                new Vector3(0f, 2.18f, 0.4f), new Vector3(4.3f, 0.04f, 3.9f), canvas);
            net.transform.localRotation = Quaternion.Euler(3f, 8f, 2f);
        }
        else
        {
            for (int i = 0; i < 5; i++)
            {
                GameObject caseGo = Part(PrimitiveType.Cylinder, "EmptyCase", g,
                    new Vector3(0.55f + i * 0.13f, 0.32f, -1.6f + i * 0.22f),
                    new Vector3(0.09f, 0.3f, 0.09f), brass);
                caseGo.transform.localRotation = Quaternion.Euler(Random.Range(-5f, 5f), 0f, 70f);
            }
        }

        Gun gun = new Gun();
        gun.barrel = barrelGroup;
        gun.barrelRest = barrelGroup.localPosition;
        gun.muzzle = muzzleGo.transform;
        gun.timer = 0f;
        gun.recoil = 0f;
        return gun;
    }

    private static void BuildWheel(Transform wheel, Material wood, Material woodDark, Material iron)
    {
        // 轮辋：12 段弧形木块拼成，辐条从缝里看得见
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f;
            float rad = 0.64f;
            GameObject felloe = Part(PrimitiveType.Cube, "Felloe", wheel,
                new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad) * rad, 0f,
                            Mathf.Sin(angle * Mathf.Deg2Rad) * rad),
                new Vector3(0.35f, 0.13f, 0.11f), woodDark);
            felloe.transform.localRotation = Quaternion.Euler(0f, angle + 90f, 0f);
        }

        for (int i = 0; i < 8; i++)
        {
            GameObject spoke = Part(PrimitiveType.Cube, "Spoke", wheel, Vector3.zero,
                new Vector3(0.07f, 0.07f, 1.26f), wood);
            spoke.transform.localRotation = Quaternion.Euler(0f, i * 22.5f, 0f);
        }

        Part(PrimitiveType.Cylinder, "Hub", wheel, Vector3.zero,
            new Vector3(0.34f, 0.1f, 0.34f), iron);
        Part(PrimitiveType.Cylinder, "HubCap", wheel, new Vector3(0f, 0.1f, 0f),
            new Vector3(0.12f, 0.05f, 0.12f), iron);
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent,
                                   Vector3 localPos, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        return go;
    }
}
