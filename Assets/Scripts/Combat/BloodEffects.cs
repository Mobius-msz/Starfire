using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 中弹血花：沿弹道喷出的血雾与血珠。
///
/// 全部是粒子系统，模拟空间设为 World，血珠受重力下坠、落到地面即消失，
/// 不会像旧版那样在身体倒下后留下一串悬在半空的血块。
/// 地面血泊是单独贴在落地高度上的薄片，不挂在任何会倒下的物体上。
/// </summary>
public static class BloodEffects
{
    private static ParticleSystem drops;
    private static ParticleSystem mist;
    private static Material dropMaterial;
    private static Material dropsMaterial;
    private static Material splatMaterial;
    private static Texture2D softDisc;

    private static readonly List<GameObject> pools = new List<GameObject>();
    private const int MaxPools = 24;
    private const float PoolLifetime = 14f;

    /// <summary>中弹：沿弹着面法线喷出一蓬血雾，外带一把受重力下坠的血珠。</summary>
    public static void Burst(Vector3 point, Vector3 normal, float amount)
    {
        EnsureSystems();

        if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
        normal.Normalize();

        // 枪伤、刀伤、爆炸的量级差得很远，按伤害值拉开喷溅规模
        int dropCount = Mathf.Clamp(Mathf.RoundToInt(amount * 0.12f) + 4, 4, 18);
        int mistCount = Mathf.Clamp(Mathf.RoundToInt(amount * 0.06f) + 2, 2, 9);
        float power = Mathf.Clamp(amount * 0.004f + 0.55f, 0.55f, 1.8f);

        // 血雾：沿法线冲出去一小段，很快被空气拽住，散成一片红
        ParticleSystem.EmitParams mistParams = new ParticleSystem.EmitParams();
        for (int i = 0; i < mistCount; i++)
        {
            mistParams.position = point + normal * Random.Range(0.01f, 0.05f)
                                  + Random.insideUnitSphere * 0.05f;
            mistParams.velocity = normal * Random.Range(1.1f, 2.8f) * power
                                  + Random.insideUnitSphere * Random.Range(0.35f, 1.1f);
            mistParams.startSize = Random.Range(0.11f, 0.24f) * power;
            mistParams.startLifetime = Random.Range(0.3f, 0.55f);
            mistParams.startColor = Random.value < 0.5f
                ? new Color(0.42f, 0.02f, 0.02f, 0.62f)
                : new Color(0.55f, 0.05f, 0.04f, 0.52f);
            mist.Emit(mistParams, 1);
        }

        // 血珠：受重力拽下来，落到地面 / 掩体面就消失
        ParticleSystem.EmitParams dropParams = new ParticleSystem.EmitParams();
        for (int i = 0; i < dropCount; i++)
        {
            dropParams.position = point + normal * 0.04f
                                  + Random.insideUnitSphere * 0.05f;
            dropParams.velocity = normal * Random.Range(0.6f, 3.1f) * power
                                  + Random.insideUnitSphere * Random.Range(0.5f, 1.7f)
                                  + Vector3.up * Random.Range(0.2f, 1.2f);
            dropParams.startSize = Random.Range(0.018f, 0.05f) * power;
            dropParams.startLifetime = Random.Range(0.55f, 1.1f);
            dropParams.startColor = Random.value < 0.5f
                ? new Color(0.52f, 0.02f, 0.02f, 0.95f)
                : new Color(0.34f, 0.015f, 0.015f, 0.95f);
            drops.Emit(dropParams, 1);
        }
    }

    /// <summary>
    /// 倒在血泊里：在身体正下方的地面上铺一块血斑。
    /// 先向下打射线找实地，找不到就不铺——宁可不铺，也不把血迹留在半空。
    /// </summary>
    public static void Pool(Vector3 position)
    {
        EnsureMaterials();

        Vector3 ground;
        if (!TryGround(position, out ground)) return;

        GameObject splat = new GameObject("BloodPool");
        splat.transform.position = ground + Vector3.up * 0.012f;
        splat.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        float size = Random.Range(0.5f, 0.85f);
        AddBlob(splat.transform, Vector3.zero,
            size * Random.Range(0.9f, 1.15f), size * Random.Range(0.62f, 0.85f),
            Random.Range(0f, 180f));

        int satellites = Random.Range(7, 12);
        for (int i = 0; i < satellites; i++)
        {
            float angle = (i / (float)satellites) * Mathf.PI * 2f
                          + Random.Range(-0.4f, 0.4f);
            float distance = size * Random.Range(0.5f, 0.9f);
            float radius = size * Random.Range(0.08f, 0.18f);
            AddBlob(splat.transform,
                new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, 0f),
                radius * Random.Range(1.1f, 1.9f), radius,
                Random.Range(0f, 360f));
        }

        pools.Add(splat);
        Object.Destroy(splat, PoolLifetime);

