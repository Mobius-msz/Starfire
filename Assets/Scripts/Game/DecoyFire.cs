using UnityEngine;

/// <summary>
/// 太平渡的假火堆：按 E 点着后，附近的敌人会被火光引过去十几秒，
/// 玩家趁机从另一侧渡河——声东击西。
/// </summary>
public class DecoyFire : MonoBehaviour
{
    private class Brazier
    {
        public Transform Root;
        public Transform Flame;
        public Light Light;
        public GameObject FireGroup;
        public bool Lit;
    }

    private Transform player;
    private HUDController hud;
    private AudioClip bangClip;
    private Brazier[] braziers;
    private bool promptShown;

    public void Initialize(Transform playerRef, HUDController hudRef, Vector3[] positions,
                           AudioClip bang)
    {
        player = playerRef;
        hud = hudRef;
        bangClip = bang;

        braziers = new Brazier[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            braziers[i] = BuildBrazier(positions[i]);
        }
    }

    private Brazier BuildBrazier(Vector3 position)
    {
        GameObject root = new GameObject("DecoyFire");
        root.transform.SetParent(transform, true);
        root.transform.position = position;

        Color stone = new Color(0.42f, 0.4f, 0.36f);
        Color wood = new Color(0.29f, 0.19f, 0.1f);

        // 石圈
        for (int i = 0; i < 8; i++)
        {
            float angle = i / 8f * Mathf.PI * 2f;
            GameObject stoneGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stoneGo.name = "BrazierStone";
            stoneGo.transform.SetParent(root.transform, false);
            stoneGo.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * 0.95f, 0.16f, Mathf.Sin(angle) * 0.95f);
            stoneGo.transform.localScale = new Vector3(0.46f, 0.32f, 0.36f);
            stoneGo.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
            stoneGo.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(stone);
            Destroy(stoneGo.GetComponent<Collider>());
        }

        // 柴堆
        for (int i = 0; i < 4; i++)
        {
            GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "BrazierLog";
            log.transform.SetParent(root.transform, false);
            log.transform.localPosition = new Vector3(
                Mathf.Cos(i * 1.6f) * 0.25f, 0.34f, Mathf.Sin(i * 1.6f) * 0.25f);
            log.transform.localScale = new Vector3(0.09f, 0.7f, 0.09f);
            log.transform.localRotation = Quaternion.Euler(90f, i * 45f, 20f);
            log.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(wood);
            Destroy(log.GetComponent<Collider>());
        }

        // 火焰（点亮前隐藏）
        GameObject fireGroup = new GameObject("Flames");
        fireGroup.transform.SetParent(root.transform, false);
        fireGroup.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        fireGroup.SetActive(false);

        Transform core = MakeFlame(fireGroup.transform, new Vector3(0f, 0.55f, 0f),
            new Vector3(0.62f, 1.15f, 0.62f), new Color(1f, 0.55f, 0.12f),
            new Color(3.2f, 1.25f, 0.18f));
        MakeFlame(fireGroup.transform, new Vector3(-0.32f, 0.35f, 0.14f),
            new Vector3(0.4f, 0.75f, 0.4f), new Color(1f, 0.62f, 0.16f),
            new Color(2.4f, 0.9f, 0.12f));
        MakeFlame(fireGroup.transform, new Vector3(0.3f, 0.4f, -0.12f),
            new Vector3(0.36f, 0.68f, 0.36f), new Color(1f, 0.67f, 0.2f),
            new Color(2.2f, 0.85f, 0.1f));

        GameObject lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        Light fireLight = lightGo.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.62f, 0.3f);
        fireLight.intensity = 0f;
        fireLight.range = 22f;

        FireFlicker flicker = lightGo.AddComponent<FireFlicker>();
        flicker.flame = core;

        return new Brazier
        {
            Root = root.transform,
            Flame = core,
            Light = fireLight,
            FireGroup = fireGroup,
            Lit = false
        };
    }

    private static Transform MakeFlame(Transform parent, Vector3 localPos, Vector3 localScale,
                                       Color color, Color emission)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Flame";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = LevelBuilder.MakeMaterial(color, emission);
        go.GetComponent<Renderer>().shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(go.GetComponent<Collider>());
        return go.transform;
    }

    private void Update()
    {
        if (player == null || braziers == null) return;

        bool nearUnlit = false;

        for (int i = 0; i < braziers.Length; i++)
        {
            Brazier brazier = braziers[i];
            if (brazier.Lit) continue;

            Vector3 delta = player.position - brazier.Root.position;
            delta.y = 0f;
            if (delta.magnitude > 3.5f) continue;

            nearUnlit = true;

            if (Input.GetKeyDown(KeyCode.E))
            {
                LightUp(brazier);
                hud.SetPrompt("", 0f);
                promptShown = false;
                return;
            }
        }

        if (nearUnlit && !promptShown)
        {
            hud.SetPrompt("按 E 点燃假火堆　声东击西", 0f);
            promptShown = true;
        }
        else if (!nearUnlit && promptShown)
        {
            hud.SetPrompt("", 0f);
            promptShown = false;
        }
    }

    private void LightUp(Brazier brazier)
    {
        brazier.Lit = true;
        brazier.FireGroup.SetActive(true);
        brazier.Light.intensity = 3.6f;

        EnemyAI.Lure(brazier.Root.position, 18f);

        if (bangClip != null)
        {
            AudioSource.PlayClipAtPoint(bangClip, brazier.Root.position, 0.35f);
        }

        hud.ShowMessage("声东击西 · 敌军朝着假火堆扑过去了", 4f);
    }
}
