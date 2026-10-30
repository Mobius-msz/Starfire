using UnityEngine;

/// <summary>
/// 河面流动：滚动水纹 UV + 顺流漂移的白色浪花粒子 + 3D 水声。
/// 只挂在水面物体上，尺寸与流向由关卡给出。
/// </summary>
public class RiverFlow : MonoBehaviour
{
    private Material waterMaterial;
    private Vector2 scrollDirection;
    private float scrollSpeed;
    private float particleSpeed = 16f;
    private float crossHalf = 12f;

    private Mesh surfaceMesh;
    private Vector3[] surfaceBase;
    private Vector3[] surfaceWork;

    /// <summary>给一块水面挂上流动效果。size 是水面的世界尺寸，flow 是水平流向。</summary>
    public static RiverFlow Attach(GameObject water, Vector3 size, Vector3 flow, float speed)
    {
        RiverFlow river = water.AddComponent<RiverFlow>();
        river.Build(size, flow, speed);
        return river;
    }

    private void Build(Vector3 size, Vector3 flow, float speed)
    {
        Vector3 flatFlow = new Vector3(flow.x, 0f, flow.z);
        if (flatFlow.sqrMagnitude < 0.01f) flatFlow = Vector3.forward;
        flatFlow.Normalize();

        // 流速整体再降 40%：水纹、浪头与飞溅用同一个速度，三者不会各走各的
        particleSpeed = Mathf.Max(3.5f, speed * 0.6f);
        crossHalf = Mathf.Max(1f, Mathf.Abs(size.x * flatFlow.z) + Mathf.Abs(size.z * flatFlow.x)) * 0.5f;

        Color waterColor = new Color(0.13f, 0.27f, 0.29f);
        waterMaterial = LevelBuilder.MakeMaterial(waterColor);
        waterMaterial.mainTexture = ProceduralTextures.WaterFlowTexture();
        waterMaterial.mainTextureScale = new Vector2(size.x / 9f, size.z / 9f);
        if (waterMaterial.HasProperty("_Glossiness")) waterMaterial.SetFloat("_Glossiness", 0.72f);
        if (waterMaterial.HasProperty("_Metallic")) waterMaterial.SetFloat("_Metallic", 0.08f);

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = waterMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        scrollDirection = new Vector2(flatFlow.x, flatFlow.z).normalized;

        BuildSurface(size);
        BuildSpray(size, flatFlow);
        BuildSound(flatFlow, size);
    }

