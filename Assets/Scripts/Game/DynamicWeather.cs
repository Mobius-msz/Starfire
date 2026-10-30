using UnityEngine;

/// <summary>
/// 动态天气与昼夜：晴天 / 大雾 / 冻雨快速轮换，太阳与天空随昼夜一起走。
///
/// 大雾：能见度下降，敌人的视距被压缩，适合潜行。
/// 冻雨：雨声盖住脚步，敌人听觉范围缩小，但体温流失更快、枪械更容易哑火。
/// </summary>
public class DynamicWeather : MonoBehaviour
{
    public enum Kind
    {
        Clear,
        Fog,
        Rain
    }

    public static DynamicWeather Active { get; private set; }

    /// <summary>敌人视距倍率，雾天压低。</summary>
    public static float EnemyVisionFactor => Active != null ? Active.visionFactor : 1f;

    /// <summary>敌人对脚步 / 枪声的听觉倍率，雨声会盖住。</summary>
    public static float HearingFactor => Active != null ? Active.hearingFactor : 1f;

    /// <summary>0 = 正常，1 = 冻雨中；ColdSystem 用它加速体温流失。</summary>
    public static float ColdSeverity => Active != null ? Active.rainBlend : 0f;

    /// <summary>枪械可靠度，冻雨里偶尔哑火。</summary>
    public static float FirearmReliability => Active != null ? Active.reliability : 1f;

    /// <summary>全图暴风雪强度，0 → 1 在开局几秒内拉满。雪花密度和敌人视距都读它。</summary>
    public static float BlizzardLevel => Active != null ? Active.stormBlend : 0f;

    private HUDController hud;
    private Transform player;
    private Light sun;
    private Light fill;
    private Material skybox;
    private ChishuiWeather storm;

    private float baseFogDensity;
    private float baseSkyExposure = 0.55f;

    // 昼夜
    private float dayPhase = 0.4f;      // 0.25 = 6:00，0.5 = 12:00
    private float dayLength = 210f;     // 加快：3.5 分钟一整天
    private float lightFactor = 1f;

    // 天气
    private Kind kind = Kind.Clear;
    private float stateTimer = 14f;
    private float holdMin = 18f;
    private float holdMax = 28f;
    private float stateElapsed;
    private bool locked;
    private float fogBlend;
    private float rainBlend;
    private float visionFactor = 1f;
    private float hearingFactor = 1f;
    private float reliability = 1f;
    private float stormBlend;

    private ParticleSystem rain;
    private AudioSource rainAudio;
    private float rainEmission;
    private float weatherClock;

    // 结算演出：锁定正午
    private bool daylight;

