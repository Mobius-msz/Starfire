using UnityEngine;

/// <summary>
/// 胜利结算镜头：主角端着枪从战场里走向镜头，到位后收枪、抬手敬礼。
/// 战场本身不动，镜头和人物只做一段固定演出，按 Enter 后随场景重载一起销毁。
/// </summary>
public class VictoryScene : MonoBehaviour
{
    private const float WalkDuration = 3.4f;
    private const float LowerDuration = 0.55f;
    private const float SaluteDuration = 0.7f;

    private const float UpperArm = 0.30f;
    private const float ForeArm = 0.34f;

    private static readonly Vector3 ShoulderLPos = new Vector3(-0.3f, 1.38f, 0f);
    private static readonly Vector3 ShoulderRPos = new Vector3(0.3f, 1.38f, 0f);

    private Transform hero;
    private Transform hipL;
    private Transform hipR;
    private Transform shoulderL;
    private Transform shoulderR;
    private Transform elbowL;
    private Transform elbowR;
    private Transform rifle;

    private Vector3 anchor;
    private Vector3 forward;
    private float heroYaw;
    private float startedAt;

    private Vector3 rifleHoldPos;
    private Quaternion rifleHoldRot;
    private Vector3 rifleSlungPos;
    private Quaternion rifleSlungRot;

    private Quaternion holdUpperL;
    private Quaternion holdForeL;
    private Quaternion holdUpperR;
    private Quaternion holdForeR;
    private Quaternion saluteUpperR;
    private Quaternion saluteForeR;

    private const float StartDistance = 11.5f;
    private const float StopDistance = 4.8f;

    public static VictoryScene Play(Transform cameraTransform, Vector3 stageAnchor, float stageYaw)
    {
        GameObject go = new GameObject("VictoryScene");
        VictoryScene scene = go.AddComponent<VictoryScene>();
        scene.anchor = stageAnchor;
        scene.forward = Quaternion.Euler(0f, stageYaw, 0f) * Vector3.forward;
        scene.heroYaw = stageYaw + 180f;
        scene.Build();
        scene.startedAt = Time.time;
        return scene;
    }

    private void Build()
    {
        Color coat = new Color(0.33f, 0.37f, 0.4f);
        Color trousers = new Color(0.26f, 0.29f, 0.31f);
        Color gear = new Color(0.3f, 0.25f, 0.17f);
        Color skin = new Color(0.56f, 0.43f, 0.33f);
        Color cap = new Color(0.3f, 0.33f, 0.34f);
        Color metal = new Color(0.17f, 0.17f, 0.18f);
        Color wood = new Color(0.22f, 0.14f, 0.08f);
        Color red = new Color(0.58f, 0.14f, 0.11f);

        GameObject heroGo = new GameObject("VictoryHero");
        hero = heroGo.transform;
        hero.SetParent(transform, true);

        Vector3 feet = anchor + forward * StartDistance;
        feet.y = GroundAt(feet.x, feet.z, anchor.y - 1.6f) + 0.02f;
        hero.position = feet;
        hero.rotation = Quaternion.Euler(0f, heroYaw, 0f);

        // ── 躯干 ──
        Part(PrimitiveType.Capsule, "Torso", hero, new Vector3(0f, 1.04f, 0f),
            new Vector3(0.5f, 0.44f, 0.34f), coat);
        Part(PrimitiveType.Cube, "CoatHem", hero, new Vector3(0f, 0.84f, 0f),
            new Vector3(0.48f, 0.34f, 0.36f), new Color(0.26f, 0.25f, 0.21f));
        Part(PrimitiveType.Cube, "Belt", hero, new Vector3(0f, 1.0f, 0f),
            new Vector3(0.52f, 0.055f, 0.38f), gear);
        Part(PrimitiveType.Cube, "Buckle", hero, new Vector3(0f, 1.0f, 0.2f),
            new Vector3(0.07f, 0.055f, 0.02f), metal);
        GameObject hemPatch = Part(PrimitiveType.Cube, "CoatPatch", hero,
            new Vector3(-0.1f, 1.16f, 0.185f), new Vector3(0.13f, 0.14f, 0.008f),
            new Color(0.43f, 0.42f, 0.36f));
        hemPatch.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);

