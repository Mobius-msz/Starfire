using UnityEngine;

/// <summary>
/// 手榴弹掉落物。敌军被击毙时约有 20% 概率掉落。
/// 玩家走进范围即可拾取，上限由 GrenadeSystem 控制。
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class GrenadePickup : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    private float phase;
    private Transform visual;
    private bool used;

    private void Awake()
    {
        visual = transform.Find("Model");
        phase = Random.Range(0f, 6.28f);
    }

    private void Update()
    {
        if (used || visual == null) return;

        phase += Time.deltaTime;
        visual.localPosition = new Vector3(0f, 0.06f + Mathf.Sin(phase * 2.2f) * 0.04f, 0f);
        visual.Rotate(Vector3.up, 55f * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (used) return;
        if (!other.CompareTag(playerTag)) return;

        GrenadeSystem grenades = other.GetComponentInParent<GrenadeSystem>();
        if (grenades == null) return;

        if (!grenades.Add(1)) return;
        used = true;
        Destroy(gameObject);
    }

    /// <summary>生成一枚可拾取的手榴弹。</summary>
    public static void SpawnPickup(Vector3 position)
    {
        GameObject go = CreateModel("GrenadeDrop");
        go.transform.position = new Vector3(position.x, Mathf.Max(position.y, 0.3f), position.z);

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.75f;

        go.AddComponent<GrenadePickup>();
    }

    /// <summary>手榴弹模型：木柄手榴弹——铸铁弹头在上，长木柄在下。投掷物也复用它。</summary>
    public static GameObject CreateModel(string name)
    {
        Color iron = new Color(0.2f, 0.23f, 0.17f);
        Color ironDark = new Color(0.14f, 0.15f, 0.12f);
        Color wood = new Color(0.36f, 0.24f, 0.12f);
        Color woodDark = new Color(0.26f, 0.16f, 0.08f);
        Color brass = new Color(0.62f, 0.5f, 0.22f);

        GameObject root = new GameObject(name);
        GameObject model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        Transform m = model.transform;

        // 铸铁弹头 + 上下箍
        Part(m, PrimitiveType.Cylinder, "Head",
            new Vector3(0f, 0.3f, 0f), new Vector3(0.11f, 0.065f, 0.11f), iron);
        Part(m, PrimitiveType.Sphere, "HeadCap",
            new Vector3(0f, 0.365f, 0f), new Vector3(0.11f, 0.06f, 0.11f), iron);
        Part(m, PrimitiveType.Cylinder, "BandTop",
            new Vector3(0f, 0.355f, 0f), new Vector3(0.115f, 0.008f, 0.115f), ironDark);
        Part(m, PrimitiveType.Cylinder, "BandBottom",
            new Vector3(0f, 0.24f, 0f), new Vector3(0.115f, 0.008f, 0.115f), ironDark);

        // 长木柄 + 颈箍 + 尾盖
        Part(m, PrimitiveType.Cylinder, "Handle",
            new Vector3(0f, 0.1f, 0f), new Vector3(0.042f, 0.11f, 0.042f), wood);
        Part(m, PrimitiveType.Cylinder, "HandleBand",
            new Vector3(0f, 0.19f, 0f), new Vector3(0.047f, 0.008f, 0.047f), woodDark);
        Part(m, PrimitiveType.Cylinder, "HandleCap",
            new Vector3(0f, -0.02f, 0f), new Vector3(0.046f, 0.012f, 0.046f), ironDark);

        // 拉火绳与拉环
        GameObject cord = Part(m, PrimitiveType.Cylinder, "PullCord",
            new Vector3(-0.035f, 0.4f, 0f), new Vector3(0.005f, 0.035f, 0.005f),
            new Color(0.62f, 0.56f, 0.42f));
        cord.transform.localRotation = Quaternion.Euler(0f, 0f, 34f);
        Part(m, PrimitiveType.Cylinder, "PullRing",
            new Vector3(-0.068f, 0.372f, 0f), new Vector3(0.022f, 0.003f, 0.022f), brass);

        return root;
    }

    private static GameObject Part(Transform parent, PrimitiveType type, string name,
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
        if (collider != null) Object.Destroy(collider);

        return go;
    }
}
