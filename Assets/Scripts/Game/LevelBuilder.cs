using UnityEngine;

/// <summary>
/// 程序化搭建关卡。全部使用引擎自带几何体，保证无需任何美术资源即可运行。
/// 场景：湘江界首渡口北岸。
/// </summary>
public static class LevelBuilder
{
    // ───────────────────────────── 材质与几何体 ─────────────────────────────

    public static Material MakeMaterial(Color color, Color? emission = null)
    {
        // 优先匹配当前渲染管线，避免 URP 项目里使用 Standard 出现紫块
        Shader shader = null;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        }
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

        Material m = new Material(shader);

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        m.color = color;

        if (emission.HasValue)
        {
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission.Value);
            m.EnableKeyword("_EMISSION");
        }

        // 统一叠一层细噪声，避免纯色塑料感；关卡物体随后会按名称换成专用贴图
        SurfaceTextures.ApplyDefault(m);

        return m;
    }

    /// <summary>旗面贴图：Resources 里放的是玩家提供的红军军旗原图。</summary>
    private static void ApplyFlagTexture(GameObject flag)
    {
        if (flag == null) return;

        Texture2D texture = Resources.Load<Texture2D>("RedArmyFlag");
        if (texture == null) return;

        texture.wrapMode = TextureWrapMode.Clamp;

        Renderer renderer = flag.GetComponent<Renderer>();
        if (renderer == null) return;

        Material material = MakeMaterial(Color.white);
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
        }
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one);
        }
        renderer.sharedMaterial = material;
    }

    /// <summary>半透明材质：爆炸烟雾、蒸汽这类"看得穿"的效果用。</summary>
    public static Material MakeTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        material.color = color;
        return material;
    }

    public static GameObject Box(string name, Vector3 position, Vector3 scale, Color color,
                                 Transform parent, bool withCollider = true)
    {
        return Shape(PrimitiveType.Cube, name, position, scale, color, parent, withCollider);
    }

    private static GameObject Shape(PrimitiveType type, string name, Vector3 position,
                                    Vector3 scale, Color color, Transform parent,
                                    bool withCollider, Color? emission = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.transform.localScale = scale;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Material material = MakeMaterial(color, emission);
            SurfaceTextures.Apply(material, name, scale);
            r.sharedMaterial = material;
        }

        if (!withCollider)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = false;
                Object.Destroy(c);
            }
        }

        return go;
    }

    /// <summary>
    /// 桥面胸墙：四道大掩体，间隔六米，左右交替留出侧翼通道。
    /// 桥不再是"一条直走廊"，而是一段一段的争夺战：冲一段、趴一段、再冲一段。
    /// </summary>
    private static void BuildBridgeCover(Transform bridge)
    {
        Color bag = new Color(0.46f, 0.41f, 0.29f);
        Color bagAlt = new Color(0.4f, 0.36f, 0.26f);
        Color bagDark = new Color(0.35f, 0.31f, 0.22f);

        // 0 = 两侧都有；-1 / 1 = 只在左 / 只在右
        float[] stations = { 20.5f, 26.5f, 32.5f, 38.5f };
        int[] sides = { 0, -1, 1, 0 };

        for (int i = 0; i < stations.Length; i++)
        {
            float z0 = stations[i];

            for (int s = 0; s < 2; s++)
            {
                if (sides[i] != 0 && sides[i] != (s == 0 ? -1 : 1)) continue;

                float x = s == 0 ? -4.7f : 4.7f;
                float inward = s == 0 ? 1f : -1f;

                // 三层沙袋、三袋长：齐胸高，蹲下能完全遮住，站起来露个钢盔
                for (int row = 0; row < 3; row++)
                {
                    for (int b = 0; b < 3; b++)
                    {
                        // 最高一层中间抽掉一袋，留出射击孔
                        if (row == 2 && b == 1 && i % 2 == 0) continue;

                        float zOff = (b - 1) * 1.15f;
                        float y = 0.51f + row * 0.34f;

                        GameObject go = Box("DeckSandbag",
                            new Vector3(x + Random.Range(-0.05f, 0.05f), y, z0 + zOff),
                            new Vector3(0.82f, 0.34f, 1.12f),
                            (b + row) % 2 == 0 ? bag : (row == 0 ? bagDark : bagAlt), bridge);
                        go.transform.rotation = Quaternion.Euler(
                            Random.Range(-2f, 2f), (b % 2 == 0 ? -3f : 3f) + row * 2f, 0f);
                    }
                }

                // 单侧墙加一段折向桥心的短墙，围成 L 形：贴墙打的人也有侧后掩护
                if (sides[i] != 0)
                {
                    float zEnd = z0 + (i % 2 == 0 ? 1.15f : -1.15f);
                    for (int row = 0; row < 2; row++)
                    {
                        for (int b = 0; b < 2; b++)
                        {
                            GameObject elbow = Box("DeckSandbagElbow",
                                new Vector3(x + inward * (0.8f + b * 1.02f),
                                    0.51f + row * 0.34f, zEnd + Random.Range(-0.04f, 0.04f)),
                                new Vector3(1.12f, 0.34f, 0.82f),
                                (b + row) % 2 == 0 ? bagAlt : bag, bridge);
                            elbow.transform.rotation = Quaternion.Euler(0f,
                                Random.Range(-4f, 4f), 0f);
                        }
                    }
                }
            }
        }

        // ── 桥上的重物：弹药箱、翻倒的大车、散落的木板 ──
        // 弹药箱垛（靠右舷，弥补右侧 26.5 米那段没有胸墙的空当）
        Color crate = new Color(0.36f, 0.28f, 0.16f);
        Color crateBand = new Color(0.2f, 0.15f, 0.09f);
        for (int c = 0; c < 2; c++)
        {
            Box("AmmoCrate", new Vector3(3.35f, 0.62f + c * 0.56f, 23.6f),
                new Vector3(0.95f, 0.55f, 0.72f), crate, bridge);
            Box("AmmoCrateBand", new Vector3(3.35f, 0.62f + c * 0.56f, 23.6f),
                new Vector3(0.99f, 0.07f, 0.76f), crateBand, bridge);
        }
        Box("AmmoCrate", new Vector3(2.55f, 0.62f, 24.15f),
            new Vector3(0.8f, 0.55f, 0.68f), crate, bridge);

        // 大车：两轮木车横在桥心偏左，车板当半身掩体
        Color cartWood = new Color(0.33f, 0.24f, 0.14f);
        Color cartWoodDark = new Color(0.24f, 0.17f, 0.1f);
        float cartX = -2.7f;
        float cartZ = 35.1f;
        Box("CartBed", new Vector3(cartX, 0.95f, cartZ),
            new Vector3(1.4f, 0.16f, 2.2f), cartWood, bridge);
        Box("CartSideL", new Vector3(cartX - 0.66f, 1.19f, cartZ),
            new Vector3(0.09f, 0.42f, 2.2f), cartWoodDark, bridge);
        Box("CartSideR", new Vector3(cartX + 0.66f, 1.19f, cartZ),
            new Vector3(0.09f, 0.42f, 2.2f), cartWoodDark, bridge);
        Box("CartEndF", new Vector3(cartX, 1.19f, cartZ + 1.06f),
            new Vector3(1.4f, 0.42f, 0.09f), cartWoodDark, bridge);
        Box("CartEndB", new Vector3(cartX, 1.19f, cartZ - 1.06f),
            new Vector3(1.4f, 0.42f, 0.09f), cartWoodDark, bridge);
        for (int w = 0; w < 2; w++)
        {
            GameObject wheel = LocalShape(PrimitiveType.Cylinder, "CartWheel", bridge,
                new Vector3(cartX + (w == 0 ? -0.78f : 0.78f), 0.89f, cartZ),
                new Vector3(1.1f, 0.07f, 1.1f), cartWoodDark, false);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        // 车辕卸下来搭在桥板上
        GameObject shaftL = Box("CartShaftL", new Vector3(cartX - 0.48f, 0.43f, cartZ + 1.95f),
            new Vector3(0.09f, 0.09f, 1.6f), cartWood, bridge);
        shaftL.transform.rotation = Quaternion.Euler(-3f, 0f, 0f);
        GameObject shaftR = Box("CartShaftR", new Vector3(cartX + 0.48f, 0.41f, cartZ + 1.95f),
            new Vector3(0.09f, 0.09f, 1.6f), cartWood, bridge);
        shaftR.transform.rotation = Quaternion.Euler(-6f, 0f, 0f);

        // 散落木板与踩破的沙袋：纯装饰，不加碰撞，免得绊住人
        for (int p = 0; p < 6; p++)
        {
            GameObject plank = Box("LoosePlank",
                new Vector3(Random.Range(-4f, 4f), 0.36f, Random.Range(18f, 41f)),
                new Vector3(Random.Range(0.5f, 1.2f), 0.035f, Random.Range(0.12f, 0.24f)),
                p % 2 == 0 ? crate : crateBand, bridge, false);
            plank.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
        }
        for (int p = 0; p < 3; p++)
        {
            GameObject spill = Box("SpilledBag",
                new Vector3(Random.Range(-4.2f, 4.2f), 0.37f, Random.Range(21f, 40f)),
                new Vector3(0.6f, 0.16f, 0.42f), bagDark, bridge, false);
            spill.transform.rotation = Quaternion.Euler(Random.Range(-6f, 6f),
                Random.Range(0f, 180f), Random.Range(-4f, 4f));
        }
    }

    /// <summary>以父物体局部坐标创建部件，常用于树、火堆等组合模型。</summary>
    private static GameObject LocalShape(PrimitiveType type, string name, Transform parent,
                                         Vector3 localPos, Vector3 localScale, Color color,
                                         bool withCollider = false, Color? emission = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Material material = MakeMaterial(color, emission);
            SurfaceTextures.Apply(material, name, localScale);
            r.sharedMaterial = material;
        }

        if (!withCollider)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = false;
                Object.Destroy(c);
            }
        }

        return go;
    }

    // ───────────────────────────── 关卡 ─────────────────────────────

    public static void BuildEnvironment(Transform root, out Transform bridgePoint,
                                        out Transform bridgeRoot, out Transform[] spawnPoints)
    {
        // 湘江河槽：河床比水面低 1.5 米，人站在河里只露头
        Color groundColor = new Color(0.36f, 0.39f, 0.41f);
        Color slopeColor = new Color(0.31f, 0.33f, 0.34f);
        Color bedColor = new Color(0.22f, 0.24f, 0.25f);

        // 近岸地面：在 x 11~20 / z 7.6~9.2 处挖出战壕开口
        // 南侧多铺 30 米：敌军一旦过桥，友军还能往村里退，不会背靠石埂无路可走
        Box("GroundWest", new Vector3(-24.5f, -0.5f, -28.25f),
            new Vector3(71f, 1f, 93.5f), groundColor, root);
        Box("GroundEast", new Vector3(40f, -0.5f, -28.25f),
            new Vector3(40f, 1f, 93.5f), groundColor, root);
        Box("GroundTrenchNorth", new Vector3(15.5f, -0.5f, 13.85f),
            new Vector3(9f, 1f, 9.3f), groundColor, root);
        Box("GroundTrenchSouth", new Vector3(15.5f, -0.5f, -33.7f),
            new Vector3(9f, 1f, 82.6f), groundColor, root);

        // 远岸地面
        Box("GroundFar", new Vector3(0f, -0.5f, 68.75f),
            new Vector3(120f, 1f, 54.5f), groundColor, root);

        // 河床与两岸斜坡（24.6°，可以直接走下去）
        Box("BedSoil", new Vector3(0f, -1.75f, 30f), new Vector3(120f, 0.3f, 16.4f),
            bedColor, root);
        GameObject nearSlope = Box("NearSlope", new Vector3(0f, -0.936f, 20.1f),
            new Vector3(120f, 0.3f, 3.9f), slopeColor, root);
        nearSlope.transform.rotation = Quaternion.Euler(24.6f, 0f, 0f);
        GameObject farSlope = Box("FarSlope", new Vector3(0f, -0.936f, 39.9f),
            new Vector3(120f, 0.3f, 3.9f), slopeColor, root);
        farSlope.transform.rotation = Quaternion.Euler(-24.6f, 0f, 0f);

        // 水面（低于岸顶 0.1 米），顺江流向下游
        GameObject riverWater = Box("RiverWater", new Vector3(0f, -0.1f, 30f),
            new Vector3(120f, 0.06f, 24f), new Color(0.07f, 0.13f, 0.18f), root, false);
        RiverFlow.Attach(riverWater, new Vector3(120f, 0.06f, 24f), new Vector3(1f, 0f, 0f), 13f);

        // 两岸：被踩实的雪泥
        Box("NearBank", new Vector3(0f, 0.15f, 16f), new Vector3(120f, 0.3f, 4f),
            new Color(0.33f, 0.32f, 0.3f), root);
        Box("FarBank", new Vector3(0f, 0.15f, 44f), new Vector3(120f, 0.3f, 4f),
            new Color(0.33f, 0.32f, 0.3f), root);

        // 浮桥：桥面加宽到 11.2 米，可容四列纵队同时通过
        // 整座桥挂在独立的 Bridge 节点下，炸桥时才方便按段拆开
        GameObject bridgeGo = new GameObject("Bridge");
        bridgeGo.transform.SetParent(root, false);
        Transform bridge = bridgeGo.transform;
        bridgeRoot = bridge;

        const float halfWidth = 5.6f;
        Color wood = new Color(0.36f, 0.25f, 0.14f);
        Color woodLight = new Color(0.41f, 0.29f, 0.17f);

        for (int i = 0; i < 14; i++)
        {
            float z = 16.5f + i * 2f;
            Box("Plank_" + i, new Vector3(0f, 0.28f, z),
                new Vector3(11.2f, 0.12f, 1.7f), i % 3 == 0 ? woodLight : wood, bridge);
        }
        // 两侧压边木，桥面边缘不再是薄薄一条线
        Box("CurbL", new Vector3(-5.52f, 0.36f, 29.5f), new Vector3(0.18f, 0.2f, 27f), woodLight, bridge);
        Box("CurbR", new Vector3(5.52f, 0.36f, 29.5f), new Vector3(0.18f, 0.2f, 27f), woodLight, bridge);

        // 上、中两道护栏：抬到齐胸高，人能趴在上面打，人也翻不下去
        Box("RailL", new Vector3(-halfWidth, 1.5f, 30f), new Vector3(0.1f, 0.1f, 28f), wood, bridge);
        Box("RailR", new Vector3(halfWidth, 1.5f, 30f), new Vector3(0.1f, 0.1f, 28f), wood, bridge);
        Box("MidRailL", new Vector3(-halfWidth, 0.95f, 30f), new Vector3(0.08f, 0.08f, 28f), wood, bridge);
        Box("MidRailR", new Vector3(halfWidth, 0.95f, 30f), new Vector3(0.08f, 0.08f, 28f), wood, bridge);
        for (int i = 0; i < 8; i++)
        {
            float z = 17f + i * 4f;
            Box("PostL_" + i, new Vector3(-halfWidth, 0.85f, z), new Vector3(0.16f, 1.9f, 0.16f), wood, bridge);
            Box("PostR_" + i, new Vector3(halfWidth, 0.85f, z), new Vector3(0.16f, 1.9f, 0.16f), wood, bridge);
        }

        // 护栏斜撑与捆扎绳索
        Color rope = new Color(0.34f, 0.29f, 0.19f);
        for (int i = 0; i < 7; i++)
        {
            float z = 19f + i * 4f;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -halfWidth : halfWidth;
                GameObject brace = Box("RailBrace", new Vector3(x, 1.12f, z - 2f),
                    new Vector3(0.07f, 0.07f, 4.1f), wood, bridge);
                brace.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            }
        }
        for (int i = 0; i < 8; i++)
        {
            float z = 17f + i * 4f;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -halfWidth : halfWidth;
                Box("Lashing", new Vector3(x, 1.28f, z), new Vector3(0.15f, 0.12f, 0.15f), rope, bridge);
            }
        }

        // 浮桥纵梁与横梁：让桥面不再是一块块悬空的木板
        Color beam = new Color(0.26f, 0.18f, 0.1f);
        Box("StringerL", new Vector3(-5.0f, 0.2f, 30f), new Vector3(0.18f, 0.18f, 28f), beam, bridge);
        Box("StringerR", new Vector3(5.0f, 0.2f, 30f), new Vector3(0.18f, 0.18f, 28f), beam, bridge);
        Box("StringerCL", new Vector3(-2.5f, 0.2f, 30f), new Vector3(0.16f, 0.16f, 28f), beam, bridge);
        Box("StringerCR", new Vector3(2.5f, 0.2f, 30f), new Vector3(0.16f, 0.16f, 28f), beam, bridge);
        Box("StringerC", new Vector3(0f, 0.2f, 30f), new Vector3(0.16f, 0.16f, 28f), beam, bridge);
        for (int i = 0; i < 10; i++)
        {
            float z = 16.5f + i * 3f;
            Box("Crossbeam_" + i, new Vector3(0f, 0.19f, z),
                new Vector3(11.3f, 0.13f, 0.16f), beam, bridge);
        }
        // 桥头系缆桩
        Box("MooringPost", new Vector3(5.9f, 0.45f, 16.2f), new Vector3(0.22f, 0.95f, 0.22f), beam, bridge);
        Box("MooringPost2", new Vector3(-5.9f, 0.45f, 16.2f), new Vector3(0.22f, 0.95f, 0.22f), beam, bridge);

        // 浮桥支撑：江面上的浮箱 + 立柱，别让桥面悬空
        Color pontoonWood = new Color(0.26f, 0.19f, 0.12f);
        for (int i = 0; i < 6; i++)
        {
            float z = 19.4f + i * 4.2f;

            Box("PontoonPlank", new Vector3(0f, -0.34f, z),
                new Vector3(10.4f, 0.32f, 4.0f), pontoonWood, bridge);
            Box("PontoonPlankRimL", new Vector3(-4.8f, -0.22f, z),
                new Vector3(0.16f, 0.24f, 4.0f), beam, bridge);
            Box("PontoonPlankRimR", new Vector3(4.8f, -0.22f, z),
                new Vector3(0.16f, 0.24f, 4.0f), beam, bridge);
            // 浮箱中缝，一眼能看出是两只木船并绑而成
            Box("PontoonSplit", new Vector3(0f, -0.17f, z),
                new Vector3(0.22f, 0.06f, 4.0f), beam, bridge);

            for (int s = 0; s < 4; s++)
            {
                float x = s == 0 ? -3.9f : (s == 1 ? 3.9f : (s == 2 ? -1.6f : 1.6f));
                Box("PontoonPost", new Vector3(x, -0.08f, z),
                    new Vector3(0.14f, 0.78f, 0.14f), beam, bridge);
            }
            // 船头船尾的挡水帮
            Box("PontoonBowL", new Vector3(-2.8f, -0.3f, z - 2.05f),
                new Vector3(3.7f, 0.2f, 0.12f), pontoonWood, bridge);
            Box("PontoonBowR", new Vector3(2.8f, -0.3f, z - 2.05f),
                new Vector3(3.7f, 0.2f, 0.12f), pontoonWood, bridge);
        }

        // 桥面两侧的沙袋掩体：大部队走中间，玩家和敌军在两侧堤下对射
        BuildBridgeCover(bridge);

        // 沙袋掩体（留出桥口），每处由成排沙袋堆成
        for (int i = 0; i < 10; i++)
        {
            float x = -9f + i * 2f;
            if (Mathf.Abs(x) < 3.9f) continue;
            SandbagStack(root, new Vector3(x, 0f, 10f));
        }
        for (int i = 0; i < 6; i++)
        {
            float x = -14f + i * 2.4f;
            SandbagStack(root, new Vector3(x, 0f, 4f));
        }

        // 出生点后方（南侧）的第二道沙袋掩体：
        // 敌军过桥后友军可以边打边退到这里，不会一路退到南边石埂
        for (int i = 0; i < 14; i++)
        {
            float x = -17.5f + i * 2.7f;
            if (Mathf.Abs(x) < 2.8f) continue;   // 中间留出通往村里的路
            SandbagStack(root, new Vector3(x, 0f, -2f));
        }

        // 村里第二道防线的侧射位：退进村子还能借着沙袋还击
        SandbagStack(root, new Vector3(-8.6f, 0f, -8f));
        SandbagStack(root, new Vector3(-5.6f, 0f, -8f));
        SandbagStack(root, new Vector3(5.6f, 0f, -8f));
        SandbagStack(root, new Vector3(8.6f, 0f, -8f));

        // 工事：右翼战壕 + 沙袋碉堡，左翼前哨，两翼拒马
        BuildTrench(root);
        BuildBunker(root, new Vector3(15f, 0f, 12.7f));
        SandbagStack(root, new Vector3(-22.6f, 0f, 11.6f));
        SandbagStack(root, new Vector3(-20f, 0f, 11.6f));
        SandbagStack(root, new Vector3(-17.4f, 0f, 11.6f));
        Abatis(root, 21.6f, 12.4f, 6, 1.55f);
        Abatis(root, -28.2f, 12.4f, 5, 1.55f);

        // 村庄
        House(root, new Vector3(-18f, 0f, 0f), new Vector3(8f, 4f, 7f));
        House(root, new Vector3(-27f, 0f, 7f), new Vector3(6f, 3.5f, 6f));
        House(root, new Vector3(-14f, 0f, 8f), new Vector3(5f, 3f, 5f));

        // 两岸的建筑群：把地图左右两侧的空地填满
        BuildVillage(root);

        // 战场痕迹：弹坑、浮冰、遗弃的枪盔、铁丝网、炊烟
        BuildBattlefieldDebris(root);

        // 火堆（信仰恢复点）：石圈 + 交叉木柴 + 双层火苗
        GameObject fireGroup = new GameObject("Campfire");
        fireGroup.transform.SetParent(root, true);
        fireGroup.transform.position = new Vector3(-8f, 0f, 3f);
        Transform fireRoot = fireGroup.transform;

        Color fireStone = new Color(0.3f, 0.29f, 0.26f);
        for (int i = 0; i < 9; i++)
        {
            float angle = i * 40f * Mathf.Deg2Rad;
            GameObject stone = LocalShape(PrimitiveType.Cube, "FireStone_" + i, fireRoot,
                new Vector3(Mathf.Cos(angle) * 0.95f, 0.12f, Mathf.Sin(angle) * 0.95f),
                new Vector3(0.3f, 0.24f, 0.28f), fireStone, true);
            stone.transform.localRotation = Quaternion.Euler(0f, i * 37f, 0f);
        }

        Color charredWood = new Color(0.16f, 0.11f, 0.07f);
        for (int i = 0; i < 4; i++)
        {
            GameObject log = LocalShape(PrimitiveType.Cylinder, "FireLog_" + i, fireRoot,
                new Vector3(0f, 0.22f, 0f), new Vector3(0.1f, 0.55f, 0.1f), charredWood);
            log.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f)
                                        * Quaternion.Euler(90f, 0f, 0f);
        }

        Color flameOuter = new Color(0.95f, 0.35f, 0.08f);
        Color flameInner = new Color(1f, 0.72f, 0.22f);
        GameObject flame = LocalShape(PrimitiveType.Cube, "Flame", fireRoot,
            new Vector3(0f, 0.6f, 0f), new Vector3(0.36f, 0.75f, 0.36f),
            flameOuter, false, flameOuter);
        flame.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        GameObject flameCore = LocalShape(PrimitiveType.Cube, "FlameCore", fireRoot,
            new Vector3(0f, 0.64f, 0f), new Vector3(0.2f, 0.46f, 0.2f),
            flameInner, false, flameInner);
        flameCore.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);

        GameObject fireLightGo = new GameObject("FireLight");
        fireLightGo.transform.SetParent(fireRoot, false);
        fireLightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        Light fireLight = fireLightGo.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.55f, 0.25f);
        fireLight.intensity = 2.4f;
        fireLight.range = 11f;
        FireFlicker flicker = fireLightGo.AddComponent<FireFlicker>();
        flicker.flame = flame.transform;

        GameObject fireTrigger = new GameObject("CampfireTrigger");
        fireTrigger.transform.SetParent(root, true);
        fireTrigger.transform.position = new Vector3(-8f, 0.8f, 3f);
        fireTrigger.tag = "Campfire";
        SphereCollider fireCol = fireTrigger.AddComponent<SphereCollider>();
        fireCol.isTrigger = true;
        fireCol.radius = 2.6f;

        // 江水寒冷区（覆盖整条河）
        GameObject coldZone = new GameObject("ColdWater");
        coldZone.transform.SetParent(root, true);
        coldZone.transform.position = new Vector3(0f, -0.9f, 30f);
        coldZone.tag = "Blizzard";
        BoxCollider coldCol = coldZone.AddComponent<BoxCollider>();
        coldCol.isTrigger = true;
        coldCol.size = new Vector3(120f, 3.2f, 24f);

        // 弹药箱（会定时刷新）
        AmmoCrate(root, new Vector3(-6f, 0.3f, 7.5f));
        AmmoCrate(root, new Vector3(6f, 0.3f, 7.5f));
        AmmoCrate(root, new Vector3(-13f, 0.3f, 5f));
        AmmoCrate(root, new Vector3(12f, 0.3f, 4f));
        AmmoCrate(root, new Vector3(-20f, 0.3f, 6f));

        // 军旗：旗面用真实的中国工农红军军旗图案（五角星、镰刀锤头、"中国工农红军"）
        Box("FlagPole", new Vector3(-4f, 2f, 5f), new Vector3(0.12f, 4f, 0.12f),
            new Color(0.2f, 0.18f, 0.15f), root);
        GameObject flag = Box("Flag", new Vector3(-3.37f, 3.4f, 5f),
            new Vector3(1.26f, 0.9f, 0.045f), Color.white, root, false);
        ApplyFlagTexture(flag);
        Shape(PrimitiveType.Sphere, "FlagPoleTop", new Vector3(-4f, 4.08f, 5f),
            new Vector3(0.22f, 0.22f, 0.22f), new Color(0.5f, 0.42f, 0.22f), root, false);

        // 江面浮冰
        Color ice = new Color(0.64f, 0.72f, 0.77f);
        for (int i = 0; i < 12; i++)
        {
            float x = -16f + i * 3f;
            if (Mathf.Abs(x) < 2.1f) continue;
            float z = 23f + (i % 4) * 3.6f;
            Box("IceFloe_" + i, new Vector3(x, -0.07f, z),
                new Vector3(1.2f + (i % 3) * 0.55f, 0.04f, 0.8f + (i % 2) * 0.3f),
                ice, root, false);
        }

        // 积雪（纯视觉，不阻挡移动）
        Color snow = new Color(0.78f, 0.8f, 0.83f);
        for (int i = 0; i < 14; i++)
        {
            float x = -26f + i * 4.1f;
            float z = 1.5f + (i % 5) * 2.7f;
            Box("SnowPatch_" + i, new Vector3(x, 0.03f, z),
                new Vector3(1.6f + (i % 3) * 0.8f, 0.05f, 1.1f + (i % 2) * 0.5f),
                snow, root, false);
        }

        // 岸边礁石
        Color boulder = new Color(0.36f, 0.36f, 0.34f);
        for (int i = 0; i < 9; i++)
        {
            float x = -32f + i * 8f;
            float z = (i % 2 == 0) ? 16.4f : 45.6f;
            if (Mathf.Abs(x) < 5f) continue;
            GameObject rockGo = Shape(PrimitiveType.Sphere, "Boulder_" + i,
                new Vector3(x, 0.34f, z),
                new Vector3(1.1f + (i % 3) * 0.5f, 0.65f, 1f + (i % 2) * 0.4f),
                boulder, root, true);
            rockGo.transform.rotation = Quaternion.Euler(0f, i * 37f, 0f);
        }

        // 枯树：远岸与两翼，撑起场景纵深
        BareTree(root, new Vector3(-27f, 0f, 47f), 1.1f, 20f);
        BareTree(root, new Vector3(-19f, 0f, 50f), 0.9f, 65f);
        BareTree(root, new Vector3(23f, 0f, 48f), 1.05f, 110f);
        BareTree(root, new Vector3(31f, 0f, 51f), 0.85f, 150f);
        BareTree(root, new Vector3(-33f, 0f, 24f), 0.95f, 200f);
        BareTree(root, new Vector3(34f, 0f, 18f), 1.0f, 260f);
        BareTree(root, new Vector3(30f, 0f, 38f), 0.8f, 300f);

        // 边界（防止跑出场景）：做成压着积雪的土石埂，别在远景里留下一条黑带
        Color rock = new Color(0.33f, 0.36f, 0.38f);
        Color capSnow = new Color(0.68f, 0.73f, 0.78f);
        Box("WallW", new Vector3(-58f, 3f, 10f), new Vector3(2f, 6f, 170f), rock, root);
        Box("WallE", new Vector3(58f, 3f, 10f), new Vector3(2f, 6f, 170f), rock, root);
        Box("WallN", new Vector3(0f, 3f, 92f), new Vector3(120f, 6f, 2f), rock, root);
        Box("WallS", new Vector3(0f, 3f, -72f), new Vector3(120f, 6f, 2f), rock, root);
        Box("WallWSnow", new Vector3(-58f, 6.05f, 10f), new Vector3(2.4f, 0.22f, 170.4f),
            capSnow, root, false);
        Box("WallESnow", new Vector3(58f, 6.05f, 10f), new Vector3(2.4f, 0.22f, 170.4f),
            capSnow, root, false);
        Box("WallNSnow", new Vector3(0f, 6.05f, 92f), new Vector3(120.4f, 0.22f, 2.4f),
            capSnow, root, false);
        Box("WallSSnow", new Vector3(0f, 6.05f, -72f), new Vector3(120.4f, 0.22f, 2.4f),
            capSnow, root, false);

        // 远山与远处地面：补出地平线层次，让雾里的对岸不只是空天
        Box("FarGround", new Vector3(0f, -0.5f, 150f), new Vector3(260f, 1f, 120f),
            new Color(0.3f, 0.33f, 0.35f), root, false);

        Color farMountain = new Color(0.27f, 0.32f, 0.38f);
        Shape(PrimitiveType.Sphere, "MountainA", new Vector3(-48f, -8f, 152f),
            new Vector3(96f, 40f, 38f), farMountain, root, false);
        Shape(PrimitiveType.Sphere, "MountainB", new Vector3(6f, -9f, 162f),
            new Vector3(126f, 44f, 44f), farMountain, root, false);
        Shape(PrimitiveType.Sphere, "MountainC", new Vector3(56f, -8f, 154f),
            new Vector3(88f, 38f, 38f), farMountain, root, false);
        Shape(PrimitiveType.Sphere, "MountainD", new Vector3(-10f, -12f, 178f),
            new Vector3(150f, 52f, 46f), farMountain, root, false);

        // 桥头炸点
        GameObject bp = new GameObject("BridgePoint");
        bp.transform.SetParent(root, true);
        bp.transform.position = new Vector3(0f, 0.5f, 18.5f);
        bridgePoint = bp.transform;

        // 敌军后方公路：从渡口直通北山脚，追兵就沿着这条路压上来
        Color roadColor = new Color(0.28f, 0.25f, 0.2f);
        Color rutColor = new Color(0.22f, 0.19f, 0.15f);

        Box("RearRoad", new Vector3(0f, 0.02f, 68.2f), new Vector3(7.4f, 0.04f, 44f),
            roadColor, root, false);
        Box("RutL", new Vector3(-1.15f, 0.045f, 68.2f), new Vector3(0.5f, 0.02f, 43.6f),
            rutColor, root, false);
        Box("RutR", new Vector3(1.15f, 0.045f, 68.2f), new Vector3(0.5f, 0.02f, 43.6f),
            rutColor, root, false);

        // 通往两侧炮位的车辙岔道
        Box("GunTrackL", new Vector3(-8f, 0.025f, 65.5f), new Vector3(9f, 0.03f, 3f),
            rutColor, root, false);
        Box("GunTrackR", new Vector3(8f, 0.025f, 64.5f), new Vector3(9f, 0.03f, 3f),
            rutColor, root, false);

        // 北侧雪埂：公路两侧堆起连绵的雪丘，中间只留公路缺口
        Color driftColor = new Color(0.74f, 0.77f, 0.8f);
        Color driftSoil = new Color(0.52f, 0.53f, 0.52f);

        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 6; i++)
            {
                float x = side * (9f + i * 9.5f);
                float height = 2.6f + (i % 2) * 0.4f;
                Shape(PrimitiveType.Sphere, "RearSnowDrift", new Vector3(x, -0.15f, 80.5f),
                    new Vector3(10f, height, 7f), driftColor, root, true);
            }

            // 雪埂脚下的土色，压住球体与地面的接缝
            for (int i = 0; i < 3; i++)
            {
                float x = side * (11f + i * 15f);
                Box("RearDriftSoil", new Vector3(x, 0.12f, 77.9f),
                    new Vector3(10f, 0.32f, 2.4f), driftSoil, root, false);
            }
        }

        // 缺口两侧的哨位沙袋
        SandbagStack(root, new Vector3(-5.8f, 0f, 82.4f));
        SandbagStack(root, new Vector3(5.8f, 0f, 82.4f));

        // 公路两侧的枯树与碎石
        BareTree(root, new Vector3(-8.5f, 0f, 74f), 0.95f, 340f);
        BareTree(root, new Vector3(9.5f, 0f, 76.5f), 0.85f, 20f);
        BareTree(root, new Vector3(-10f, 0f, 58f), 1.0f, 80f);
        BareTree(root, new Vector3(10.5f, 0f, 54f), 0.9f, 140f);
        for (int i = 0; i < 10; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * UnityEngine.Random.Range(4.2f, 9f);
            float z = UnityEngine.Random.Range(48f, 88f);
            GameObject stone = Box("RoadStone_" + i, new Vector3(x, 0.12f, z),
                new Vector3(UnityEngine.Random.Range(0.3f, 0.8f), 0.24f,
                            UnityEngine.Random.Range(0.3f, 0.7f)),
                new Color(0.3f, 0.3f, 0.3f), root, false);
            stone.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        }

        // 敌军后方出生点：全部藏在土埂之后，沿公路列队压上来
        Vector3[] spawnPositions =
        {
            new Vector3(-2.2f, 0f, 87f),
            new Vector3(2.2f, 0f, 85f),
            new Vector3(0f, 0f, 89f),
            new Vector3(-1.6f, 0f, 83.5f),
            new Vector3(1.6f, 0f, 86.2f),
            new Vector3(0.4f, 0f, 84.2f),
        };
        spawnPoints = new Transform[spawnPositions.Length];
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            GameObject p = new GameObject("EnemySpawn_" + i);
            p.transform.SetParent(root, true);
            p.transform.position = spawnPositions[i];
            spawnPoints[i] = p.transform;
        }
    }

    // ───────────────────────────── 细节 ─────────────────────────────

    private static void House(Transform root, Vector3 pos, Vector3 size, float yaw = 0f)
    {
        Color wall = new Color(0.36f, 0.32f, 0.27f);
        Color stone = new Color(0.3f, 0.29f, 0.27f);
        Color roof = new Color(0.24f, 0.15f, 0.11f);
        Color woodDark = new Color(0.17f, 0.12f, 0.08f);

        // 门窗都开在本地 -z 面；yaw=180 时正面转向 +z，背靠出生点的村子才能看见门窗
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 P(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
        float frontZ = P(0f, 0f, -size.z * 0.5f - 0.04f).z;
        float outZ = (rot * Vector3.back).z;
        float postLeft = P(-0.82f, 0f, 0f).x;
        float postRight = P(0.82f, 0f, 0f).x;

        // 石砌墙基与土墙
        Box("HouseFoundation", P(0f, 0.3f, 0f),
            new Vector3(size.x + 0.25f, 0.6f, size.z + 0.25f), stone, root);
        Box("HouseBase", P(0f, size.y * 0.5f, 0f), size, wall, root);

        // 屋顶、屋脊、烟囱
        Box("HouseRoof", P(0f, size.y + 0.3f, 0f),
            new Vector3(size.x + 1f, 0.6f, size.z + 1f), roof, root);
        Box("RoofRidge", P(0f, size.y + 0.68f, 0f),
            new Vector3(size.x + 1.1f, 0.16f, 0.34f), new Color(0.18f, 0.11f, 0.08f), root);
        Box("Chimney", P(size.x * 0.3f, size.y + 0.95f, size.z * 0.15f),
            new Vector3(0.56f, 1.4f, 0.56f), stone, root);

        // 门与门框
        Box("HouseDoor", P(0f, 1.2f, -size.z * 0.5f - 0.02f),
            new Vector3(1.4f, 2.4f, 0.1f), new Color(0.18f, 0.13f, 0.09f), root, false);
        Box("DoorPostL", new Vector3(postLeft, 1.28f, frontZ),
            new Vector3(0.16f, 2.56f, 0.14f), woodDark, root, false);
        Box("DoorPostR", new Vector3(postRight, 1.28f, frontZ),
            new Vector3(0.16f, 2.56f, 0.14f), woodDark, root, false);
        Box("DoorLintel", new Vector3(pos.x, 2.56f, frontZ),
            new Vector3(1.8f, 0.18f, 0.14f), woodDark, root, false);

        // 两扇窗户与木窗棂
        for (int i = 0; i < 2; i++)
        {
            float wx = P(i == 0 ? -size.x * 0.28f : size.x * 0.28f, 0f, 0f).x;
            float wy = size.y * 0.64f;
            float frameZ = frontZ + outZ * 0.03f;
            float barZ = frontZ + outZ * 0.06f;

            Box("Window_" + i, new Vector3(wx, wy, frontZ),
                new Vector3(1.15f, 1.0f, 0.08f), new Color(0.09f, 0.09f, 0.1f), root, false);
            // 窗框四边
            Box("WindowTop_" + i, new Vector3(wx, wy + 0.58f, frameZ),
                new Vector3(1.34f, 0.1f, 0.08f), woodDark, root, false);
            Box("WindowBottom_" + i, new Vector3(wx, wy - 0.58f, frameZ),
                new Vector3(1.34f, 0.1f, 0.08f), woodDark, root, false);
            Box("WindowLeft_" + i, new Vector3(wx - 0.62f, wy, frameZ),
                new Vector3(0.1f, 1.18f, 0.08f), woodDark, root, false);
            Box("WindowRight_" + i, new Vector3(wx + 0.62f, wy, frameZ),
                new Vector3(0.1f, 1.18f, 0.08f), woodDark, root, false);
            Box("WindowBarV_" + i, new Vector3(wx, wy, barZ),
                new Vector3(0.07f, 1.02f, 0.03f), woodDark, root, false);
            Box("WindowBarH_" + i, new Vector3(wx, wy, barZ),
                new Vector3(1.17f, 0.07f, 0.03f), woodDark, root, false);
        }
    }

    /// <summary>屋前的暖色补光，压在背光面一侧。</summary>
    private static void AddFillLight(Transform root, Vector3 pos, float intensity, float range)
    {
        GameObject go = new GameObject("FillLight");
        go.transform.SetParent(root, true);
        go.transform.position = pos;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.87f, 0.7f);
        light.intensity = intensity;
        light.range = range;
    }

    /// <summary>
    /// 两岸建筑群：西村、东村、对岸村落，加上废墟、草垛、篱笆与水井，
    /// 让玩家两侧与江对岸都有东西可看，不再是空荡荡的雪地。
    /// </summary>
    private static void BuildVillage(Transform root)
    {
        // 出生点身后的村落：主角背靠村子守渡口，转身就能看见后方的屋脊与村道
        Color laneColor = new Color(0.29f, 0.26f, 0.21f);
        Box("VillageLane", new Vector3(0f, 0.02f, -9f), new Vector3(3.2f, 0.04f, 34f),
            laneColor, root, false);
        // 村子在玩家背后，房屋正面统一朝向 +z，转身就能看见门窗
        House(root, new Vector3(-10f, 0f, -13f), new Vector3(8f, 4.2f, 7f), 180f);
        House(root, new Vector3(7f, 0f, -18f), new Vector3(7f, 3.6f, 6f), 180f);
        House(root, new Vector3(-8f, 0f, -30f), new Vector3(9f, 4.4f, 7f), 180f);
        House(root, new Vector3(19f, 0f, -28f), new Vector3(7f, 3.8f, 6f), 180f);
        Ruin(root, new Vector3(21f, 0f, -12f), 5f, 2.6f, 30f);
        Haystack(root, new Vector3(-18f, 0f, -18f));
        FenceLine(root, new Vector3(-16f, 0f, -22f), 7f, 0f);
        Well(root, new Vector3(6f, 0f, -8f));

        // 村中的暖色补光：太阳在房子背面，正面全靠这两盏灯提亮
        AddFillLight(root, new Vector3(0f, 5.5f, -5f), 1.05f, 40f);
        AddFillLight(root, new Vector3(0f, 6.5f, -22f), 0.85f, 34f);

        // 近岸西侧村落
        House(root, new Vector3(-34f, 0f, -4f), new Vector3(7f, 4f, 6f));
        House(root, new Vector3(-43f, 0f, 6f), new Vector3(6f, 3.5f, 7f));
        House(root, new Vector3(-25f, 0f, -14f), new Vector3(5.5f, 3f, 5f));
        House(root, new Vector3(-45f, 0f, -10f), new Vector3(6f, 3.2f, 6f));
        Ruin(root, new Vector3(-37f, 0f, 12f), 6f, 2.8f, 20f);
        Haystack(root, new Vector3(-30f, 0f, 4.5f));
        Haystack(root, new Vector3(-40.5f, 0f, 0.5f));
        FenceLine(root, new Vector3(-31f, 0f, -9f), 7f, 0f);
        Well(root, new Vector3(-22f, 0f, -6f));

        // 近岸东侧村落（碉堡后方）
        House(root, new Vector3(32f, 0f, 1f), new Vector3(7f, 3.6f, 6f));
        House(root, new Vector3(43f, 0f, 8f), new Vector3(6f, 3.2f, 6f));
        House(root, new Vector3(30f, 0f, -14f), new Vector3(5f, 3f, 5f));
        House(root, new Vector3(44f, 0f, -10f), new Vector3(6f, 3.4f, 5f));
        Ruin(root, new Vector3(36f, 0f, 16f), 5f, 2.6f, -12f);
        Haystack(root, new Vector3(40f, 0f, -2f));
        FenceLine(root, new Vector3(27f, 0f, -6f), 6f, 90f);
        Well(root, new Vector3(37f, 0f, -6f));

        // 对岸村落：公路从中间穿过，房屋退到两侧，炮位与道路都留空
        House(root, new Vector3(-30f, 0f, 56f), new Vector3(7f, 3.8f, 6f));
        House(root, new Vector3(31f, 0f, 59f), new Vector3(6f, 3.4f, 6f));
        House(root, new Vector3(-30f, 0f, 71f), new Vector3(6f, 3.2f, 5f));
        House(root, new Vector3(30f, 0f, 72f), new Vector3(7f, 4f, 6f));
        House(root, new Vector3(-9f, 0f, 87f), new Vector3(8f, 4.2f, 7f));
        Ruin(root, new Vector3(-16f, 0f, 74f), 5f, 2.4f, 35f);
        Haystack(root, new Vector3(-15f, 0f, 53f));
        Haystack(root, new Vector3(17f, 0f, 52f));
        FenceLine(root, new Vector3(-34f, 0f, 50f), 14f, 0f);
        FenceLine(root, new Vector3(8f, 0f, 50f), 14f, 0f);
        Well(root, new Vector3(-6f, 0f, 76f));
    }

    /// <summary>
    /// 战场痕迹：冰封江面上的浮冰、冻土弹坑、丢下的枪盔、
    /// 桥头铁丝网与村庄炊烟。让这块地看起来"刚刚打过仗"。
    /// </summary>
    private static void BuildBattlefieldDebris(Transform root)
    {
        Color craterSoil = new Color(0.19f, 0.18f, 0.16f);
        Color iceColor = new Color(0.76f, 0.83f, 0.86f);
        Color iceDeep = new Color(0.62f, 0.72f, 0.77f);
        Color metal = new Color(0.2f, 0.21f, 0.2f);
        Color wood = new Color(0.26f, 0.18f, 0.1f);

        // ── 冻土弹坑：江滩与村口各挨过几炮 ──
        Vector3[] craters =
        {
            new Vector3(-12f, 0f, 6f), new Vector3(6.5f, 0f, 9f),
            new Vector3(14f, 0f, -6f), new Vector3(-19f, 0f, 14f),
            new Vector3(22f, 0f, 26f), new Vector3(-26f, 0f, 30f),
            new Vector3(3f, 0f, 51f), new Vector3(-14f, 0f, 56f),
            new Vector3(11f, 0f, 62f), new Vector3(-30f, 0f, 66f)
        };
        foreach (Vector3 c in craters)
        {
            float radius = UnityEngine.Random.Range(0.9f, 2.1f);
            GameObject rim = Box("CraterRim", new Vector3(c.x, 0.035f, c.z),
                new Vector3(radius * 2.4f, 0.07f, radius * 2.4f),
                new Color(0.3f, 0.29f, 0.27f), root, false);
            rim.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            Box("CraterPit", new Vector3(c.x, 0.05f, c.z),
                new Vector3(radius * 1.5f, 0.05f, radius * 1.5f), craterSoil, root, false);
        }

        // ── 江面浮冰：顺流漂下来的碎冰，边缘薄、色发青 ──
        for (int i = 0; i < 16; i++)
        {
            float x = UnityEngine.Random.Range(-34f, 34f);
            if (Mathf.Abs(x) < 0.8f) x += 3f;
            float z = UnityEngine.Random.Range(17.2f, 42.6f);
            GameObject floe = Box("IceFloe", new Vector3(x, -0.012f, z),
                new Vector3(UnityEngine.Random.Range(0.9f, 2.8f), 0.07f,
                            UnityEngine.Random.Range(0.9f, 2.4f)),
                i % 3 == 0 ? iceColor : iceDeep, root, false);
            floe.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        }

        // ── 遗弃的枪盔：北岸滩头一次冲锋留下的 ──
        Vector3[] gear =
        {
            new Vector3(-3.6f, 0.12f, 13.6f), new Vector3(2.9f, 0.12f, 14.4f),
            new Vector3(7.8f, 0.12f, 21f), new Vector3(-8.9f, 0.12f, 22.6f),
            new Vector3(12.4f, 0.12f, 15.5f), new Vector3(-14.5f, 0.12f, 11.2f)
        };
        for (int i = 0; i < gear.Length; i++)
        {
            // 钢盔：扣在冻土上，有的歪着
            GameObject helmet = Shape(PrimitiveType.Sphere, "AbandonedHelmet",
                gear[i] + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0f,
                                      UnityEngine.Random.Range(-0.5f, 0.5f)),
                new Vector3(0.3f, 0.2f, 0.3f), metal, root, false);
            helmet.transform.rotation = Quaternion.Euler(
                UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(-20f, 20f));

            // 扔在地上的步枪：枪托、机匣、枪管三截
            float yaw = UnityEngine.Random.Range(0f, 360f);
            Vector3 riflePos = gear[i] + new Vector3(UnityEngine.Random.Range(0.3f, 1.4f),
                0.055f, UnityEngine.Random.Range(-1f, 1f));
            GameObject stock = Box("AbandonedRifle", riflePos,
                new Vector3(0.065f, 0.07f, 0.34f), wood, root, false);
            stock.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            GameObject barrel = Box("AbandonedBarrel",
                riflePos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.5f),
                new Vector3(0.03f, 0.03f, 0.62f), metal, root, false);
            barrel.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ── 担架：抬到一半扔在村口 ──
        Vector3 stretch = new Vector3(-11.2f, 0.16f, -3.2f);
        GameObject stretcher = new GameObject("Stretcher");
        stretcher.transform.SetParent(root, false);
        stretcher.transform.position = stretch;
        stretcher.transform.rotation = Quaternion.Euler(0f, 28f, 0f);
        LocalShape(PrimitiveType.Cube, "StretcherPoleL", stretcher.transform,
            new Vector3(-0.31f, 0f, 0f), new Vector3(0.06f, 0.06f, 2.3f), wood, true);
        LocalShape(PrimitiveType.Cube, "StretcherPoleR", stretcher.transform,
            new Vector3(0.31f, 0f, 0f), new Vector3(0.06f, 0.06f, 2.3f), wood, true);
        LocalShape(PrimitiveType.Cube, "StretcherCanvas", stretcher.transform,
            new Vector3(0f, 0.03f, 0f), new Vector3(0.62f, 0.035f, 2.1f),
            new Color(0.55f, 0.51f, 0.4f), true);
        // 担架旁翻倒的药箱
        Box("MedicBox", stretch + new Vector3(0.85f, 0.16f, 0.6f),
            new Vector3(0.5f, 0.32f, 0.36f), new Color(0.4f, 0.38f, 0.3f), root);
        Box("MedicCrossH", stretch + new Vector3(0.85f, 0.33f, 0.6f),
            new Vector3(0.2f, 0.02f, 0.075f), new Color(0.62f, 0.15f, 0.12f), root, false);
        Box("MedicCrossV", stretch + new Vector3(0.85f, 0.33f, 0.6f),
            new Vector3(0.075f, 0.02f, 0.2f), new Color(0.62f, 0.15f, 0.12f), root, false);

        // ── 桥头铁丝网：木桩 + 两道铁蒺藜，只封两翼，桥口留给人走 ──
        BuildWire(root, 44.6f, -14f, -6.6f, 2.4f, metal, wood);
        BuildWire(root, 44.6f, 6.6f, 14f, 2.4f, metal, wood);
        BuildWire(root, 12.6f, -14f, -6.6f, 2.4f, metal, wood);
        BuildWire(root, 12.6f, 6.6f, 14f, 2.4f, metal, wood);

        // ── 炊烟：村里还有人在生火做饭 ──
        SmokeColumn(root, new Vector3(-18f, 4.2f, 0f), 1f);
        SmokeColumn(root, new Vector3(-27f, 3.7f, 7f), 0.75f);
        SmokeColumn(root, new Vector3(9f, 1.2f, 57f), 1.3f);

        // ── 红军标语牌：木框糊红布，字是找识字的战士写的（用色块代替） ──
        SignBoard(root, new Vector3(-6.5f, 0f, -6.5f), 18f, new Color(0.58f, 0.13f, 0.1f), wood);
        SignBoard(root, new Vector3(16.5f, 0f, 8.5f), -32f, new Color(0.56f, 0.12f, 0.09f), wood);
    }

    /// <summary>一段铁丝网：几根木桩之间拉两道蒺藜线。</summary>
    private static void BuildWire(Transform root, float z, float x0, float x1, float step,
                                  Color metal, Color wood)
    {
        int posts = Mathf.FloorToInt((x1 - x0) / step);
        for (int i = 0; i <= posts; i++)
        {
            float x = x0 + i * step;
            GameObject post = Box("WirePost", new Vector3(x, 0.5f, z),
                new Vector3(0.09f, 1f, 0.09f), wood, root);
            post.transform.rotation = Quaternion.Euler(
                UnityEngine.Random.Range(-5f, 5f), 0f, UnityEngine.Random.Range(-5f, 5f));
        }
        for (int line = 0; line < 2; line++)
        {
            float y = 0.42f + line * 0.36f;
            Box("WireLine", new Vector3((x0 + x1) * 0.5f, y, z),
                new Vector3(x1 - x0, 0.025f, 0.025f), metal, root, false);
        }
    }

    /// <summary>红布标语牌：两根木柱撑一块斜靠的红布。</summary>
    private static void SignBoard(Transform root, Vector3 pos, float yaw, Color cloth, Color wood)
    {
        GameObject group = new GameObject("SignBoard");
        group.transform.SetParent(root, false);
        group.transform.position = pos;
        group.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform g = group.transform;

        LocalShape(PrimitiveType.Cube, "SignPostL", g, new Vector3(-0.86f, 0.6f, 0f),
            new Vector3(0.1f, 1.2f, 0.1f), wood, true);
        LocalShape(PrimitiveType.Cube, "SignPostR", g, new Vector3(0.86f, 0.6f, 0f),
            new Vector3(0.1f, 1.2f, 0.1f), wood, true);
        LocalShape(PrimitiveType.Cube, "SignCloth", g, new Vector3(0f, 1.15f, 0f),
            new Vector3(1.9f, 0.72f, 0.04f), cloth, false);
        // 红布上的白边与几道墨块，远看就是一行标语
        LocalShape(PrimitiveType.Cube, "SignEdge", g, new Vector3(0f, 1.15f, 0.024f),
            new Vector3(1.98f, 0.8f, 0.02f), new Color(0.82f, 0.78f, 0.66f), false);
        for (int i = 0; i < 5; i++)
        {
            LocalShape(PrimitiveType.Cube, "SignGlyph", g,
                new Vector3(-0.68f + i * 0.34f, 1.15f, 0.04f),
                new Vector3(0.2f, 0.24f, 0.02f), new Color(0.14f, 0.1f, 0.09f), false);
        }
    }

    /// <summary>炊烟：缓缓上升的灰色粒子柱，加一点风偏。</summary>
    public static void SmokeColumn(Transform root, Vector3 position, float scale)
    {
        GameObject go = new GameObject("SmokeColumn");
        go.transform.SetParent(root, false);
        go.transform.position = position;

        ParticleSystem system = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(1f * scale, 2.2f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.58f, 0.57f, 0.55f, 0.34f),
            new Color(0.34f, 0.34f, 0.34f, 0.26f));
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.04f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 14f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.4f * scale;

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.1f, 0.55f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 2.6f)));

        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0.15f, 0f), new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f)
            });
        fade.color = new ParticleSystem.MinMaxGradient(gradient);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new Material(shader);
            material.mainTexture = ProceduralTextures.ParticleGlowTexture();
            renderer.material = material;
        }
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>半塌的土屋：三面高低不一的残墙、焦黑椽木与瓦砾。</summary>
    private static void Ruin(Transform root, Vector3 pos, float width, float height, float yaw)
    {
        Color stone = new Color(0.33f, 0.31f, 0.28f);
        Color charred = new Color(0.16f, 0.12f, 0.09f);
        float depth = width * 0.8f;

        GameObject group = new GameObject("Ruin");
        group.transform.SetParent(root, true);
        group.transform.position = pos;
        group.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform g = group.transform;

        LocalShape(PrimitiveType.Cube, "RuinBack", g,
            new Vector3(0f, height * 0.5f, -depth * 0.5f),
            new Vector3(width, height, 0.34f), stone, true);
        LocalShape(PrimitiveType.Cube, "RuinFrontL", g,
            new Vector3(-width * 0.28f, height * 0.31f, depth * 0.5f),
            new Vector3(width * 0.44f, height * 0.62f, 0.34f), stone, true);
        LocalShape(PrimitiveType.Cube, "RuinFrontR", g,
            new Vector3(width * 0.34f, height * 0.2f, depth * 0.5f),
            new Vector3(width * 0.3f, height * 0.4f, 0.34f), stone, true);
        LocalShape(PrimitiveType.Cube, "RuinSideL", g,
            new Vector3(-width * 0.5f, height * 0.4f, 0f),
            new Vector3(0.34f, height * 0.8f, depth * 0.8f), stone, true);

        for (int i = 0; i < 3; i++)
        {
            GameObject beam = LocalShape(PrimitiveType.Cube, "RuinBeam_" + i, g,
                new Vector3(-width * 0.2f + i * width * 0.22f, height * 0.55f + i * 0.12f, 0f),
                new Vector3(width * 0.75f, 0.12f, 0.12f), charred, true);
            beam.transform.localRotation = Quaternion.Euler(0f, 0f, 18f - i * 15f);
        }

        Color rubble = new Color(0.3f, 0.28f, 0.25f);
        for (int i = 0; i < 6; i++)
        {
            float rx = Mathf.Cos(i * 1.7f) * width * 0.4f;
            float rz = Mathf.Sin(i * 2.3f) * depth * 0.35f;
            float s = 0.3f + (i % 3) * 0.18f;
            LocalShape(PrimitiveType.Cube, "Rubble_" + i, g,
                new Vector3(rx, s * 0.5f, rz), new Vector3(s, s * 0.7f, s),
                rubble, true);
        }
    }

    private static void Haystack(Transform root, Vector3 pos)
    {
        Color straw = new Color(0.52f, 0.43f, 0.22f);
        Color strawDark = new Color(0.42f, 0.34f, 0.17f);
        Shape(PrimitiveType.Cylinder, "HayBase", pos + new Vector3(0f, 0.7f, 0f),
            new Vector3(1.7f, 0.7f, 1.7f), strawDark, root, true);
        Shape(PrimitiveType.Cylinder, "HayTop", pos + new Vector3(0f, 1.62f, 0f),
            new Vector3(1.45f, 0.42f, 1.45f), straw, root, true);
        Shape(PrimitiveType.Sphere, "HayCap", pos + new Vector3(0f, 2.05f, 0f),
            new Vector3(1.5f, 0.7f, 1.5f), straw, root, false);
    }

    private static void FenceLine(Transform root, Vector3 start, float length, float yaw)
    {
        Color wood = new Color(0.28f, 0.2f, 0.12f);
        GameObject group = new GameObject("Fence");
        group.transform.SetParent(root, true);
        group.transform.position = start;
        group.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform g = group.transform;

        int posts = Mathf.Max(2, Mathf.RoundToInt(length / 1.8f) + 1);
        for (int i = 0; i < posts; i++)
        {
            float x = -length * 0.5f + i * (length / (posts - 1));
            LocalShape(PrimitiveType.Cube, "FencePost_" + i, g,
                new Vector3(x, 0.55f, 0f), new Vector3(0.12f, 1.1f, 0.12f), wood, true);
        }
        LocalShape(PrimitiveType.Cube, "FenceRailA", g,
            new Vector3(0f, 0.82f, 0f), new Vector3(length, 0.1f, 0.08f), wood, true);
        LocalShape(PrimitiveType.Cube, "FenceRailB", g,
            new Vector3(0f, 0.42f, 0f), new Vector3(length, 0.1f, 0.08f), wood, true);
    }

    private static void Well(Transform root, Vector3 pos)
    {
        Color stone = new Color(0.34f, 0.32f, 0.29f);
        Color wood = new Color(0.24f, 0.17f, 0.1f);

        Shape(PrimitiveType.Cylinder, "WellRing", pos + new Vector3(0f, 0.45f, 0f),
            new Vector3(1.15f, 0.45f, 1.15f), stone, root, true);
        Shape(PrimitiveType.Cylinder, "WellWater", pos + new Vector3(0f, 0.88f, 0f),
            new Vector3(0.9f, 0.02f, 0.9f), new Color(0.08f, 0.12f, 0.16f), root, false);

        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? -1f : 1f;
            Shape(PrimitiveType.Cube, "WellPost", pos + new Vector3(x, 1.1f, 0f),
                new Vector3(0.14f, 2.2f, 0.14f), wood, root, true);
        }
        Box("WellBar", pos + new Vector3(0f, 2.05f, 0f),
            new Vector3(2.2f, 0.12f, 0.12f), wood, root);
        GameObject roof = Box("WellRoof", pos + new Vector3(0f, 2.5f, 0f),
            new Vector3(2.5f, 0.14f, 1.6f), new Color(0.28f, 0.18f, 0.11f), root);
        roof.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
    }

    private static readonly int[] SandbagRows = { 3, 2, 3, 2, 1 };

    private static void SandbagStack(Transform root, Vector3 basePos)
    {
        Color bag = new Color(0.46f, 0.41f, 0.29f);
        Color bagAlt = new Color(0.4f, 0.36f, 0.26f);

        // 五层错缝堆叠：底层三袋、中间两层两袋、顶上一袋压顶，
        // 高度到胸口（约 1.35 米），能挡住直射的步枪弹
        for (int row = 0; row < SandbagRows.Length; row++)
        {
            int count = SandbagRows[row];
            for (int i = 0; i < count; i++)
            {
                float x = count == 1 ? 0f
                    : (count == 2 ? (i == 0 ? -0.4f : 0.4f) : (i - 1) * 0.78f);
                float y = 0.16f + row * 0.3f;

                GameObject go = Box("Sandbag", basePos + new Vector3(x, y, 0f),
                    new Vector3(0.88f, 0.3f, 0.62f),
                    (i + row) % 2 == 0 ? bag : bagAlt, root);
                go.transform.rotation = Quaternion.Euler(0f,
                    (i == 0 ? -4f : 4f) + row * 3f, 0f);
            }
        }
    }

    /// <summary>
    /// 右翼战壕：在地面挖出 0.8 米深的壕沟（地图上留了开口），
    /// 配木护壁、射击踏台、面向江面的沙袋胸墙与西端台阶。
    /// </summary>
    private static void BuildTrench(Transform root)
    {
        Color soil = new Color(0.28f, 0.26f, 0.22f);
        Color plankColor = new Color(0.3f, 0.21f, 0.12f);
        Color darkWood = new Color(0.2f, 0.14f, 0.08f);
        Color bag = new Color(0.46f, 0.41f, 0.29f);
        Color bagAlt = new Color(0.4f, 0.36f, 0.26f);

        const float x0 = 11f, x1 = 20f, z0 = 7.6f, z1 = 9.2f;
        float midX = (x0 + x1) * 0.5f;
        float midZ = (z0 + z1) * 0.5f;

        // 壕底
        Box("TrenchFloor", new Vector3(midX, -0.95f, midZ),
            new Vector3(x1 - x0 + 0.5f, 0.3f, z1 - z0 + 0.5f), soil, root);

        // 两侧木护壁与固定木桩
        for (int side = 0; side < 2; side++)
        {
            float z = side == 0 ? z0 + 0.04f : z1 - 0.04f;

            Box("TrenchPlank", new Vector3(midX, -0.56f, z),
                new Vector3(x1 - x0 - 0.3f, 0.52f, 0.06f), plankColor, root, false);

            for (int i = 0; i < 7; i++)
            {
                Box("TrenchFixPost", new Vector3(x0 + 0.6f + i * 1.35f, -0.55f, z),
                    new Vector3(0.1f, 0.62f, 0.09f), darkWood, root, false);
            }
        }

        // 面向江面的胸墙：站直后视线刚好越过，蹲下就能完全隐蔽
        for (int i = 0; i < 5; i++)
        {
            GameObject wall = Box("Sandbag", new Vector3(x0 + 0.95f + i * 1.76f, 0.26f, z1 + 0.42f),
                new Vector3(1.76f, 0.58f, 0.62f), i % 2 == 0 ? bag : bagAlt, root);
            wall.transform.rotation = Quaternion.Euler(0f, i % 2 == 0 ? -2f : 2f, 0f);
        }

        // 射击踏台
        Box("TrenchFireStep", new Vector3(midX, -0.69f, z1 - 0.36f),
            new Vector3(x1 - x0 - 0.5f, 0.26f, 0.5f), new Color(0.31f, 0.28f, 0.23f), root);

        // 西端台阶：每级 0.2 米，角色控制器可以直接走上来
        for (int i = 0; i < 3; i++)
        {
            float top = -0.2f - i * 0.2f;
            float centerY = top - 0.4f;

            Box("TrenchStep", new Vector3(x0 + 1.5f - i * 0.66f, centerY, midZ),
                new Vector3(0.7f, 0.8f, 1.5f), soil, root);
        }

        // 壕沿南侧的土堆，压住地面切口的硬边
        for (int i = 0; i < 5; i++)
        {
            float x = x0 + 0.6f + i * 1.8f;
            Box("TrenchRim", new Vector3(x, 0.1f, z0 - 0.26f),
                new Vector3(1.7f, 0.22f, 0.5f), soil, root, false);
        }
    }

    /// <summary>
    /// 沙袋与圆木碉堡：三面墙、正面射击孔、圆木顶盖，里面架一挺机枪。
    /// 入口在背向江面的南侧，玩家可以从战壕方向走进去。
    /// </summary>
    private static void BuildBunker(Transform root, Vector3 center)
    {
        Color bag = new Color(0.46f, 0.41f, 0.29f);
        Color bagAlt = new Color(0.4f, 0.36f, 0.26f);
        Color wood = new Color(0.27f, 0.19f, 0.11f);
        Color earth = new Color(0.3f, 0.27f, 0.22f);
        Color metal = new Color(0.16f, 0.17f, 0.17f);

        float halfX = 2.6f;
        float halfZ = 1.6f;
        float x0 = center.x - halfX;
        float x1 = center.x + halfX;
        float z0 = center.z - halfZ;
        float z1 = center.z + halfZ;

        // 背墙留出入口（南侧）
        Box("Sandbag", new Vector3(x0 + 0.8f, 0.72f, z0),
            new Vector3(1.7f, 1.44f, 0.6f), bag, root);
        Box("Sandbag", new Vector3(x1 - 0.8f, 0.72f, z0),
            new Vector3(1.7f, 1.44f, 0.6f), bagAlt, root);

        // 左右厚墙
        Box("Sandbag", new Vector3(x0, 0.76f, center.z),
            new Vector3(0.6f, 1.52f, halfZ * 2f), bagAlt, root);
        Box("Sandbag", new Vector3(x1, 0.76f, center.z),
            new Vector3(0.6f, 1.52f, halfZ * 2f), bag, root);

        // 正面：下墙 + 上沿，中间 1.2~1.66 是射击孔
        Box("Sandbag", new Vector3(center.x, 0.6f, z1),
            new Vector3(halfX * 2f, 1.2f, 0.6f), bag, root);
        Box("Sandbag", new Vector3(center.x, 1.79f, z1),
            new Vector3(halfX * 2f, 0.28f, 0.6f), bagAlt, root);

        // 四角立柱
        for (int i = 0; i < 4; i++)
        {
            float px = (i == 0 || i == 2) ? x0 + 0.35f : x1 - 0.35f;
            float pz = (i < 2) ? z0 + 0.35f : z1 - 0.35f;
            Box("BunkerPost", new Vector3(px, 1.0f, pz),
                new Vector3(0.2f, 2.05f, 0.2f), wood, root);
        }

        // 圆木顶盖 + 覆土
        for (int i = 0; i < 7; i++)
        {
            float z = z0 + 0.2f + i * 0.47f;
            GameObject log = Shape(PrimitiveType.Cylinder, "BunkerRoofPlank",
                new Vector3(center.x, 2.07f, z),
                new Vector3(0.15f, halfX + 0.25f, 0.15f), wood, root, false);
            log.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        Box("BunkerRoofEarth", new Vector3(center.x, 2.2f, center.z),
            new Vector3(halfX * 2f + 0.3f, 0.26f, halfZ * 2f + 0.2f), earth, root, false);

        for (int i = 0; i < 4; i++)
        {
            Box("Sandbag", new Vector3(x0 + 1.1f + i * 1.05f, 2.4f, z0 + 0.9f),
                new Vector3(1.0f, 0.26f, 0.55f), i % 2 == 0 ? bag : bagAlt, root, false);
        }

        // 里面的机枪：三脚架 + 机匣 + 枪管 + 弹匣
        Vector3 gunPos = new Vector3(center.x, 0f, center.z + 0.5f);

        for (int i = 0; i < 3; i++)
        {
            float angle = 120f * i + 30f;
            GameObject leg = Shape(PrimitiveType.Cylinder, "MgTripod",
                gunPos + new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * 0.28f, 0.42f,
                                     Mathf.Cos(angle * Mathf.Deg2Rad) * 0.28f),
                new Vector3(0.045f, 0.48f, 0.045f), metal, root, false);
            leg.transform.rotation = Quaternion.Euler(
                28f * Mathf.Cos(angle * Mathf.Deg2Rad), 0f,
                -28f * Mathf.Sin(angle * Mathf.Deg2Rad));
        }

        Box("MgReceiver", gunPos + new Vector3(0f, 0.92f, 0.05f),
            new Vector3(0.16f, 0.16f, 0.44f), metal, root, false);
        Box("MgMagazine", gunPos + new Vector3(0f, 1.06f, 0.12f),
            new Vector3(0.2f, 0.14f, 0.3f), new Color(0.24f, 0.22f, 0.16f), root, false);

        GameObject barrel = Shape(PrimitiveType.Cylinder, "MgBarrel",
            gunPos + new Vector3(0f, 0.97f, 0.52f),
            new Vector3(0.04f, 0.42f, 0.04f), metal, root, false);
        barrel.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    /// <summary>斜插在地上的削尖木桩（拒马），用来丰富两翼的空地。</summary>
    private static void Abatis(Transform root, float startX, float z, int count, float spacing)
    {
        Color wood = new Color(0.3f, 0.22f, 0.13f);
        Color tip = new Color(0.44f, 0.36f, 0.24f);

        for (int i = 0; i < count; i++)
        {
            float x = startX + i * spacing;
            float yaw = Random.Range(-14f, 14f);

            GameObject stake = Box("AbatisPost", new Vector3(x, 0.72f, z),
                new Vector3(0.13f, 0.13f, 2.4f), wood, root, false);
            stake.transform.rotation = Quaternion.Euler(-55f, yaw, 0f);

            GameObject spike = Box("AbatisPostTip", new Vector3(x, 1.7f, z + 0.66f),
                new Vector3(0.16f, 0.16f, 0.34f), tip, root, false);
            spike.transform.rotation = Quaternion.Euler(-55f, yaw, 0f);
        }
    }

    private static void BareTree(Transform root, Vector3 pos, float scale, float yaw)
    {
        GameObject tree = new GameObject("BareTree");
        tree.transform.SetParent(root, true);
        tree.transform.position = pos;
        tree.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform t = tree.transform;

        Color bark = new Color(0.22f, 0.18f, 0.14f);
        float height = 3.2f * scale;

        LocalShape(PrimitiveType.Cylinder, "Trunk", t,
            new Vector3(0f, height * 0.5f, 0f),
            new Vector3(0.22f * scale, height * 0.5f, 0.22f * scale), bark, true);

        GameObject branchA = LocalShape(PrimitiveType.Cylinder, "BranchA", t,
            new Vector3(0.34f * scale, height * 0.82f, 0f),
            new Vector3(0.08f * scale, height * 0.22f, 0.08f * scale), bark);
        branchA.transform.localRotation = Quaternion.Euler(0f, 0f, -38f);

        GameObject branchB = LocalShape(PrimitiveType.Cylinder, "BranchB", t,
            new Vector3(-0.34f * scale, height * 0.74f, 0f),
            new Vector3(0.07f * scale, height * 0.18f, 0.07f * scale), bark);
        branchB.transform.localRotation = Quaternion.Euler(0f, 0f, 34f);

        GameObject branchC = LocalShape(PrimitiveType.Cylinder, "BranchC", t,
            new Vector3(0f, height * 0.9f, -0.24f * scale),
            new Vector3(0.06f * scale, height * 0.15f, 0.06f * scale), bark);
        branchC.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
    }

    private static void AmmoCrate(Transform root, Vector3 pos)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "AmmoCrate";
        go.transform.SetParent(root, true);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.9f, 0.55f, 0.9f);
        go.GetComponent<Renderer>().sharedMaterial =
            MakeMaterial(new Color(0.42f, 0.30f, 0.14f));

        BoxCollider col = go.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;

        // 箱盖、铁箍与红五星
        LocalShape(PrimitiveType.Cube, "CrateLid", go.transform,
            new Vector3(0f, 0.6f, 0f), new Vector3(1.06f, 0.16f, 1.06f),
            new Color(0.33f, 0.22f, 0.1f));
        LocalShape(PrimitiveType.Cube, "CrateBandL", go.transform,
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.06f, 1.02f, 1.04f),
            new Color(0.19f, 0.18f, 0.17f));
        LocalShape(PrimitiveType.Cube, "CrateBandR", go.transform,
            new Vector3(0.5f, 0f, 0f), new Vector3(0.06f, 1.02f, 1.04f),
            new Color(0.19f, 0.18f, 0.17f));
        GameObject star = LocalShape(PrimitiveType.Cube, "CrateStar", go.transform,
            new Vector3(0f, 0.02f, -0.52f), new Vector3(0.24f, 0.24f, 0.03f),
            new Color(0.66f, 0.1f, 0.08f));
        star.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        AmmoPickup pickup = go.AddComponent<AmmoPickup>();
        pickup.Configure(3, 35f); // 每个补给包 3 发，35 秒刷新
    }
}