        // ── 背包与铺盖卷 ──
        Part(PrimitiveType.Cube, "Backpack", hero, new Vector3(0f, 1.12f, -0.26f),
            new Vector3(0.36f, 0.42f, 0.18f), gear);
        GameObject bedroll = Part(PrimitiveType.Cylinder, "Bedroll", hero,
            new Vector3(0f, 1.35f, -0.27f), new Vector3(0.09f, 0.2f, 0.09f),
            new Color(0.42f, 0.39f, 0.28f));
        bedroll.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Part(PrimitiveType.Cylinder, "Canteen", hero, new Vector3(0.25f, 0.9f, -0.23f),
            new Vector3(0.08f, 0.11f, 0.08f), new Color(0.2f, 0.26f, 0.2f));

        // ── 头与八角帽 ──
        Part(PrimitiveType.Cylinder, "Neck", hero, new Vector3(0f, 1.47f, 0f),
            new Vector3(0.13f, 0.08f, 0.13f), skin);
        Part(PrimitiveType.Sphere, "Head", hero, new Vector3(0f, 1.66f, 0f),
            new Vector3(0.3f, 0.34f, 0.3f), skin);
        Part(PrimitiveType.Sphere, "Cap", hero, new Vector3(0f, 1.8f, 0f),
            new Vector3(0.35f, 0.21f, 0.35f), cap);
        Part(PrimitiveType.Cylinder, "CapBrim", hero, new Vector3(0f, 1.73f, 0.03f),
            new Vector3(0.36f, 0.014f, 0.36f), cap);
        GameObject star = Part(PrimitiveType.Cube, "RedStar", hero,
            new Vector3(0f, 1.81f, 0.155f), new Vector3(0.085f, 0.085f, 0.02f), red);
        star.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

        Part(PrimitiveType.Cube, "CollarL", hero, new Vector3(-0.1f, 1.42f, 0.15f),
            new Vector3(0.1f, 0.055f, 0.02f), red);
        Part(PrimitiveType.Cube, "CollarR", hero, new Vector3(0.1f, 1.42f, 0.15f),
            new Vector3(0.1f, 0.055f, 0.02f), red);
        Part(PrimitiveType.Cube, "EyeL", hero, new Vector3(-0.055f, 1.68f, 0.145f),
            new Vector3(0.03f, 0.018f, 0.01f), metal);
        Part(PrimitiveType.Cube, "EyeR", hero, new Vector3(0.055f, 1.68f, 0.145f),
            new Vector3(0.03f, 0.018f, 0.01f), metal);
        Part(PrimitiveType.Cube, "Nose", hero, new Vector3(0f, 1.63f, 0.152f),
            new Vector3(0.035f, 0.045f, 0.035f), skin);

        // ── 腿：直接绕髋摆动 ──
        hipL = Pivot(hero, "HipL", new Vector3(-0.13f, 0.8f, 0f));
        hipR = Pivot(hero, "HipR", new Vector3(0.13f, 0.8f, 0f));
        Part(PrimitiveType.Cube, "LegL", hipL, new Vector3(0f, -0.37f, 0f),
            new Vector3(0.16f, 0.72f, 0.17f), trousers);
        Part(PrimitiveType.Cube, "LegR", hipR, new Vector3(0f, -0.37f, 0f),
            new Vector3(0.16f, 0.72f, 0.17f), trousers);
        Color puttee = new Color(0.4f, 0.42f, 0.39f);
        foreach (Transform hip in new[] { hipL, hipR })
        {
            Part(PrimitiveType.Cube, "Puttee", hip, new Vector3(0f, -0.55f, 0f),
                new Vector3(0.172f, 0.1f, 0.182f), puttee);
            Part(PrimitiveType.Cube, "PutteeWrap", hip, new Vector3(0f, -0.61f, 0f),
                new Vector3(0.178f, 0.022f, 0.188f), new Color(0.33f, 0.35f, 0.33f));
            Part(PrimitiveType.Cube, "StrawShoe", hip, new Vector3(0f, -0.72f, 0.05f),
                new Vector3(0.18f, 0.09f, 0.27f), new Color(0.58f, 0.5f, 0.31f));
            Part(PrimitiveType.Cube, "ShoeSole", hip, new Vector3(0f, -0.765f, 0.05f),
                new Vector3(0.185f, 0.025f, 0.275f), new Color(0.24f, 0.2f, 0.13f));
        }

