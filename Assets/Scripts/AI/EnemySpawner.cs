using UnityEngine;

/// <summary>
/// 敌军波次生成器。按固定间隔在渡口对岸生成敌人，限制同时存活数量。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    /// <summary>敌军番号：中央军（硬）、川军（多）、黔军（残）。</summary>
    public enum Faction
    {
        CentralArmy,
        Sichuan,
        Guizhou
    }

    private static Faction[] factionPool = { Faction.CentralArmy };

    /// <summary>按关卡切换敌军构成：第一关以中央军为主，第二关川军、黔军混杂。</summary>
    public static void SetFactionPool(bool chishui)
    {
        factionPool = chishui
            ? new[]
            {
                Faction.Sichuan, Faction.Sichuan, Faction.Sichuan, Faction.Sichuan,
                Faction.Guizhou, Faction.Guizhou, Faction.Guizhou,
                Faction.CentralArmy
            }
            : new[]
            {
                Faction.CentralArmy, Faction.CentralArmy, Faction.CentralArmy,
                Faction.CentralArmy, Faction.Guizhou, Faction.Sichuan
            };
    }

    private static Faction PickFaction()
    {
        if (factionPool == null || factionPool.Length == 0) return Faction.CentralArmy;
        return factionPool[Random.Range(0, factionPool.Length)];
    }

    private Transform player;
    private Transform[] spawnPoints;
    private Transform levelRoot;
    private AudioClip shotClip;

    private bool active;
    private float timer;
    private int spawned;

    private const int MaxAlive = 8;

    public void Initialize(Transform playerRef, Transform[] points,
                           AudioClip shot, Transform root)
    {
        player = playerRef;
        spawnPoints = points;
        shotClip = shot;
        levelRoot = root;
    }

    public void SetActive(bool value)
    {
        active = value;
        if (value) timer = 1.5f;
    }

    /// <summary>停止所有敌人的 AI（胜利 / 失败后调用）。</summary>
    public void StopAllEnemies()
    {
        foreach (EnemyAI ai in FindObjectsOfType<EnemyAI>())
        {
            if (ai != null) ai.SetAIEnabled(false);
        }
    }

    private void Update()
    {
        if (!active || player == null) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = Mathf.Max(2.4f, 4.8f - spawned * 0.1f);

        if (CountAlive() >= MaxAlive) return;

        Spawn();
    }

    private int CountAlive()
    {
        int count = 0;
        foreach (EnemyAI ai in FindObjectsOfType<EnemyAI>())
        {
            if (ai != null && !ai.IsDead) count++;
        }
        return count;
    }

    private void Spawn()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
        // 出生点都在后方公路的土埂缺口里，横向散不开太多，免得落到土埂外面露馅
        Vector3 position = point.position + new Vector3(
            Random.Range(-1.6f, 1.6f), 0f, Random.Range(-1.2f, 1.2f));

        SpawnAt(levelRoot, position, player, shotClip);
        spawned++;
    }

    /// <summary>在指定位置生成一名敌军。第二关的游击队导演也复用这里。</summary>
    public static EnemyAI SpawnAt(Transform parent, Vector3 position, Transform player, AudioClip shot)
    {
        return SpawnAt(parent, position, player, shot, PickFaction());
    }

    /// <summary>指定番号生成（开发截图用，保证三种敌军同框）。</summary>
    public static EnemyAI SpawnAt(Transform parent, Vector3 position, Transform player,
                                  AudioClip shot, Faction faction)
    {
        GameObject root = new GameObject("Enemy_" + faction);
        root.tag = "Enemy";
        root.transform.SetParent(parent, true);
        root.transform.position = position;

        BuildBody(root.transform, false, faction);

        Health health = root.AddComponent<Health>();
        // 中央军精锐最硬，黔军残部一打就散
        health.ConfigureMaxHealth(faction == Faction.CentralArmy ? 145f
            : (faction == Faction.Sichuan ? 95f : 75f));

        EnemyAI ai = root.AddComponent<EnemyAI>();
        ai.Initialize(player, shot);

        return ai;
    }

    /// <summary>
    /// 生成士兵模型。
    /// ally = true：红军战友（灰蓝粗布、八角帽红五星、红领章、绑腿草鞋）。
    /// 敌军按番号区分：中央军黄绿笔挺、川军土黄包头、黔军灰黑长衫。
    /// </summary>
    public static void BuildBody(Transform root, bool ally,
                                 Faction faction = Faction.CentralArmy)
    {
        bool central = !ally && faction == Faction.CentralArmy;
        bool sichuan = !ally && faction == Faction.Sichuan;
        bool guizhou = !ally && faction == Faction.Guizhou;

        // 同一番号内部的差异：军官、包头、瓜皮帽、长衫、大烟枪
        bool officer = central && Random.value < 0.22f;
        bool turban = (sichuan || guizhou) && Random.value < 0.55f;
        bool melonCap = guizhou && !turban && Random.value < 0.5f;
        bool pipe = sichuan && Random.value < 0.55f;
        bool gown = guizhou && Random.value < 0.55f;

        Color coat;
        Color trousers;
        Color gear;
        Color helmet;
        if (ally)
        {
            coat = new Color(0.33f, 0.37f, 0.4f);
            trousers = new Color(0.26f, 0.29f, 0.31f);
            gear = new Color(0.3f, 0.25f, 0.17f);
            helmet = new Color(0.3f, 0.33f, 0.34f);
        }
        else if (central)
        {
            coat = new Color(0.55f, 0.54f, 0.34f);
            trousers = new Color(0.46f, 0.46f, 0.3f);
            gear = new Color(0.36f, 0.23f, 0.12f);
            helmet = new Color(0.35f, 0.37f, 0.31f);
        }
        else if (sichuan)
        {
            coat = new Color(0.45f, 0.4f, 0.27f);
            trousers = new Color(0.37f, 0.33f, 0.24f);
            gear = new Color(0.3f, 0.24f, 0.15f);
            helmet = new Color(0.36f, 0.33f, 0.24f);
        }
        else
        {
            coat = new Color(0.28f, 0.28f, 0.26f);
            trousers = new Color(0.23f, 0.23f, 0.22f);
            gear = new Color(0.2f, 0.19f, 0.16f);
            helmet = new Color(0.22f, 0.22f, 0.2f);
        }

        Color skin = new Color(0.56f, 0.43f, 0.33f);
        Color handColor = officer ? new Color(0.88f, 0.86f, 0.8f) : skin;
        Color metal = new Color(0.17f, 0.17f, 0.18f);
        Color wood = new Color(0.22f, 0.14f, 0.08f);
        Color red = ally ? new Color(0.58f, 0.14f, 0.11f) : new Color(0.7f, 0.13f, 0.1f);
        Color mud = new Color(0.26f, 0.25f, 0.21f);
        Color hair = new Color(0.11f, 0.09f, 0.08f);

        // ── 躯干与军装 ──
        Part(PrimitiveType.Capsule, "Torso", root, new Vector3(0f, 1.04f, 0f),
            new Vector3(0.5f, 0.44f, 0.34f), coat, BodyZone.Torso);
        Part(PrimitiveType.Cube, "CoatHem", root, new Vector3(0f, 0.84f, 0f),
            new Vector3(0.48f, 0.34f, 0.36f),
            ally ? mud : (guizhou ? new Color(0.23f, 0.23f, 0.21f) : coat), null);
        Part(PrimitiveType.Cube, "Belt", root, new Vector3(0f, 1.0f, 0f),
            new Vector3(0.52f, 0.055f, 0.38f), gear, null);
        Part(PrimitiveType.Cube, "Buckle", root, new Vector3(0f, 1.0f, 0.2f),
            new Vector3(0.07f, 0.055f, 0.02f), metal, null);

        if (ally)
        {
            // 粗布军装上的补丁，和中央军崭新的制服拉开质感
            GameObject patch = Part(PrimitiveType.Cube, "CoatPatch", root,
                new Vector3(-0.1f, 1.16f, 0.185f), new Vector3(0.13f, 0.14f, 0.008f),
                new Color(0.43f, 0.42f, 0.36f), null);
            patch.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        }
        else if (central)
        {
            // 皮质弹匣包、图囊、工兵铲
            Part(PrimitiveType.Cube, "PouchL", root, new Vector3(-0.13f, 0.98f, 0.19f),
                new Vector3(0.12f, 0.14f, 0.07f), gear, null);
            Part(PrimitiveType.Cube, "PouchR", root, new Vector3(0.13f, 0.98f, 0.19f),
                new Vector3(0.12f, 0.14f, 0.07f), gear, null);
            Part(PrimitiveType.Cube, "MapCase", root, new Vector3(-0.24f, 0.9f, -0.12f),
                new Vector3(0.06f, 0.16f, 0.2f), gear, null);
            Part(PrimitiveType.Cube, "ShovelHandle", root,
                new Vector3(0.25f, 0.86f, -0.14f), new Vector3(0.03f, 0.26f, 0.03f),
                wood, null);
            Part(PrimitiveType.Cube, "ShovelBlade", root,
                new Vector3(0.25f, 0.71f, -0.14f), new Vector3(0.11f, 0.13f, 0.02f),
                new Color(0.3f, 0.32f, 0.32f), null);
        }
        else if (sichuan)
        {
            // 简易布腰带 + 一个老式弹药袋
            Part(PrimitiveType.Cube, "Sash", root, new Vector3(0f, 0.985f, 0.19f),
                new Vector3(0.42f, 0.045f, 0.02f), new Color(0.5f, 0.45f, 0.33f), null);
            Part(PrimitiveType.Cube, "PouchL", root, new Vector3(-0.15f, 0.97f, 0.19f),
                new Vector3(0.13f, 0.13f, 0.06f), gear, null);
        }
        else
        {
            // 黔军残部：七拼八凑的补丁与水壶
            GameObject patch = Part(PrimitiveType.Cube, "CoatPatch", root,
                new Vector3(0.12f, 1.12f, 0.182f), new Vector3(0.15f, 0.17f, 0.008f),
                new Color(0.35f, 0.35f, 0.31f), null);
            patch.transform.localRotation = Quaternion.Euler(0f, 0f, -11f);
            Part(PrimitiveType.Cylinder, "Gourd", root, new Vector3(-0.24f, 0.86f, -0.16f),
                new Vector3(0.09f, 0.1f, 0.09f), new Color(0.45f, 0.36f, 0.2f), null);
        }

        // ── 背包、铺盖卷、水壶 ──
        Part(PrimitiveType.Cube, "Backpack", root, new Vector3(0f, 1.12f, -0.25f),
            new Vector3(0.36f, 0.42f, 0.18f), gear, null);
        GameObject bedroll = Part(PrimitiveType.Cylinder, "Bedroll", root,
            new Vector3(0f, 1.35f, -0.26f), new Vector3(0.09f, 0.2f, 0.09f),
            new Color(0.42f, 0.39f, 0.28f), null);
        bedroll.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        // 捆铺盖的两道皮带，免得行军时散开
        Part(PrimitiveType.Cube, "BedrollStrapA", root, new Vector3(-0.075f, 1.35f, -0.26f),
            new Vector3(0.022f, 0.1f, 0.1f), new Color(0.24f, 0.17f, 0.1f), null);
        Part(PrimitiveType.Cube, "BedrollStrapB", root, new Vector3(0.075f, 1.35f, -0.26f),
            new Vector3(0.022f, 0.1f, 0.1f), new Color(0.24f, 0.17f, 0.1f), null);
        Part(PrimitiveType.Cylinder, "Canteen", root, new Vector3(0.25f, 0.9f, -0.22f),
            new Vector3(0.08f, 0.11f, 0.08f), new Color(0.2f, 0.26f, 0.2f), null);
        // 水壶套口和壶塞
        Part(PrimitiveType.Cylinder, "CanteenNeck", root, new Vector3(0.25f, 0.965f, -0.22f),
            new Vector3(0.04f, 0.02f, 0.04f), new Color(0.3f, 0.28f, 0.18f), null);
        Part(PrimitiveType.Cylinder, "CanteenCap", root, new Vector3(0.25f, 0.983f, -0.22f),
            new Vector3(0.045f, 0.012f, 0.045f), new Color(0.14f, 0.12f, 0.09f), null);

        // ── 头部、军帽 ──
        Part(PrimitiveType.Cylinder, "Neck", root, new Vector3(0f, 1.47f, 0f),
            new Vector3(0.13f, 0.08f, 0.13f), skin, null);
        Part(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 1.66f, 0f),
            new Vector3(0.3f, 0.34f, 0.3f), skin, BodyZone.Head);

        if (ally)
        {
            // 八角帽 + 红五星
            Part(PrimitiveType.Sphere, "Cap", root, new Vector3(0f, 1.8f, 0f),
                new Vector3(0.35f, 0.21f, 0.35f), helmet, null);
            Part(PrimitiveType.Cylinder, "CapBrim", root, new Vector3(0f, 1.73f, 0.03f),
                new Vector3(0.36f, 0.014f, 0.36f), helmet, null);
            Part(PrimitiveType.Cylinder, "CapBand", root, new Vector3(0f, 1.75f, 0f),
                new Vector3(0.355f, 0.02f, 0.355f), new Color(0.26f, 0.29f, 0.3f), null);
            GameObject star = Part(PrimitiveType.Cube, "RedStar", root,
                new Vector3(0f, 1.81f, 0.155f), new Vector3(0.085f, 0.085f, 0.02f), red, null);
            star.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            // 红领章
            Part(PrimitiveType.Cube, "CollarL", root, new Vector3(-0.1f, 1.42f, 0.15f),
                new Vector3(0.1f, 0.055f, 0.02f), red, null);
            Part(PrimitiveType.Cube, "CollarR", root, new Vector3(0.1f, 1.42f, 0.15f),
                new Vector3(0.1f, 0.055f, 0.02f), red, null);
        }
        else if (central && officer)
        {
            // 中央军军官：大檐帽、帽徽、白手套
            Color capColor = new Color(0.58f, 0.57f, 0.37f);
            Part(PrimitiveType.Sphere, "OfficerCap", root, new Vector3(0f, 1.82f, 0f),
                new Vector3(0.37f, 0.19f, 0.38f), capColor, null);
            Part(PrimitiveType.Cylinder, "OfficerCapBand", root,
                new Vector3(0f, 1.745f, 0.01f), new Vector3(0.375f, 0.03f, 0.385f),
                new Color(0.24f, 0.24f, 0.17f), null);
            Part(PrimitiveType.Cube, "OfficerVisor", root,
                new Vector3(0f, 1.722f, 0.15f), new Vector3(0.4f, 0.02f, 0.13f),
                new Color(0.07f, 0.07f, 0.06f), null);
            Part(PrimitiveType.Cube, "CapBadge", root,
                new Vector3(0f, 1.825f, 0.187f), new Vector3(0.05f, 0.05f, 0.01f),
                new Color(0.82f, 0.84f, 0.9f), null);
        }
        else if (central)
        {
            GameObject helmetGo = Part(PrimitiveType.Sphere, "Helmet", root,
                new Vector3(0f, 1.79f, 0f), new Vector3(0.37f, 0.23f, 0.37f),
                helmet, null);
            Part(PrimitiveType.Cylinder, "HelmetBrim", root, new Vector3(0f, 1.72f, 0.02f),
                new Vector3(0.38f, 0.015f, 0.38f), helmet, null);
            Part(PrimitiveType.Cube, "ChinStrapL", root, new Vector3(-0.145f, 1.67f, 0.08f),
                new Vector3(0.014f, 0.11f, 0.022f), new Color(0.09f, 0.07f, 0.05f), null);
            Part(PrimitiveType.Cube, "ChinStrapR", root, new Vector3(0.145f, 1.67f, 0.08f),
                new Vector3(0.014f, 0.11f, 0.022f), new Color(0.09f, 0.07f, 0.05f), null);
            Part(PrimitiveType.Cube, "HelmetDecal", root,
                new Vector3(0.02f, 1.805f, 0.186f), new Vector3(0.04f, 0.05f, 0.008f),
                new Color(0.78f, 0.8f, 0.84f), null);

            // M35 钢盔压出一层高光：雾里、竹林里先看到的是敌人盔顶的反光
            Material shine = LevelBuilder.MakeMaterial(helmet);
            if (shine.HasProperty("_Glossiness")) shine.SetFloat("_Glossiness", 0.55f);
            if (shine.HasProperty("_Smoothness")) shine.SetFloat("_Smoothness", 0.55f);
            if (shine.HasProperty("_Metallic")) shine.SetFloat("_Metallic", 0.25f);
            Renderer r = helmetGo.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = shine;
        }
        else if (turban)
        {
            // 川军 / 黔军：黑布或白布包头
            Color cloth = Random.value < 0.5f
                ? new Color(0.12f, 0.12f, 0.11f)
                : new Color(0.68f, 0.66f, 0.58f);
            Part(PrimitiveType.Cylinder, "TurbanA", root, new Vector3(0f, 1.785f, 0f),
                new Vector3(0.36f, 0.045f, 0.36f), cloth, null);
            Part(PrimitiveType.Cylinder, "TurbanB", root, new Vector3(0f, 1.735f, 0f),
                new Vector3(0.355f, 0.04f, 0.355f), cloth * 0.9f, null);
            GameObject knot = Part(PrimitiveType.Cube, "TurbanKnot", root,
                new Vector3(0.15f, 1.79f, 0.09f), new Vector3(0.07f, 0.06f, 0.05f), cloth, null);
            knot.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
        }
        else if (melonCap)
        {
            // 黔军：瓜皮帽，顶上一点红
            Part(PrimitiveType.Sphere, "MelonCap", root, new Vector3(0f, 1.8f, 0f),
                new Vector3(0.33f, 0.22f, 0.33f), new Color(0.09f, 0.09f, 0.09f), null);
            Part(PrimitiveType.Cube, "MelonKnot", root, new Vector3(0f, 1.905f, 0.01f),
                new Vector3(0.045f, 0.045f, 0.045f), red, null);
        }
        else
        {
            // 川军 / 黔军的旧大檐帽，帽墙已经发灰
            Color capColor = sichuan ? new Color(0.38f, 0.35f, 0.25f)
                                     : new Color(0.24f, 0.24f, 0.22f);
            Part(PrimitiveType.Sphere, "OldCap", root, new Vector3(0f, 1.81f, 0f),
                new Vector3(0.36f, 0.18f, 0.37f), capColor, null);
            Part(PrimitiveType.Cylinder, "OldCapBand", root,
                new Vector3(0f, 1.74f, 0.01f), new Vector3(0.37f, 0.028f, 0.38f),
                capColor * 0.85f, null);
            Part(PrimitiveType.Cube, "OldVisor", root, new Vector3(0f, 1.72f, 0.14f),
                new Vector3(0.38f, 0.018f, 0.11f), new Color(0.1f, 0.09f, 0.08f), null);
        }

        // 帽子盖不住的后脑勺头发
        if (!central || officer)
        {
            Part(PrimitiveType.Sphere, "Hair", root, new Vector3(0f, 1.685f, -0.075f),
                new Vector3(0.27f, 0.25f, 0.17f), hair, null);
        }
        Part(PrimitiveType.Cube, "EyeL", root, new Vector3(-0.055f, 1.68f, 0.145f),
            new Vector3(0.03f, 0.018f, 0.01f), metal, null);
        Part(PrimitiveType.Cube, "EyeR", root, new Vector3(0.055f, 1.68f, 0.145f),
            new Vector3(0.03f, 0.018f, 0.01f), metal, null);
        Part(PrimitiveType.Cube, "Nose", root, new Vector3(0f, 1.63f, 0.152f),
            new Vector3(0.035f, 0.045f, 0.035f), skin, null);

        // ── 五官与鬓角：近看也要像一张脸 ──
        Part(PrimitiveType.Cube, "BrowL", root, new Vector3(-0.056f, 1.714f, 0.151f),
            new Vector3(0.058f, 0.012f, 0.012f), hair, null);
        Part(PrimitiveType.Cube, "BrowR", root, new Vector3(0.056f, 1.714f, 0.151f),
            new Vector3(0.058f, 0.012f, 0.012f), hair, null);
        Part(PrimitiveType.Cube, "Mouth", root, new Vector3(0f, 1.582f, 0.153f),
            new Vector3(0.062f, 0.012f, 0.01f), new Color(0.34f, 0.2f, 0.18f), null);

        // 胡子、胡茬、脸颊疤：一队兵里总有几个老兵，不是同一张脸反复复制
        float faceSeed = Random.value;
        if (faceSeed < 0.34f)
        {
            // 八字胡：老兵和军官居多
            Color whisker = hair * 0.9f;
            GameObject mL = Part(PrimitiveType.Cube, "MustacheL", root,
                new Vector3(-0.032f, 1.605f, 0.156f), new Vector3(0.05f, 0.016f, 0.012f),
                whisker, null);
            mL.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            GameObject mR = Part(PrimitiveType.Cube, "MustacheR", root,
                new Vector3(0.032f, 1.605f, 0.156f), new Vector3(0.05f, 0.016f, 0.012f),
                whisker, null);
            mR.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
        }
        else if (faceSeed < 0.62f)
        {
            // 没刮的胡茬：下巴一圈青灰
            Part(PrimitiveType.Cube, "Stubble", root, new Vector3(0f, 1.597f, 0.146f),
                new Vector3(0.13f, 0.05f, 0.016f), skin * 0.72f, null);
        }

        if (faceSeed > 0.86f)
        {
            // 脸颊刀疤
            GameObject scar = Part(PrimitiveType.Cube, "CheekScar", root,
                new Vector3(0.1f, 1.655f, 0.142f), new Vector3(0.038f, 0.008f, 0.008f),
                new Color(0.55f, 0.36f, 0.3f), null);
            scar.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);
        }
        Part(PrimitiveType.Cube, "EarL", root, new Vector3(-0.153f, 1.655f, 0f),
            new Vector3(0.032f, 0.082f, 0.06f), skin, null);
        Part(PrimitiveType.Cube, "EarR", root, new Vector3(0.153f, 1.655f, 0f),
            new Vector3(0.032f, 0.082f, 0.06f), skin, null);

        // ── 军装门襟、扣子、口袋与背带 ──
        Color placket = ally ? new Color(0.29f, 0.33f, 0.36f) : new Color(0.46f, 0.45f, 0.28f);
        Color button = ally ? new Color(0.23f, 0.26f, 0.27f) : new Color(0.72f, 0.7f, 0.55f);
        Part(PrimitiveType.Cube, "Placket", root, new Vector3(0f, 1.2f, 0.176f),
            new Vector3(0.052f, 0.33f, 0.014f), placket, null);
        for (int b = 0; b < 5; b++)
        {
            Part(PrimitiveType.Cube, "Button" + b, root,
                new Vector3(0f, 1.34f - b * 0.075f, 0.186f),
                new Vector3(0.024f, 0.024f, 0.01f), button, null);
        }
        Part(PrimitiveType.Cube, "CollarBand", root, new Vector3(0f, 1.425f, 0f),
            new Vector3(0.24f, 0.045f, 0.24f), placket, null);

        if (ally)
        {
            Part(PrimitiveType.Cube, "PocketL", root, new Vector3(-0.14f, 1.19f, 0.177f),
                new Vector3(0.11f, 0.1f, 0.012f), placket, null);
            Part(PrimitiveType.Cube, "PocketR", root, new Vector3(0.14f, 1.19f, 0.177f),
                new Vector3(0.11f, 0.1f, 0.012f), placket, null);
            Part(PrimitiveType.Cube, "Towel", root, new Vector3(0f, 1.455f, 0.02f),
                new Vector3(0.2f, 0.05f, 0.2f), new Color(0.62f, 0.6f, 0.52f), null);
        }
        else
        {
            Color pocket = central ? new Color(0.47f, 0.46f, 0.29f)
                : (sichuan ? new Color(0.4f, 0.36f, 0.25f) : new Color(0.24f, 0.24f, 0.22f));
            Part(PrimitiveType.Cube, "PocketL", root, new Vector3(-0.14f, 1.19f, 0.177f),
                new Vector3(0.115f, 0.105f, 0.012f), pocket, null);
            Part(PrimitiveType.Cube, "PocketR", root, new Vector3(0.14f, 1.19f, 0.177f),
                new Vector3(0.115f, 0.105f, 0.012f), pocket, null);
            Part(PrimitiveType.Cube, "PocketFlapL", root, new Vector3(-0.14f, 1.245f, 0.186f),
                new Vector3(0.125f, 0.028f, 0.01f), pocket, null);
            Part(PrimitiveType.Cube, "PocketFlapR", root, new Vector3(0.14f, 1.245f, 0.186f),
                new Vector3(0.125f, 0.028f, 0.01f), pocket, null);

            if (central)
            {
                Part(PrimitiveType.Cube, "ShoulderBoardL", root, new Vector3(-0.3f, 1.425f, 0f),
                    new Vector3(0.13f, 0.026f, 0.08f), new Color(0.58f, 0.57f, 0.37f), null);
                Part(PrimitiveType.Cube, "ShoulderBoardR", root, new Vector3(0.3f, 1.425f, 0f),
                    new Vector3(0.13f, 0.026f, 0.08f), new Color(0.58f, 0.57f, 0.37f), null);
                Part(PrimitiveType.Cube, "CollarInsigniaL", root,
                    new Vector3(-0.1f, 1.43f, 0.153f), new Vector3(0.045f, 0.045f, 0.012f),
                    new Color(0.18f, 0.22f, 0.4f), null);
                Part(PrimitiveType.Cube, "CollarInsigniaR", root,
                    new Vector3(0.1f, 1.43f, 0.153f), new Vector3(0.045f, 0.045f, 0.012f),
                    new Color(0.18f, 0.22f, 0.4f), null);
            }
            else if (sichuan)
            {
                Part(PrimitiveType.Cube, "CollarPatchL", root,
                    new Vector3(-0.1f, 1.43f, 0.153f), new Vector3(0.05f, 0.04f, 0.01f),
                    new Color(0.34f, 0.3f, 0.2f), null);
                Part(PrimitiveType.Cube, "CollarPatchR", root,
                    new Vector3(0.1f, 1.43f, 0.153f), new Vector3(0.05f, 0.04f, 0.01f),
                    new Color(0.34f, 0.3f, 0.2f), null);
            }
        }

        // 斜挎的干粮袋背带
        GameObject strap = Part(PrimitiveType.Cube, "ChestStrap", root,
            new Vector3(0f, 1.17f, 0.178f), new Vector3(0.052f, 0.5f, 0.012f), gear, null);
        strap.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);

        if (sichuan)
        {
            // 对襟短褂：套在军装外面的土布上衣，比军服短一截
            Color jacket = new Color(0.47f, 0.41f, 0.28f);
            Part(PrimitiveType.Capsule, "ShortJacket", root, new Vector3(0f, 1.05f, 0f),
                new Vector3(0.53f, 0.32f, 0.375f), jacket, null);
            Part(PrimitiveType.Cube, "JacketHem", root, new Vector3(0f, 0.755f, 0f),
                new Vector3(0.52f, 0.05f, 0.37f), jacket * 0.85f, null);
            for (int f = 0; f < 4; f++)
            {
                Part(PrimitiveType.Cube, "Frog" + f, root,
                    new Vector3(0f, 1.185f - f * 0.075f, 0.185f),
                    new Vector3(0.075f, 0.016f, 0.012f), jacket * 0.72f, null);
            }
        }
        else if (guizhou && gown)
        {
            // 长袍马褂：军服外面罩一件过膝的旧长衫，下摆遮住大腿
            Color gownColor = new Color(0.24f, 0.24f, 0.21f);
            Part(PrimitiveType.Cube, "GownSkirt", root, new Vector3(0f, 0.72f, 0f),
                new Vector3(0.46f, 0.44f, 0.35f), gownColor, null);
            Part(PrimitiveType.Cube, "GownHem", root, new Vector3(0f, 0.505f, 0f),
                new Vector3(0.462f, 0.04f, 0.352f), gownColor * 1.18f, null);
            Part(PrimitiveType.Cube, "GownPlacket", root, new Vector3(0.02f, 0.95f, 0.178f),
                new Vector3(0.045f, 0.46f, 0.012f), gownColor * 1.25f, null);
        }

        // ── 装具：子弹带、挎包、搪瓷缸、水壶套 ──
        // 立领把脖颈包住：军装不再是"头直接插在肩膀上"
        Part(PrimitiveType.Cylinder, "CollarStand", root, new Vector3(0f, 1.452f, 0f),
            new Vector3(0.27f, 0.05f, 0.27f), coat * 0.82f, null);
        // 风纪扣：领口最上面那颗，红军查军容抓得最紧的地方
        Part(PrimitiveType.Cube, "CollarHook", root, new Vector3(0f, 1.402f, 0.164f),
            new Vector3(0.026f, 0.026f, 0.012f), button, null);

        if (ally || central)
        {
            // 斜挂子弹带：一排黄铜弹壳，是当时步兵身上最显眼的装备。
            // 和门襟上的干粮袋背带交叉成 X，胸前不再只有一根斜带
            GameObject bandolier = Part(PrimitiveType.Cube, "Bandolier", root,
                new Vector3(0f, 1.18f, 0.186f), new Vector3(0.055f, 0.5f, 0.014f),
                new Color(0.34f, 0.27f, 0.16f), null);
            bandolier.transform.localRotation = Quaternion.Euler(0f, 0f, -36f);

            Color brass = new Color(0.66f, 0.52f, 0.22f);
            for (int c = 0; c < 7; c++)
            {
                float t = c / 6f - 0.5f;
                GameObject shell = Part(PrimitiveType.Cylinder, "Cartridge" + c, root,
                    new Vector3(t * 0.3f, 1.18f + t * 0.42f, 0.2f),
                    new Vector3(0.03f, 0.024f, 0.03f), brass, null);
                shell.transform.localRotation = Quaternion.Euler(0f, 0f, -36f);
            }
        }

        if (ally)
        {
            // 红军战士右腰的粗布挎包：包身、包盖、扣带，行军时装干粮和弹药
            Part(PrimitiveType.Cube, "Satchel", root, new Vector3(0.19f, 0.9f, 0.2f),
                new Vector3(0.12f, 0.16f, 0.09f), new Color(0.38f, 0.34f, 0.24f), null);
            Part(PrimitiveType.Cube, "SatchelFlap", root, new Vector3(0.19f, 0.962f, 0.235f),
                new Vector3(0.126f, 0.05f, 0.03f), new Color(0.44f, 0.4f, 0.29f), null);
            Part(PrimitiveType.Cube, "SatchelBuckle", root, new Vector3(0.19f, 0.93f, 0.247f),
                new Vector3(0.028f, 0.032f, 0.012f), metal, null);
        }

        // 背包外侧挂的搪瓷缸：缸沿磕掉一块瓷，露出黑铁
        Part(PrimitiveType.Cylinder, "TinCup", root, new Vector3(0.14f, 1.0f, -0.355f),
            new Vector3(0.075f, 0.055f, 0.075f), new Color(0.68f, 0.68f, 0.62f), null);
        Part(PrimitiveType.Cube, "TinCupChip", root, new Vector3(0.17f, 1.035f, -0.355f),
            new Vector3(0.025f, 0.02f, 0.025f), new Color(0.16f, 0.15f, 0.14f), null);

        if (officer)
        {
            // 军官：武装带、手枪套、白手套已经戴好
            GameObject cross = Part(PrimitiveType.Cube, "SamBrowne", root,
                new Vector3(0f, 1.17f, 0.181f), new Vector3(0.06f, 0.5f, 0.012f),
                new Color(0.16f, 0.11f, 0.07f), null);
            cross.transform.localRotation = Quaternion.Euler(0f, 0f, -38f);
            Part(PrimitiveType.Cube, "PistolHolster", root, new Vector3(0.26f, 0.87f, 0.06f),
                new Vector3(0.085f, 0.17f, 0.06f), new Color(0.18f, 0.11f, 0.07f), null);
        }

        // 放大的隐形爆头盒：覆盖头部与肩颈，打到上胸也会判为爆头
        GameObject headHitbox = new GameObject("HeadHitbox");
        headHitbox.transform.SetParent(root, false);
        headHitbox.transform.localPosition = new Vector3(0f, 1.66f, 0.02f);
        SphereCollider headSphere = headHitbox.AddComponent<SphereCollider>();
        // 中央军 M35 盔的判定更小：硬核敌人，逼玩家打准
        headSphere.radius = ally ? 0.3f : (central ? 0.24f : (sichuan ? 0.28f : 0.3f));
        headHitbox.AddComponent<HitZone>().Zone = BodyZone.Head;

        // ── 四肢：挂在髋 / 肩枢轴上，供 EnemyAI 做行走摆动 ──
        Transform hipL = Pivot(root, "HipL", new Vector3(-0.13f, 0.8f, 0f));
        Transform hipR = Pivot(root, "HipR", new Vector3(0.13f, 0.8f, 0f));

        // 大腿 / 膝盖 / 小腿分成两节：蹲下时膝盖真的折起来，
        // 不再是一条直腿从髋部甩到身后、上身往前一躬像个鞠躬
        Transform kneeL = Pivot(hipL, "KneeL", new Vector3(0f, -0.38f, 0f));
        Transform kneeR = Pivot(hipR, "KneeR", new Vector3(0f, -0.38f, 0f));

        Part(PrimitiveType.Cube, "ThighL", hipL, new Vector3(0f, -0.19f, 0f),
            new Vector3(0.16f, 0.38f, 0.17f), trousers, BodyZone.Limb);
        Part(PrimitiveType.Cube, "ThighR", hipR, new Vector3(0f, -0.19f, 0f),
            new Vector3(0.16f, 0.38f, 0.17f), trousers, BodyZone.Limb);
        Part(PrimitiveType.Cube, "ShinL", kneeL, new Vector3(0f, -0.195f, 0f),
            new Vector3(0.16f, 0.39f, 0.17f), trousers, BodyZone.Limb);
        Part(PrimitiveType.Cube, "ShinR", kneeR, new Vector3(0f, -0.195f, 0f),
            new Vector3(0.16f, 0.39f, 0.17f), trousers, BodyZone.Limb);

        // 补丁：长途行军，裤腿磨破了就找块布缝上，针脚歪歪扭扭
        if (Random.value < 0.75f)
        {
            Color patch = trousers * (Random.value < 0.5f ? 0.78f : 1.22f);
            Transform leg = Random.value < 0.5f ? hipL : hipR;
            GameObject p1 = Part(PrimitiveType.Cube, "TrouserPatch", leg,
                new Vector3(0.012f, -0.25f, 0.087f), new Vector3(0.085f, 0.09f, 0.006f),
                patch, null);
            p1.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));
            if (Random.value < 0.45f)
            {
                Part(PrimitiveType.Cube, "TrouserPatchKnee",
                    Random.value < 0.5f ? kneeL : kneeR,
                    new Vector3(0f, -0.14f, 0.087f), new Vector3(0.07f, 0.07f, 0.006f),
                    patch * 0.9f, null);
            }
        }

        Transform shoulderL = Pivot(root, "ShoulderL", new Vector3(-0.3f, 1.38f, 0f));
        Transform shoulderR = Pivot(root, "ShoulderR", new Vector3(0.3f, 1.38f, 0f));
        // 大臂、小臂分成两节：卧倒时前臂要真的折下去撑住上身，和主角的骨架一致
        Transform elbowL = Pivot(shoulderL, "ElbowL", new Vector3(0f, -0.29f, 0f));
        Transform elbowR = Pivot(shoulderR, "ElbowR", new Vector3(0f, -0.29f, 0f));
        Part(PrimitiveType.Cube, "ArmL", shoulderL, new Vector3(0f, -0.145f, 0f),
            new Vector3(0.13f, 0.29f, 0.14f), coat, BodyZone.Limb);
        Part(PrimitiveType.Cube, "ArmR", shoulderR, new Vector3(0f, -0.145f, 0f),
            new Vector3(0.13f, 0.29f, 0.14f), coat, BodyZone.Limb);
        Part(PrimitiveType.Cube, "ForearmL", elbowL, new Vector3(0f, -0.145f, 0f),
            new Vector3(0.13f, 0.29f, 0.14f), coat, BodyZone.Limb);
        Part(PrimitiveType.Cube, "ForearmR", elbowR, new Vector3(0f, -0.145f, 0f),
            new Vector3(0.13f, 0.29f, 0.14f), coat, BodyZone.Limb);

        if (ally)
        {
            // 左臂红袖标：远处也能一眼分清敌我
            Part(PrimitiveType.Cube, "Armband", shoulderL, new Vector3(0f, -0.2f, 0f),
                new Vector3(0.147f, 0.09f, 0.157f), red, null);
        }

        // 袖口 + 手（中央军军官戴白手套）
        Color cuff = ally ? new Color(0.28f, 0.31f, 0.33f)
            : (central ? new Color(0.47f, 0.46f, 0.29f)
                       : (sichuan ? new Color(0.39f, 0.35f, 0.24f)
                                  : new Color(0.24f, 0.24f, 0.22f)));
        Part(PrimitiveType.Cube, "CuffL", elbowL, new Vector3(0f, -0.245f, 0f),
            new Vector3(0.138f, 0.055f, 0.148f), cuff, null);
        Part(PrimitiveType.Cube, "CuffR", elbowR, new Vector3(0f, -0.245f, 0f),
            new Vector3(0.138f, 0.055f, 0.148f), cuff, null);

        Part(PrimitiveType.Cube, "HandL", elbowL, new Vector3(0f, -0.31f, 0.01f),
            new Vector3(0.11f, 0.12f, 0.11f), handColor, null);
        Part(PrimitiveType.Cube, "HandR", elbowR, new Vector3(0f, -0.31f, 0.01f),
            new Vector3(0.11f, 0.12f, 0.11f), handColor, null);
        Part(PrimitiveType.Cube, "ThumbL", elbowL, new Vector3(-0.045f, -0.295f, 0.055f),
            new Vector3(0.032f, 0.05f, 0.032f), handColor, null);
        Part(PrimitiveType.Cube, "ThumbR", elbowR, new Vector3(0.045f, -0.295f, 0.055f),
            new Vector3(0.032f, 0.05f, 0.032f), handColor, null);

        // 鞋：红军 / 川军 / 黔军打绑腿穿草鞋，中央军是皮靴
        if (ally || sichuan || guizhou)
        {
            Color wrap = ally ? new Color(0.4f, 0.42f, 0.39f)
                : (sichuan ? new Color(0.38f, 0.34f, 0.25f) : new Color(0.27f, 0.27f, 0.24f));
            Color shoe = ally ? new Color(0.58f, 0.5f, 0.31f)
                : (sichuan ? new Color(0.55f, 0.47f, 0.28f) : new Color(0.42f, 0.37f, 0.24f));
            Color sole = new Color(0.24f, 0.2f, 0.13f);
            bool wrapped = ally || sichuan;   // 黔军残兵连绑腿都懒得打
            foreach (Transform knee in new[] { kneeL, kneeR })
            {
                if (wrapped)
                {
                    Part(PrimitiveType.Cube, "Puttee", knee, new Vector3(0f, -0.17f, 0f),
                        new Vector3(0.172f, 0.1f, 0.182f), wrap, null);
                    Part(PrimitiveType.Cube, "PutteeWrap", knee, new Vector3(0f, -0.23f, 0f),
                        new Vector3(0.178f, 0.022f, 0.188f), wrap * 0.82f, null);
                    // 一圈圈缠上去的布条，深浅交错才不会像一只套筒
                    Part(PrimitiveType.Cube, "PutteeWrapA", knee, new Vector3(0f, -0.132f, 0f),
                        new Vector3(0.178f, 0.016f, 0.188f), wrap * 0.78f, null);
                    Part(PrimitiveType.Cube, "PutteeWrapB", knee, new Vector3(0f, -0.21f, 0f),
                        new Vector3(0.18f, 0.014f, 0.19f), wrap * 1.12f, null);
                    Part(PrimitiveType.Cube, "PutteeWrapC", knee, new Vector3(0f, -0.266f, 0f),
                        new Vector3(0.178f, 0.016f, 0.188f), wrap * 0.86f, null);
                    // 布条收尾的结，塞在腿外侧
                    Part(PrimitiveType.Cube, "PutteeKnot", knee, new Vector3(0.088f, -0.26f, 0.02f),
                        new Vector3(0.02f, 0.05f, 0.03f), wrap * 0.72f, null);
                }
                else
                {
                    // 黔军：裤脚敞着，露出瘦腿
                    Part(PrimitiveType.Cube, "BareShin", knee, new Vector3(0f, -0.22f, 0f),
                        new Vector3(0.152f, 0.16f, 0.162f), skin * 0.92f, null);
                }
                Part(PrimitiveType.Cube, "StrawShoe", knee, new Vector3(0f, -0.34f, 0.05f),
                    new Vector3(0.18f, 0.09f, 0.27f), shoe, null);
                Part(PrimitiveType.Cube, "ShoeSole", knee, new Vector3(0f, -0.385f, 0.05f),
                    new Vector3(0.185f, 0.025f, 0.275f), sole, null);

                // 草鞋鼻梁上的麻绳：两道横绳加一个系在脚踝的活结
                Color cord = new Color(0.36f, 0.29f, 0.17f);
                Part(PrimitiveType.Cube, "ShoeCordA", knee, new Vector3(0f, -0.301f, 0.115f),
                    new Vector3(0.185f, 0.014f, 0.016f), cord, null);
                Part(PrimitiveType.Cube, "ShoeCordB", knee, new Vector3(0f, -0.298f, 0.03f),
                    new Vector3(0.185f, 0.014f, 0.016f), cord, null);
                GameObject toeCord = Part(PrimitiveType.Cube, "ShoeCordToe", knee,
                    new Vector3(0f, -0.31f, 0.155f), new Vector3(0.02f, 0.012f, 0.09f),
                    cord, null);
                toeCord.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                Part(PrimitiveType.Cube, "ShoeTie", knee, new Vector3(0.086f, -0.29f, -0.02f),
                    new Vector3(0.018f, 0.045f, 0.018f), cord * 1.15f, null);
            }
        }
        else
        {
            Color leather = new Color(0.2f, 0.13f, 0.08f);
            Part(PrimitiveType.Cube, "BootL", kneeL, new Vector3(0f, -0.24f, 0.01f),
                new Vector3(0.175f, 0.2f, 0.185f), leather, null);
            Part(PrimitiveType.Cube, "BootR", kneeR, new Vector3(0f, -0.24f, 0.01f),
                new Vector3(0.175f, 0.2f, 0.185f), leather, null);
            Part(PrimitiveType.Cube, "SoleL", kneeL,
                new Vector3(0f, -0.35f, 0.05f), new Vector3(0.18f, 0.1f, 0.29f),
                new Color(0.12f, 0.09f, 0.06f), null);
            Part(PrimitiveType.Cube, "SoleR", kneeR,
                new Vector3(0f, -0.35f, 0.05f), new Vector3(0.18f, 0.1f, 0.29f),
                new Color(0.12f, 0.09f, 0.06f), null);
        }

        // ── 步枪（视觉件，不参与碰撞） ──
        // 整把枪放进 Rifle 空物体，枢轴放在枪托上：AI 端枪时以枪托为轴抬起
        Transform rifleRoot = Pivot(root, "Rifle", new Vector3(0.2f, 1.08f, 0.2f));

        Part(PrimitiveType.Cube, "RifleStock", rifleRoot, new Vector3(0f, 0f, 0f),
            new Vector3(0.05f, 0.08f, 0.34f), wood, null);
        Part(PrimitiveType.Cube, "RifleReceiver", rifleRoot, new Vector3(0f, 0.05f, 0.2f),
            new Vector3(0.045f, 0.055f, 0.14f), metal, null);
        Part(PrimitiveType.Cube, "RifleBarrel", rifleRoot,
            new Vector3(0f, 0.07f, 0.52f), new Vector3(0.022f, 0.022f, 0.5f),
            metal, null);
        Part(PrimitiveType.Cube, "FrontSight", rifleRoot, new Vector3(0f, 0.095f, 0.74f),
            new Vector3(0.012f, 0.03f, 0.012f), metal, null);
        Part(PrimitiveType.Cube, "RearSight", rifleRoot, new Vector3(0f, 0.085f, 0.28f),
            new Vector3(0.03f, 0.018f, 0.018f), metal, null);

        if (sichuan || guizhou)
        {
            // 老套筒：枪管外露一节木护手，是川军、黔军手里的老家伙
            Part(PrimitiveType.Cube, "RifleHandguard", rifleRoot,
                new Vector3(0f, 0.057f, 0.36f), new Vector3(0.042f, 0.034f, 0.3f),
                wood * 0.92f, null);
        }
        if (central)
        {
            Part(PrimitiveType.Cube, "RifleSling", rifleRoot, new Vector3(0f, -0.035f, 0.22f),
                new Vector3(0.012f, 0.022f, 0.5f), new Color(0.32f, 0.25f, 0.16f), null);
        }
        if (guizhou)
        {
            // 拿布条缠住的枪托
            Part(PrimitiveType.Cube, "RifleWrap", rifleRoot, new Vector3(0f, -0.005f, 0.07f),
                new Vector3(0.056f, 0.032f, 0.1f), new Color(0.44f, 0.38f, 0.26f), null);
        }

        // 枪机拉柄（汉阳造是栓动枪，敌我都得拉栓）
        Part(PrimitiveType.Cube, "BoltHandle", rifleRoot,
            new Vector3(0.035f, 0.05f, 0.25f), new Vector3(0.045f, 0.018f, 0.02f),
            metal, null);

        if (pipe)
        {
            // 川军"双枪兵"：左手还攥着一杆大烟枪
            GameObject stem = Part(PrimitiveType.Cylinder, "OpiumPipe", shoulderL,
                new Vector3(0.02f, -0.62f, 0.16f), new Vector3(0.014f, 0.15f, 0.014f),
                new Color(0.3f, 0.2f, 0.11f), null);
            stem.transform.localRotation = Quaternion.Euler(78f, 0f, 0f);
            Part(PrimitiveType.Cylinder, "PipeBowl", shoulderL,
                new Vector3(0.02f, -0.589f, 0.307f), new Vector3(0.032f, 0.022f, 0.032f),
                new Color(0.2f, 0.17f, 0.14f), null);
            Part(PrimitiveType.Cylinder, "PipeMouthpiece", shoulderL,
                new Vector3(0.02f, -0.651f, 0.012f), new Vector3(0.011f, 0.028f, 0.011f),
                new Color(0.62f, 0.6f, 0.5f), null);
        }
    }

    private static Transform Pivot(Transform root, string name, Vector3 localPos)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(root, false);
        pivot.transform.localPosition = localPos;
        return pivot.transform;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent,
                                   Vector3 localPos, Vector3 localScale, Color color,
                                   BodyZone? zone)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = LevelBuilder.MakeMaterial(color);

        if (zone.HasValue)
        {
            go.AddComponent<HitZone>().Zone = zone.Value;
        }
        else
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Object.Destroy(collider);
            }
        }

        return go;
    }
}
