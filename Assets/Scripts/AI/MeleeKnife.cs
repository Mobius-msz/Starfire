using UnityEngine;

/// <summary>
/// 敌军 / 战友近战拼刀时手里的大刀：黑漆宽刃、斜刀尖、环首、缠绳刀柄、红缨。
/// 挂在人物 Pose 节点下，切刀时由各自 AI 显示并摆出劈砍动作。
/// </summary>
public static class MeleeKnife
{
    public static Transform Create(Transform parent)
    {
        GameObject root = new GameObject("MeleeKnife");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0.36f, 1.0f, 0.34f);
        root.transform.localRotation = Quaternion.Euler(72f, 0f, -12f);
        root.transform.localScale = new Vector3(0.82f, 0.82f, 0.82f);

        Color blade = new Color(0.12f, 0.12f, 0.13f);
        Color edge = new Color(0.5f, 0.53f, 0.56f);
        Color brass = new Color(0.5f, 0.4f, 0.18f);
        Color wrap = new Color(0.22f, 0.14f, 0.08f);
        Color crimson = new Color(0.62f, 0.09f, 0.07f);
        Color iron = new Color(0.23f, 0.23f, 0.25f);

        // 单边开刃的梯形刀身：刀背竖直，刃口斜收到平的刀尖
        KnifeBlade.Create("Blade", root.transform,
            new Vector2(-0.0375f, 0.08f), new Vector2(0.0375f, 0.08f),
            new Vector2(0.0375f, 0.42f), new Vector2(-0.002f, 0.42f),
            0.01f, blade, true);
        Part(PrimitiveType.Cube, "Edge", root.transform, new Vector3(-0.038f, 0.19f, 0.001f),
            new Vector3(0.011f, 0.22f, 0.009f), edge);
        GameObject tipEdge = Part(PrimitiveType.Cube, "TipEdge", root.transform,
            new Vector3(-0.02f, 0.36f, 0.001f), new Vector3(0.011f, 0.13f, 0.009f), edge);
        tipEdge.transform.localRotation = Quaternion.Euler(0f, 0f, -16.7f);
        Part(PrimitiveType.Cube, "Spine", root.transform, new Vector3(0.033f, 0.245f, 0f),
            new Vector3(0.011f, 0.34f, 0.012f), new Color(0.07f, 0.07f, 0.08f));
        Part(PrimitiveType.Cube, "Guard", root.transform, new Vector3(0f, 0.075f, 0f),
            new Vector3(0.12f, 0.016f, 0.03f), iron);
        Part(PrimitiveType.Cube, "Handle", root.transform, new Vector3(0f, 0.005f, 0f),
            new Vector3(0.028f, 0.13f, 0.034f), wrap);
        Part(PrimitiveType.Cube, "Wrap1", root.transform, new Vector3(0f, 0.03f, 0f),
            new Vector3(0.032f, 0.008f, 0.038f), wrap * 0.65f);
        Part(PrimitiveType.Cube, "Wrap2", root.transform, new Vector3(0f, -0.005f, 0f),
            new Vector3(0.032f, 0.008f, 0.038f), wrap * 0.65f);
        Part(PrimitiveType.Cube, "Wrap3", root.transform, new Vector3(0f, -0.04f, 0f),
            new Vector3(0.032f, 0.008f, 0.038f), wrap * 0.65f);
        Part(PrimitiveType.Cube, "Pommel", root.transform, new Vector3(0f, -0.075f, 0f),
            new Vector3(0.046f, 0.018f, 0.04f), brass);
        GameObject ring = Part(PrimitiveType.Cylinder, "PommelRing", root.transform,
            new Vector3(0f, -0.1f, 0f), new Vector3(0.02f, 0.007f, 0.02f), brass);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Part(PrimitiveType.Cylinder, "Tassel", root.transform, new Vector3(0f, -0.125f, 0f),
            new Vector3(0.013f, 0.028f, 0.013f), crimson);

        root.SetActive(false);
        return root.transform;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent,
                                   Vector3 pos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = LevelBuilder.MakeMaterial(color);

        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);

        return go;
    }
}