        // ── 两节手臂：上臂 + 小臂，敬礼时能弯肘 ──
        shoulderL = Pivot(hero, "ShoulderL", ShoulderLPos);
        shoulderR = Pivot(hero, "ShoulderR", ShoulderRPos);
        Part(PrimitiveType.Cube, "UpperArmL", shoulderL, new Vector3(0f, -0.15f, 0f),
            new Vector3(0.12f, 0.3f, 0.13f), coat);
        Part(PrimitiveType.Cube, "UpperArmR", shoulderR, new Vector3(0f, -0.15f, 0f),
            new Vector3(0.12f, 0.3f, 0.13f), coat);
        Part(PrimitiveType.Cube, "Armband", shoulderL, new Vector3(0f, -0.2f, 0f),
            new Vector3(0.137f, 0.09f, 0.147f), red);

        elbowL = Pivot(shoulderL, "ElbowL", new Vector3(0f, -0.3f, 0f));
        elbowR = Pivot(shoulderR, "ElbowR", new Vector3(0f, -0.3f, 0f));
        Part(PrimitiveType.Cube, "ForeArmL", elbowL, new Vector3(0f, -0.16f, 0f),
            new Vector3(0.11f, 0.32f, 0.12f), coat);
        Part(PrimitiveType.Cube, "ForeArmR", elbowR, new Vector3(0f, -0.16f, 0f),
            new Vector3(0.11f, 0.32f, 0.12f), coat);
        Part(PrimitiveType.Cube, "HandL", elbowL, new Vector3(0f, -0.34f, 0.01f),
            new Vector3(0.11f, 0.12f, 0.11f), skin);
        Part(PrimitiveType.Cube, "HandR", elbowR, new Vector3(0f, -0.34f, 0.01f),
            new Vector3(0.11f, 0.12f, 0.11f), skin);

        // ── 步枪：单独一组，收枪时能整体放下去 ──
        GameObject rifleGo = new GameObject("HeroRifle");
        rifle = rifleGo.transform;
        rifle.SetParent(hero, false);
        Part(PrimitiveType.Cube, "Stock", rifle, new Vector3(0f, 0f, -0.12f),
            new Vector3(0.06f, 0.09f, 0.34f), wood);
        Part(PrimitiveType.Cube, "ButtPlate", rifle, new Vector3(0f, 0f, -0.295f),
            new Vector3(0.055f, 0.1f, 0.02f), metal);
        Part(PrimitiveType.Cube, "Receiver", rifle, new Vector3(0f, 0.02f, 0.13f),
            new Vector3(0.05f, 0.06f, 0.18f), metal);
        Part(PrimitiveType.Cube, "Handguard", rifle, new Vector3(0f, 0.002f, 0.32f),
            new Vector3(0.055f, 0.05f, 0.22f), wood);
        Part(PrimitiveType.Cube, "Barrel", rifle, new Vector3(0f, 0.025f, 0.56f),
            new Vector3(0.024f, 0.024f, 0.32f), metal);
        Part(PrimitiveType.Cube, "FrontSight", rifle, new Vector3(0f, 0.05f, 0.68f),
            new Vector3(0.012f, 0.03f, 0.012f), metal);

