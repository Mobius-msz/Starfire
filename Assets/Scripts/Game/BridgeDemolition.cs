using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 断桥演出：桥头炸药起爆后，一串爆点沿桥面推向对岸，
/// 浮桥按段断开、倾斜、沉进江里。
///
/// 火球、闪光、烟柱、水柱和碎木都自己算，不依赖粒子系统或美术资源；
/// 桥上所有构件在起爆瞬间按 z 分成若干段，各自独立下沉。
/// </summary>
public class BridgeDemolition : MonoBehaviour
{
    private const int SectionCount = 7;
    private const float BridgeMinZ = 15.9f;
    private const float BridgeMaxZ = 44.3f;
    private const int Charges = 12;
    private const float FirstChargeDelay = 0.9f;
    private const float ChargeGap = 0.14f;
    private const float SectionDuration = 3.4f;
    private const float SurfaceY = -0.07f;

    private class Section
    {
        public Transform T;
        public Vector3 StartPos;
        public Quaternion StartRot;
        public float Delay;
        public float Drop;
        public float Tilt;
        public float Roll;
        public float Drift;
        public float Age;
        public bool Broken;
    }

    private class Particle
    {
        public Transform T;
        public Material Mat;
        public Vector3 Velocity;
        public Vector3 Spin;
        public Vector3 ScaleFrom;
        public Vector3 ScaleTo;
        public Color Color;
        public float Gravity;
        public float Age;
        public float Life;
        public bool Fade;
    }

    private readonly List<Section> sections = new List<Section>();
    private readonly List<Particle> particles = new List<Particle>();

    private CameraShake shake;
    private AudioSource audioSource;
    private AudioClip boomClip;

    private float age;
    private int chargesFired;
    private float[] chargeTimes;
    private Vector3[] chargePoints;
    private bool running;

    /// <summary>
    /// 起爆。bridgeRoot 是第一关浮桥的根节点，eye 只用来判断镜头远近做震动衰减。
    /// </summary>
    public static BridgeDemolition Play(Transform bridgeRoot, Transform eye,
                                        CameraShake cameraShake, AudioSource source, AudioClip boom)
    {
        GameObject go = new GameObject("BridgeDemolition");
        BridgeDemolition demo = go.AddComponent<BridgeDemolition>();
        demo.shake = cameraShake;
        demo.audioSource = source;
        demo.boomClip = boom;
        demo.Begin(bridgeRoot);
        return demo;
    }

    private void Begin(Transform bridgeRoot)
    {
        BuildSections(bridgeRoot);

        chargeTimes = new float[Charges];
        chargePoints = new Vector3[Charges];

        for (int i = 0; i < Charges; i++)
        {
            float t = i / (float)(Charges - 1);
            float z = Mathf.Lerp(17.2f, 43.2f, t);

            chargeTimes[i] = FirstChargeDelay + i * ChargeGap;
            chargePoints[i] = new Vector3(Random.Range(-1.7f, 1.7f), 0.5f, z);
        }

        // 每段的下沉比爆点晚一点，像是先炸开、再塌下去
        for (int i = 0; i < sections.Count; i++)
        {
            Section s = sections[i];
            float t = i / Mathf.Max(1f, sections.Count - 1f);
            s.Delay = FirstChargeDelay + 0.25f + t * 2.6f;
            s.Drop = Mathf.Lerp(0.85f, 1.7f, t);
            s.Tilt = Random.Range(16f, 30f) * (i % 2 == 0 ? 1f : -1f);
            s.Roll = Random.Range(5f, 13f);
            s.Drift = Random.Range(-1.6f, 1.6f);
        }

        // 起爆前先冒火星，给玩家一个"要响了"的提前量
        SparkFuse(chargePoints[0]);

        // 桥上残余的敌军掉头往对岸跑，跑不掉的会被气浪掀起来
        EnemyAI.RoutBridge(new Vector3(0f, 0f, BridgeMaxZ + 3.2f));

        // 桥头那一炮最响
        Boom(chargePoints[0], 1.4f);
        EnemyAI.BlastBridgeEnemies(chargePoints[0], 5.5f);
        chargesFired = 1;
        running = true;
    }

