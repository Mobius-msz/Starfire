using UnityEngine;

/// <summary>
/// 程序化表面材质：噪声、木纹、石材、布料、积雪、冰面与雪花贴图。
/// 全部在运行时生成，用来打破纯色几何体的塑料感，不依赖任何外部图片。
/// </summary>
public static class SurfaceTextures
{
    private static Texture2D noise;
    private static Texture2D wood;
    private static Texture2D stone;
    private static Texture2D cloth;
    private static Texture2D snow;
    private static Texture2D ice;
    private static Texture2D snowflake;

    public static Texture2D Noise
    {
        get
        {
            if (noise == null)
            {
                noise = Create("Noise", 256, (x, y) =>
                {
                    float broad = Mathf.PerlinNoise(x * 0.09f + 11.3f, y * 0.09f + 4.7f);
                    float fine = Mathf.PerlinNoise(x * 0.5f + 31.1f, y * 0.5f + 7.3f);
                    return 0.82f + broad * 0.12f + fine * 0.06f;
                });
            }

            return noise;
        }
    }

    public static Texture2D Wood
    {
        get
        {
            if (wood == null)
            {
                wood = Create("Wood", 256, (x, y) =>
                {
                    float warp = Mathf.PerlinNoise(x * 0.02f + 5.1f, y * 0.02f + 9.2f);
                    float grain = Mathf.Sin((y + warp * 22f) * 0.55f);
                    float fine = Mathf.PerlinNoise(x * 0.45f + 1.7f, y * 0.07f + 3.3f);
                    return 0.8f + grain * 0.07f + fine * 0.12f;
                });
            }

            return wood;
        }
    }

    public static Texture2D Stone
    {
        get
        {
            if (stone == null)
            {
                stone = Create("Stone", 256, (x, y) =>
                {
                    float broad = Mathf.PerlinNoise(x * 0.06f + 2.2f, y * 0.06f + 8.8f);
                    float speck = Mathf.PerlinNoise(x * 0.8f + 15.5f, y * 0.8f + 2.1f);
                    float value = 0.82f + broad * 0.14f + speck * 0.06f;
                    return speck < 0.28f ? value - 0.08f : value;
                });
            }

            return stone;
        }
    }

    public static Texture2D Cloth
    {
        get
        {
            if (cloth == null)
            {
                cloth = Create("Cloth", 128, (x, y) =>
                {
                    bool weave = (((x / 4) + (y / 4)) & 1) == 0;
                    float baseValue = weave ? 0.96f : 0.89f;
                    float n = Mathf.PerlinNoise(x * 0.1f + 6.6f, y * 0.1f + 1.2f);
                    return baseValue + n * 0.05f;
                });
            }

            return cloth;
        }
    }

    public static Texture2D Snow
    {
        get
        {
            if (snow == null)
            {
                snow = Create("Snow", 128, (x, y) =>
                {
                    float n = Mathf.PerlinNoise(x * 0.22f + 8.1f, y * 0.22f + 3.4f);
                    return 0.94f + n * 0.06f;
                });
            }

            return snow;
        }
    }

    public static Texture2D Ice
    {
        get
        {
            if (ice == null)
            {
                ice = Create("Ice", 256, (x, y) =>
                {
                    float n = Mathf.PerlinNoise(x * 0.05f + 3.1f, y * 0.05f + 7.7f);
                    float crack = Mathf.Abs(Mathf.Sin((x + y * 0.7f + n * 18f) * 0.11f));
                    return crack < 0.045f ? 0.7f : 0.9f + n * 0.1f;
                });
            }

            return ice;
        }
    }

    public static Texture2D Snowflake
    {
        get
        {
            if (snowflake == null)
            {
                const int size = 64;
                snowflake = new Texture2D(size, size, TextureFormat.RGBA32, false);
                snowflake.name = "Snowflake";
                snowflake.wrapMode = TextureWrapMode.Clamp;
                snowflake.filterMode = FilterMode.Bilinear;

                Color[] pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - size * 0.5f) / (size * 0.5f);
                        float dy = (y - size * 0.5f) / (size * 0.5f);
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float alpha = Mathf.Clamp01(1f - distance);
                        alpha *= alpha;
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }

                snowflake.SetPixels(pixels);
                snowflake.Apply(false, true);
            }

            return snowflake;
        }
    }

    /// <summary>按物体名称挑选表面贴图，并设置光泽度 / 金属度。</summary>
    public static void Apply(Material material, string objectName, Vector3 scale)
    {
        if (material == null) return;

        Texture2D texture = PickTexture(objectName);
        float smoothness = 0.18f;
        float metallic = 0f;

        if (Contains(objectName, "River") || Contains(objectName, "Ice"))
        {
            smoothness = 0.9f;
            metallic = 0.08f;
        }
        else if (Contains(objectName, "Snow"))
        {
            smoothness = 0.12f;
        }
        else if (Contains(objectName, "Sandbag") || Contains(objectName, "Flag"))
        {
            smoothness = 0.06f;
        }
        else if (Contains(objectName, "Metal") || Contains(objectName, "Band"))
        {
            smoothness = 0.62f;
            metallic = 0.7f;
        }
        else if (texture == Wood)
        {
            smoothness = 0.1f;
        }
        else if (texture == Stone)
        {
            smoothness = 0.16f;
        }

        float tiling = Mathf.Clamp(Mathf.Max(scale.x, scale.z), 1f, 10f);
        Vector2 tilingVector = new Vector2(tiling, tiling);

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", tilingVector);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tilingVector);
        }

        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
    }

    /// <summary>不按名称区分的通用细噪声，供角色与武器模型使用。</summary>
    public static void ApplyDefault(Material material)
    {
        if (material == null) return;

        Texture2D texture = Noise;
        Vector2 tiling = new Vector2(2f, 2f);

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", tiling);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling);
        }

        // 默认材质不要留 URP 的 0.5 光滑度，否则地面草地上会出现一块刺眼的高光
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
    }

    private static Texture2D PickTexture(string objectName)
    {
        string n = objectName ?? "";

        if (Contains(n, "River") || Contains(n, "Ice")) return Ice;
        if (Contains(n, "Snow")) return Snow;

        // 红旗上的五角星、旗杆顶珠保持干净，不叠布料或木纹
        if (Contains(n, "FlagStar") || Contains(n, "FlagPoleTop")) return Noise;

        if (Contains(n, "Plank") || Contains(n, "Rail") || Contains(n, "Post")
            || Contains(n, "Stringer") || Contains(n, "Crossbeam")
            || Contains(n, "Mooring") || Contains(n, "FlagPole")
            || Contains(n, "Trunk") || Contains(n, "Branch")
            || Contains(n, "HouseBase") || Contains(n, "HouseRoof")
            || Contains(n, "RoofRidge") || Contains(n, "Door")
            || Contains(n, "Window") || Contains(n, "Crate"))
        {
            return Wood;
        }

        if (Contains(n, "Sandbag") || Contains(n, "Flag")) return Cloth;

        if (Contains(n, "Boulder") || Contains(n, "FireStone")
            || Contains(n, "Foundation") || Contains(n, "Chimney")
            || Contains(n, "Wall") || Contains(n, "FlagPoleTop"))
        {
            return Stone;
        }

        return Noise;
    }

    private static bool Contains(string source, string value)
    {
        return source.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Texture2D Create(string name, int size,
                                    System.Func<int, int, float> shade)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);
        texture.name = name;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 4;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = Mathf.Clamp01(shade(x, y));
                pixels[y * size + x] = new Color(value, value, value, 1f);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, true);
        return texture;
    }
}