        // ── 背枪的挎带：从右胯绕过左肩，正面一道、背面一道，肩上压一小段接起来 ──
        Color sling = new Color(0.38f, 0.31f, 0.2f);
        SlingBand("SlingChest", hero, new Vector3(0.215f, 0.99f, 0.19f),
            new Vector3(-0.19f, 1.37f, 0.155f), sling);
        SlingBand("SlingBack", hero, new Vector3(0.2f, 0.98f, -0.36f),
            new Vector3(-0.21f, 1.36f, -0.36f), sling);
        Part(PrimitiveType.Cube, "SlingShoulder", hero, new Vector3(-0.215f, 1.385f, -0.1f),
            new Vector3(0.05f, 0.05f, 0.52f), sling);

        rifleHoldPos = new Vector3(0.18f, 1.02f, 0.18f);
        rifleHoldRot = Quaternion.Euler(-35f, -60f, 0f);
        // 收枪后斜挎在背后：枪托落在右胯，枪口从左肩后探出来，整支枪贴着背包。
        // 先绕 X 转 -90° 把枪身从 +Z 立起来，再绕 Z 转 32° 让枪身斜着倒向左边，
        // 位置按枪身几何中心反推（模型中心在枪的局部 z≈0.19 处）。
        rifleSlungPos = new Vector3(0.15f, 1f, -0.42f);
        rifleSlungRot = Quaternion.Euler(0f, 0f, 32f) * Quaternion.Euler(-90f, 0f, 0f);

        rifle.localPosition = rifleHoldPos;
        rifle.localRotation = rifleHoldRot;

        Vector3 rightGrip = hero.InverseTransformPoint(
            rifle.TransformPoint(new Vector3(0.012f, 0f, 0.04f)));
        Vector3 leftGrip = hero.InverseTransformPoint(
            rifle.TransformPoint(new Vector3(0.012f, 0.005f, 0.40f)));

        SolveArm(ShoulderLPos, leftGrip, out holdUpperL, out holdForeL);
        SolveArm(ShoulderRPos, rightGrip, out holdUpperR, out holdForeR);
        SolveArm(ShoulderRPos, new Vector3(0.12f, 1.74f, 0.2f),
            out saluteUpperR, out saluteForeR);

        ApplyArm(shoulderL, elbowL, holdUpperL, holdForeL);
        ApplyArm(shoulderR, elbowR, holdUpperR, holdForeR);

