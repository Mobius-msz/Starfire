using UnityEngine;

/// <summary>
/// 枪口火光：三片交叠的星芒面片 + 瞬时点光，开火时闪一下。
/// 全部程序化生成，不依赖预制体、贴图或后期 Bloom。
/// </summary>
public class MuzzleFlash : MonoBehaviour
{
    private const float DefaultLife = 0.06f;
    private float duration = DefaultLife;

    private static Mesh starMesh;
    private static Material flareMaterial;

    private Transform visual;
    private Transform[] blades;
    private Light glow;
    private float timer;
    private float baseSize = 0.2f;
    private float baseLight = 3.4f;
    private float currentSize = 0.2f;

    public static MuzzleFlash Create(Transform parent, Vector3 localPosition, float size)
    {
        GameObject go = new GameObject("MuzzleFlash");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;

        MuzzleFlash flash = go.AddComponent<MuzzleFlash>();
        flash.baseSize = size;
        flash.Build();
        flash.Hide();
        return flash;
    }

    private void Build()
    {
        if (flareMaterial == null) flareMaterial = BuildFlareMaterial();
        if (starMesh == null) starMesh = BuildStarMesh();

        GameObject visualGo = new GameObject("Visual");
        visual = visualGo.transform;
        visual.SetParent(transform, false);
        visual.localScale = Vector3.one * baseSize;

        // 一片正对枪尾（第一人称能看见），两片沿枪管轴交叉（第三人称从侧面能看见）
        Quaternion[] orientations =
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(0f, 90f, 0f)
        };

        blades = new Transform[orientations.Length];
        for (int i = 0; i < orientations.Length; i++)
        {
            GameObject blade = new GameObject("Flare" + i);
            blade.transform.SetParent(visual, false);
            blade.transform.localRotation = orientations[i];

            MeshFilter filter = blade.AddComponent<MeshFilter>();
            filter.sharedMesh = starMesh;

            MeshRenderer renderer = blade.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = flareMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            blades[i] = blade.transform;
        }

        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 5f;
        glow.intensity = baseLight;
        glow.color = new Color(1f, 0.76f, 0.42f);
        glow.shadows = LightShadows.None;
        glow.renderMode = LightRenderMode.ForceVertex;
    }

    /// <summary>扣扳机时调用。sizeMultiplier 可让不同枪型闪得大小不同。</summary>
    public void Play(float sizeMultiplier = 1f)
    {
        duration = DefaultLife;
        StartFlash(sizeMultiplier);
    }

    /// <summary>开发截图用：让火光保持亮着，方便定格检查。</summary>
    public void PlayHold(float seconds)
    {
        duration = Mathf.Max(0.05f, seconds);
        StartFlash(1.15f);
    }

    private void StartFlash(float sizeMultiplier)
    {
        timer = duration;
        transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 180f));

        if (visual != null)
        {
            currentSize = baseSize * sizeMultiplier * Random.Range(0.85f, 1.2f);
            visual.gameObject.SetActive(true);
            visual.localScale = Vector3.one * currentSize;
        }

        if (glow != null)
        {
            glow.enabled = true;
            glow.intensity = baseLight * Random.Range(0.75f, 1.25f);
        }
    }

    private void Update()
    {
        if (timer <= 0f) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            Hide();
            return;
        }

        float t = 1f - timer / duration;             // 0 → 1
        float fade = 1f - t;

        if (visual != null)
        {
            // 一闪即胀开，随后整个火光被隐藏
            visual.localScale = Vector3.one * currentSize * (1f + 0.65f * t);
        }

        if (glow != null) glow.intensity = baseLight * fade * fade;
    }

    private void Hide()
    {
        timer = 0f;
        if (visual != null) visual.gameObject.SetActive(false);
        if (glow != null) glow.enabled = false;
    }

    private static Mesh BuildStarMesh()
    {
        const int points = 8;
        Vector3[] vertices = new Vector3[points + 1];
        Vector2[] uvs = new Vector2[points + 1];

        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < points; i++)
        {
            float angle = i / (float)points * Mathf.PI * 2f;
            float radius = (i % 2 == 0) ? 0.5f : 0.18f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f,
                                     0.5f + Mathf.Sin(angle) * 0.5f);
        }

        int[] triangles = new int[points * 6];
        for (int i = 0; i < points; i++)
        {
            int next = (i + 1) % points;
            int v = i * 6;

            // 正反两面都画，避免加到单面材质时从背后看不见
            triangles[v] = 0;
            triangles[v + 1] = i + 1;
            triangles[v + 2] = next + 1;

            triangles[v + 3] = 0;
            triangles[v + 4] = next + 1;
            triangles[v + 5] = i + 1;
        }

        Mesh mesh = new Mesh();
        mesh.name = "MuzzleFlashStar";
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material BuildFlareMaterial()
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = "MuzzleFlashFlare";
        material.mainTexture = BuildGlowTexture();

        Color tint = new Color(1f, 0.82f, 0.5f, 1f);
        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", tint);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.35f) * 3f);
        }

        return material;
    }

    private static Texture2D BuildGlowTexture()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "MuzzleFlashGlow";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;

                float radius = Mathf.Sqrt(u * u + v * v);
                float glow = Mathf.Pow(Mathf.Clamp01(1f - radius), 2.1f);

                // 十字星芒，让火光不是一团圆球
                float spikeH = Mathf.Clamp01(1f - Mathf.Abs(u) * 9f)
                             * Mathf.Clamp01(1f - Mathf.Abs(v) * 1.1f);
                float spikeV = Mathf.Clamp01(1f - Mathf.Abs(v) * 9f)
                             * Mathf.Clamp01(1f - Mathf.Abs(u) * 1.1f);
                float alpha = Mathf.Clamp01(glow + Mathf.Max(spikeH, spikeV) * 0.6f);

                pixels[y * size + x] = new Color(1f, 0.93f, 0.8f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