        while (pools.Count > MaxPools)
        {
            GameObject oldest = pools[0];
            pools.RemoveAt(0);
            if (oldest != null) Object.Destroy(oldest);
        }
    }

    private static bool TryGround(Vector3 position, out Vector3 ground)
    {
        ground = position;

        Vector3 origin = position + Vector3.up * 1.6f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4.5f, ~0,
                             QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.normal.y < 0.4f) return false;

        ground = hit.point;
        return true;
    }

    /// <summary>把一颗压扁的血点贴到已经落到地面的血泊平面上。</summary>
    private static void AddBlob(Transform parent, Vector3 offset,
                                float radiusX, float radiusY, float yaw)
    {
        GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blob.name = "BloodBlob";
        Object.Destroy(blob.GetComponent<Collider>());

        blob.transform.SetParent(parent, false);
        blob.transform.localPosition = offset;
        blob.transform.localRotation = Quaternion.Euler(0f, 0f, yaw);
        blob.transform.localScale = new Vector3(radiusX * 2f, 0.01f, radiusY * 2f);

        Renderer renderer = blob.GetComponent<Renderer>();
        renderer.sharedMaterial = splatMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void EnsureSystems()
    {
        if (drops != null && mist != null) return;

        EnsureMaterials();

        drops = BuildSystem("BloodDrops", dropsMaterial, 0.05f, 1.7f, 420);
        // 血珠碰到地面、沙袋、桥板就消失，不会穿到地下再飘回来
        ParticleSystem.CollisionModule dropCollision = drops.collision;
        dropCollision.enabled = true;
        dropCollision.type = ParticleSystemCollisionType.World;
        dropCollision.mode = ParticleSystemCollisionMode.Collision3D;
        dropCollision.collidesWith = ~0;
        dropCollision.enableDynamicColliders = false;
        dropCollision.quality = ParticleSystemCollisionQuality.Low;
        dropCollision.lifetimeLoss = 1f;
        dropCollision.bounce = 0f;
        dropCollision.dampen = 1f;

        mist = BuildSystem("BloodMist", dropMaterial, 0.2f, 0.3f, 260);
        // 血雾越飘越大、越飘越淡
        ParticleSystem.SizeOverLifetimeModule mistSize = mist.sizeOverLifetime;
        mistSize.enabled = true;
        mistSize.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.7f)));

        ParticleSystem.ColorOverLifetimeModule mistFade = mist.colorOverLifetime;
        mistFade.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.55f, 0.5f),
                    new GradientAlphaKey(0f, 1f) });
        mistFade.color = new ParticleSystem.MinMaxGradient(fade);

        ParticleSystem.ColorOverLifetimeModule dropFade = drops.colorOverLifetime;
        dropFade.enabled = true;
        Gradient dropAlpha = new Gradient();
        dropAlpha.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f) });
        dropFade.color = new ParticleSystem.MinMaxGradient(dropAlpha);
    }

    private static ParticleSystem BuildSystem(string name, Material material,
                                              float startSize, float gravity, int max)
    {
        GameObject go = new GameObject(name);
        go.transform.position = Vector3.zero;

        ParticleSystem system = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.prewarm = false;
        main.playOnAwake = true;
        main.startLifetime = 0.8f;
        main.startSpeed = 0f;
        main.startSize = startSize;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        // 只靠 Emit() 定点喷射，不持续冒血
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        system.Play();
        return system;
    }

    private static void EnsureMaterials()
    {
        if (softDisc == null) softDisc = SoftDiscTexture();

        if (dropMaterial == null)
        {
            dropMaterial = LevelBuilder.MakeTransparentMaterial(new Color(0.5f, 0.02f, 0.02f, 0.9f));
            ApplySprite(dropMaterial, softDisc);
        }

        if (dropsMaterial == null)
        {
            dropsMaterial = LevelBuilder.MakeTransparentMaterial(new Color(0.45f, 0.02f, 0.02f, 1f));
            ApplySprite(dropsMaterial, softDisc);
        }

        if (splatMaterial == null)
        {
            splatMaterial = LevelBuilder.MakeMaterial(new Color(0.26f, 0.02f, 0.02f));
        }
    }

    private static void ApplySprite(Material material, Texture2D texture)
    {
        // Sprites/Default 走透明混合，粒子边缘才不会被切成硬方块
        Shader sprite = Shader.Find("Sprites/Default");
        if (sprite != null) material.shader = sprite;
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        material.mainTexture = texture;
        // 颜色全部交给每一颗粒子的 startColor，材质本身保持白色
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        material.color = Color.white;
        material.renderQueue = 3000;
    }

    /// <summary>中间实、边缘虚的圆点，血珠与血雾都用它。</summary>
    private static Texture2D SoftDiscTexture()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                // 边缘打碎一点，免得每一颗都是规整的圆
                float wobble = Mathf.PerlinNoise(nx * 3.1f + 11f, ny * 3.1f + 4f) * 0.18f;
                float alpha = Mathf.Clamp01(1f - d + wobble);
                alpha *= alpha;
                alpha = Mathf.SmoothStep(0f, 1f, alpha);

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