        // 主角脚下留一块影子，免得人物像浮在战场上
        Part(PrimitiveType.Cylinder, "HeroShadow", hero, new Vector3(0f, 0.015f, 0.05f),
            new Vector3(0.75f, 0.005f, 0.42f), new Color(0.09f, 0.09f, 0.08f));
    }

    private void Update()
    {
        if (hero == null) return;

        float t = Time.time - startedAt;
        float walkU = Mathf.Clamp01(t / WalkDuration);

        float distance = Mathf.Lerp(StartDistance, StopDistance, Mathf.SmoothStep(0f, 1f, walkU));
        Vector3 feet = anchor + forward * distance;
        feet.y = GroundAt(feet.x, feet.z, anchor.y - 1.6f) + 0.02f;
        hero.position = feet;

        // 走路时迈腿；最后 0.35 秒把摆幅收回去，避免定格时一条腿悬着
        float swing = 0f;
        if (t < WalkDuration)
        {
            float fade = Mathf.Clamp01((WalkDuration - t) / 0.35f);
            swing = Mathf.Sin(t * 7.4f) * 25f * fade;
        }
        hipL.localRotation = Quaternion.Euler(swing, 0f, 0f);
        hipR.localRotation = Quaternion.Euler(-swing, 0f, 0f);

        if (t <= WalkDuration)
        {
            ApplyArm(shoulderL, elbowL, holdUpperL, holdForeL);
            ApplyArm(shoulderR, elbowR, holdUpperR, holdForeR);
            return;
        }

        float lowerU = Mathf.Clamp01((t - WalkDuration) / LowerDuration);

        // 收枪：提着枪从肩上绕到背后斜挎，双臂顺势放回身侧。
        // 直线插值会让枪从胸口穿过去，所以中途往外、往上各鼓一下，走一条绕过肩膀的弧线。
        float arc = Mathf.Sin(lowerU * Mathf.PI);
        Vector3 slungPos = Vector3.Lerp(rifleHoldPos, rifleSlungPos, lowerU);
        slungPos.x += 0.18f * arc;
        slungPos.y += 0.30f * arc;
        rifle.localPosition = slungPos;
        rifle.localRotation = Quaternion.Slerp(rifleHoldRot, rifleSlungRot, lowerU);
        ApplyArm(shoulderL, elbowL,
            Quaternion.Slerp(holdUpperL, Quaternion.identity, lowerU),
            Quaternion.Slerp(holdForeL, Quaternion.identity, lowerU));
        ApplyArm(shoulderR, elbowR,
            Quaternion.Slerp(holdUpperR, Quaternion.identity, lowerU),
            Quaternion.Slerp(holdForeR, Quaternion.identity, lowerU));

        float saluteU = Mathf.Clamp01((t - WalkDuration - LowerDuration) / SaluteDuration);
        if (saluteU <= 0f) return;

        float ease = Mathf.SmoothStep(0f, 1f, saluteU);
        ApplyArm(shoulderR, elbowR,
            Quaternion.Slerp(Quaternion.identity, saluteUpperR, ease),
            Quaternion.Slerp(Quaternion.identity, saluteForeR, ease));
    }

    private static void ApplyArm(Transform shoulder, Transform elbow,
                                 Quaternion upperRot, Quaternion foreRot)
    {
        shoulder.localRotation = upperRot;
        elbow.localRotation = foreRot;
    }

    /// <summary>两节手臂的解析 IK：给定手心位置，求上臂与肘的旋转。</summary>
    private static void SolveArm(Vector3 shoulderPos, Vector3 target,
                                 out Quaternion upperRot, out Quaternion foreRot)
    {
        Vector3 v = target - shoulderPos;
        float reach = Mathf.Min(v.magnitude, UpperArm + ForeArm - 0.01f);
        reach = Mathf.Max(reach, 0.1f);
        Vector3 dir = v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;

        float cosA = Mathf.Clamp(
            (UpperArm * UpperArm + reach * reach - ForeArm * ForeArm) / (2f * UpperArm * reach),
            -1f, 1f);
        float angle = Mathf.Acos(cosA) * Mathf.Rad2Deg;

        // 在绕视线方向的各个滚动角里挑一个让手肘最靠外、略向前的解
        float side = shoulderPos.x >= 0f ? 1f : -1f;
        Vector3 best = dir;
        float bestScore = float.MinValue;

        for (int i = 0; i < 36; i++)
        {
            Quaternion roll = Quaternion.AngleAxis(i * 10f, dir);
            Vector3 candidate = Quaternion.AngleAxis(angle, roll * Vector3.right) * dir;
            Vector3 elbow = shoulderPos + candidate * UpperArm;
            float score = elbow.x * side + elbow.z * 0.35f - Mathf.Abs(elbow.y - shoulderPos.y) * 0.15f;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        Vector3 elbowPos = shoulderPos + best * UpperArm;
        Vector3 foreDir = (target - elbowPos).normalized;

        upperRot = Quaternion.FromToRotation(Vector3.down, best);
        foreRot = Quaternion.Inverse(upperRot) * Quaternion.FromToRotation(Vector3.down, foreDir);
    }

    private static float GroundAt(float x, float z, float fallback)
    {
        if (Physics.Raycast(new Vector3(x, fallback + 30f, z), Vector3.down,
                            out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return fallback;
    }

    /// <summary>两点之间拉一条扁带子，长度自动按两点距离算。</summary>
    private static GameObject SlingBand(string name, Transform parent, Vector3 a, Vector3 b, Color color)
    {
        Vector3 dir = b - a;
        GameObject go = Part(PrimitiveType.Cube, name, parent, (a + b) * 0.5f,
            new Vector3(0.045f, dir.magnitude, 0.016f), color);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        return go;
    }

    private static Transform Pivot(Transform parent, string name, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent,
                                   Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = LevelBuilder.MakeMaterial(color);

        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        return go;
    }
}
