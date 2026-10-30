using UnityEngine;

/// <summary>程序化生成全屏贴图：冰冻边框、受伤红边（供 IMGUI 直接绘制）。</summary>
public static class ProceduralTextures
{
    private static Texture2D frost;
    private static Texture2D red;
    private static Texture2D topShade;
    private static Texture2D blood;
    private static Texture2D waterFlow;
    private static Texture2D particleGlow;
    private static Texture2D ground;

    /// <summary>顶部压暗渐变，保证亮天空下的任务与倒计时文字可读。</summary>
    public static Texture2D TopShadeTexture()
    {
        if (topShade == null)
        {
            const int height = 128;
            topShade = new Texture2D(1, height, TextureFormat.RGBA32, false);
            topShade.wrapMode = TextureWrapMode.Clamp;
            topShade.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[height];
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1); // 0 = 贴图底行（屏幕下方），1 = 顶部
                float alpha = Mathf.SmoothStep(0f, 1f, t) * 0.55f;
                pixels[y] = new Color32(0, 0, 0, (byte)(alpha * 255f));
            }

            topShade.SetPixels32(pixels);
            topShade.Apply();
        }
        return topShade;
    }

    public static Texture2D FrostTexture()
    {
        if (frost == null)
        {
            frost = CreateVignette(new Color(0.78f, 0.9f, 1f), 0.42f, true);
        }
        return frost;
    }

    public static Texture2D RedVignetteTexture()
    {
        if (red == null)
        {
            red = CreateVignette(new Color(0.75f, 0.05f, 0.05f), 0.30f, false);
        }
        return red;
    }

    /// <summary>
    /// 中弹溅到镜头上的血迹：四周一摊摊不规则血斑，每摊再往外甩出一串带拖尾的血点，
    /// 中间留空以免挡视线。
    /// </summary>
    public static Texture2D BloodTexture()
    {
        if (blood != null) return blood;

        const int size = 256;
        blood = new Texture2D(size, size, TextureFormat.RGBA32, false);
        blood.wrapMode = TextureWrapMode.Clamp;
        blood.filterMode = FilterMode.Bilinear;

        System.Random rng = new System.Random(1934);
        float[] alphaMap = new float[size * size];

        // 血斑集中在画面边缘，中心保持干净
        const int blobCount = 26;
        for (int i = 0; i < blobCount; i++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float edge = 0.58f + (float)rng.NextDouble() * 0.5f;
            float cx = 0.5f + Mathf.Cos(angle) * edge * 0.72f;
            float cy = 0.5f + Mathf.Sin(angle) * edge * 0.72f;
            float radius = 0.05f + (float)rng.NextDouble() * 0.16f;
            float phase = (float)rng.NextDouble() * Mathf.PI * 2f;

            PaintBlob(alphaMap, size, cx, cy, radius, phase);

            // 从血斑外缘甩出去的血点：沿径向拉长成一小道，方向就是溅射方向
            int droplets = 3 + rng.Next(3);
            for (int k = 0; k < droplets; k++)
            {
                float dir = (float)rng.NextDouble() * Mathf.PI * 2f;
                float distance = radius * (1.0f + (float)rng.NextDouble() * 0.75f);
                float halfLength = radius * (0.12f + (float)rng.NextDouble() * 0.18f);
                float halfWidth = halfLength * (0.3f + (float)rng.NextDouble() * 0.3f);

                PaintStreak(alphaMap, size,
                    cx + Mathf.Cos(dir) * distance,
                    cy + Mathf.Sin(dir) * distance,
                    dir, halfLength, halfWidth);
            }
        }

        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;
                float alpha = alphaMap[index];
                if (alpha <= 0.001f)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // 血斑内部深浅不一，避免一整片死红
                float tone = 0.28f + (float)rng.NextDouble() * 0.14f;
                pixels[index] = new Color32(
                    (byte)(tone * 255f),
                    (byte)(tone * 0.09f * 255f),
                    (byte)(tone * 0.08f * 255f),
                    (byte)(Mathf.Clamp01(alpha * 0.92f) * 255f));
            }
        }

        blood.SetPixels32(pixels);
        blood.Apply();
        return blood;
    }

    /// <summary>画一摊边缘起伏的血斑。</summary>
    private static void PaintBlob(float[] alphaMap, int size,
                                  float cx, float cy, float radius, float phase)
    {
        float inv = 1f / (size - 1);
        int minX = Mathf.Max(0, Mathf.FloorToInt((cx - radius * 1.4f) * (size - 1)));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt((cx + radius * 1.4f) * (size - 1)));
        int minY = Mathf.Max(0, Mathf.FloorToInt((cy - radius * 1.4f) * (size - 1)));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt((cy + radius * 1.4f) * (size - 1)));

        for (int y = minY; y <= maxY; y++)
        {
            float v = y * inv;
            for (int x = minX; x <= maxX; x++)
            {
                float u = x * inv;
                float dx = u - cx;
                float dy = v - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // 沿圆周起伏的半径，让血斑有飞溅的毛边
                float angle = Mathf.Atan2(dy, dx);
                float edge = radius
                    * (0.78f + 0.22f * Mathf.Sin(angle * 3f + phase))
                    * (0.9f + 0.1f * Mathf.Sin(angle * 7f + phase * 2f));
                if (dist >= edge) continue;

                float alpha = 1f - Mathf.SmoothStep(edge * 0.35f, edge, dist);
                int index = y * size + x;
                if (alpha > alphaMap[index]) alphaMap[index] = alpha;
            }
        }
    }

    /// <summary>沿某个方向拉长的一颗小血点，拖尾朝向血斑中心。</summary>
    private static void PaintStreak(float[] alphaMap, int size,
                                    float cx, float cy, float dir,
                                    float halfLength, float halfWidth)
    {
        float inv = 1f / (size - 1);
        float span = halfLength + halfWidth + 0.002f;
        int minX = Mathf.Max(0, Mathf.FloorToInt((cx - span) * (size - 1)));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt((cx + span) * (size - 1)));
        int minY = Mathf.Max(0, Mathf.FloorToInt((cy - span) * (size - 1)));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt((cy + span) * (size - 1)));

        float cos = Mathf.Cos(dir);
        float sin = Mathf.Sin(dir);

        for (int y = minY; y <= maxY; y++)
        {
            float v = y * inv;
            for (int x = minX; x <= maxX; x++)
            {
                float u = x * inv;
                float dx = u - cx;
                float dy = v - cy;

                float along = (dx * cos + dy * sin) / halfLength;
                float across = (-dx * sin + dy * cos) / halfWidth;
                float e = Mathf.Sqrt(along * along + across * across);
                if (e >= 1f) continue;

                float alpha = 1f - Mathf.SmoothStep(0.45f, 1f, e);
                int index = y * size + x;
                if (alpha > alphaMap[index]) alphaMap[index] = alpha;
            }
        }
    }

    /// <summary>水纹贴图：顺流方向的细长拉丝，滚动 UV 就能看出流速。</summary>
    public static Texture2D WaterFlowTexture()
    {
        if (waterFlow != null) return waterFlow;

        const int size = 256;
        waterFlow = new Texture2D(size, size, TextureFormat.RGBA32, false);
        waterFlow.wrapMode = TextureWrapMode.Repeat;
        waterFlow.filterMode = FilterMode.Bilinear;

        System.Random rng = new System.Random(1935);
        Color32[] pixels = new Color32[size * size];

        // 沿 V 方向拉长的噪声条纹
        float[] columns = new float[size];
        for (int i = 0; i < size; i++) columns[i] = 0.82f + (float)rng.NextDouble() * 0.18f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float streak = Mathf.PerlinNoise(x * 0.09f, y * 0.012f);
                float ripple = Mathf.PerlinNoise(x * 0.02f + 17f, y * 0.06f + 5f);
                float value = (streak * 0.65f + ripple * 0.35f) * columns[x];

                // 只有亮条纹会被材质颜色染出来，暗处接近透明
                byte alpha = (byte)(Mathf.Clamp01((value - 0.42f) * 1.8f) * 210f);
                byte tone = (byte)(Mathf.Clamp01(value) * 255f);

                pixels[y * size + x] = new Color32(tone, tone, tone, alpha);
            }
        }

        waterFlow.SetPixels32(pixels);
        waterFlow.Apply();
        return waterFlow;
    }

    /// <summary>粒子用的圆形柔光点。</summary>
    public static Texture2D ParticleGlowTexture()
    {
        if (particleGlow != null) return particleGlow;

        const int size = 32;
        particleGlow = new Texture2D(size, size, TextureFormat.RGBA32, false);
        particleGlow.wrapMode = TextureWrapMode.Clamp;
        particleGlow.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = Mathf.Clamp01(1f - d);
                alpha *= alpha;

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        particleGlow.SetPixels32(pixels);
        particleGlow.Apply();
        return particleGlow;
    }

    /// <summary>草皮斑驳贴图：让河岸地形不是一整片纯色。</summary>
    public static Texture2D GroundTexture()
    {
        if (ground != null) return ground;

        const int size = 256;
        ground = new Texture2D(size, size, TextureFormat.RGBA32, true);
        ground.wrapMode = TextureWrapMode.Repeat;
        ground.filterMode = FilterMode.Trilinear;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float broad = Mathf.PerlinNoise(x * 0.035f + 8.1f, y * 0.035f + 2.7f);
                float fine = Mathf.PerlinNoise(x * 0.19f + 4.3f, y * 0.19f + 6.5f);
                float speck = Mathf.PerlinNoise(x * 0.6f + 1.7f, y * 0.6f + 3.1f);

                float v = Mathf.Clamp01(0.74f + broad * 0.2f
                                        + (fine - 0.5f) * 0.16f
                                        + (speck - 0.5f) * 0.07f);

                pixels[y * size + x] = new Color32(
                    (byte)(v * 255f),
                    (byte)(Mathf.Clamp01(v * 1.02f) * 255f),
                    (byte)(Mathf.Clamp01(v * 0.86f) * 255f),
                    255);
            }
        }

        ground.SetPixels32(pixels);
        ground.Apply(true);
        return ground;
    }

    private static Texture2D CreateVignette(Color color, float innerRadius, bool frosty)
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        System.Random rng = new System.Random(1234);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - size * 0.5f) / (size * 0.5f);
                float ny = (y - size * 0.5f) / (size * 0.5f);
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                float alpha = Mathf.SmoothStep(innerRadius, 1.28f, dist);

                if (frosty)
                {
                    float noise = (float)rng.NextDouble();
                    alpha *= 0.72f + noise * 0.38f;
                }

                alpha = Mathf.Clamp01(alpha);
                pixels[y * size + x] = new Color32(
                    (byte)(color.r * 255f),
                    (byte)(color.g * 255f),
                    (byte)(color.b * 255f),
                    (byte)(alpha * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
