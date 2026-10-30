using UnityEngine;

/// <summary>
/// 跟随玩家的飘雪粒子。发射器挂在玩家上方，粒子在世界空间下落，
/// 玩家移动时旧雪留在原地、新雪在头顶补充，形成持续的雪幕。
/// </summary>
public static class Snowfall
{
    public static void Attach(Transform player)
    {
        if (player == null) return;

        GameObject go = new GameObject("Snowfall");
        go.transform.SetParent(player, false);
        go.transform.localPosition = new Vector3(0f, 10f, 0f);

        ParticleSystem system = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.95f, 0.97f, 1f, 0.9f),
            new Color(0.82f, 0.88f, 0.98f, 0.6f));
        main.maxParticles = 3200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.05f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 340f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(46f, 0.5f, 46f);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-1.1f, -0.35f);
        velocity.y = new ParticleSystem.MinMaxCurve(-2.3f, -1.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

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

        // 开局立即铺满雪幕，避免前几秒头顶空空
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.Play();
        system.Simulate(6f, true, false, true);

        go.AddComponent<SnowfallStorm>().Initialize(system);
    }
}

/// <summary>
/// 让雪幕跟着全图暴风雪一起变猛：雪量、雪片大小、横风都随 BlizzardLevel 上升。
/// </summary>
public class SnowfallStorm : MonoBehaviour
{
    private ParticleSystem system;
    private float intensity;

    public void Initialize(ParticleSystem particleSystem)
    {
        system = particleSystem;
    }

    private void Update()
    {
        if (system == null) return;

        float target = DynamicWeather.BlizzardLevel;
        intensity = Mathf.MoveTowards(intensity, target, Time.deltaTime * 0.35f);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = Mathf.Lerp(340f, 1500f, intensity);

        ParticleSystem.MainModule main = system.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            0.03f + 0.02f * intensity, 0.075f + 0.05f * intensity);

        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(
            -1.1f - 3.6f * intensity, -0.35f - 1.3f * intensity);
        velocity.y = new ParticleSystem.MinMaxCurve(
            -2.3f - 1.6f * intensity, -1.4f - 1.2f * intensity);

        ParticleSystem.ShapeModule shape = system.shape;
        shape.scale = new Vector3(46f + 22f * intensity, 0.5f, 46f + 22f * intensity);
    }
}
