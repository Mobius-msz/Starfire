using UnityEngine;

/// <summary>
/// 程序化合成音效。不依赖任何音频文件，保证空项目可运行。
/// 枪声、拉栓、空仓、装填、挥刀、风声。
/// </summary>
public static class ProceduralAudio
{
    private const int Rate = 44100;

    /// <summary>
    /// 步枪枪声（拉栓枪取向），以"啪"为主：
    ///   1. 高频脆裂只留枪口点火的一瞬，压得很轻，不再刺耳；
    ///   2. 中频爆音是主体，衰减放慢一点，听感是厚实的"啪"；
    ///   3. 低频冲击压低、拉长一点，给胸口一下闷响；
    ///   4. 去掉金属余振，整段再走一遍低通，把毛刺磨掉。
    /// 回声只挂近处一次硬反射和远处一次软反射，尾巴利落。
    /// </summary>
    public static AudioClip Gunshot()
    {
        int n = (int)(Rate * 0.52f);
        float[] dry = new float[n];
        float[] high = new float[n];
        System.Random rng = new System.Random(101);

        float lpMid = 0f;
        float lpLow = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

            // 两阶低通，拆出高 / 中 / 低三个频段
            lpMid += (noise - lpMid) * 0.5f;
            lpLow += (noise - lpLow) * 0.05f;
            float bandHigh = noise - lpMid;
            float bandMid = lpMid - lpLow;

            // 起音的那一下脆声只占很小比重，其余交给闷响
            float crack = bandHigh * Mathf.Exp(-t * 120f) * 0.45f;
            float body = bandMid * Mathf.Exp(-t * 24f) * 1.05f;
            float rumble = lpLow * Mathf.Exp(-t * 10f) * 0.55f;

            // 低频闷响：比旧版更低、收得稍慢，"啪啪"的胸腔感更足
            float freq = Mathf.Lerp(118f, 72f, Mathf.Clamp01(t / 0.12f));
            float thump = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 20f) * 0.55f;

