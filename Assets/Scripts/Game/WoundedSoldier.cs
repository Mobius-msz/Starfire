using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 青杠坡上的伤员：跪坐在路边，按住 E 救护。
///
/// 救起一名伤员给玩家回一点血，并累计士气。伤员没有 Health，
/// 碰撞体全是触发器，不会替玩家挡子弹，也不会被流弹打死。
/// </summary>
public class WoundedSoldier : MonoBehaviour
{
    private Transform player;
    private HUDController hud;
    private Health playerHealth;
    private Action onRescued;

    private Transform visual;
    private Transform hipL;
    private Transform hipR;
    private Transform shoulderL;
    private Transform shoulderR;
    private Transform marker;

    private float progress;
    private bool rescued;
    private bool promptShown;

    public static WoundedSoldier Spawn(Transform root, Vector3 groundPos, Transform playerRef,
                                       HUDController hudRef, Action rescuedCallback)
    {
        GameObject go = new GameObject("WoundedSoldier");
        go.tag = "Ally";
        go.transform.SetParent(root, true);
        go.transform.position = groundPos;

        WoundedSoldier wounded = go.AddComponent<WoundedSoldier>();
        wounded.Build(playerRef, hudRef, rescuedCallback);
        return wounded;
    }

    private void Build(Transform playerRef, HUDController hudRef, Action rescuedCallback)
    {
        player = playerRef;
        hud = hudRef;
        onRescued = rescuedCallback;
        playerHealth = playerRef != null ? playerRef.GetComponent<Health>() : null;

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        visual.localPosition = new Vector3(0f, -0.42f, 0f);

        EnemySpawner.BuildBody(visual, true);

        hipL = visual.Find("HipL");
        hipR = visual.Find("HipR");
        shoulderL = visual.Find("ShoulderL");
        shoulderR = visual.Find("ShoulderR");

        // 跪坐：两腿前伸、上身微驼，两手垂在身前
        if (hipL != null) hipL.localRotation = Quaternion.Euler(76f, 0f, -6f);
        if (hipR != null) hipR.localRotation = Quaternion.Euler(72f, 0f, 6f);
        if (shoulderL != null) shoulderL.localRotation = Quaternion.Euler(-28f, 0f, 12f);
        if (shoulderR != null) shoulderR.localRotation = Quaternion.Euler(-24f, 0f, -12f);

        // 伤员不参与命中：所有碰撞体改成触发器
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.isTrigger = true;

        BuildMarker();
    }

    /// <summary>身边插一面白底红十字的小旗，雾里也能找到。</summary>
    private void BuildMarker()
    {
        GameObject markerGo = new GameObject("WoundedMarker");
        markerGo.transform.SetParent(transform, false);
        markerGo.transform.localPosition = new Vector3(0.95f, 0f, 0f);
        marker = markerGo.transform;

        Part(PrimitiveType.Cylinder, "MarkerPole", marker, new Vector3(0f, 1.05f, 0f),
            new Vector3(0.05f, 1.05f, 0.05f), new Color(0.4f, 0.33f, 0.24f));
        Part(PrimitiveType.Cube, "MarkerCloth", marker, new Vector3(0f, 2.0f, 0f),
            new Vector3(0.72f, 0.52f, 0.03f), new Color(0.92f, 0.92f, 0.88f));
        Part(PrimitiveType.Cube, "MarkerCrossV", marker, new Vector3(0f, 2.0f, -0.03f),
            new Vector3(0.1f, 0.36f, 0.02f), new Color(0.72f, 0.11f, 0.09f),
            new Color(0.5f, 0.03f, 0.02f));
        Part(PrimitiveType.Cube, "MarkerCrossH", marker, new Vector3(0f, 2.0f, -0.03f),
            new Vector3(0.32f, 0.1f, 0.02f), new Color(0.72f, 0.11f, 0.09f),
            new Color(0.5f, 0.03f, 0.02f));
    }

    private void Update()
    {
        if (rescued || player == null) return;

        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        float distance = delta.magnitude;

        if (distance > 3.6f)
        {
            if (promptShown)
            {
                hud.SetPrompt("", 0f);
                promptShown = false;
            }
            return;
        }

        promptShown = true;

        if (Input.GetKey(KeyCode.E))
        {
            progress += Time.deltaTime / 2.2f;
            hud.SetPrompt("按住 E 救护伤员", Mathf.Clamp01(progress));

            if (progress >= 1f) Rescued();
        }
        else
        {
            hud.SetPrompt("按 E 救护伤员", progress);
        }
    }

    private void Rescued()
    {
        rescued = true;

        if (hud != null)
        {
            hud.SetPrompt("", 0f);
            hud.ShowMessage("救起一名伤员 · 士气 +1", 2.6f);
        }

        if (playerHealth != null) playerHealth.Restore(20f);
        if (onRescued != null) onRescued();

        if (marker != null) marker.gameObject.SetActive(false);
        StartCoroutine(LeaveRoutine());
    }

    /// <summary>站起来，捂着胳膊往后方走，走远后消失。</summary>
    private IEnumerator LeaveRoutine()
    {
        if (hipL != null) hipL.localRotation = Quaternion.identity;
        if (hipR != null) hipR.localRotation = Quaternion.identity;
        if (shoulderL != null) shoulderL.localRotation = Quaternion.Euler(-18f, 0f, 8f);
        if (shoulderR != null) shoulderR.localRotation = Quaternion.Euler(-12f, 0f, -8f);

        float t = 0f;
        while (t < 0.7f)
        {
            t += Time.deltaTime;
            if (visual != null)
            {
                visual.localPosition = new Vector3(0f, Mathf.Lerp(-0.42f, 0f, t / 0.7f), 0f);
            }
            yield return null;
        }

        Vector3 start = transform.position;
        Vector3 end = start + new Vector3(0.6f, 0f, -11f);

        t = 0f;
        while (t < 4.6f)
        {
            t += Time.deltaTime;
            float k = t / 4.6f;

            float bob = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.06f;
            transform.position = Vector3.Lerp(start, end, k) + Vector3.up * bob;
            transform.rotation = Quaternion.Euler(-3f, 176f, 0f);

            float swing = Mathf.Sin(t * 10f) * 28f;
            if (hipL != null) hipL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (hipR != null) hipR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            if (shoulderL != null) shoulderL.localRotation = Quaternion.Euler(-swing * 0.4f, 0f, 8f);
            if (shoulderR != null) shoulderR.localRotation = Quaternion.Euler(swing * 0.4f, 0f, -8f);

            yield return null;
        }

        Destroy(gameObject);
    }

    private static void Part(PrimitiveType type, string name, Transform parent,
                             Vector3 localPos, Vector3 localScale, Color color,
                             Color? emission = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        Destroy(go.GetComponent<Collider>());

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = LevelBuilder.MakeMaterial(color, emission);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