    /// <summary>
    /// 用一张细分网格替换方盒的顶面：水面的起伏、明暗都在网格上实时算出来。
    /// 方盒本体只留作定位与水下的暗色衬底。
    /// </summary>
    private void BuildSurface(Vector3 size)
    {
        Renderer boxRenderer = GetComponent<Renderer>();
        if (boxRenderer != null) boxRenderer.enabled = false;

        GameObject surface = new GameObject("RiverSurface");
        surface.transform.SetParent(transform.parent, true);
        surface.transform.position = transform.position + Vector3.up * (size.y * 0.5f);

        int nx = Mathf.Clamp(Mathf.RoundToInt(size.x / 3f), 4, 48);
        int nz = Mathf.Clamp(Mathf.RoundToInt(size.z / 3f), 4, 120);
        int count = (nx + 1) * (nz + 1);

        surfaceBase = new Vector3[count];
        surfaceWork = new Vector3[count];
        Vector2[] uv = new Vector2[count];
        int[] tris = new int[nx * nz * 6];

        float halfW = size.x * 0.5f;
        float halfL = size.z * 0.5f;
        int i = 0;
        for (int z = 0; z <= nz; z++)
        {
            float tz = (float)z / nz;
            for (int x = 0; x <= nx; x++, i++)
            {
                float tx = (float)x / nx;
                surfaceBase[i] = new Vector3(
                    Mathf.Lerp(-halfW, halfW, tx), 0f, Mathf.Lerp(-halfL, halfL, tz));
                uv[i] = new Vector2(tx, tz);
            }
        }

        int ti = 0;
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int a = z * (nx + 1) + x;
                int b = a + 1;
                int c = a + nx + 1;
                int d = c + 1;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
            }
        }

        surfaceMesh = new Mesh();
        surfaceMesh.name = "RiverSurfaceMesh";
        surfaceMesh.MarkDynamic();
        surfaceMesh.vertices = surfaceBase;
        surfaceMesh.uv = uv;
        surfaceMesh.triangles = tris;
        surfaceMesh.RecalculateNormals();
        surfaceMesh.RecalculateBounds();

        MeshFilter filter = surface.AddComponent<MeshFilter>();
        filter.sharedMesh = surfaceMesh;

        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = waterMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void AnimateSurface()
    {
        if (surfaceMesh == null || surfaceBase == null) return;

        // 波速跟着流速一起放慢，浪头推进才不会比水纹快
        float t = Time.time * 0.6f;
        for (int i = 0; i < surfaceBase.Length; i++)
        {
            Vector3 p = surfaceBase[i];
            // 顺流方向与横河方向：浪分主涌、副涌和碎浪三层，
            // 主涌顺流推进，副涌斜着压过来，碎浪把整齐的浪线打散
            float along = p.x * scrollDirection.x + p.z * scrollDirection.y;
            float across = -p.x * scrollDirection.y + p.z * scrollDirection.x;

            // 波峰收窄、波谷拉平：像水在走，而不是一条正弦曲线在抖
            float swell = Mathf.Sin(along * 0.34f - t * 1.15f + across * 0.055f) * 0.5f + 0.5f;
            float y = 0.1f * (swell * swell - 0.5f);
            y += 0.038f * Mathf.Sin(across * 0.23f + along * 0.05f - t * 0.8f);
            float chop = Mathf.Sin(along * 0.82f + across * 0.37f - t * 1.9f);
            y += 0.016f * chop * chop * chop;

            // 近岸水浅，浪衰减下来
            float shore = Mathf.Clamp01(Mathf.Abs(across) / crossHalf);
            y *= Mathf.Lerp(1f, 0.55f, shore * shore);

            surfaceWork[i] = new Vector3(p.x, y, p.z);
        }

        surfaceMesh.vertices = surfaceWork;
        surfaceMesh.RecalculateNormals();
    }

    private void Update()
    {
        if (waterMaterial != null)
        {
            // 每 9 米一个纹理单位，scrollSpeed 直接就是"米/秒"
            Vector2 offset = waterMaterial.mainTextureOffset;
            offset += scrollDirection * (particleSpeed / 9f) * Time.deltaTime;
            waterMaterial.mainTextureOffset = offset;
        }

        AnimateSurface();
    }

    private void BuildSpray(Vector3 size, Vector3 flow)
    {
        GameObject sprayGo = new GameObject("RiverSpray");
        sprayGo.transform.SetParent(transform.parent, true);
        sprayGo.transform.position = transform.position + Vector3.up * 0.04f;

        ParticleSystem system = sprayGo.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.startLifetime = 5f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.9f);
        main.startColor = new Color(0.8f, 0.9f, 0.92f, 0.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 650;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 80f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(size.x * 0.88f, 0.25f, size.z * 0.95f);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // 三条曲线必须同为 TwoConstants，混用 Constant 会让 Unity 每帧刷警告
        float vx = flow.x * particleSpeed;
        float vz = flow.z * particleSpeed;
        velocity.x = new ParticleSystem.MinMaxCurve(vx, vx);
        velocity.z = new ParticleSystem.MinMaxCurve(vz, vz);
        // 水花只在贴着水面的薄层里散：桥面离水面约 0.4 米，
        // 把纵向速度压到 ±0.03，粒子最多浮起十几厘米，不会再穿过桥板
        velocity.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.03f);

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.93f, 0.95f), 0f),
                new GradientColorKey(new Color(0.75f, 0.88f, 0.9f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.55f, 0.15f),
                new GradientAlphaKey(0.35f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = gradient;

        ParticleSystemRenderer systemRenderer = sprayGo.GetComponent<ParticleSystemRenderer>();
        systemRenderer.renderMode = ParticleSystemRenderMode.Billboard;

        // 打包后没有材质引用 Particles/Standard Unlit，会被剥离成洋红；
        // Sprites/Default 随 Snowfall 一起进包，稳定可用
        Shader particleShader = Shader.Find("Sprites/Default");
        if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");

        if (particleShader != null)
        {
            Material material = new Material(particleShader);
            material.mainTexture = ProceduralTextures.ParticleGlowTexture();
            material.SetColor("_Color", new Color(0.85f, 0.92f, 0.95f, 0.8f));
            material.renderQueue = 3000;
            systemRenderer.sharedMaterial = material;
        }

        system.Play();
    }

    private void BuildSound(Vector3 flow, Vector3 size)
    {
        AudioClip clip = ProceduralAudio.RiverFlow();
        if (clip == null) return;

        float length = Mathf.Max(size.x * Mathf.Abs(flow.x), size.z * Mathf.Abs(flow.z));
        Vector3 axis = flow;

        for (int i = 0; i < 3; i++)
        {
            float t = i == 0 ? -0.33f : (i == 1 ? 0f : 0.33f);

            GameObject soundGo = new GameObject("RiverSound_" + i);
            soundGo.transform.SetParent(transform.parent, true);
            soundGo.transform.position = transform.position + axis * (length * t)
                                         + Vector3.up * 0.6f;

            AudioSource source = soundGo.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0.9f;
            source.volume = 0.175f;   // 在 0.25 的基础上再降 30%
            source.minDistance = 8f;
            source.maxDistance = 150f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();
        }
    }
}