    private void BuildSections(Transform bridgeRoot)
    {
        if (bridgeRoot == null) return;

        Transform[] children = new Transform[bridgeRoot.childCount];
        for (int i = 0; i < children.Length; i++) children[i] = bridgeRoot.GetChild(i);
        if (children.Length == 0) return;

        int[] bucket = new int[children.Length];
        Vector3[] sum = new Vector3[SectionCount];
        int[] count = new int[SectionCount];

        for (int i = 0; i < children.Length; i++)
        {
            float z = children[i].position.z;
            float t = Mathf.InverseLerp(BridgeMinZ, BridgeMaxZ, z);
            int index = Mathf.Clamp(Mathf.FloorToInt(t * SectionCount), 0, SectionCount - 1);

            bucket[i] = index;
            sum[index] += children[i].position;
            count[index]++;
        }

        for (int i = 0; i < SectionCount; i++)
        {
            GameObject go = new GameObject("BridgeSection_" + i);
            go.transform.SetParent(bridgeRoot, false);

            // 段节点挪到这一段的重心，断开时就绕自己的重心翻沉，
            // 不会因为桥长 30 米而把远端甩出去一大截
            Vector3 center = count[i] > 0
                ? sum[i] / count[i]
                : new Vector3(0f, 0.25f, Mathf.Lerp(BridgeMinZ, BridgeMaxZ, (i + 0.5f) / SectionCount));
            go.transform.position = center;

            sections.Add(new Section { T = go.transform });
        }

        for (int i = 0; i < children.Length; i++)
        {
            children[i].SetParent(sections[bucket[i]].T, true);
        }
    }

