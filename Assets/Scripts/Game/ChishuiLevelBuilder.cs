using UnityEngine;

/// <summary>
/// 第二关「四渡赤水」：一条南北走向的赤水河谷，东西两岸被石壁夹住。
///
/// 路线被迫四次过河：
///   一渡 —— 踩着河中跳石过河；
///   二渡 —— 走过一条会晃的木板索桥；
///   三渡 —— 蹚过浅滩石堤，全程暴露在敌火下；
///   四渡 —— 沿断桥独木梁过河，中间还要跳过一段缺口。
/// 渡口都在对岸，跑得慢就会被追兵咬住。
/// </summary>
public static class ChishuiLevelBuilder
{
    /// <summary>
    /// 河岸高度：从河床（x≈±10.5）抬升到岸顶（x≈±16），
    /// 再往内陆用两层 Perlin 堆出起伏的丘陵，靠河一段只留细碎起伏，
    /// 既不是平地，也不会把跑动路线变成爬坡。
    /// </summary>
    public static float GroundHeight(float x, float z)
    {
        float ax = Mathf.Abs(x);

        float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10.5f, 16f, ax));
        float shore = Mathf.Lerp(-1.4f, 0.12f, rise);

        float hillMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17.5f, 40f, ax));
        float hills = Mathf.PerlinNoise(x * 0.045f + 12.3f, z * 0.05f + 4.7f) * 3.4f;
        float bumps = (Mathf.PerlinNoise(x * 0.17f + 3.1f, z * 0.2f + 8.9f) - 0.5f)
                      * (0.45f + hillMask * 0.9f);

        // 外侧远坡：越往外抬得越高，把河谷围成一条走廊
        float ridgeMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(46f, 92f, ax));
        float ridge = Mathf.PerlinNoise(x * 0.018f + 7.7f, z * 0.02f + 2.3f) * 13f + 3f;

        return shore + hills * hillMask + bumps + ridge * ridgeMask;
    }

    /// <summary>
    /// 干地上士兵应踩的地形高度：河岸丘陵 + 娄山关石阶、关顶平台。
    /// 不含树冠、竹竿、房顶、岩石这类道具碰撞体，AI 贴地时用它兜底，
    /// 免得生成或走路时被留在半空。
    /// </summary>
    public static float WalkHeight(float x, float z)
    {
        float ground = GroundHeight(x, z);
        float pass = PassHeight(x, z);
        return pass > ground ? pass : ground;
    }

    /// <summary>把一整岸做成网格地形，MeshCollider 直接给玩家和敌军踩。</summary>
    private static void BuildBankTerrain(Transform root, bool east, Color color)
    {
        float sign = east ? 1f : -1f;
        const float nearX = 10.5f;
        const float farX = 96f;
        const float zMin = -120f;
        const float zMax = 340f;
        const float step = 1.5f;

        int nx = Mathf.CeilToInt((farX - nearX) / step);
        int nz = Mathf.CeilToInt((zMax - zMin) / step);

        Vector3[] vertices = new Vector3[(nx + 1) * (nz + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[nx * nz * 6];

        for (int iz = 0; iz <= nz; iz++)
        {
            float z = Mathf.Lerp(zMin, zMax, iz / (float)nz);

            for (int ix = 0; ix <= nx; ix++)
            {
                float x = sign * Mathf.Lerp(nearX, farX, ix / (float)nx);
                int index = iz * (nx + 1) + ix;

                vertices[index] = new Vector3(x, GroundHeight(x, z), z);
                uvs[index] = new Vector2(x / 9f, z / 9f);
            }
        }

        int t = 0;
        for (int iz = 0; iz < nz; iz++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                int a = iz * (nx + 1) + ix;
                int b = a + 1;
                int c = a + nx + 1;
                int d = c + 1;

                if (sign > 0f)
                {
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
                else
                {
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = east ? "EastBankMesh" : "WestBankMesh";
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject terrain = new GameObject(east ? "EastBank" : "WestBank");
        terrain.transform.SetParent(root, false);

        MeshFilter filter = terrain.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = terrain.AddComponent<MeshRenderer>();
        Material terrainMaterial = LevelBuilder.MakeMaterial(color);
        terrainMaterial.mainTexture = ProceduralTextures.GroundTexture();
        renderer.sharedMaterial = terrainMaterial;

        MeshCollider collider = terrain.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
    }

    public static void Build(Transform root, out Vector3 spawn, out float spawnYaw,
                             out Vector3[] crossings, out Transform[] guardSpawns,
                             out int[] guardSegments, out Transform beacon)
    {
        spawn = new Vector3(26f, GroundHeight(26f, 0f) + 0.35f, 0f);
        spawnYaw = -35f;

        // 重新生成关卡时，上一局登记的敌方通行走廊要先清掉
        ChishuiWaterRule.ClearCorridors();

        // 四大渡口 + 最后一道娄山关
        Vector3[] crossingSpots =
        {
            new Vector3(-20f, 0f, 30f),
            new Vector3(20f, 0f, 96f),
            new Vector3(-20f, 0f, 162f),
            new Vector3(20f, 0f, 228f)
        };

        Color ground = new Color(0.33f, 0.38f, 0.29f);
        Color groundDark = new Color(0.28f, 0.32f, 0.25f);
        Color bankMud = new Color(0.3f, 0.29f, 0.23f);
        Color bedColor = new Color(0.19f, 0.22f, 0.2f);
        Color waterColor = new Color(0.08f, 0.16f, 0.17f);
        Color rock = new Color(0.35f, 0.34f, 0.31f);
        Color rockDark = new Color(0.26f, 0.26f, 0.24f);
        Color wood = new Color(0.33f, 0.23f, 0.14f);
        Color woodDark = new Color(0.24f, 0.17f, 0.1f);
        Color rope = new Color(0.42f, 0.36f, 0.24f);
        Color path = new Color(0.42f, 0.38f, 0.28f);
        Color foliage = new Color(0.22f, 0.32f, 0.2f);
        Color bamboo = new Color(0.35f, 0.4f, 0.26f);
        Color red = new Color(0.62f, 0.11f, 0.08f);

        // ── 河谷地形 ──
        BuildBankTerrain(root, true, ground);
        BuildBankTerrain(root, false, groundDark);

        LevelBuilder.Box("RiverBed", new Vector3(0f, -1.65f, 100f),
            new Vector3(24f, 0.3f, 340f), bedColor, root);

        GameObject water = LevelBuilder.Box("RiverWater", new Vector3(0f, -0.25f, 100f),
            new Vector3(26f, 0.08f, 340f), waterColor, root, false);
        RiverFlow.Attach(water, new Vector3(26f, 0.08f, 340f), Vector3.forward, 17f);

        // 两岸泥滩
        LevelBuilder.Box("EastMud", new Vector3(14.2f, 0.06f, 100f),
            new Vector3(2.6f, 0.16f, 340f), bankMud, root);
        LevelBuilder.Box("WestMud", new Vector3(-14.2f, 0.06f, 100f),
            new Vector3(2.6f, 0.16f, 340f), bankMud, root);

        // 远山交给地形本身的外坡和雾气，不再放方盒子当剪影



        // ── 地面引导土路 ──
        Path(root, new Vector3(26f, 0.12f, 0f), new Vector3(14f, 0.12f, 24f), path);
        Path(root, new Vector3(-18f, 0.12f, 32f), new Vector3(-16f, 0.12f, 88f), path);
        Path(root, new Vector3(18f, 0.12f, 98f), new Vector3(16f, 0.12f, 152f), path);
        Path(root, new Vector3(-18f, 0.12f, 164f), new Vector3(-16f, 0.12f, 213f), path);
        Path(root, new Vector3(17f, 0.12f, 224f), new Vector3(24f, 0.12f, 242f), path);

        // ── 一渡：跳石 ──
        BuildSteppingStones(root, 24f, rock, rockDark);

        // ── 二渡：木板索桥 ──
        BuildRopeBridge(root, 88f, wood, woodDark, rope);

        // ── 三渡：浅滩石堤 ──
        BuildFord(root, 152f, rock, bankMud);

        // ── 四渡：断桥独木梁 ──
        BuildBrokenBridge(root, 215f, wood, woodDark, rope);

        // ── 岸上景物：乱石、竹林、芦苇、断墙、红旗 ──
        BuildBankScenery(root, ground, rock, rockDark, bamboo, foliage, red);

        // ── 河滩与平地的沙袋工事、两岸民居 ──
        BuildGuerrillaCover(root, wood, woodDark, path);

        // ── 炊烟与战场余烬：营地、镇子、被炮打着的山腰 ──
        LevelBuilder.SmokeColumn(root,
            new Vector3(-22f, GroundHeight(-22f, 84f) + 0.5f, 84f), 1.1f);
        LevelBuilder.SmokeColumn(root,
            new Vector3(-29f, GroundHeight(-29f, 184f) + 0.5f, 184f), 0.9f);
        LevelBuilder.SmokeColumn(root,
            new Vector3(41f, GroundHeight(41f, 152f) + 0.5f, 152f), 1.5f);
        LevelBuilder.SmokeColumn(root,
            new Vector3(-19f, GroundHeight(-19f, 206f) + 0.4f, 206f), 0.8f);

        // ── 河谷两端：南岸渡口山村、北端娄山关下的梯田与松林 ──
        BuildRiverEnds(root, wood, woodDark, rope, rock, rockDark, foliage, bamboo, path, red);

        // ── 三个区域各自的标志性场景 ──
        BuildQinggangpo(root, bamboo, foliage, rockDark);
        BuildEngineerCamp(root, wood, woodDark, rope, red);
        BuildMaotaiTown(root, wood, woodDark, path, red);

        // ── 最后一段：娄山关石阶与关顶 ──
        Vector3 summit = BuildLoushanPass(root, rock, rockDark, woodDark, red);
        BuildSnowstorm(root, summit);

        // ── 渡口坐标（含关顶）与军旗 ──
        crossings = new Vector3[5];
        for (int i = 0; i < crossingSpots.Length; i++)
        {
            crossings[i] = new Vector3(crossingSpots[i].x,
                GroundHeight(crossingSpots[i].x, crossingSpots[i].z) + 0.2f,
                crossingSpots[i].z);
        }
        crossings[4] = summit + Vector3.up * 0.15f;

        for (int i = 0; i < 4; i++)
        {
            Flag(root, crossings[i] + new Vector3(i % 2 == 0 ? -2.4f : 2.4f, 0f, 0f),
                i % 2 == 0 ? 20f : 200f, red);
        }

        // ── 敌军守备位置：每渡 5 人，最后一段守娄山关 ──
        Vector3[] guards =
        {
            // 一渡 · 土城青杠坡（西岸山地）
            new Vector3(-16f, 0f, 26f), new Vector3(-25f, 0f, 33f), new Vector3(-19f, 0f, 39f),
            new Vector3(-13f, 0f, 46f), new Vector3(-27f, 0f, 22f),
            // 二渡 · 太平渡（东岸渡口）
            new Vector3(16f, 0f, 90f),  new Vector3(24f, 0f, 98f),  new Vector3(18f, 0f, 104f),
            new Vector3(12f, 0f, 96f),  new Vector3(27f, 0f, 86f),
            // 三渡 · 二郎滩（西岸）
            new Vector3(-16f, 0f, 156f), new Vector3(-24f, 0f, 164f), new Vector3(-18f, 0f, 170f),
            new Vector3(-12f, 0f, 162f), new Vector3(-27f, 0f, 172f),
            // 四渡 · 茅台镇巷战
            new Vector3(-20f, 0f, 182f), new Vector3(-27f, 0f, 190f), new Vector3(-17f, 0f, 198f),
            new Vector3(-30f, 0f, 206f), new Vector3(-20f, 0f, 214f),
            // 娄山关 · 石阶与关顶
            new Vector3(30f, 0f, 258f), new Vector3(25f, 0f, 270f), new Vector3(35f, 0f, 278f),
            new Vector3(29f, 0f, 292f), new Vector3(36f, 0f, 297f)
        };

        guardSegments = new int[guards.Length];
        guardSpawns = new Transform[guards.Length];

        for (int i = 0; i < guards.Length; i++)
        {
            float y = PassHeight(guards[i].x, guards[i].z);
            if (y == float.MinValue) y = GroundHeight(guards[i].x, guards[i].z);

            GameObject point = new GameObject("GuardSpawn_" + i);
            point.transform.SetParent(root, false);
            point.transform.position = new Vector3(guards[i].x, y + 0.05f, guards[i].z);
            guardSpawns[i] = point.transform;
            guardSegments[i] = i / 5;
        }

        beacon = BuildBeacon(root, crossings[0]);
    }

    // ───────────────────── 区域一：土城青杠坡 ─────────────────────

    /// <summary>
    /// 青杠坡：密林、绝壁上的机枪阵地。敌人居高临下，
    /// 玩家只能贴着乱石和竹林摸上去。路边还留着没撤下来的伤员。
    /// </summary>
    private static void BuildQinggangpo(Transform root, Color bamboo, Color foliage, Color rock)
    {
        // 额外密林：只在第一段路两侧加密，走过之后视野逐渐打开
        Vector3[] groves =
        {
            new Vector3(20f, 0f, 8f), new Vector3(31f, 0f, 14f), new Vector3(19f, 0f, 22f),
            new Vector3(34f, 0f, 30f), new Vector3(24f, 0f, 40f), new Vector3(33f, 0f, 52f),
            new Vector3(-22f, 0f, 20f), new Vector3(-30f, 0f, 28f), new Vector3(-21f, 0f, 38f),
            new Vector3(-32f, 0f, 48f), new Vector3(-24f, 0f, 58f), new Vector3(-36f, 0f, 62f)
        };
        for (int i = 0; i < groves.Length; i++) BambooCluster(root, groves[i], bamboo, foliage);

        // 山顶两处机枪阵地，正压在跳石渡口上
        MGNest(root, new Vector3(-29f, 0f, 21f), 118f, rock);
        MGNest(root, new Vector3(-34f, 0f, 41f), 132f, rock);

        // 半塌的掩体与弹坑
        RuinedWall(root, new Vector3(-25f, 0f, 34f), 5f, 1.3f, -18f, rock);
        RuinedWall(root, new Vector3(-18f, 0f, 49f), 3.4f, 1.1f, 32f, rock);

        for (int i = 0; i < 7; i++)
        {
            float x = Random.Range(16f, 36f);
            float z = Random.Range(4f, 60f);
            Crater(root, new Vector3(x, GroundHeight(x, z) + 0.03f, z));
        }
    }

    private static void MGNest(Transform root, Vector3 position, float yaw, Color sand)
    {
        position.y = GroundHeight(position.x, position.z);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        // 沙袋 U 形工事（两层）
        for (int row = 0; row < 2; row++)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = -100f + i * 33f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 local = dir * 1.5f + Vector3.up * (0.22f + row * 0.4f);
                GameObject bag = LevelBuilder.Box("Sandbag",
                    position + rot * local, new Vector3(0.62f, 0.34f, 0.42f),
                    new Color(0.44f, 0.41f, 0.33f), root);
                bag.transform.rotation = Quaternion.Euler(0f, yaw + angle, 0f);
            }
        }

        Color metal = new Color(0.16f, 0.16f, 0.17f);
        Color wood = new Color(0.24f, 0.17f, 0.1f);

        // 机枪：三脚架 + 机匣 + 枪管
        Vector3 center = position + rot * new Vector3(0f, 0.65f, 0f);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * 120f;
            GameObject leg = LevelBuilder.Box("MG_Leg", center,
                new Vector3(0.06f, 0.7f, 0.06f), wood, root, false);
            leg.transform.rotation = Quaternion.Euler(18f, yaw + angle, 0f);
            leg.transform.position = center + rot * new Vector3(
                Mathf.Sin(angle * Mathf.Deg2Rad) * 0.32f, -0.18f,
                Mathf.Cos(angle * Mathf.Deg2Rad) * 0.32f);
        }

        GameObject receiver = LevelBuilder.Box("MG_Receiver", center + rot * new Vector3(0f, 0.28f, 0f),
            new Vector3(0.24f, 0.2f, 0.62f), metal, root, false);
        receiver.transform.rotation = rot;

        GameObject barrel = LevelBuilder.Box("MG_Barrel", center + rot * new Vector3(0f, 0.3f, 0.72f),
            new Vector3(0.07f, 0.07f, 0.9f), metal, root, false);
        barrel.transform.rotation = rot;

        GameObject shield = LevelBuilder.Box("MG_Shield", center + rot * new Vector3(0f, 0.34f, 0.14f),
            new Vector3(0.86f, 0.5f, 0.06f), metal, root, false);
        shield.transform.rotation = rot;
    }

    private static void Crater(Transform root, Vector3 position)
    {
        GameObject crater = LevelBuilder.Box("Crater", position,
            new Vector3(Random.Range(1.4f, 2.6f), 0.03f, Random.Range(1.4f, 2.6f)),
            new Color(0.2f, 0.19f, 0.16f), root, false);
        crater.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
    }

    // ───────────────────── 区域二：太平渡工兵营地 ─────────────────────

    /// <summary>浮桥边的工兵营地：木料、绳索、帐篷和堆起来的门板。</summary>
    private static void BuildEngineerCamp(Transform root, Color wood, Color woodDark,
                                          Color rope, Color red)
    {
        Vector3 camp = new Vector3(-22f, GroundHeight(-22f, 84f), 84f);

        // A 字形帐篷
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject canvas = LevelBuilder.Box("TentCanvas",
                camp + new Vector3(side * 1.15f, 1.05f, 0f),
                new Vector3(2.6f, 0.08f, 4.2f), new Color(0.44f, 0.41f, 0.31f), root);
            // 两片帆布朝内上斜，脊线在正中合拢，才是 A 字形
            canvas.transform.rotation = Quaternion.Euler(0f, 0f, side * -48f);
        }
        LevelBuilder.Box("TentBack", camp + new Vector3(0f, 0.78f, 2.0f),
            new Vector3(1.3f, 1.45f, 0.08f), new Color(0.36f, 0.33f, 0.26f), root);
        LevelBuilder.Box("TentPole", camp + new Vector3(0f, 1.35f, -2.0f),
            new Vector3(0.09f, 2.7f, 0.09f), woodDark, root);

        // 木料堆
        for (int row = 0; row < 3; row++)
        {
            for (int i = 0; i < 3 - row; i++)
            {
                GameObject log = LevelBuilder.Box("Timber",
                    camp + new Vector3(-3.8f - row * 0.5f, 0.35f + i * 0.34f, -1.2f + i * 0.36f),
                    new Vector3(0.32f, 0.32f, 3.2f), wood, root);
                log.transform.rotation = Quaternion.Euler(0f, 4f, 90f);
            }
        }

        // 门板与绳索
        for (int i = 0; i < 5; i++)
        {
            GameObject plank = LevelBuilder.Box("PontoonPlank",
                camp + new Vector3(-2.4f + i * 0.16f, 0.12f + i * 0.09f, -0.6f),
                new Vector3(0.5f, 0.08f, 3.6f), i % 2 == 0 ? wood : woodDark, root, false);
            plank.transform.rotation = Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f);
        }

        for (int i = 0; i < 3; i++)
        {
            GameObject coil = LevelBuilder.Box("RopeCoil",
                camp + new Vector3(-3.4f + i * 0.9f, 0.12f, 1.3f),
                new Vector3(0.7f, 0.16f, 0.7f), rope, root, false);
            coil.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
        }

        Flag(root, camp + new Vector3(1.6f, 0f, 0.2f), 24f, red);
    }

    // ───────────────────── 区域三：茅台镇 ─────────────────────

    /// <summary>茅台镇：西岸一条石板街，两侧是酒坊与仓房，街口堆着酒坛。</summary>
    private static void BuildMaotaiTown(Transform root, Color wood, Color woodDark,
                                        Color path, Color red)
    {
        Color wall = new Color(0.4f, 0.36f, 0.29f);
        Color roof = new Color(0.25f, 0.17f, 0.12f);
        Color stone = new Color(0.34f, 0.33f, 0.3f);

        // 石板街：沿地形铺，不在坡上架空
        Path(root, new Vector3(-18f, 0.12f, 164f), new Vector3(-18f, 0.12f, 216f), path, 7.2f);
        Path(root, new Vector3(-14.7f, 0.12f, 164f), new Vector3(-14.7f, 0.12f, 216f), stone, 1.0f);

        Vector3[] houses =
        {
            new Vector3(-29f, 0f, 172f), new Vector3(-38f, 0f, 184f),
            new Vector3(-27f, 0f, 196f), new Vector3(-37f, 0f, 208f),
            new Vector3(-28f, 0f, 220f)
        };
        float[] widths = { 7f, 8f, 6f, 7.5f, 6.5f };
        float[] heights = { 4f, 4.4f, 3.6f, 4.2f, 3.8f };

        for (int i = 0; i < houses.Length; i++)
        {
            TownHouse(root, houses[i], widths[i], heights[i], wall, roof, woodDark, red);
        }

        // 东岸渡口的小仓房，给最后一段路一点遮挡
        TownHouse(root, new Vector3(33f, 0f, 236f), 6f, 3.6f, wall, roof, woodDark, red);

        // 酒坛：街边一排，打碎了也只是气氛
        Color jar = new Color(0.34f, 0.22f, 0.14f);
        Color jarCloth = new Color(0.62f, 0.58f, 0.46f);
        Vector3[] jars =
        {
            new Vector3(-23.2f, 0f, 176f), new Vector3(-22.4f, 0f, 180f),
            new Vector3(-24.3f, 0f, 186f), new Vector3(-22.6f, 0f, 191f),
            new Vector3(-23.8f, 0f, 199f), new Vector3(-22.9f, 0f, 204f),
            new Vector3(-24.1f, 0f, 211f), new Vector3(-23.3f, 0f, 216f),
            new Vector3(-31.5f, 0f, 202f), new Vector3(-32.2f, 0f, 203.1f)
        };
        for (int i = 0; i < jars.Length; i++) WineJar(root, jars[i], jar, jarCloth);

        // 军用物资：弹药箱、油桶、补给
        Vector3 cache = new Vector3(-22.2f, GroundHeight(-22.2f, 206f), 206f);
        for (int i = 0; i < 4; i++)
        {
            GameObject crate = LevelBuilder.Box("AmmoCrate",
                cache + new Vector3((i % 2) * 0.7f, 0.25f + (i / 2) * 0.5f, (i / 2) * 0.68f),
                new Vector3(0.66f, 0.5f, 0.62f), i % 2 == 0 ? wood : woodDark, root);
            crate.transform.rotation = Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
        }
        for (int i = 0; i < 2; i++)
        {
            GameObject barrel = LevelBuilder.Box("FuelBarrel",
                cache + new Vector3(1.5f + i * 0.55f, 0.36f, 0.5f),
                new Vector3(0.46f, 0.72f, 0.46f), new Color(0.26f, 0.28f, 0.24f), root);
            barrel.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        }

        AmmoPickup.SpawnPickup(cache + new Vector3(0.35f, 0.9f, -0.9f), 6);
        AmmoPickup.SpawnPickup(cache + new Vector3(1.2f, 0.9f, -1.1f), 6);

        // 街口的灯笼
        for (int i = 0; i < 3; i++)
        {
            Vector3 lampPos = new Vector3(-21.8f, 0f, 178f + i * 16f);
            lampPos.y = GroundHeight(lampPos.x, lampPos.z);
            TownLantern(root, lampPos, woodDark);
        }
    }

    private static void TownHouse(Transform root, Vector3 position, float width, float height,
                                  Color wall, Color roof, Color woodDark, Color red)
    {
        position.y = GroundHeight(position.x, position.z);

        LevelBuilder.Box("TownFound", position + Vector3.up * 0.25f,
            new Vector3(width + 0.3f, 0.5f, 5.6f), new Color(0.3f, 0.29f, 0.27f), root);
        LevelBuilder.Box("TownWall", position + Vector3.up * (height * 0.5f + 0.3f),
            new Vector3(width, height, 5.2f), wall, root);
        LevelBuilder.Box("TownRoof", position + Vector3.up * (height + 0.55f),
            new Vector3(width + 0.9f, 0.5f, 5.9f), roof, root);
        LevelBuilder.Box("TownRidge", position + Vector3.up * (height + 0.82f),
            new Vector3(width + 1.0f, 0.12f, 0.3f), new Color(0.19f, 0.12f, 0.09f), root);

        // 朝街的门与两扇窗
        float faceX = position.x + width * 0.5f + 0.03f;
        LevelBuilder.Box("TownDoor", new Vector3(faceX, position.y + 1.25f, position.z),
            new Vector3(0.08f, 2.35f, 1.3f), woodDark, root, false);

        for (int i = 0; i < 2; i++)
        {
            float wz = position.z + (i == 0 ? -1.15f : 1.15f);
            LevelBuilder.Box("TownWindow", new Vector3(faceX, position.y + height * 0.66f, wz),
                new Vector3(0.06f, 0.9f, 0.95f), new Color(0.1f, 0.1f, 0.11f), root, false);
            LevelBuilder.Box("TownWindowBar", new Vector3(faceX + 0.03f, position.y + height * 0.66f, wz),
                new Vector3(0.04f, 0.95f, 0.08f), woodDark, root, false);
        }

        // 屋檐下挂一小块红布，年节没撤
        LevelBuilder.Box("TownBanner", new Vector3(faceX + 0.05f, position.y + height - 0.3f,
                position.z + 0.9f), new Vector3(0.04f, 0.7f, 0.5f), red, root, false);
    }

    private static void WineJar(Transform root, Vector3 position, Color jar, Color cloth)
    {
        position.y = GroundHeight(position.x, position.z);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "WineJar";
        body.transform.SetParent(root, true);
        body.transform.position = position + Vector3.up * 0.3f;
        body.transform.localScale = new Vector3(0.56f, 0.3f, 0.56f);
        body.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(jar);

        GameObject neck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        neck.name = "WineJarNeck";
        neck.transform.SetParent(root, true);
        neck.transform.position = position + Vector3.up * 0.62f;
        neck.transform.localScale = new Vector3(0.26f, 0.12f, 0.26f);
        neck.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(jar);
        Object.Destroy(neck.GetComponent<Collider>());

        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cap.name = "WineJarCap";
        cap.transform.SetParent(root, true);
        cap.transform.position = position + Vector3.up * 0.75f;
        cap.transform.localScale = new Vector3(0.3f, 0.035f, 0.3f);
        cap.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(cloth);
        Object.Destroy(cap.GetComponent<Collider>());
    }

    private static void TownLantern(Transform root, Vector3 position, Color post)
    {
        LevelBuilder.Box("LanternPost", position + Vector3.up * 1.4f,
            new Vector3(0.09f, 2.8f, 0.09f), post, root);
        LevelBuilder.Box("LanternArm", position + new Vector3(0.4f, 2.72f, 0f),
            new Vector3(0.8f, 0.06f, 0.06f), post, root, false);

        GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lamp.name = "Lantern";
        lamp.transform.SetParent(root, true);
        lamp.transform.position = position + new Vector3(0.72f, 2.42f, 0f);
        lamp.transform.localScale = new Vector3(0.3f, 0.4f, 0.3f);
        lamp.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(
            new Color(0.8f, 0.2f, 0.12f), new Color(1.4f, 0.25f, 0.1f));
        Object.Destroy(lamp.GetComponent<Collider>());

        GameObject lightGo = new GameObject("LanternLight");
        lightGo.transform.SetParent(root, true);
        lightGo.transform.position = position + new Vector3(0.72f, 2.35f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.55f, 0.32f);
        light.intensity = 0.8f;
        light.range = 8f;
    }

    // ───────────────────── 最后区域：娄山关 ─────────────────────

    private const float PassStartZ = 252f;
    private const float PassEndZ = 288f;
    private const float PassCenterX = 30f;
    private const float PassHalfWidth = 8f;
    private const float PassSummitY = 9.4f;

    private static float PassBaseY => GroundHeight(PassCenterX, PassStartZ) + 0.12f;

    /// <summary>关内石阶/关顶平台的高度；不在关内返回 float.MinValue。</summary>
    private static float PassHeight(float x, float z)
    {
        if (x < PassCenterX - PassHalfWidth || x > PassCenterX + PassHalfWidth) return float.MinValue;
        if (z >= PassEndZ) return PassSummitY + 0.2f;
        if (z >= PassStartZ)
        {
            return Mathf.Lerp(PassBaseY, PassSummitY,
                (z - PassStartZ) / (PassEndZ - PassStartZ));
        }
        return float.MinValue;
    }

    private static Vector3 BuildLoushanPass(Transform root, Color rock, Color rockDark,
                                            Color woodDark, Color red)
    {
        float baseY = PassBaseY;
        float rise = PassSummitY - baseY;
        float length = PassEndZ - PassStartZ;
        float slopeLength = Mathf.Sqrt(length * length + rise * rise);
        float pitch = Mathf.Atan2(rise, length) * Mathf.Rad2Deg;

        // 斜坡本体
        GameObject ramp = LevelBuilder.Box("LoushanRamp",
            new Vector3(PassCenterX, (baseY + PassSummitY) * 0.5f - 0.55f,
                        (PassStartZ + PassEndZ) * 0.5f),
            new Vector3(PassHalfWidth * 2f, 1.0f, slopeLength), rockDark, root);
        ramp.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);

        // 石阶线：每 1.6 米一道，跑上去有踏阶的节奏
        for (float z = PassStartZ + 0.8f; z < PassEndZ; z += 1.6f)
        {
            float y = Mathf.Lerp(baseY, PassSummitY, (z - PassStartZ) / length);
            GameObject step = LevelBuilder.Box("LoushanStep",
                new Vector3(PassCenterX, y + 0.04f, z),
                new Vector3(PassHalfWidth * 2f - 0.8f, 0.14f, 0.55f), rock, root, false);
            step.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);
        }

        // 两侧石栏
        for (int side = -1; side <= 1; side += 2)
        {
            float x = PassCenterX + side * (PassHalfWidth - 0.55f);
            GameObject rail = LevelBuilder.Box("LoushanRail",
                new Vector3(x, (baseY + PassSummitY) * 0.5f + 0.55f,
                            (PassStartZ + PassEndZ) * 0.5f),
                new Vector3(0.7f, 1.9f, slopeLength), rock, root);
            rail.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);

            // 栏上的积雪
            GameObject snow = LevelBuilder.Box("LoushanRailSnow",
                new Vector3(x, (baseY + PassSummitY) * 0.5f + 1.55f,
                            (PassStartZ + PassEndZ) * 0.5f),
                new Vector3(0.78f, 0.16f, slopeLength), new Color(0.7f, 0.75f, 0.8f), root, false);
            snow.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);
        }

        // 关顶平台：下面垫一整块山岩，避免平台悬空
        LevelBuilder.Box("LoushanMesa",
            new Vector3(PassCenterX, 3.5f, 298f),
            new Vector3(18.4f, 11.2f, 20.4f), rockDark, root);
        LevelBuilder.Box("LoushanPlatform",
            new Vector3(PassCenterX, PassSummitY + 0.2f - 0.45f, 298f),
            new Vector3(18f, 0.9f, 20f), rock, root);
        LevelBuilder.Box("LoushanPlateauSnow",
            new Vector3(PassCenterX, PassSummitY + 0.2f + 0.02f, 298f),
            new Vector3(18f, 0.04f, 20f), new Color(0.7f, 0.75f, 0.8f), root, false);
        LevelBuilder.Box("LoushanRampLip",
            new Vector3(PassCenterX, PassSummitY + 0.08f, PassEndZ - 0.1f),
            new Vector3(18f, 0.16f, 1.2f), rock, root, false);

        // 关门：两根石柱 + 门楣
        for (int side = -1; side <= 1; side += 2)
        {
            float x = PassCenterX + side * 4.2f;
            LevelBuilder.Box("LoushanGatePillar",
                new Vector3(x, PassSummitY + 2.4f, PassEndZ + 1.2f),
                new Vector3(1.3f, 4.4f, 1.3f), rock, root);
            LevelBuilder.Box("LoushanGateCap",
                new Vector3(x, PassSummitY + 4.7f, PassEndZ + 1.2f),
                new Vector3(1.7f, 0.3f, 1.7f), rockDark, root, false);
        }
        LevelBuilder.Box("LoushanGateLintel",
            new Vector3(PassCenterX, PassSummitY + 4.3f, PassEndZ + 1.2f),
            new Vector3(9.6f, 0.6f, 1.2f), rockDark, root);
        Flag(root, new Vector3(PassCenterX - 5.6f, PassSummitY + 0.2f, PassEndZ + 1.2f), 20f, red);
        Flag(root, new Vector3(PassCenterX + 5.6f, PassSummitY + 0.2f, PassEndZ + 1.2f), 200f, red);

        // 关顶的碉楼与碎石
        LevelBuilder.Box("SummitTowerBase",
            new Vector3(PassCenterX + 6.2f, PassSummitY + 1.0f, 302f),
            new Vector3(2.6f, 2.0f, 2.6f), rockDark, root);
        LevelBuilder.Box("SummitTowerDeck",
            new Vector3(PassCenterX + 6.2f, PassSummitY + 2.3f, 302f),
            new Vector3(3.2f, 0.3f, 3.2f), woodDark, root);
        for (int px = -1; px <= 1; px += 2)
        {
            for (int pz = -1; pz <= 1; pz += 2)
            {
                LevelBuilder.Box("SummitTowerPost",
                    new Vector3(PassCenterX + 6.2f + px * 1.35f, PassSummitY + 2.85f, 302f + pz * 1.35f),
                    new Vector3(0.18f, 0.86f, 0.18f), woodDark, root, false);
            }
        }
        LevelBuilder.Box("SummitTowerRoof",
            new Vector3(PassCenterX + 6.2f, PassSummitY + 3.4f, 302f),
            new Vector3(4.0f, 0.18f, 4.0f), new Color(0.22f, 0.14f, 0.1f), root, false);

        for (int i = 0; i < 14; i++)
        {
            Vector3 p = new Vector3(Random.Range(PassCenterX - 8f, PassCenterX + 8f), 0f,
                                    Random.Range(PassEndZ + 2f, 307f));
            p.y = PassSummitY + 0.3f;
            GameObject rubble = LevelBuilder.Box("SummitRubble", p,
                new Vector3(Random.Range(0.4f, 1.3f), Random.Range(0.2f, 0.6f),
                            Random.Range(0.4f, 1.2f)), rockDark, root, false);
            rubble.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        // 石阶两侧的嶙峋山岩，把关道夹在山体里
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 11; i++)
            {
                float z = 248f + i * 5.6f;
                float x = PassCenterX + side * Random.Range(10.5f, 15f);
                float w = Random.Range(2.2f, 4.4f);
                float h = Random.Range(2.6f, 6.4f);
                float groundY = GroundHeight(x, z);
                GameObject crag = LevelBuilder.Box("LoushanCrag",
                    new Vector3(x, groundY + h * 0.4f, z),
                    new Vector3(w, h, Random.Range(2.2f, 4.2f)), rock, root);
                crag.transform.rotation = Quaternion.Euler(
                    Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
            }
        }

        return new Vector3(PassCenterX, PassSummitY + 0.2f, 302f);
    }

    /// <summary>关顶的暴风雪粒子，范围固定，只覆盖娄山关。</summary>
    private static void BuildSnowstorm(Transform root, Vector3 summit)
    {
        GameObject go = new GameObject("LoushanSnowstorm");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(summit.x, summit.y + 12f, summit.z - 10f);

        ParticleSystem system = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 4.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.96f, 0.98f, 1f, 0.95f), new Color(0.86f, 0.92f, 1f, 0.7f));
        main.maxParticles = 4200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.08f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 1100f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(58f, 2f, 58f);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-4.2f, -1.6f);
        velocity.y = new ParticleSystem.MinMaxCurve(-6.5f, -3.5f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new Material(shader);
            material.mainTexture = SurfaceTextures.Snowflake;
            renderer.material = material;
        }
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ───────────────────────────── 四次渡河 ─────────────────────────────

    /// <summary>
    /// 一渡：六块河中跳石。石面做成能落两个人的大平台，
    /// 石头之间只留一脚宽的缝，跑动中不必每块都精确起跳。
    /// </summary>
    private static void BuildSteppingStones(Transform root, float z, Color rock, Color rockDark)
    {
        for (int i = 0; i < 6; i++)
        {
            float x = 11.5f - i * 4.6f;
            GameObject stone = LevelBuilder.Box("StepStone_" + i,
                new Vector3(x, -0.66f, z + (i % 2 == 0 ? 0f : 0.9f)),
                new Vector3(3.8f, 1.6f, Random.Range(3.6f, 4.0f)),
                i % 2 == 0 ? rock : rockDark, root);
            stone.transform.rotation = Quaternion.Euler(0f, Random.Range(-10f, 10f), 0f);

            // 石面青苔
            LevelBuilder.Box("StepMoss_" + i,
                new Vector3(x, 0.16f, stone.transform.position.z),
                new Vector3(3.4f, 0.05f, 3.3f), new Color(0.24f, 0.34f, 0.21f), root, false);
        }

        // 两侧沙洲，起跳和落脚都有一块缓冲
        LevelBuilder.Box("StepSandE", new Vector3(12.8f, -0.2f, z),
            new Vector3(5.6f, 0.9f, 5.6f), new Color(0.4f, 0.37f, 0.28f), root);
        LevelBuilder.Box("StepSandW", new Vector3(-12.8f, -0.2f, z),
            new Vector3(5.6f, 0.9f, 5.6f), new Color(0.4f, 0.37f, 0.28f), root);
    }

    /// <summary>
    /// 二渡：木板索桥。桥板加宽到能并排走两个人，板与板互相搭接，
    /// 不用再盯着每一块板落脚，但桥面仍然中间下沉、两边晃。
    /// </summary>
    private static void BuildRopeBridge(Transform root, float z, Color wood, Color woodDark, Color rope)
    {
        const float halfSpan = 13f;
        const int plankCount = 29;
        const float deckHalfZ = 2.0f;
        const float deckY = 0.3f;

        // 桥面登记为敌军的可通行走廊：追兵可以沿着桥板过河，
        // 而不是挤在桥头被栏杆卡住。走廊一直包到两岸踏台，
        // 桥口不再被岸线夹住；宽度按 4 米桥板来，够两个人错身。
        ChishuiWaterRule.AddBridgeCorridor(new Vector3(0f, 0f, z), halfSpan + 2.8f,
                                          deckHalfZ - 0.3f, deckY);

        for (int i = 0; i < plankCount; i++)
        {
            float x = -halfSpan + i * (halfSpan * 2f / (plankCount - 1));
            float sag = 0.28f - 0.1f * Mathf.Cos(x / halfSpan * Mathf.PI * 0.5f);

            GameObject plank = LevelBuilder.Box("RopePlank_" + i,
                new Vector3(x, sag, z), new Vector3(1.05f, 0.08f, deckHalfZ * 2f),
                i % 4 == 0 ? woodDark : wood, root);
            plank.transform.rotation = Quaternion.Euler(0f, Random.Range(-2f, 2f), 0f);
        }

        // 端头踏台
        LevelBuilder.Box("RopePadW", new Vector3(-14.2f, 0.16f, z),
            new Vector3(3.2f, 0.34f, deckHalfZ * 2f + 1.4f), woodDark, root);
        LevelBuilder.Box("RopePadE", new Vector3(14.2f, 0.16f, z),
            new Vector3(3.2f, 0.34f, deckHalfZ * 2f + 1.4f), woodDark, root);

        // 两侧扶手绳：分段跟随桥面下沉
        for (int side = 0; side < 2; side++)
        {
            float zSide = z + (side == 0 ? -deckHalfZ : deckHalfZ);

            for (int i = 0; i < 12; i++)
            {
                float x0 = -halfSpan + i * (halfSpan * 2f / 12f);
                float x1 = -halfSpan + (i + 1) * (halfSpan * 2f / 12f);
                float y0 = 1.05f - 0.1f * Mathf.Cos(x0 / halfSpan * Mathf.PI * 0.5f);
                float y1 = 1.05f - 0.1f * Mathf.Cos(x1 / halfSpan * Mathf.PI * 0.5f);

                Vector3 a = new Vector3(x0, y0, zSide);
                Vector3 b = new Vector3(x1, y1, zSide);
                GameObject segment = LevelBuilder.Box("RopeRail",
                    (a + b) * 0.5f, new Vector3(0.06f, 0.06f, 0.06f), rope, root, false);
                segment.transform.localScale = new Vector3(0.06f, 0.06f, (b - a).magnitude);
                segment.transform.rotation = Quaternion.LookRotation(b - a);
            }

            // 立柱
            LevelBuilder.Box("RopePostW", new Vector3(-13.6f, 0.75f, zSide),
                new Vector3(0.16f, 1.6f, 0.16f), woodDark, root);
            LevelBuilder.Box("RopePostE", new Vector3(13.6f, 0.75f, zSide),
                new Vector3(0.16f, 1.6f, 0.16f), woodDark, root);
        }

        // 悬吊绳
        for (int i = 2; i < 28; i += 3)
        {
            float x = -halfSpan + i * (halfSpan * 2f / (plankCount - 1));
            float sag = 0.28f - 0.1f * Mathf.Cos(x / halfSpan * Mathf.PI * 0.5f);

            for (int side = 0; side < 2; side++)
            {
                float zSide = z + (side == 0 ? -deckHalfZ : deckHalfZ);
                LevelBuilder.Box("RopeHanger", new Vector3(x, sag + 0.38f, zSide),
                    new Vector3(0.035f, 0.76f, 0.035f), rope, root, false);
            }
        }
    }

    /// <summary>
    /// 三渡：一条淹在水下的石堤。堤面加宽到能跑开，两侧露出水面，
    /// 走得慢仍然要在开阔地挨枪。
    /// </summary>
    private static void BuildFord(Transform root, float z, Color rock, Color mud)
    {
        LevelBuilder.Box("FordCauseway", new Vector3(0f, -0.49f, z),
            new Vector3(27f, 1.0f, 7.2f), mud, root);
        LevelBuilder.Box("FordCrown", new Vector3(0f, 0.02f, z),
            new Vector3(26.5f, 0.06f, 5.8f), new Color(0.4f, 0.37f, 0.29f), root, false);

        for (int i = 0; i < 9; i++)
        {
            float x = -12f + i * 3f;
            GameObject stone = LevelBuilder.Box("FordStone", new Vector3(x, 0.04f, z + Random.Range(-2.4f, 2.4f)),
                new Vector3(1.7f, 0.12f, 1.5f), rock, root, false);
            stone.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }
    }

    /// <summary>
    /// 四渡：断桥。两段残桥之间只剩一根加宽的独木梁，
    /// 梁上还缺了一块，跨过去就到对岸。
    /// </summary>
    private static void BuildBrokenBridge(Transform root, float z, Color wood, Color woodDark, Color rope)
    {
        // 西岸桥面
        LevelBuilder.Box("BrokenDeckW", new Vector3(-10f, 0.24f, z),
            new Vector3(7.2f, 0.14f, 4.4f), wood, root);
        LevelBuilder.Box("BrokenDeckW2", new Vector3(-6.2f, 0.24f, z),
            new Vector3(2.8f, 0.14f, 4.4f), woodDark, root);
        // 东岸桥面
        LevelBuilder.Box("BrokenDeckE", new Vector3(10f, 0.24f, z),
            new Vector3(7.2f, 0.14f, 4.4f), wood, root);
        LevelBuilder.Box("BrokenDeckE2", new Vector3(6.2f, 0.24f, z),
            new Vector3(2.8f, 0.14f, 4.4f), woodDark, root);

        // 独木梁：西段 -5.4 → -1.2，东段 0.6 → 4.8，中间 1.8 米跨过去
        LevelBuilder.Box("BeamW", new Vector3(-3.3f, 0.3f, z),
            new Vector3(4.2f, 0.16f, 1.3f), woodDark, root);
        LevelBuilder.Box("BeamE", new Vector3(2.7f, 0.3f, z),
            new Vector3(4.2f, 0.16f, 1.3f), woodDark, root);

        // 残破的斜撑和垂下的断绳，说明这里是被炸断的
        for (int side = 0; side < 2; side++)
        {
            float zSide = z + (side == 0 ? -1.5f : 1.5f);
            GameObject strut = LevelBuilder.Box("BrokenStrut", new Vector3(-5.4f, 0.55f, zSide),
                new Vector3(0.14f, 0.14f, 2.6f), wood, root);
            strut.transform.rotation = Quaternion.Euler(0f, 0f, 32f);

            LevelBuilder.Box("BrokenRope", new Vector3(4.9f, -0.1f, zSide),
                new Vector3(0.05f, 1.1f, 0.05f), rope, root, false);
        }

        // 桥墩
        LevelBuilder.Box("BrokenPierW", new Vector3(-5.2f, -0.6f, z),
            new Vector3(0.8f, 1.3f, 3.6f), woodDark, root);
        LevelBuilder.Box("BrokenPierE", new Vector3(4.9f, -0.6f, z),
            new Vector3(0.8f, 1.3f, 3.6f), woodDark, root);
    }

    // ───────────────────────────── 岸上景物 ─────────────────────────────

    private static void BuildBankScenery(Transform root, Color ground, Color rock, Color rockDark,
                                         Color bamboo, Color foliage, Color red)
    {
        // 东岸乱石与竹林
        Vector3[] eastRocks =
        {
            new Vector3(22f, 0f, 10f), new Vector3(34f, 0f, 20f), new Vector3(29f, 0f, 44f),
            new Vector3(40f, 0f, 62f), new Vector3(24f, 0f, 74f), new Vector3(35f, 0f, 112f),
            new Vector3(27f, 0f, 128f), new Vector3(42f, 0f, 140f), new Vector3(30f, 0f, 178f),
            new Vector3(22f, 0f, 198f), new Vector3(38f, 0f, 206f), new Vector3(44f, 0f, 246f)
        };

        Vector3[] westRocks =
        {
            new Vector3(-24f, 0f, 12f), new Vector3(-36f, 0f, 38f), new Vector3(-22f, 0f, 52f),
            new Vector3(-33f, 0f, 66f), new Vector3(-27f, 0f, 82f), new Vector3(-40f, 0f, 118f),
            new Vector3(-25f, 0f, 142f), new Vector3(-37f, 0f, 176f), new Vector3(-23f, 0f, 194f),
            new Vector3(-34f, 0f, 210f), new Vector3(-29f, 0f, 232f)
        };

        for (int i = 0; i < eastRocks.Length; i++)
        {
            RockOutcrop(root, eastRocks[i], i % 2 == 0 ? rock : rockDark);
        }
        for (int i = 0; i < westRocks.Length; i++)
        {
            RockOutcrop(root, westRocks[i], i % 2 == 0 ? rockDark : rock);
        }

        // 竹林
        Vector3[] bambooSpots =
        {
            new Vector3(31f, 0f, 34f), new Vector3(44f, 0f, 48f), new Vector3(25f, 0f, 100f),
            new Vector3(36f, 0f, 118f), new Vector3(-31f, 0f, 22f), new Vector3(-42f, 0f, 56f),
            new Vector3(-26f, 0f, 96f), new Vector3(-38f, 0f, 132f), new Vector3(-27f, 0f, 186f),
            new Vector3(28f, 0f, 232f)
        };

        for (int i = 0; i < bambooSpots.Length; i++)
        {
            BambooCluster(root, bambooSpots[i], bamboo, foliage);
        }

        // 渡口边的芦苇
        for (int i = 0; i < 26; i++)
        {
            float z = Random.Range(-20f, 250f);
            float x = Random.value < 0.5f ? Random.Range(13.4f, 16.5f) : Random.Range(-16.5f, -13.4f);
            Reeds(root, new Vector3(x, 0f, z), foliage);
        }

        // 断墙、废弃哨楼：让两岸不只是空地
        RuinedWall(root, new Vector3(38f, 0f, 86f), 7f, 2.2f, 18f, rockDark);
        RuinedWall(root, new Vector3(-34f, 0f, 108f), 6f, 1.8f, -24f, rockDark);
        RuinedWall(root, new Vector3(30f, 0f, 168f), 8f, 2.4f, 8f, rockDark);
        RuinedWall(root, new Vector3(-36f, 0f, 200f), 6f, 2f, -12f, rockDark);
        Watchtower(root, new Vector3(43f, 0f, 150f), new Color(0.3f, 0.24f, 0.16f), red);

        // 河边小片水草
        for (int i = 0; i < 12; i++)
        {
            float z = 6f + i * 20f;
            LevelBuilder.Box("ShoalMoss", new Vector3(Random.Range(-10f, 10f), -0.22f, z),
                new Vector3(Random.Range(2f, 4f), 0.04f, Random.Range(2f, 4f)),
                new Color(0.2f, 0.3f, 0.2f), root, false);
        }
    }

    // ───────────────────── 河滩与平地的游击掩体 ─────────────────────

    /// <summary>
    /// 每个渡口两岸都摆上沙袋胸墙：贴岸的一排挡正面火力，
    /// 平地里的几处给绕后的人落脚。谁先占住谁就能探头打、缩头换弹。
    /// 沙袋按地形最低角落地，不会一半悬在坡上。
    /// </summary>
    private static void BuildGuerrillaCover(Transform root, Color wood, Color woodDark,
                                            Color path)
    {
        Color bag = new Color(0.45f, 0.42f, 0.32f);
        Color bagAlt = new Color(0.38f, 0.35f, 0.27f);

        // ── 一渡 · 土城两岸 ──
        BankSandbagWall(root, 18.6f, 22f, 90f, 5, 3, bag, bagAlt);
        BankSandbagWall(root, 15.9f, 39f, 90f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, -16.8f, 26f, 90f, 5, 3, bag, bagAlt);
        BankSandbagWall(root, -16.2f, 42f, 90f, 4, 2, bag, bagAlt);
        // 平地上的一处，守住从渡口通往山脚的路
        BankSandbagWall(root, 24.5f, 58f, 12f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, -24.5f, 74f, 6f, 4, 2, bag, bagAlt);

        // ── 二渡 · 太平渡 ──
        BankSandbagWall(root, 17.2f, 86f, 90f, 5, 3, bag, bagAlt);
        BankSandbagWall(root, 16.4f, 103f, 90f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, -16.9f, 90f, 90f, 5, 2, bag, bagAlt);
        BankSandbagWall(root, -17.3f, 104f, 90f, 4, 3, bag, bagAlt);
        BankSandbagWall(root, 26f, 132f, -8f, 4, 2, bag, bagAlt);

        // ── 三渡 · 二郎滩 ──
        BankSandbagWall(root, 17f, 150f, 90f, 5, 2, bag, bagAlt);
        BankSandbagWall(root, 16.6f, 174f, 90f, 4, 3, bag, bagAlt);
        BankSandbagWall(root, -16.6f, 150f, 90f, 5, 3, bag, bagAlt);
        BankSandbagWall(root, -24.5f, 172f, 6f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, -24f, 122f, -6f, 4, 2, bag, bagAlt);

        // ── 四渡 · 茅台镇外 ──
        // 镇里街面留给通行，沙袋靠河沿和屋后摆
        BankSandbagWall(root, -20.4f, 176f, 90f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, -20.7f, 192f, 90f, 5, 2, bag, bagAlt);
        BankSandbagWall(root, -20.4f, 208f, 90f, 4, 3, bag, bagAlt);
        BankSandbagWall(root, -17f, 222f, 90f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, 18.2f, 214f, 90f, 5, 3, bag, bagAlt);
        BankSandbagWall(root, 17.4f, 234f, 90f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, 22.5f, 246f, 0f, 4, 2, bag, bagAlt);
        BankSandbagWall(root, -24f, 232f, 90f, 4, 2, bag, bagAlt);

        // ── 两岸新增民居 ──
        // 东岸：一趟吊脚楼顺着河排开，屋顶把渡口切成一段一段
        StiltHouse(root, new Vector3(32f, 0f, 56f), 6.2f, 3.1f, -14f, wood, woodDark, path);
        StiltHouse(root, new Vector3(35f, 0f, 72f), 5.4f, 2.8f, 8f, wood, woodDark, path);
        StiltHouse(root, new Vector3(29.5f, 0f, 120f), 5.6f, 2.9f, -22f, wood, woodDark, path);
        StiltHouse(root, new Vector3(34f, 0f, 138f), 6.4f, 3.2f, 16f, wood, woodDark, path);
        StiltHouse(root, new Vector3(33f, 0f, 190f), 5.8f, 3.0f, -8f, wood, woodDark, path);
        StiltHouse(root, new Vector3(29f, 0f, 212f), 5.2f, 2.7f, 24f, wood, woodDark, path);

        // 西岸：青杠坡脚下与二郎滩背后的几家农户
        StiltHouse(root, new Vector3(-31f, 0f, 58f), 6f, 3.0f, 12f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-35f, 0f, 74f), 5.2f, 2.7f, -18f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-31f, 0f, 126f), 5.6f, 2.8f, 20f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-34f, 0f, 146f), 5f, 2.6f, -6f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-34f, 0f, 226f), 5.4f, 2.8f, 12f, wood, woodDark, path);
    }

    /// <summary>沿 yaw 方向码一堵沙袋墙：默认沿河岸走向，朝河心的一面挡子弹。</summary>
    private static void BankSandbagWall(Transform root, float x, float z, float yaw,
                                        int bags, int rows, Color bag, Color bagAlt)
    {
        const float spacing = 0.76f;
        const float halfLength = 0.36f;
        const float halfDepth = 0.25f;

        Vector3 along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        Vector3 across = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        for (int row = 0; row < rows; row++)
        {
            // 最上层中间留一个射击孔，蹲在后面能探枪
            int gap = (row == rows - 1 && bags >= 4) ? bags / 2 : -1;

            for (int b = 0; b < bags; b++)
            {
                if (b == gap) continue;

                Vector3 center = new Vector3(x, 0f, z)
                    + along * ((b - (bags - 1) * 0.5f) * spacing)
                    + across * (row % 2 == 0 ? -0.04f : 0.04f);
                center += across * Random.Range(-0.03f, 0.03f);

                // 取四角最低点：宁可让沙袋陷进坡里，也不能悬空
                float ground = Mathf.Min(
                    Mathf.Min(
                        GroundHeight(center.x - along.x * halfLength - across.x * halfDepth,
                                     center.z - along.z * halfLength - across.z * halfDepth),
                        GroundHeight(center.x + along.x * halfLength + across.x * halfDepth,
                                     center.z + along.z * halfLength + across.z * halfDepth)),
                    Mathf.Min(
                        GroundHeight(center.x - along.x * halfLength + across.x * halfDepth,
                                     center.z - along.z * halfLength + across.z * halfDepth),
                        GroundHeight(center.x + along.x * halfLength - across.x * halfDepth,
                                     center.z + along.z * halfLength - across.z * halfDepth)));

                GameObject go = LevelBuilder.Box("GuerrillaSandbag",
                    new Vector3(center.x, ground + 0.165f + row * 0.33f, center.z),
                    new Vector3(0.72f, 0.33f, 0.5f),
                    (b + row) % 2 == 0 ? bag : bagAlt, root);
                go.transform.rotation = Quaternion.Euler(Random.Range(-2f, 2f),
                    yaw + Random.Range(-3f, 3f), 0f);
            }
        }
    }


    private static void RockOutcrop(Transform root, Vector3 position, Color color)
    {
        position.y = GroundHeight(position.x, position.z);

        for (int i = 0; i < 3; i++)
        {
            Vector3 size = new Vector3(Random.Range(1.6f, 3.6f), Random.Range(0.9f, 2.4f),
                                       Random.Range(1.6f, 3.2f));
            Vector3 offset = new Vector3(Random.Range(-1.4f, 1.4f), 0f,
                                         Random.Range(-1.4f, 1.4f));

            // 坡上的石头按四角最低点落地，矮石不能悬在半坡上
            float bx = position.x + offset.x;
            float bz = position.z + offset.z;
            float ground = Mathf.Min(
                Mathf.Min(GroundHeight(bx - size.x * 0.5f, bz - size.z * 0.5f),
                          GroundHeight(bx + size.x * 0.5f, bz + size.z * 0.5f)),
                Mathf.Min(GroundHeight(bx - size.x * 0.5f, bz + size.z * 0.5f),
                          GroundHeight(bx + size.x * 0.5f, bz - size.z * 0.5f)));

            GameObject rock = LevelBuilder.Box("Rock",
                new Vector3(bx, ground + size.y * 0.5f - 0.15f, bz),
                size, color, root);
            rock.transform.rotation = Quaternion.Euler(
                Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
        }
    }

    private static void BambooCluster(Transform root, Vector3 position, Color stalk, Color leaf)
    {
        position.y = GroundHeight(position.x, position.z);
        int count = Random.Range(4, 7);

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
            float height = Random.Range(3.4f, 6.2f);

            LevelBuilder.Box("BambooStalk", position + offset + Vector3.up * (height * 0.5f),
                new Vector3(0.09f, height, 0.09f), stalk, root);

            for (int l = 0; l < 3; l++)
            {
                GameObject leafGeo = LevelBuilder.Box("BambooLeaf",
                    position + offset + Vector3.up * (height * 0.62f + l * 0.7f)
                    + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)),
                    new Vector3(1.1f, 0.04f, 0.42f), leaf, root, false);
                leafGeo.transform.rotation = Quaternion.Euler(
                    Random.Range(-24f, 24f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
            }
        }
    }

    private static void Reeds(Transform root, Vector3 position, Color color)
    {
        position.y = GroundHeight(position.x, position.z);
        int count = Random.Range(3, 6);
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.7f, 0.7f), 0f, Random.Range(-0.7f, 0.7f));
            GameObject reed = LevelBuilder.Box("Reed", position + offset + Vector3.up * 0.45f,
                new Vector3(0.035f, Random.Range(0.7f, 1.2f), 0.035f), color, root, false);
            reed.transform.rotation = Quaternion.Euler(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
        }
    }

    private static void RuinedWall(Transform root, Vector3 position, float width, float height,
                                   float yaw, Color color)
    {
        position.y = GroundHeight(position.x, position.z);

        GameObject wall = LevelBuilder.Box("RuinWall", position + Vector3.up * (height * 0.5f),
            new Vector3(width, height, 0.5f), color, root);
        wall.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // 塌掉的缺口
        GameObject gap = LevelBuilder.Box("RuinGap", position + new Vector3(0f, height * 0.8f, 0f),
            new Vector3(width * 0.28f, height * 0.4f, 0.6f), new Color(0.08f, 0.08f, 0.07f), root, false);
        gap.transform.rotation = wall.transform.rotation;

        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = wall.transform.rotation * new Vector3(
                Random.Range(-width * 0.6f, width * 0.6f), 0f, Random.Range(0.8f, 2.2f));
            LevelBuilder.Box("Rubble", position + offset + Vector3.up * 0.12f,
                new Vector3(Random.Range(0.3f, 0.8f), Random.Range(0.15f, 0.4f), Random.Range(0.3f, 0.7f)),
                color, root);
        }
    }

    private static void Watchtower(Transform root, Vector3 position, Color wood, Color red)
    {
        position.y = GroundHeight(position.x, position.z);

        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -1.2f : 1.2f;
            float z = i < 2 ? -1.2f : 1.2f;
            LevelBuilder.Box("TowerLeg", position + new Vector3(x, 2.2f, z),
                new Vector3(0.2f, 4.4f, 0.2f), wood, root);
        }

        LevelBuilder.Box("TowerDeck", position + Vector3.up * 4.5f,
            new Vector3(3.2f, 0.2f, 3.2f), wood, root);
        LevelBuilder.Box("TowerRail", position + new Vector3(0f, 5.1f, -1.6f),
            new Vector3(3.2f, 0.8f, 0.1f), wood, root, false);
        LevelBuilder.Box("TowerRoof", position + Vector3.up * 6.1f,
            new Vector3(4.0f, 0.16f, 4.0f), new Color(0.24f, 0.2f, 0.14f), root, false);

        // 哨楼上的敌旗
        LevelBuilder.Box("TowerPole", position + new Vector3(1.2f, 6.9f, 1.2f),
            new Vector3(0.08f, 2.6f, 0.08f), wood, root, false);
        LevelBuilder.Box("TowerFlag", position + new Vector3(1.9f, 7.7f, 1.2f),
            new Vector3(1.1f, 0.7f, 0.03f), red, root, false);
    }

    private static void Flag(Transform root, Vector3 position, float yaw, Color red)
    {
        if (position.y < 0.01f) position.y = GroundHeight(position.x, position.z);

        LevelBuilder.Box("FlagPole", position + Vector3.up * 1.9f,
            new Vector3(0.08f, 3.8f, 0.08f), new Color(0.28f, 0.22f, 0.14f), root);
        GameObject banner = LevelBuilder.Box("FlagBanner", position + new Vector3(0.7f, 3.3f, 0f),
            new Vector3(1.4f, 0.8f, 0.03f), red, root, false);
        banner.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private static void Path(Transform root, Vector3 from, Vector3 to, Color color)
    {
        Path(root, from, to, color, 3.2f);
    }

    private static void Path(Transform root, Vector3 from, Vector3 to, Color color, float width)
    {
        Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
        float length = flat.magnitude;
        if (length < 0.01f) return;

        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 3f));

        for (int i = 0; i < steps; i++)
        {
            Vector3 a = Vector3.Lerp(from, to, i / (float)steps);
            Vector3 b = Vector3.Lerp(from, to, (i + 1) / (float)steps);

            a.y = GroundHeight(a.x, a.z) + 0.04f;
            b.y = GroundHeight(b.x, b.z) + 0.04f;

            Vector3 delta = b - a;
            GameObject path = LevelBuilder.Box("DirtPath", (a + b) * 0.5f,
                new Vector3(width, 0.08f, delta.magnitude + 0.2f), color, root, false);
            path.transform.rotation = Quaternion.LookRotation(delta.normalized);
        }
    }

    // ───────────────────────────── 河谷两端 ─────────────────────────────

    /// <summary>
    /// 地图南北两头原本只有空地。这里补上赤水河两岸真正该有的东西：
    /// 南端渡口码头、木船竹筏、吊脚楼、水车与酒坛，
    /// 北端进关前的石牌坊、梯田、松林和山民木屋。
    /// </summary>
    private static void BuildRiverEnds(Transform root, Color wood, Color woodDark, Color rope,
                                       Color rock, Color rockDark, Color foliage, Color bamboo,
                                       Color path, Color red)
    {
        // ── 南端：出发的东岸渡口 ──
        FerryLanding(root, 1f, 14f, wood, woodDark, rope, rock);
        FerryLanding(root, -1f, 6f, wood, woodDark, rope, rock);
        Waterwheel(root, new Vector3(16.6f, 0f, 19f), wood, woodDark, rock);

        StiltHouse(root, new Vector3(33f, 0f, -6f), 6.4f, 3.2f, -18f, wood, woodDark, path);
        StiltHouse(root, new Vector3(38f, 0f, 14f), 5.6f, 2.9f, 24f, wood, woodDark, path);
        StiltHouse(root, new Vector3(30f, 0f, 22f), 5.0f, 2.6f, -40f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-27f, 0f, 8f), 5.8f, 3.0f, 118f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-35f, 0f, 18f), 5.2f, 2.7f, 154f, wood, woodDark, path);
        StiltHouse(root, new Vector3(-25f, 0f, -8f), 5.0f, 2.6f, 76f, wood, woodDark, path);

        // 渡口边的酒坛与晒架：赤水河一带家家酿酒
        WineJar(root, new Vector3(28.5f, 0f, 12f), new Color(0.32f, 0.24f, 0.17f),
            new Color(0.7f, 0.66f, 0.52f));
        WineJar(root, new Vector3(27.4f, 0f, 13.6f), new Color(0.3f, 0.23f, 0.16f),
            new Color(0.68f, 0.64f, 0.5f));
        WineJar(root, new Vector3(-31f, 0f, 4f), new Color(0.32f, 0.24f, 0.17f),
            new Color(0.7f, 0.66f, 0.52f));
        DryingRack(root, new Vector3(31f, 0f, 6f), wood);
        DryingRack(root, new Vector3(-30f, 0f, 20f), wood);

        // 渡口牌：一块木牌，告诉来往的人这是哪里
        Signpost(root, new Vector3(19.5f, 0f, 4.5f), 205f, wood, woodDark, red);

        BambooCluster(root, new Vector3(42f, 0f, -10f), bamboo, foliage);
        BambooCluster(root, new Vector3(28f, 0f, 27f), bamboo, foliage);
        BambooCluster(root, new Vector3(-39f, 0f, -4f), bamboo, foliage);
        BambooCluster(root, new Vector3(-28f, 0f, 24f), bamboo, foliage);
        RockOutcrop(root, new Vector3(45f, 0f, 8f), rockDark);
        RockOutcrop(root, new Vector3(-43f, 0f, 2f), rock);

        // ── 北端：娄山关下的进关路 ──
        StoneArchway(root, new Vector3(24f, 0f, 238f), 12f, rock, red);
        StoneArchway(root, new Vector3(-24f, 0f, 236f), -8f, rockDark, red);

        TerracedField(root, new Vector3(-36f, 0f, 262f), 18f, 30f, -6f, rockDark);
        TerracedField(root, new Vector3(50f, 0f, 248f), 14f, 22f, 10f, rockDark);

        MountainHut(root, new Vector3(-30f, 0f, 288f), 96f, wood, woodDark);
        MountainHut(root, new Vector3(50f, 0f, 284f), -74f, wood, woodDark);

        // 松林：把光秃秃的山坡填满
        Vector3[] pines =
        {
            new Vector3(44f, 0f, 232f), new Vector3(54f, 0f, 244f), new Vector3(46f, 0f, 262f),
            new Vector3(58f, 0f, 274f), new Vector3(42f, 0f, 284f), new Vector3(52f, 0f, 298f),
            new Vector3(-28f, 0f, 246f), new Vector3(-38f, 0f, 252f), new Vector3(-48f, 0f, 242f),
            new Vector3(-30f, 0f, 272f), new Vector3(-44f, 0f, 280f), new Vector3(-34f, 0f, 296f),
            new Vector3(-52f, 0f, 264f), new Vector3(60f, 0f, 252f), new Vector3(36f, 0f, 306f)
        };
        for (int i = 0; i < pines.Length; i++)
        {
            PineTree(root, pines[i], woodDark, foliage);
        }

        // 关口的号火堆：石头圈、柴垛和一面红旗
        BeaconPit(root, new Vector3(21f, 0f, 230f), rock, wood, red);

        RockOutcrop(root, new Vector3(-26f, 0f, 240f), rock);
        RockOutcrop(root, new Vector3(46f, 0f, 226f), rockDark);
        RockOutcrop(root, new Vector3(-42f, 0f, 302f), rock);

        // 两头的水边再补一层芦苇，别让河岸像一条光板
        for (int i = 0; i < 18; i++)
        {
            float z = Random.value < 0.5f
                ? Random.Range(-12f, 30f)
                : Random.Range(230f, 268f);
            float x = Random.value < 0.5f
                ? Random.Range(13.4f, 16.5f)
                : Random.Range(-16.5f, -13.4f);
            Reeds(root, new Vector3(x, 0f, z), foliage);
        }
    }

    /// <summary>河岸码头：石阶从岸顶下到泥滩，旁边拴着木船、竹筏和渔网架。</summary>
    private static void FerryLanding(Transform root, float side, float z, Color wood,
                                     Color woodDark, Color rope, Color rock)
    {
        float sign = side >= 0f ? 1f : -1f;

        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            float x = sign * Mathf.Lerp(16.8f, 13.6f, t);
            float y = Mathf.Lerp(0.64f, 0.12f, t);

            GameObject step = LevelBuilder.Box("FerryStep_" + i,
                new Vector3(x, y, z + Random.Range(-0.15f, 0.15f)),
                new Vector3(1.4f, 0.24f, Random.Range(3.1f, 3.5f)), rock, root);
            step.transform.rotation = Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
        }

        // 系船桩和缆绳
        for (int i = 0; i < 2; i++)
        {
            float pz = z + (i == 0 ? -2.4f : 2.4f);
            LevelBuilder.Box("MoorPost", new Vector3(sign * 14.1f, 0.6f, pz),
                new Vector3(0.22f, 1.1f, 0.22f), woodDark, root);
        }

        Vector3 a = new Vector3(sign * 14.1f, 0.95f, z - 2.4f);
        Vector3 b = new Vector3(sign * 14.1f, 0.95f, z + 2.4f);
        GameObject ropeLine = LevelBuilder.Box("MoorRope", (a + b) * 0.5f,
            new Vector3(0.05f, 0.05f, 4.8f), rope, root, false);
        ropeLine.transform.rotation = Quaternion.LookRotation(b - a);

        Boat(root, new Vector3(sign * 11.2f, -0.12f, z + 3.8f),
            sign > 0f ? -6f : 186f, wood, woodDark);
        Raft(root, new Vector3(sign * 11.5f, -0.1f, z - 3.6f), wood, rope);
        FishingNet(root, new Vector3(sign * 15.6f, 0f, z + 6.2f), 0f, wood, rope);
    }

    /// <summary>木渡船：船壳、翘起的船头船尾、篷子和一根撑篙。</summary>
    private static void Boat(Transform root, Vector3 position, float yaw, Color wood, Color woodDark)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 At(float x, float y, float z) => position + rotation * new Vector3(x, y, z);

        LevelBuilder.Box("BoatHull", At(0f, 0f, 0f),
            new Vector3(1.55f, 0.5f, 4.2f), wood, root).transform.rotation = rotation;
        LevelBuilder.Box("BoatWell", At(0f, 0.16f, 0f),
            new Vector3(1.05f, 0.18f, 3.4f), new Color(0.16f, 0.12f, 0.09f), root, false)
            .transform.rotation = rotation;

        // 船头、船尾上翘
        GameObject bow = LevelBuilder.Box("BoatBow", At(0f, 0.2f, 2.3f),
            new Vector3(1.2f, 0.4f, 1.2f), woodDark, root);
        bow.transform.rotation = rotation * Quaternion.Euler(-24f, 0f, 0f);

        GameObject stern = LevelBuilder.Box("BoatStern", At(0f, 0.2f, -2.3f),
            new Vector3(1.2f, 0.4f, 1.2f), woodDark, root);
        stern.transform.rotation = rotation * Quaternion.Euler(24f, 0f, 0f);

        // 竹篷
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -0.62f : 0.62f;
            float z = i < 2 ? -1.2f : 1.2f;
            LevelBuilder.Box("BoatPost", At(x, 0.55f, z),
                new Vector3(0.08f, 0.8f, 0.08f), woodDark, root, false);
        }

        GameObject canopy = LevelBuilder.Box("BoatCanopy", At(0f, 1.0f, 0f),
            new Vector3(1.7f, 0.12f, 3.0f), new Color(0.42f, 0.37f, 0.22f), root, false);
        canopy.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 8f);

        // 撑篙
        GameObject pole = LevelBuilder.Box("BoatPole", At(0.7f, 0.9f, -1.4f),
            new Vector3(0.06f, 3.6f, 0.06f), wood, root, false);
        pole.transform.rotation = rotation * Quaternion.Euler(52f, 0f, 12f);
    }

    /// <summary>竹筏：十来根竹子并排，两道横箍，一根撑竿。</summary>
    private static void Raft(Transform root, Vector3 position, Color bamboo, Color rope)
    {
        for (int i = 0; i < 8; i++)
        {
            float x = -1.05f + i * 0.3f;
            LevelBuilder.Box("RaftPole", position + new Vector3(x, 0f, 0f),
                new Vector3(0.24f, 0.14f, 3.8f), i % 2 == 0 ? bamboo : new Color(0.4f, 0.42f, 0.25f),
                root, false);
        }

        for (int i = 0; i < 2; i++)
        {
            float z = i == 0 ? -1.1f : 1.1f;
            LevelBuilder.Box("RaftTie", position + new Vector3(0f, 0.1f, z),
                new Vector3(2.5f, 0.12f, 0.16f), rope, root, false);
        }

        GameObject pole = LevelBuilder.Box("RaftPoleUp", position + new Vector3(1.2f, 1.1f, 1.4f),
            new Vector3(0.07f, 3.2f, 0.07f), bamboo, root, false);
        pole.transform.rotation = Quaternion.Euler(-28f, 0f, 16f);
    }

    /// <summary>渔网架：两根竹竿挑着一张网，赤水河上的常见营生。</summary>
    private static void FishingNet(Transform root, Vector3 position, float yaw,
                                   Color wood, Color rope)
    {
        position.y = GroundHeight(position.x, position.z);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -1.0f : 1.0f;
            Vector3 post = position + rotation * new Vector3(x, 1.35f, 0f);
            LevelBuilder.Box("NetPost", post, new Vector3(0.1f, 2.7f, 0.1f), wood, root, false);
        }

        GameObject net = LevelBuilder.Box("Net", position + rotation * new Vector3(0f, 2.3f, 0f),
            new Vector3(2.2f, 1.5f, 0.04f), new Color(0.2f, 0.22f, 0.18f), root, false);
        net.transform.rotation = rotation * Quaternion.Euler(0f, 0f, 3f);

        // 挂网的两根横杆
        for (int i = 0; i < 2; i++)
        {
            float y = i == 0 ? 2.95f : 1.6f;
            LevelBuilder.Box("NetBar", position + rotation * new Vector3(0f, y, 0f),
                new Vector3(2.4f, 0.07f, 0.07f), rope, root, false);
        }
    }

    /// <summary>水车：半泡在河里的大木轮，撑起河湾的生气。</summary>
    private static void Waterwheel(Transform root, Vector3 position, Color wood,
                                   Color woodDark, Color rock)
    {
        position.y = GroundHeight(position.x, position.z);

        // 支架
        for (int i = 0; i < 2; i++)
        {
            float z = i == 0 ? -1.3f : 1.3f;
            LevelBuilder.Box("WheelPost", position + new Vector3(-0.6f, 1.8f, z),
                new Vector3(0.24f, 3.6f, 0.24f), woodDark, root);
        }

        Vector3 hub = position + new Vector3(-2.4f, 1.5f, 0f);

        // 外圈用一圈短木段围出来
        for (int i = 0; i < 16; i++)
        {
            float angle = i * 22.5f;
            Vector3 dir = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
            GameObject rimSeg = LevelBuilder.Box("WheelRim", hub + dir * 1.98f,
                new Vector3(0.42f, 0.2f, 0.22f), wood, root, false);
            rimSeg.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // 辐条与刮板
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f;
            Vector3 spokeDir = Quaternion.Euler(0f, 0f, angle) * Vector3.up;

            GameObject spoke = LevelBuilder.Box("WheelSpoke", hub + spokeDir * 1.0f,
                new Vector3(0.12f, 2.1f, 0.12f), woodDark, root, false);
            spoke.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            GameObject paddle = LevelBuilder.Box("WheelPaddle", hub + spokeDir * 2.05f,
                new Vector3(0.7f, 0.5f, 0.9f), wood, root, false);
            paddle.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // 轴与石基
        LevelBuilder.Box("WheelAxle", hub, new Vector3(0.18f, 0.18f, 3.2f), woodDark, root, false);
        LevelBuilder.Box("WheelBase", position + new Vector3(-0.6f, 0.2f, 0f),
            new Vector3(1.4f, 0.5f, 3.4f), rock, root);
    }

    /// <summary>吊脚楼：木桩撑在坡上，挑出一层楼板，屋顶是两面坡。</summary>
    private static void StiltHouse(Transform root, Vector3 position, float width, float height,
                                   float yaw, Color wood, Color woodDark, Color thatch)
    {
        position.y = GroundHeight(position.x, position.z);

        GameObject house = new GameObject("StiltHouse");
        house.transform.SetParent(root, false);
        house.transform.position = position;
        house.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Transform t = house.transform;
        float depth = width * 0.78f;
        float deckY = height * 0.42f;

        // 木桩
        for (int ix = 0; ix < 2; ix++)
        {
            for (int iz = 0; iz < 2; iz++)
            {
                float x = (ix == 0 ? -1f : 1f) * (width * 0.42f);
                float z = (iz == 0 ? -1f : 1f) * (depth * 0.42f);
                // 桩脚多埋 0.8 米：坡地上的房子不会有一角悬空
                LevelBuilder.Box("Stilt", t.TransformPoint(new Vector3(x, deckY * 0.5f - 0.4f, z)),
                    new Vector3(0.24f, deckY + 0.8f, 0.24f), woodDark, t);
            }
        }

        // 楼板与墙体
        LevelBuilder.Box("HouseDeck", t.TransformPoint(new Vector3(0f, deckY, 0f)),
            new Vector3(width, 0.18f, depth), wood, t);

        LevelBuilder.Box("HouseBody", t.TransformPoint(new Vector3(0f, deckY + height * 0.3f, 0f)),
            new Vector3(width * 0.92f, height * 0.6f, depth * 0.9f), woodDark, t);

        // 木骨格：外墙上几条竖棂
        for (int i = -2; i <= 2; i++)
        {
            LevelBuilder.Box("HouseBeam",
                t.TransformPoint(new Vector3(i * width * 0.2f, deckY + height * 0.3f, depth * 0.46f)),
                new Vector3(0.1f, height * 0.62f, 0.08f), wood, t, false);
        }

        // 门与窗
        LevelBuilder.Box("HouseDoor",
            t.TransformPoint(new Vector3(-width * 0.15f, deckY + height * 0.24f, depth * 0.47f)),
            new Vector3(width * 0.24f, height * 0.42f, 0.06f), new Color(0.13f, 0.09f, 0.06f), t, false);
        LevelBuilder.Box("HouseWindow",
            t.TransformPoint(new Vector3(width * 0.26f, deckY + height * 0.38f, depth * 0.47f)),
            new Vector3(width * 0.2f, height * 0.2f, 0.06f), new Color(0.12f, 0.11f, 0.09f), t, false);

        // 两面坡屋顶
        float roofY = deckY + height * 0.62f;
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject slope = LevelBuilder.Box("HouseRoof",
                t.TransformPoint(new Vector3(side * width * 0.26f, roofY + height * 0.12f, 0f)),
                new Vector3(width * 0.62f, 0.14f, depth * 1.2f), thatch, t);
            // 屋脊在中间、檐口在两边：坡面要向外侧压下去，别做成反水的 V 形
            slope.transform.rotation = t.rotation * Quaternion.Euler(0f, 0f, -side * 26f);
        }

        LevelBuilder.Box("HouseRidge", t.TransformPoint(new Vector3(0f, roofY + height * 0.24f, 0f)),
            new Vector3(0.16f, 0.14f, depth * 1.2f), woodDark, t, false);

        // 廊下挂的红辣椒串，点一点生活气
        LevelBuilder.Box("HousePepper",
            t.TransformPoint(new Vector3(width * 0.4f, deckY + height * 0.44f, depth * 0.5f)),
            new Vector3(0.1f, 0.5f, 0.1f), new Color(0.66f, 0.16f, 0.1f), t, false);
    }

    /// <summary>晒架：两根叉杆挑一根横木，上面挂着待晒的粮食。</summary>
    private static void DryingRack(Transform root, Vector3 position, Color wood)
    {
        position.y = GroundHeight(position.x, position.z);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -1.4f : 1.4f;
            LevelBuilder.Box("RackPost", position + new Vector3(x, 0.95f, 0f),
                new Vector3(0.12f, 1.9f, 0.12f), wood, root);
        }

        LevelBuilder.Box("RackBar", position + new Vector3(0f, 1.8f, 0f),
            new Vector3(3.1f, 0.1f, 0.1f), wood, root, false);

        for (int i = 0; i < 5; i++)
        {
            float x = -1.1f + i * 0.55f;
            LevelBuilder.Box("RackHang", position + new Vector3(x, 1.45f, 0f),
                new Vector3(0.24f, 0.55f, 0.05f), new Color(0.62f, 0.53f, 0.26f), root, false);
        }
    }

    /// <summary>渡口牌：木柱撑一块写着渡口的木牌，柱上缠红布。</summary>
    private static void Signpost(Transform root, Vector3 position, float yaw,
                                 Color wood, Color woodDark, Color red)
    {
        position.y = GroundHeight(position.x, position.z);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.7f : 0.7f;
            LevelBuilder.Box("SignPost", position + rotation * new Vector3(x, 1.2f, 0f),
                new Vector3(0.14f, 2.4f, 0.14f), woodDark, root);
        }

        GameObject board = LevelBuilder.Box("SignBoard",
            position + rotation * new Vector3(0f, 1.95f, 0f),
            new Vector3(2.2f, 0.9f, 0.1f), wood, root);
        board.transform.rotation = rotation;

        GameObject cloth = LevelBuilder.Box("SignCloth",
            position + rotation * new Vector3(0.85f, 1.5f, 0.08f),
            new Vector3(0.16f, 0.8f, 0.03f), red, root, false);
        cloth.transform.rotation = rotation;
    }

    /// <summary>梯田：几层顺着坡势叠下去的石坎田。</summary>
    private static void TerracedField(Transform root, Vector3 center, float width, float depth,
                                      float yaw, Color wall)
    {
        const int levels = 4;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        for (int i = 0; i < levels; i++)
        {
            float t = i / (float)(levels - 1);
            Vector3 local = new Vector3(-t * width * 0.45f, 0f, 0f);
            Vector3 at = center + rotation * local;
            at.y = GroundHeight(at.x, at.z);

            float w = Mathf.Lerp(width, width * 0.55f, t);
            float d = Mathf.Lerp(depth, depth * 0.7f, t);

            GameObject field = LevelBuilder.Box("Terrace",
                at + Vector3.up * 0.08f, new Vector3(w, 0.16f, d),
                new Color(0.29f, 0.35f, 0.22f), root);
            field.transform.rotation = rotation;

            GameObject wallGeo = LevelBuilder.Box("TerraceWall",
                at + Vector3.up * 0.22f + rotation * new Vector3(w * 0.5f, 0f, 0f),
                new Vector3(0.3f, 0.6f, d), wall, root);
            wallGeo.transform.rotation = rotation;

            // 田埂
            LevelBuilder.Box("TerraceEdge",
                at + Vector3.up * 0.1f + rotation * new Vector3(0f, 0f, d * 0.5f),
                new Vector3(w, 0.22f, 0.16f), wall, root, false).transform.rotation = rotation;
        }
    }

    /// <summary>松树：一根挺直的树干，三层叠起来的深绿树冠。</summary>
    private static void PineTree(Transform root, Vector3 position, Color trunk, Color leaf)
    {
        position.y = GroundHeight(position.x, position.z);
        float height = Random.Range(5f, 8.5f);

        LevelBuilder.Box("PineTrunk", position + Vector3.up * (height * 0.35f),
            new Vector3(0.26f, height * 0.7f, 0.26f), trunk, root);

        for (int i = 0; i < 3; i++)
        {
            float t = i / 2f;
            GameObject tier = LevelBuilder.Box("PineTier",
                position + Vector3.up * Mathf.Lerp(height * 0.55f, height * 0.95f, t),
                new Vector3(Mathf.Lerp(3.0f, 1.2f, t), Mathf.Lerp(1.3f, 0.9f, t),
                            Mathf.Lerp(3.0f, 1.2f, t)),
                i == 1 ? new Color(0.18f, 0.28f, 0.18f) : leaf, root);
            tier.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
        }
    }

    /// <summary>山民木屋：石砌矮墙加一层木架，顶上盖茅草。</summary>
    private static void MountainHut(Transform root, Vector3 position, float yaw,
                                    Color wood, Color woodDark)
    {
        position.y = GroundHeight(position.x, position.z);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        LevelBuilder.Box("HutBase", position + Vector3.up * 0.35f,
            new Vector3(5.4f, 0.7f, 4.2f), new Color(0.33f, 0.32f, 0.27f), root)
            .transform.rotation = rotation;

        LevelBuilder.Box("HutBody", position + Vector3.up * 1.5f,
            new Vector3(4.8f, 1.6f, 3.6f), wood, root).transform.rotation = rotation;

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject roof = LevelBuilder.Box("HutRoof",
                position + Vector3.up * 2.75f + rotation * new Vector3(side * 1.3f, 0f, 0f),
                new Vector3(3.1f, 0.14f, 5.0f), new Color(0.4f, 0.36f, 0.22f), root);
            // 同 StiltHouse：屋脊抬高、檐口压低
            roof.transform.rotation = rotation * Quaternion.Euler(0f, 0f, -side * 28f);
        }

        LevelBuilder.Box("HutDoor", position + rotation * new Vector3(0f, 1.3f, 1.84f),
            new Vector3(1.1f, 1.8f, 0.1f), woodDark, root, false).transform.rotation = rotation;

        LevelBuilder.Box("HutChimney", position + rotation * new Vector3(1.5f, 3.0f, -0.8f),
            new Vector3(0.5f, 1.2f, 0.5f), new Color(0.3f, 0.29f, 0.26f), root);
    }

    /// <summary>石牌坊：进关前最后一道门面，两根石柱抬一块匾。</summary>
    private static void StoneArchway(Transform root, Vector3 position, float yaw,
                                     Color rock, Color red)
    {
        position.y = GroundHeight(position.x, position.z);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -2.3f : 2.3f;
            LevelBuilder.Box("ArchPillar", position + rotation * new Vector3(x, 2.1f, 0f),
                new Vector3(0.8f, 4.2f, 0.8f), rock, root);
            LevelBuilder.Box("ArchFoot", position + rotation * new Vector3(x, 0.3f, 0f),
                new Vector3(1.3f, 0.6f, 1.3f), rock, root);
        }

        LevelBuilder.Box("ArchLintel", position + rotation * new Vector3(0f, 4.35f, 0f),
            new Vector3(6.2f, 0.7f, 0.7f), rock, root).transform.rotation = rotation;

        LevelBuilder.Box("ArchCap", position + rotation * new Vector3(0f, 4.95f, 0f),
            new Vector3(7.2f, 0.45f, 1.0f), rock, root, false).transform.rotation = rotation;

        LevelBuilder.Box("ArchPlaque", position + rotation * new Vector3(0f, 3.55f, 0f),
            new Vector3(2.4f, 0.85f, 0.24f), new Color(0.62f, 0.58f, 0.48f), root, false)
            .transform.rotation = rotation;

        // 匾下的红布
        for (int i = -1; i <= 1; i++)
        {
            LevelBuilder.Box("ArchCloth",
                position + rotation * new Vector3(i * 0.8f, 3.0f, 0.2f),
                new Vector3(0.5f, 0.6f, 0.03f), red, root, false).transform.rotation = rotation;
        }
    }

    /// <summary>号火堆：石头围一圈，中间堆柴，旁边立红旗。</summary>
    private static void BeaconPit(Transform root, Vector3 position, Color rock,
                                  Color wood, Color red)
    {
        position.y = GroundHeight(position.x, position.z);

        for (int i = 0; i < 8; i++)
        {
            float angle = i / 8f * Mathf.PI * 2f;
            Vector3 at = position + new Vector3(Mathf.Cos(angle) * 1.3f, 0.2f,
                                               Mathf.Sin(angle) * 1.3f);
            GameObject stone = LevelBuilder.Box("PitStone", at,
                new Vector3(Random.Range(0.5f, 0.8f), Random.Range(0.3f, 0.5f),
                            Random.Range(0.5f, 0.8f)), rock, root);
            stone.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        for (int i = 0; i < 5; i++)
        {
            GameObject log = LevelBuilder.Box("PitWood",
                position + new Vector3(Random.Range(-0.5f, 0.5f), 0.42f,
                                       Random.Range(-0.5f, 0.5f)),
                new Vector3(0.2f, 0.9f, 0.2f), wood, root, false);
            log.transform.rotation = Quaternion.Euler(Random.Range(24f, 66f),
                Random.Range(0f, 360f), Random.Range(-18f, 18f));
        }

        LevelBuilder.Box("PitAsh", position + Vector3.up * 0.12f,
            new Vector3(1.6f, 0.08f, 1.6f), new Color(0.13f, 0.13f, 0.12f), root, false);

        Flag(root, position + new Vector3(2.6f, 0f, 0f), 30f, red);
    }

    // ───────────────────────────── 渡口信标 ─────────────────────────────

    private static Transform BuildBeacon(Transform root, Vector3 position)
    {
        if (position.y < 0.01f) position.y = GroundHeight(position.x, position.z);

        GameObject beacon = new GameObject("WaypointBeacon");
        beacon.transform.SetParent(root, false);
        beacon.transform.position = position + Vector3.up * 0.1f;

        LocalPart(PrimitiveType.Cylinder, "BeaconPole", beacon.transform,
            new Vector3(0f, 2f, 0f), new Vector3(0.1f, 2f, 0.1f),
            new Color(0.2f, 0.15f, 0.1f));

        LocalPart(PrimitiveType.Sphere, "BeaconLight", beacon.transform,
            new Vector3(0f, 4.05f, 0f), new Vector3(0.38f, 0.38f, 0.38f),
            new Color(1f, 0.45f, 0.2f), new Color(2.4f, 0.7f, 0.2f));

        GameObject ring = LocalPart(PrimitiveType.Cylinder, "BeaconRing", beacon.transform,
            new Vector3(0f, 0.05f, 0f), new Vector3(1.3f, 0.02f, 1.3f),
            new Color(0.5f, 0.12f, 0.08f), new Color(1.2f, 0.18f, 0.1f));

        LocalPart(PrimitiveType.Cube, "BeaconBanner", beacon.transform,
            new Vector3(0.75f, 3.0f, 0f), new Vector3(1.3f, 0.8f, 0.03f),
            new Color(0.68f, 0.12f, 0.09f));

        GameObject lightGo = new GameObject("BeaconLightSource");
        lightGo.transform.SetParent(beacon.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 3.4f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.5f, 0.25f);
        light.range = 14f;
        light.intensity = 1.6f;

        return beacon.transform;
    }

    private static GameObject LocalPart(PrimitiveType type, string name, Transform parent,
                                        Vector3 localPos, Vector3 localScale, Color color,
                                        Color? emission = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Object.Destroy(go.GetComponent<Collider>());

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = LevelBuilder.MakeMaterial(color, emission);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go;
    }
}
