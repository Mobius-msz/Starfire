using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌军炮火覆盖。第一关全程持续炮击，第二关在追兵逼近时呼叫炮火。
///
/// 每发炮弹先有 1.5 秒落点预警（尖啸 + 地面红点），再爆炸：
/// 范围伤害对敌我双方都生效，所以蹲在弹坑边也可能被自己人的炮弹掀翻。
/// </summary>
public class ArtillerySystem : MonoBehaviour
{
    [SerializeField] private float minInterval = 8f;
    [SerializeField] private float maxInterval = 13f;
    [SerializeField] private float blastRadius = 6.5f;
    [SerializeField] private float blastDamage = 48f;

    private Transform player;
    private Transform levelRoot;
    private AudioClip whistleClip;
    private AudioClip blastClip;
    private CameraShake cameraShake;

    private bool active;
    private float timer;

    private static Material markerMaterial;
    private static Material flashMaterial;
    private static Material smokeMaterial;
    private static Material debrisMaterial;
    private static Material craterMaterial;

    public void Initialize(Transform playerRef, Transform root, AudioClip whistle,
                           AudioClip blast, CameraShake shake)
    {
        player = playerRef;
        levelRoot = root;
        whistleClip = whistle;
        blastClip = blast;
        cameraShake = shake;
        timer = 10f;
    }

    /// <summary>第一关：开启持续炮击。第二关不开自动炮击，改由导演喊火力覆盖。</summary>
    public void SetActive(bool value)
    {
        active = value;
        if (value) timer = Mathf.Max(timer, 8f);
    }

    private void Update()
    {
        if (!active || player == null) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        timer = Random.Range(minInterval, maxInterval);
        FireVolley(Random.value < 0.35f ? 2 : 1);
    }

    public void FireVolley(int shells)
    {
        StartCoroutine(VolleyRoutine(Mathf.Max(1, shells)));
    }

    /// <summary>
    /// 敌军炮兵阵地专用：弹丸已经在空中飞完，落地直接结算爆炸。
    /// </summary>
    public void FireAt(Vector3 point)
    {
        Explode(point);
    }

    /// <summary>炮弹出膛时先在地上画预警圈，弹丸落得慢也有时间躲。</summary>
    public void MarkImpact(Vector3 point)
    {
        SpawnMarker(point);
    }

    private IEnumerator VolleyRoutine(int shells)
    {
        for (int i = 0; i < shells; i++)
        {
            StartCoroutine(ShellRoutine(PickStrikePoint()));
            yield return new WaitForSeconds(Random.Range(0.5f, 1.1f));
        }
    }