    private void Update()
    {
        if (!running) return;

        float dt = Time.deltaTime;
        age += dt;

        // ── 爆点连锁 ──
        while (chargesFired < Charges && age >= chargeTimes[chargesFired])
        {
            Boom(chargePoints[chargesFired], 1f);
            // 冲击波扫过：这一段桥面上的敌兵被掀起，抛物线落进江里
            EnemyAI.BlastBridgeEnemies(chargePoints[chargesFired], 6.5f);
            FireChargeSound(chargesFired);
            chargesFired++;
        }

        // ── 桥段下沉 ──
        for (int i = 0; i < sections.Count; i++)
        {
            Section s = sections[i];
            if (s.Age <= 0f && age < s.Delay) continue;

            if (!s.Broken)
            {
                s.Broken = true;
                s.StartPos = s.T.localPosition;
                s.StartRot = s.T.localRotation;
                DetachDebris(s);
            }

            s.Age += dt;

            float p = Mathf.Clamp01(s.Age / SectionDuration);

            // 先翻转，后下沉：桥板先翘起来、断开，再慢慢没进江里
            float tiltP = Mathf.Clamp01(p / 0.55f);
            tiltP = 1f - Mathf.Pow(1f - tiltP, 2f);
            float sinkP = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 0.35f) / 0.65f));

            s.T.localPosition = s.StartPos
                                + new Vector3(s.Drift * sinkP, -s.Drop * sinkP, 0f);
            s.T.localRotation = s.StartRot * Quaternion.Euler(s.Tilt * tiltP, 0f, s.Roll * tiltP);

            // 沉到水面以下后轻轻晃两下，像浮箱还在水里打转
            if (p >= 1f)
            {
                float bob = Mathf.Sin((s.Age - SectionDuration) * 2.1f + i) * 0.035f;
                s.T.localPosition += new Vector3(0f, bob * dt * 6f, 0f);
            }
        }

        UpdateParticles(dt);

        if (age > 9f) running = false;
    }

    private void FireChargeSound(int index)
    {
        if (audioSource == null || boomClip == null) return;
        float volume = index % 3 == 1 ? 0.55f : 0.8f;
        audioSource.PlayOneShot(boomClip, volume);
    }

    // ───────────────────────────── 爆炸 ─────────────────────────────

    private void Boom(Vector3 point, float scale)
    {
        // 闪光 + 点光源
        GameObject lightGo = new GameObject("BlastLight");
        lightGo.transform.SetParent(transform, true);
        lightGo.transform.position = point + Vector3.up * 1.4f;
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.64f, 0.3f);
        light.intensity = 7f * scale;
        light.range = 20f * scale;
        Destroy(lightGo, 0.3f);

        SpawnGlow(point + Vector3.up * 0.35f, 1.1f * scale, 3.4f * scale,
            new Color(1f, 0.72f, 0.36f), 0.2f);

        // 火球
        SpawnGlow(point + Vector3.up * 0.25f, 0.7f * scale, 2.3f * scale,
            new Color(0.98f, 0.5f, 0.14f), 0.42f);

        // 烟：分成一小团一小团往上翻
        int puffs = Mathf.RoundToInt(9 * scale);
        for (int i = 0; i < puffs; i++)
        {
            Vector3 at = point + new Vector3(Random.Range(-1.3f, 1.3f) * scale,
                                             Random.Range(0.2f, 1.4f),
                                             Random.Range(-1.3f, 1.3f) * scale);
            float life = Random.Range(1.9f, 3.6f);
            SpawnPuff(at, Random.Range(0.6f, 1.1f) * scale, Random.Range(1.7f, 2.9f) * scale,
                new Color(0.2f, 0.19f, 0.17f, 0.66f), life,
                new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(1.1f, 2.4f),
                            Random.Range(-0.5f, 0.5f)));
        }

        // 火星与碎木
        int bits = Mathf.RoundToInt(10 * scale);
        for (int i = 0; i < bits; i++)
        {
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.35f, 1.5f),
                                      Random.Range(-1f, 1f)).normalized;
            SpawnDebris(point + Vector3.up * 0.3f, dir * Random.Range(4f, 11f),
                Random.Range(0.08f, 0.26f), new Color(0.3f, 0.2f, 0.11f),
                Random.Range(2.2f, 3.6f));
        }

        // 桥上炸点落在水上：水柱和泡沫圈
        if (point.z > 18.5f)
        {
            SpawnSplash(point, scale);
        }

        if (shake != null) shake.Shake(Mathf.Clamp01(0.55f * scale));
    }

    private void SpawnSplash(Vector3 point, float scale)
    {
        for (int i = 0; i < 7; i++)
        {
            Vector3 at = point + new Vector3(Random.Range(-1.2f, 1.2f), 0.2f,
                                             Random.Range(-1.2f, 1.2f));
            SpawnPuff(at, Random.Range(0.35f, 0.7f) * scale, Random.Range(1.1f, 2.1f) * scale,
                new Color(0.82f, 0.89f, 0.9f, 0.7f), Random.Range(0.7f, 1.25f),
                new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(3.4f, 6.8f) * scale,
                            Random.Range(-1.5f, 1.5f)), 5.5f);
        }

        // 水面上的白沫环
        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "SplashRing";
        ring.transform.SetParent(transform, true);
        ring.transform.position = new Vector3(point.x, SurfaceY + 0.02f, point.z);
        Destroy(ring.GetComponent<Collider>());

        Material mat = LevelBuilder.MakeTransparentMaterial(new Color(0.86f, 0.9f, 0.9f, 0.6f));
        ring.GetComponent<Renderer>().sharedMaterial = mat;

        particles.Add(new Particle
        {
            T = ring.transform,
            Mat = mat,
            ScaleFrom = new Vector3(1.2f, 0.02f, 1.2f) * scale,
            ScaleTo = new Vector3(5.4f, 0.02f, 5.4f) * scale,
            Color = new Color(0.86f, 0.9f, 0.9f, 0.6f),
            Life = 0.9f,
            Fade = true
        });
    }

    /// <summary>起爆前的火星引信。</summary>
    private void SparkFuse(Vector3 point)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.5f, 1.8f),
                                      Random.Range(-1f, 1f)).normalized;
            SpawnDebris(point + Vector3.up * 0.4f, dir * Random.Range(1.5f, 4.5f),
                Random.Range(0.03f, 0.07f), new Color(1f, 0.74f, 0.32f),
                Random.Range(0.5f, 0.9f), true);
        }
    }

    private void SpawnGlow(Vector3 position, float from, float to, Color color, float life)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BlastGlow";
        go.transform.SetParent(transform, true);
        go.transform.position = position;
        Destroy(go.GetComponent<Collider>());

        Material mat = LevelBuilder.MakeMaterial(color, color);
        go.GetComponent<Renderer>().sharedMaterial = mat;

        particles.Add(new Particle
        {
            T = go.transform,
            Mat = mat,
            ScaleFrom = Vector3.one * from,
            ScaleTo = Vector3.one * to,
            Color = new Color(color.r, color.g, color.b, 1f),
            Life = life
        });
    }

    private void SpawnPuff(Vector3 position, float from, float to, Color color, float life,
                           Vector3 velocity, float gravity = 0f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BlastSmoke";
        go.transform.SetParent(transform, true);
        go.transform.position = position;
        Destroy(go.GetComponent<Collider>());

        Material mat = LevelBuilder.MakeTransparentMaterial(color);
        go.GetComponent<Renderer>().sharedMaterial = mat;

        particles.Add(new Particle
        {
            T = go.transform,
            Mat = mat,
            Velocity = velocity,
            Spin = new Vector3(0f, Random.Range(-30f, 30f), 0f),
            ScaleFrom = Vector3.one * from,
            ScaleTo = Vector3.one * to,
            Color = color,
            Gravity = gravity,
            Life = life,
            Fade = true
        });
    }

    private void SpawnDebris(Vector3 position, Vector3 velocity, float size, Color color,
                             float life, bool glow = false)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "BridgeDebris";
        go.transform.SetParent(transform, true);
        go.transform.position = position;
        go.transform.localScale = new Vector3(size, size * Random.Range(0.45f, 1f),
                                              size * Random.Range(0.4f, 1.3f));
        go.transform.rotation = Random.rotation;
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = glow
            ? LevelBuilder.MakeMaterial(color, color)
            : LevelBuilder.MakeMaterial(color);

        particles.Add(new Particle
        {
            T = go.transform,
            Velocity = velocity,
            Spin = new Vector3(Random.Range(-360f, 360f), Random.Range(-360f, 360f),
                               Random.Range(-360f, 360f)),
            ScaleFrom = go.transform.localScale,
            ScaleTo = go.transform.localScale,
            Color = color,
            Gravity = 13f,
            Life = life
        });
    }

    /// <summary>断开瞬间从桥段里崩几块木头出来。</summary>
    private void DetachDebris(Section s)
    {
        Vector3 center = s.T.position;

        for (int i = 0; i < 4; i++)
        {
            Vector3 at = center + new Vector3(Random.Range(-2.4f, 2.4f),
                                              Random.Range(-0.2f, 0.6f),
                                              Random.Range(-1.6f, 1.6f));
            Vector3 velocity = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(2f, 5.5f),
                                           Random.Range(-2.5f, 2.5f));
            SpawnDebris(at, velocity, Random.Range(0.16f, 0.42f),
                new Color(0.32f, 0.22f, 0.12f), Random.Range(2.4f, 3.8f));
        }

        // 桥段落水，掀起一大片水花
        if (center.z > 18.5f) SpawnSplash(center, 1.5f);
    }

    private void UpdateParticles(float dt)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.Age += dt;

            if (p.Age >= p.Life || p.T == null)
            {
                if (p.T != null) Destroy(p.T.gameObject);
                particles.RemoveAt(i);
                continue;
            }

            float t = p.Age / p.Life;

            if (p.Velocity != Vector3.zero || p.Gravity > 0f)
            {
                p.Velocity.y -= p.Gravity * dt;
                p.T.position += p.Velocity * dt;
                p.T.Rotate(p.Spin * dt);

                // 碎木掉进江里就收掉
                if (p.T.position.y < SurfaceY - 0.4f && p.Gravity > 0f)
                {
                    Destroy(p.T.gameObject);
                    particles.RemoveAt(i);
                    continue;
                }
            }

            if (p.ScaleFrom != p.ScaleTo)
            {
                float grow = 1f - Mathf.Pow(1f - t, 2f);
                p.T.localScale = Vector3.Lerp(p.ScaleFrom, p.ScaleTo, grow);
            }

            if (p.Fade && p.Mat != null && p.Mat.HasProperty("_Color"))
            {
                float alpha = p.Color.a * Mathf.Clamp01(1f - t * t);
                p.Mat.color = new Color(p.Color.r, p.Color.g, p.Color.b, alpha);
            }
        }
    }
}