    /// <summary>
    /// 结算动画切到白天：时间拨到正午，收起雨雪和风雪，
    /// 雾也放淡，让远处江面和山影亮起来。
    /// </summary>
    public void SetDaylight()
    {
        daylight = true;
        dayPhase = 0.5f;
        locked = true;
        kind = Kind.Clear;
        fogBlend = 0f;
        rainBlend = 0f;
        stormBlend = 0f;
        visionFactor = 1f;
        hearingFactor = 1f;
        baseFogDensity = 0.0025f;

        if (storm != null) storm.enabled = false;

        if (rain != null)
        {
            ParticleSystem.EmissionModule emission = rain.emission;
            emission.rateOverTime = 0f;
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (rainAudio != null) rainAudio.Stop();

        Apply(0f);
    }

    public static void Attach(Transform playerRef, HUDController hudRef,
                              float fogDensity, int forcedWeather, float forcedPhase)
    {
        GameObject go = new GameObject("DynamicWeather");
        DynamicWeather weather = go.AddComponent<DynamicWeather>();
        weather.Initialize(playerRef, hudRef, fogDensity, forcedWeather, forcedPhase);
    }

    public void Initialize(Transform playerRef, HUDController hudRef,
                           float fogDensity, int forcedWeather, float forcedPhase)
    {
        Active = this;
        player = playerRef;
        hud = hudRef;
        baseFogDensity = fogDensity;
        storm = FindObjectOfType<ChishuiWeather>();

        sun = RenderSettings.sun;
        GameObject fillGo = GameObject.Find("FillLight");
        if (fillGo != null) fill = fillGo.GetComponent<Light>();
        skybox = RenderSettings.skybox;
        if (skybox != null && skybox.HasProperty("_Exposure"))
        {
            baseSkyExposure = skybox.GetFloat("_Exposure");
        }

        dayPhase = 0.4f;
        if (forcedPhase >= 0f) dayPhase = Mathf.Repeat(forcedPhase, 1f);

        if (forcedWeather == 1) SetWeather(Kind.Fog, true);
        else if (forcedWeather == 2) SetWeather(Kind.Rain, true);
        else if (forcedWeather == 0) SetWeather(Kind.Clear, true);
        else SetWeather(Kind.Clear, true);

        BuildRain();
        Apply(0f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (daylight)
        {
            dayPhase = 0.5f;
            Apply(dt);
            return;
        }

        dayPhase = Mathf.Repeat(dayPhase + dt / dayLength, 1f);

        if (!locked)
        {
            stateTimer -= dt;
            if (stateTimer <= 0f) AdvanceWeather();
        }

        stateElapsed += dt;

        float fogTarget = kind == Kind.Fog ? 1f : 0f;
        float rainTarget = kind == Kind.Rain ? 1f : 0f;
        float blendSpeed = 1f / 3.5f; // 快速切换：3.5 秒过渡完
        fogBlend = Mathf.MoveTowards(fogBlend, fogTarget, dt * blendSpeed);
        rainBlend = Mathf.MoveTowards(rainBlend, rainTarget, dt * blendSpeed);
        stormBlend = Mathf.MoveTowards(stormBlend, 1f, dt * 0.22f);

        // 风雪里敌人同样看不清：视距、听觉一起压，枪也打得更飘
        visionFactor = Mathf.Clamp(
            1f - fogBlend * 0.58f - rainBlend * 0.22f - stormBlend * 0.22f, 0.28f, 1f);
        hearingFactor = Mathf.Clamp(
            1f - rainBlend * 0.62f - fogBlend * 0.12f - stormBlend * 0.2f, 0.22f, 1f);
        reliability = Mathf.Lerp(1f, 0.87f, rainBlend);

        Apply(dt);
    }

    private void AdvanceWeather()
    {
        // 晴 → 雾 → 雨 → 晴，保证两种天气都能很快轮到
        if (kind == Kind.Clear) SetWeather(Kind.Fog, false);
        else if (kind == Kind.Fog) SetWeather(Kind.Rain, false);
        else SetWeather(Kind.Clear, false);

        stateTimer = Random.Range(holdMin, holdMax);
    }

    private void SetWeather(Kind next, bool force)
    {
        kind = next;
        stateElapsed = 0f;
        if (force)
        {
            locked = true;
            stateTimer = 9999f;
            fogBlend = next == Kind.Fog ? 1f : (next == Kind.Rain ? 0.25f : 0f);
            rainBlend = next == Kind.Rain ? 1f : 0f;
            return;
        }

        if (hud == null) return;

        if (next == Kind.Fog)
        {
            hud.ShowMessage("大雾漫过河谷，敌人在雾里看不清你", 3.2f);
        }
        else if (next == Kind.Rain)
        {
            hud.ShowMessage("冻雨落下：雨声盖住脚步，但体温和枪械都受考验", 3.4f);
        }
        else
        {
            hud.ShowMessage("云开雾散，能见度恢复", 2.6f);
        }
    }

    private void Apply(float dt)
    {
        float solar = Mathf.Sin((dayPhase - 0.25f) * Mathf.PI * 2f);
        float height = Mathf.Clamp01(solar * 1.3f);
        lightFactor = Mathf.SmoothStep(0f, 1f, height);

        // ── 昼夜光照 ──
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(
                Mathf.Lerp(-10f, 68f, lightFactor), 30f + dayPhase * 360f, 0f);
            sun.intensity = Mathf.Lerp(0.02f, 1.05f, lightFactor) * (1f - rainBlend * 0.45f);
            float horizon = Mathf.Clamp01(1f - Mathf.Abs(solar) * 3.5f);
            sun.color = Color.Lerp(new Color(1f, 0.93f, 0.82f),
                new Color(1f, 0.56f, 0.32f), horizon * 0.85f);
        }

        if (fill != null)
        {
            fill.intensity = Mathf.Lerp(0.05f, 0.28f, lightFactor) * (1f - rainBlend * 0.3f);
        }

        Color daySky = new Color(0.34f, 0.42f, 0.52f);
        Color nightSky = new Color(0.035f, 0.05f, 0.09f);
        RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, lightFactor);
        RenderSettings.ambientEquatorColor = Color.Lerp(
            new Color(0.02f, 0.025f, 0.045f), new Color(0.22f, 0.25f, 0.28f), lightFactor);
        RenderSettings.ambientGroundColor = Color.Lerp(
            new Color(0.008f, 0.01f, 0.015f), new Color(0.08f, 0.09f, 0.08f), lightFactor);

        if (daylight)
        {
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.52f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.34f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.12f);
        }