            dry[i] = crack + body + rumble + thump;
            high[i] = crack + body * 0.5f;
        }

        // 整段再低通一次，把高频毛刺磨掉，听感偏闷
        float lp = 0f;
        float[] d = new float[n];
        int delayA = (int)(Rate * 0.06f);
        int delayB = (int)(Rate * 0.15f);
        int delayC = (int)(Rate * 0.3f);
        for (int i = 0; i < n; i++)
        {
            float v = dry[i];
            if (i >= delayA) v += dry[i - delayA] * 0.18f;
            if (i >= delayB) v += high[i - delayB] * 0.09f;
            if (i >= delayC) v += high[i - delayC] * 0.04f;

            lp += ((float)System.Math.Tanh(v * 1.35f) - lp) * 0.3f;
            d[i] = lp * 1.1f;
        }

        return Make("Gunshot", d, 0.98f);
    }

    /// <summary>手枪：比步枪短、亮一点，但同样是闷响为主的"啪"。</summary>
    public static AudioClip PistolShot()
    {
        int n = (int)(Rate * 0.4f);
        float[] d = new float[n];
        System.Random rng = new System.Random(202);

        float lpMid = 0f;
        float lpLow = 0f;
        float lp = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

            lpMid += (noise - lpMid) * 0.5f;
            lpLow += (noise - lpLow) * 0.1f;

            float crack = (noise - lpMid) * Mathf.Exp(-t * 120f) * 0.55f;
            float body = (lpMid - lpLow) * Mathf.Exp(-t * 34f) * 1f;
            float thump = Mathf.Sin(2f * Mathf.PI * 128f * t) * Mathf.Exp(-t * 22f) * 0.55f;

            float v = crack + body + thump;
            if (i >= (int)(Rate * 0.05f)) v += d[i - (int)(Rate * 0.05f)] * 0.16f;

            lp += (Mathf.Clamp(v, -1f, 1f) - lp) * 0.34f;
            d[i] = lp * 1.05f;
        }

        return Make("PistolShot", d, 0.9f);
    }

    /// <summary>处决：刀风 + 一记很沉的入体声，尾音拖一点金属余响。</summary>
    public static AudioClip Execution()
    {
        int n = (int)(Rate * 1.05f);
        float[] d = new float[n];
        System.Random rng = new System.Random(909);
        float prev = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);

            // 前 0.18 秒：刀刃破风的"唰"
            prev = Mathf.Lerp(prev, raw, 0.09f);
            float wind = prev * Mathf.Sin(Mathf.Clamp01(t / 0.18f) * Mathf.PI) * 1.6f;

            float value = wind;

            if (t > 0.16f)
            {
                float u = t - 0.16f;
                float body = prev * Mathf.Exp(-u * 26f) * 2.2f;
                float thump = Mathf.Sin(2f * Mathf.PI * 78f * u) * Mathf.Exp(-u * 13f) * 1.1f;
                float wet = Mathf.Sin(2f * Mathf.PI * 210f * u) * Mathf.Exp(-u * 32f) * 0.35f;
                value += body + thump + wet;
            }

            // 余响：一声很低的金属颤鸣，像刀从骨头缝里拔出来
            if (t > 0.42f)
            {
                float u = t - 0.42f;
                value += Mathf.Sin(2f * Mathf.PI * 460f * u) * Mathf.Exp(-u * 9f) * 0.16f;
            }

            d[i] = (float)System.Math.Tanh(value * 1.2f) * 0.9f;
        }

        return Make("Execution", d, 0.85f);
    }

    /// <summary>重击挥刀：比普通挥砍更低、更长的风声。</summary>
    public static AudioClip HeavySwing()
    {
        int n = (int)(Rate * 0.5f);
        float[] d = new float[n];
        System.Random rng = new System.Random(606);
        float prev = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            prev = Mathf.Lerp(prev, raw, 0.05f);

            float ramp = Mathf.Clamp01(t / 0.16f);
            float env = ramp * Mathf.Exp(-Mathf.Max(0f, t - 0.16f) * 9f);
            d[i] = prev * env * 1.25f;
        }

        return Make("HeavySwing", d, 0.55f);
    }

    /// <summary>炮弹下落：由高到低的尖啸，给玩家 1.5 秒反应时间。</summary>
    public static AudioClip ShellWhistle()
    {
        int n = (int)(Rate * 1.5f);
        float[] d = new float[n];

        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float k = Mathf.Clamp01(t / 1.5f);
            float freq = Mathf.Lerp(1500f, 380f, k * k);
            phase += 2f * Mathf.PI * freq / Rate;

            float fade = Mathf.SmoothStep(0f, 1f, k * 5f) * (1f - k * 0.35f);
            float flutter = 0.82f + 0.18f * Mathf.Sin(t * 42f);

            d[i] = Mathf.Sin(phase) * fade * flutter * 0.5f;
        }

        return Make("ShellWhistle", d, 0.8f);
    }

    /// <summary>赤水河的水声：低频水声 + 快速流动的拍岸噪声，可无缝循环。</summary>
    public static AudioClip RiverFlow()
    {
        if (riverCache != null) return riverCache;

        int n = (int)(Rate * 6f);
        float[] d = new float[n];
        System.Random rng = new System.Random(88);

        float lp1 = 0f;
        float lp2 = 0f;
        float bubblePhase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

            lp1 += (noise - lp1) * 0.09f;
            lp2 += (lp1 - lp2) * 0.02f;

            // 水面拍击：疏密变化的细碎高频
            float chop = (noise - lp1) * (0.35f + 0.25f * Mathf.Sin(t * 2.1f));

            // 偶发的气泡声
            float bubbleFreq = 180f + 70f * Mathf.Sin(t * 3.7f);
            bubblePhase += 2f * Mathf.PI * bubbleFreq / Rate;
            float bubble = Mathf.Sin(bubblePhase) * 0.05f
                           * Mathf.Clamp01(Mathf.Sin(t * 1.7f) + 0.4f);

            d[i] = lp1 * 0.55f + lp2 * 0.85f + chop * 0.22f + bubble;
        }

        // 把尾巴叠回开头，循环时不会"咔"一下
        int fade = (int)(Rate * 0.6f);
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            d[i] = d[i] * k + d[n - fade + i] * (1f - k);
        }
        System.Array.Resize(ref d, n - fade);

        riverCache = Make("RiverFlow", d, 0.7f);
        return riverCache;
    }

    private static AudioClip riverCache;

    /// <summary>手榴弹爆炸：尖锐的起爆 + 低频冲击 + 碎石落地。</summary>
    public static AudioClip Explosion()
    {
        int n = (int)(Rate * 1.7f);
        float[] d = new float[n];
        System.Random rng = new System.Random(505);

        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

            lp += (noise - lp) * 0.1f;
            float debris = noise - lp;

            float blast = lp * Mathf.Exp(-t * 4.2f) * 1.1f;
            float crack = debris * Mathf.Exp(-t * 70f) * 0.8f;

            float freq = Mathf.Lerp(74f, 24f, Mathf.Clamp01(t / 0.6f));
            float boom = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 3.6f) * 1.0f;

            // 后半段的碎石雨
            float grit = 0f;
            if (t > 0.18f)
            {
                float e = Mathf.Exp(-(t - 0.18f) * 2.2f);
                grit = (float)(rng.NextDouble() * 2.0 - 1.0) * e * 0.12f;
            }

            d[i] = (float)System.Math.Tanh((blast + crack + boom + grit) * 1.15f) * 0.9f;
        }

        return Make("Explosion", d, 1f);
    }

    /// <summary>
    /// 拉栓：野外的汉阳造，远处收音。
    /// 老枪的金属已经磨损，抬栓、拉栓、推回、闭锁都是发闷发涩的摩擦声，
    /// 没有清脆的高频，也没有回响，只衬一层很轻的山风。
    /// 0.8 秒，干声。
    /// </summary>
    public static AudioClip Bolt()
    {
        int n = (int)(Rate * 0.8f);
        float[] d = new float[n];
        System.Random rng = new System.Random(404);

        float bandMid = 0f;   // 约 2.2 kHz，留一点砂砾感
        float bandLow = 0f;   // 约 400 Hz，闷
        float grain = 0.5f;   // 慢变化的幅度抖动，模拟金属涩住

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

            // 远距离收音：高频先滚下去，声音自然是闷的
            bandMid += (noise - bandMid) * 0.28f;
            bandLow += (noise - bandLow) * 0.055f;

            // 金属涩住时的抖动：不是均匀的白噪声，而是一顿一顿地刮
            grain += ((noise * 0.5f + 0.5f) - grain) * 0.10f;

            // 抬栓：闷闷的一记"嗒"，两颗低频的金属音，衰减很快
            float liftEnv = Mathf.Exp(-Mathf.Abs(t - 0.04f) * 105f);
            float lift = (Mathf.Sin(2f * Mathf.PI * 690f * t)
                        + 0.42f * Mathf.Sin(2f * Mathf.PI * 1060f * t)) * liftEnv;

            // 后拉、推回：两段发涩的滑动，中间停一下
            float slideBack = Swell(t, 0.07f, 0.26f);
            float slideFwd = Swell(t, 0.36f, 0.54f) * 0.9f;
            float slide = slideBack + slideFwd;

            float scrape = bandMid * slide * (0.55f + 0.85f * grain) * 0.95f;
            // 涩住时的低频摩擦，让滑动带一点"咕"的顿挫
            float drag = Mathf.Sin(2f * Mathf.PI * 128f * t) * slide
                         * (0.35f + 0.65f * grain) * 0.11f;

            // 闭锁：闩推到位，一记闷响，不是清脆的咔哒
            float lockEnv = Mathf.Exp(-Mathf.Abs(t - 0.60f) * 125f);
            float lockEnv2 = Mathf.Exp(-Mathf.Abs(t - 0.645f) * 150f) * 0.55f;
            float lockTone = (Mathf.Sin(2f * Mathf.PI * 480f * t) * lockEnv
                            + 0.7f * Mathf.Sin(2f * Mathf.PI * 760f * t) * lockEnv)
                           + Mathf.Sin(2f * Mathf.PI * 610f * t) * lockEnv2;
            float lockHit = bandLow * (lockEnv + lockEnv2) * 1.5f;

            // 机匣的分量：很轻的一点低频，不抢戏
            float body = Mathf.Sin(2f * Mathf.PI * 175f * t)
                         * (lockEnv * 0.26f + liftEnv * 0.10f);

            // 山风：极轻的一层底噪，缓慢起伏
            float wind = bandLow * (0.55f + 0.28f * Mathf.Sin(2f * Mathf.PI * 0.55f * t))
                         * (0.22f + 0.10f * t);

            d[i] = lift * 0.24f + scrape + drag + lockTone * 0.30f + lockHit * 0.35f
                 + body + wind;
        }

        // 收尾：0.72 秒后迅速收干净，不留尾巴
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float fade = 1f - Mathf.Clamp01((t - 0.72f) / 0.06f);
            d[i] = Mathf.Clamp(d[i] * 1.05f, -1f, 1f) * fade;
        }

        return Make("Bolt", d, 0.55f);
    }

    /// <summary>矩形包络：起落各 20 毫秒，用于把滑动摩擦切成两段。</summary>
    private static float Swell(float t, float start, float end)
    {
        if (t <= start || t >= end) return 0f;

        const float edge = 0.02f;
        float s = Mathf.Min(Mathf.Clamp01((t - start) / edge),
                            Mathf.Clamp01((end - t) / edge));
        return s * s * (3f - 2f * s);
    }

    public static AudioClip Empty()
    {
        int n = (int)(Rate * 0.12f);
        float[] d = new float[n];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Exp(-t * 90f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 1600f * t) * 0.6f
                  + Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.4f) * env;
        }

        return Make("Empty", d, 0.5f);
    }

    public static AudioClip Reload()
    {
        int n = (int)(Rate * 0.7f);
        float[] d = new float[n];
        System.Random rng = new System.Random(303);
        float[] clicks = { 0.08f, 0.28f, 0.45f, 0.58f };

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float sum = 0f;
            for (int c = 0; c < clicks.Length; c++)
            {
                float e = Mathf.Exp(-Mathf.Abs(t - clicks[c]) * 160f);
                sum += Mathf.Sin(2f * Mathf.PI * (1200f + c * 220f) * t) * e * 0.4f;
            }
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 8f) * 0.08f;
            d[i] = sum + noise;
        }

        return Make("Reload", d, 0.55f);
    }

    public static AudioClip Swing()
    {
        int n = (int)(Rate * 0.32f);
        float[] d = new float[n];
        System.Random rng = new System.Random(404);
        float prev = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            // 一阶低通，做出"呼"的风声
            prev = Mathf.Lerp(prev, raw, 0.08f);
            float env = Mathf.Sin(Mathf.Clamp01(t / 0.32f) * Mathf.PI);
            d[i] = prev * env;
        }

        return Make("Swing", d, 0.45f);
    }

    // ── 命中 / 击杀反馈（参考 CS2 的命中提示音） ──

    /// <summary>普通命中：短促清亮的“嗒”。</summary>
    public static AudioClip HitTick()
    {
        int n = (int)(Rate * 0.055f);
        float[] d = new float[n];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Exp(-t * 120f);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 1900f * t) * 0.6f
                  + Mathf.Sin(2f * Mathf.PI * 2900f * t) * 0.35f) * env;
        }

        return Make("HitTick", d, 0.45f);
    }

    /// <summary>爆头：更亮、更长的金属“叮”。</summary>
    public static AudioClip HeadshotTick()
    {
        int n = (int)(Rate * 0.2f);
        float[] d = new float[n];
        System.Random rng = new System.Random(707);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float ping = Mathf.Sin(2f * Mathf.PI * 2650f * t) * Mathf.Exp(-t * 26f) * 0.55f
                       + Mathf.Sin(2f * Mathf.PI * 3970f * t) * Mathf.Exp(-t * 38f) * 0.28f;
            float click = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 160f) * 0.35f;
            d[i] = ping + click;
        }

        return Make("HeadshotTick", d, 0.5f);
    }

    /// <summary>击杀确认：低频闷响 + 高频收尾。</summary>
    public static AudioClip KillConfirm()
    {
        int n = (int)(Rate * 0.46f);
        float[] d = new float[n];

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float thump = Mathf.Sin(2f * Mathf.PI * 72f * t) * Mathf.Exp(-t * 15f) * 0.85f;
            float ping = 0f;

            if (t > 0.06f)
            {
                float u = t - 0.06f;
                ping = Mathf.Sin(2f * Mathf.PI * 1560f * u) * Mathf.Exp(-u * 15f) * 0.5f
                     + Mathf.Sin(2f * Mathf.PI * 2340f * u) * Mathf.Exp(-u * 24f) * 0.22f;
            }

            d[i] = thump + ping;
        }

        return Make("KillConfirm", d, 0.62f);
    }

    /// <summary>刀砍到东西：低沉的“噗”。</summary>
    public static AudioClip MeleeHit()
    {
        int n = (int)(Rate * 0.2f);
        float[] d = new float[n];
        System.Random rng = new System.Random(808);
        float prev = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            prev = Mathf.Lerp(prev, raw, 0.18f);
            float body = prev * Mathf.Exp(-t * 24f) * 1.7f;
            float thump = Mathf.Sin(2f * Mathf.PI * 105f * t) * Mathf.Exp(-t * 18f) * 0.6f;
            d[i] = body + thump;
        }

        return Make("MeleeHit", d, 0.55f);
    }

    public static AudioClip Wind()
    {
        int n = Rate * 6;
        float[] d = new float[n];
        System.Random rng = new System.Random(505);
        float prev = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            prev = Mathf.Lerp(prev, raw, 0.015f);

            float mod = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 0.11f * t)
                      * Mathf.Sin(2f * Mathf.PI * 0.037f * t + 1.3f);

            // 两端淡入淡出，避免循环接缝爆音
            float edge = Mathf.Clamp01(Mathf.Min(t, 6f - t) / 0.4f);
            d[i] = prev * mod * edge * 3.5f;
        }

        return Make("Wind", d, 0.35f);
    }

    public static AudioClip Rain()
    {
        int n = Rate * 6;
        float[] d = new float[n];
        System.Random rng = new System.Random(909);
        float low = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            low = Mathf.Lerp(low, raw, 0.22f);

            // 高频雨点 + 一层低频雨幕
            float hiss = raw - low;
            float mod = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.19f * t);

            float edge = Mathf.Clamp01(Mathf.Min(t, 6f - t) / 0.4f);
            d[i] = (hiss * 0.75f + low * 0.3f) * mod * edge;
        }

        return Make("Rain", d, 0.4f);
    }

    /// <summary>
    /// 急促的喘息：鼻吸短而窄、口呼长而沉，三次呼吸一次比一次重，
    /// 叠一点胸腔的低音与嗓子发紧的抖动，听起来是人在喘，不是风在吹。
    /// </summary>
    public static AudioClip Breath()
    {
        const float cycle = 0.8f;
        int n = Mathf.RoundToInt(Rate * cycle * 3f);
        float[] d = new float[n];
        System.Random rng = new System.Random(731);
        float low = 0f;
        float mid = 0f;
        float high = 0f;
        float total = n / (float)Rate;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);

            // 三层气流：胸腔低频、口腔中频、鼻息高频
            low = Mathf.Lerp(low, raw, 0.05f);
            mid = Mathf.Lerp(mid, raw, 0.2f);
            high = Mathf.Lerp(high, raw, 0.55f);

            int cycleIndex = Mathf.Min(2, (int)(t / cycle));
            float c = t - cycleIndex * cycle;
            float weight = cycleIndex == 0 ? 0.9f : (cycleIndex == 1 ? 1f : 0.84f);

            // 吸气：短、窄，主要走鼻子，高频多
            float inhale = 0f;
            if (c > 0.02f && c < 0.33f)
            {
                float u = (c - 0.02f) / 0.31f;
                inhale = Mathf.Sin(u * Mathf.PI);
                inhale *= inhale;          // 两头收干净，不留拖尾
            }

            // 呼气：长、宽，从口腔出来，中低频多，尾音慢慢散掉
            float exhale = 0f;
            if (c > 0.4f && c < 0.77f)
            {
                float u = (c - 0.4f) / 0.37f;
                exhale = Mathf.Sin(Mathf.Pow(u, 0.7f) * Mathf.PI);
                exhale *= 1f - 0.3f * u;
            }

            // 声带的一点低音：人喘气时有胸腔的闷响，纯噪声听着像风
            float voice = Mathf.Sin(2f * Mathf.PI * 116f * t)
                        + 0.4f * Mathf.Sin(2f * Mathf.PI * 232f * t + 0.8f);
            // 嗓子发紧时每口气都带一点颤
            float rasp = 1f + 0.1f * Mathf.Sin(2f * Mathf.PI * 6.3f * t + 0.5f);

            float air = high * 0.85f + mid * 0.3f;
            float chest = low * 1.2f + mid * 0.25f + voice * 0.08f;

            float sample = air * inhale * 0.55f
                         + (mid - low) * inhale * 0.3f
                         + chest * exhale
                         + (high - mid) * exhale * 0.18f * (1f - exhale);
            sample *= rasp * weight;

            // 首尾留白，循环接缝处不会“咔”一声
            float edge = Mathf.Clamp01(Mathf.Min(t, total - t) / 0.08f);
            d[i] = sample * edge;
        }

        return Make("Breath", d, 0.55f);
    }

    /// <summary>
    /// 火把燃烧：一层闷闷的呼呼声打底，上面零星爆出松脂哔剥的细响，
    /// 火舌被山风吹得忽高忽低。正好两秒一段，接缝淡入淡出，可以无缝循环。
    /// </summary>
    public static AudioClip TorchFire()
    {
        const float duration = 2f;
        int n = Mathf.RoundToInt(Rate * duration);
        float[] d = new float[n];
        System.Random rng = new System.Random(717);
        float low = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (raw - low) * 0.035f;
            float hiss = raw - low;

            // 两层慢速起伏，周期与两秒循环对齐，接缝处不会突然换气
            float breath = 0.78f + 0.16f * Mathf.Sin(2f * Mathf.PI * t / duration)
                                  + 0.06f * Mathf.Sin(2f * Mathf.PI * 3f * t / duration + 1.7f);

            d[i] = (low * 2.2f + hiss * 0.12f) * breath;
        }

        // 松脂爆响：随机十几处短促的"哔剥"，有些带一点音高，有些只是碎响
        for (int k = 0; k < 18; k++)
        {
            int start = rng.Next(Rate / 8, n - Rate / 8);
            int len = 240 + rng.Next(0, 420);
            float amp = 0.25f + (float)rng.NextDouble() * 0.5f;
            float pitch = 780f + (float)rng.NextDouble() * 620f;

            for (int i = 0; i < len && start + i < n; i++)
            {
                float env = Mathf.Exp(-i / (len * 0.22f));
                float tone = Mathf.Sin(2f * Mathf.PI * pitch * i / Rate);
                float grit = (float)(rng.NextDouble() * 2.0 - 1.0);
                d[start + i] += (tone * 0.4f + grit * 0.6f) * env * amp;
            }
        }

        int edge = Mathf.RoundToInt(Rate * 0.12f);
        for (int i = 0; i < edge; i++)
        {
            float k = i / (float)edge;
            d[i] *= k;
            d[n - 1 - i] *= k;
        }

        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i], -1f, 1f);

        return Make("TorchFire", d, 0.5f);
    }

    /// <summary>
    /// 冲锋号：铜管四声递进（G4–C5–E5–G5），最后一口气拉长，
    /// 用 1~6 次谐波叠出铜的亮，起音带一点上滑和气流嘶声。
    /// </summary>
    public static AudioClip Bugle()
    {
        float[] starts = { 0f, 0.62f, 1.18f, 1.62f };
        float[] lengths = { 0.58f, 0.52f, 0.42f, 1.32f };
        float[] notes = { 392f, 523.25f, 659.25f, 783.99f };

        const float total = 3.4f;
        int n = Mathf.RoundToInt(Rate * total);
        float[] d = new float[n];
        System.Random rng = new System.Random(1949);
        float breath = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            breath = Mathf.Lerp(breath, raw, 0.12f);

            float sample = 0f;
            float gate = 0f;

            for (int k = 0; k < notes.Length; k++)
            {
                float local = t - starts[k];
                if (local < 0f || local >= lengths[k]) continue;

                // 起音 35 毫秒冲上去，收尾 120 毫秒放开；长音后半略弱
                float attack = Mathf.Clamp01(local / 0.035f);
                float release = Mathf.Clamp01((lengths[k] - local) / 0.12f);
                float env = attack * release * Mathf.Lerp(1f, 0.86f, local / lengths[k]);
                gate = Mathf.Max(gate, env);

                // 铜管起音时音准略低，吹稳之后才到位；长音带 5.4 Hz 颤音
                float bend = 1f - 0.012f * Mathf.Exp(-local * 24f);
                float vibDepth = 0.004f * Mathf.Clamp01((local - 0.35f) * 2f);
                float vib = 1f + vibDepth * Mathf.Sin(2f * Mathf.PI * 5.4f * local);
                float phase = 2f * Mathf.PI * notes[k] * bend * vib * local;

                float tone = Mathf.Sin(phase)
                           + 0.55f * Mathf.Sin(phase * 2f)
                           + 0.38f * Mathf.Sin(phase * 3f)
                           + 0.22f * Mathf.Sin(phase * 4f)
                           + 0.13f * Mathf.Sin(phase * 5f)
                           + 0.07f * Mathf.Sin(phase * 6f);

                sample += tone * env;
            }

            // 号嘴的气流嘶声：只在吹气时出现，给铜管加一点毛边
            sample = sample * 0.42f + breath * gate * 0.3f;

            float edge = Mathf.Clamp01(Mathf.Min(t, total - t) / 0.05f);
            d[i] = sample * edge;
        }

        return Make("Bugle", d, 0.72f);
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static AudioClip Make(string name, float[] data, float peak)
    {
        Normalize(data, peak);
        AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static void Normalize(float[] data, float peak)
    {
        float max = 0.0001f;
        for (int i = 0; i < data.Length; i++)
        {
            float a = Mathf.Abs(data[i]);
            if (a > max) max = a;
        }

        float scale = peak / max;
        for (int i = 0; i < data.Length; i++) data[i] *= scale;
    }
}