    private Vector3 PickStrikePoint()
    {
        Vector3 center = player != null ? player.position : transform.position;

        Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(5f, 18f);
        Vector3 point = center + new Vector3(offset.x, 0f, offset.y);

        if (Physics.Raycast(point + Vector3.up * 40f, Vector3.down,
                            out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
        {
            point.y = hit.point.y;
        }
        else
        {
            point.y = center.y;
        }

        return point;
    }

    private IEnumerator ShellRoutine(Vector3 target)
    {
        if (whistleClip != null) AudioSource.PlayClipAtPoint(whistleClip, target, 0.85f);

        SpawnMarker(target);
        yield return new WaitForSeconds(1.5f);

        Explode(target);
    }

    private void Explode(Vector3 point)
    {
        if (blastClip != null) AudioSource.PlayClipAtPoint(blastClip, point, 1f);

        SpawnVisuals(point);

        HashSet<Health> hit = new HashSet<Health>();
        Collider[] colliders = Physics.OverlapSphere(point, blastRadius, ~0,
                                                     QueryTriggerInteraction.Ignore);

        for (int i = 0; i < colliders.Length; i++)
        {
            Health health = colliders[i].GetComponentInParent<Health>();
            if (health == null || health.IsDead || !hit.Add(health)) continue;

            float distance = Vector3.Distance(point, colliders[i].transform.position);
            float falloff = Mathf.Pow(1f - Mathf.Clamp01(distance / blastRadius), 1.3f);
            if (falloff <= 0f) continue;

            Vector3 normal = (colliders[i].transform.position - point).normalized;
            health.TakeDamage(blastDamage * falloff, colliders[i].transform.position,
                              normal, BodyZone.Torso);
        }

        // 离得越近震得越狠
        if (cameraShake != null && player != null)
        {
            float distance = Vector3.Distance(player.position, point);
            float strength = Mathf.Clamp01(1f - distance / 26f);
            if (strength > 0.02f) cameraShake.Shake(0.35f + strength * 0.6f);
        }
    }

    private void SpawnMarker(Vector3 point)
    {
        if (markerMaterial == null)
        {
            markerMaterial = LevelBuilder.MakeMaterial(
                new Color(0.5f, 0.08f, 0.05f), new Color(0.9f, 0.12f, 0.06f));
        }

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "ShellMarker";
        marker.transform.position = point + Vector3.up * 0.05f;
        marker.transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);
        Object.Destroy(marker.GetComponent<Collider>());

        Renderer renderer = marker.GetComponent<Renderer>();
        renderer.sharedMaterial = markerMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Object.Destroy(marker, 2.8f);
    }

    private void SpawnVisuals(Vector3 point)
    {
        if (flashMaterial == null)
        {
            flashMaterial = LevelBuilder.MakeMaterial(
                new Color(1f, 0.7f, 0.3f), new Color(2.2f, 1.1f, 0.3f));
            smokeMaterial = LevelBuilder.MakeTransparentMaterial(
                new Color(0.34f, 0.33f, 0.31f, 0.55f));
            debrisMaterial = LevelBuilder.MakeMaterial(new Color(0.22f, 0.2f, 0.17f));
            craterMaterial = LevelBuilder.MakeMaterial(new Color(0.14f, 0.12f, 0.1f));
        }

        GameObject lightGo = new GameObject("BlastLight");
        lightGo.transform.SetParent(levelRoot, true);
        lightGo.transform.position = point + Vector3.up * 1.2f;
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.66f, 0.32f);
        light.intensity = 5f;
        light.range = 16f;
        Object.Destroy(lightGo, 0.18f);

        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "BlastFlash";
        flash.transform.SetParent(levelRoot, true);
        flash.transform.position = point + Vector3.up * 0.6f;
        flash.transform.localScale = Vector3.one * 1.8f;
        Object.Destroy(flash.GetComponent<Collider>());
        flash.GetComponent<Renderer>().sharedMaterial = flashMaterial;
        Object.Destroy(flash, 0.16f);

        for (int i = 0; i < 12; i++)
        {
            GameObject debris = GameObject.CreatePrimitive(PrimitiveType.Cube);
            debris.name = "ShellDebris";
            debris.transform.SetParent(levelRoot, true);
            debris.transform.position = point + Random.insideUnitSphere * 0.5f;
            debris.transform.localScale = Vector3.one * Random.Range(0.08f, 0.26f);
            debris.transform.rotation = Random.rotation;
            Object.Destroy(debris.GetComponent<Collider>());
            debris.GetComponent<Renderer>().sharedMaterial = debrisMaterial;

            Rigidbody body = debris.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.velocity = Vector3.up * Random.Range(4f, 9f)
                            + Random.insideUnitSphere * Random.Range(2f, 6f);
            Object.Destroy(debris, Random.Range(1.4f, 2.6f));
        }

        for (int i = 0; i < 6; i++)
        {
            GameObject smoke = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            smoke.name = "ShellSmoke";
            smoke.transform.SetParent(levelRoot, true);
            smoke.transform.position = point
                + Vector3.up * Random.Range(0.4f, 1.8f)
                + Random.insideUnitSphere * 1.2f;
            smoke.transform.localScale = Vector3.one * Random.Range(0.9f, 1.6f);
            Object.Destroy(smoke.GetComponent<Collider>());
            Renderer renderer = smoke.GetComponent<Renderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(smoke, Random.Range(1.8f, 3.2f));
        }

        GameObject crater = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        crater.name = "Crater";
        crater.transform.SetParent(levelRoot, true);
        crater.transform.position = point + Vector3.up * 0.02f;
        crater.transform.localScale = new Vector3(3.2f, 0.03f, 3.2f);
        Object.Destroy(crater.GetComponent<Collider>());
        Renderer craterRenderer = crater.GetComponent<Renderer>();
        craterRenderer.sharedMaterial = craterMaterial;
        craterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(crater, 50f);
    }
}