        if (skybox != null)
        {
            if (skybox.HasProperty("_Exposure"))
            {
                skybox.SetFloat("_Exposure",
                    Mathf.Lerp(0.04f, baseSkyExposure, lightFactor) * (1f - rainBlend * 0.4f));
            }
            if (skybox.HasProperty("_SkyTint"))
            {
                skybox.SetColor("_SkyTint", Color.Lerp(
                    new Color(0.05f, 0.08f, 0.16f), new Color(0.32f, 0.42f, 0.56f), lightFactor));
            }
        }

        // ── 雾 ──
        // 全图暴风雪：开局的雪幕越来越猛，远近的雾都一并变白
        float localStorm = storm != null ? storm.StormLevel : 0f;
        float stormLevel = daylight ? 0f : Mathf.Max(stormBlend, localStorm);
        float density = baseFogDensity
                        + fogBlend * 0.034f
                        + rainBlend * 0.011f
                        + stormLevel * 0.018f;
        RenderSettings.fogDensity = density;

        Color nightFog = new Color(0.02f, 0.03f, 0.05f);
        Color dayFog = new Color(0.4f, 0.46f, 0.54f);
        Color fogColor = Color.Lerp(nightFog, dayFog, lightFactor);
        fogColor = Color.Lerp(fogColor, new Color(0.68f, 0.71f, 0.74f),
            fogBlend * 0.75f * Mathf.Max(0.25f, lightFactor));
        fogColor = Color.Lerp(fogColor, new Color(0.3f, 0.33f, 0.38f),
            rainBlend * 0.55f * Mathf.Max(0.2f, lightFactor));
        fogColor = Color.Lerp(fogColor, new Color(0.72f, 0.76f, 0.8f),
            stormLevel * 0.6f * Mathf.Max(0.3f, lightFactor));

        if (daylight) fogColor = new Color(0.6f, 0.68f, 0.76f);

        RenderSettings.fogColor = fogColor;

        // ── 雨 ──
        if (rain != null)
        {
            ParticleSystem.EmissionModule emission = rain.emission;
            rainEmission = Mathf.MoveTowards(rainEmission, rainBlend * 1400f, dt * 2600f);
            emission.rateOverTime = rainEmission;
        }

        if (rainAudio != null)
        {
            rainAudio.volume = Mathf.MoveTowards(rainAudio.volume, rainBlend * 0.5f, dt * 0.8f);
            if (rainAudio.volume > 0.01f && !rainAudio.isPlaying) rainAudio.Play();
        }

        weatherClock -= dt;
        if (weatherClock <= 0f && hud != null)
        {
            weatherClock = 0.5f;
            int totalMinutes = Mathf.FloorToInt(dayPhase * 24f * 60f);
            string label = kind == Kind.Fog ? "大雾" : (kind == Kind.Rain ? "冻雨" : "阴");
            hud.SetWeatherText(label + " · "
                + (totalMinutes / 60 % 24).ToString("00") + ":"
                + (totalMinutes % 60).ToString("00")
                + "　　能见度 " + Mathf.RoundToInt(EnemyVisionFactor * 100f) + "%");
        }
    }

    private void BuildRain()
    {
        GameObject go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 12f, 0f);

        rain = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = rain.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.028f);
        main.startColor = new Color(0.72f, 0.8f, 0.88f, 0.55f);
        main.maxParticles = 3200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = rain.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(34f, 0.4f, 34f);

        ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-2.2f, -1.2f);
        velocity.y = new ParticleSystem.MinMaxCurve(-26f, -21f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.055f;
        renderer.lengthScale = 3.2f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new Material(shader);
            material.mainTexture = Texture2D.whiteTexture;
            material.renderQueue = 3000;
            renderer.sharedMaterial = material;
        }

        rain.Play();

        // 雨声：低频噪声铺底，跟着玩家走，不需要 3D 定位
        AudioClip clip = ProceduralAudio.Rain();
        if (clip != null)
        {
            GameObject audioGo = new GameObject("RainAudio");
            audioGo.transform.SetParent(transform, false);
            rainAudio = audioGo.AddComponent<AudioSource>();
            rainAudio.clip = clip;
            rainAudio.loop = true;
            rainAudio.spatialBlend = 0f;
            rainAudio.volume = 0f;
            rainAudio.playOnAwake = false;
        }
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        RenderSettings.fogDensity = baseFogDensity;
        RenderSettings.fogColor = new Color(0.4f, 0.46f, 0.54f);
    }
}
