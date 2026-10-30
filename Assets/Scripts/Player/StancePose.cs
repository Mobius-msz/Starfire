using UnityEngine;

/// <summary>
/// 蹲下 / 卧倒的关节关键帧。主角和战友读同一份数据，
/// 两边的动作和动作链条才会一致，改一处两边同时生效。
/// </summary>
public static class StancePose
{
    /// <summary>
    /// 卧倒 / 起身的动作链关键帧。数值是各关节绕 X 的角度：
    /// 大腿负角 = 膝盖向前抬，正角 = 腿向后伸；小腿正角 = 往回折；
    /// 脚踝正角 = 脚尖向后压（俯卧时脚背贴地）。
    /// 从站立 → 半蹲 → 单膝触地 → 前臂撑地 → 后腿放平 → 俯卧，
    /// 起身时按同一套关键帧倒放。
    /// </summary>
    public struct Key
    {
        public float lean;
        public float bodyY;
        public float thighL;
        public float shinL;
        public float thighR;
        public float shinR;
        public float armL;
        public float armR;
        public float forearmL;
        public float forearmR;
        public float footL;
        public float footR;
        public float head;
    }

    public static readonly float[] ProneStops = { 0f, 0.22f, 0.42f, 0.62f, 0.82f, 1f };

    public static readonly Key[] ProneKeys =
    {
        // 站立
        new Key { lean = 0f, bodyY = 0f, thighL = 0f, shinL = 0f,
                  thighR = 0f, shinR = 0f, armL = 0f, armR = 0f,
                  forearmL = 0f, forearmR = 0f, footL = 0f, footR = 0f, head = 0f },
        // 屈膝下沉：先蹲下去，枪还在肩上
        new Key { lean = 8f, bodyY = -0.24f, thighL = -52f, shinL = 86f,
                  thighR = -52f, shinR = 86f, armL = 8f, armR = 8f,
                  forearmL = -12f, forearmR = -12f, footL = 0f, footR = 0f, head = -6f },
        // 单膝触地：前腿踏出去撑住，后膝先着地，上身仍然抬着
        new Key { lean = 16f, bodyY = -0.40f, thighL = -90f, shinL = 90f,
                  thighR = 0f, shinR = 118f, armL = 12f, armR = 10f,
                  forearmL = -20f, forearmR = -18f, footL = 0f, footR = 40f, head = -10f },
        // 俯身前探：上身折下去，双臂伸出去找地面
        new Key { lean = 46f, bodyY = -0.38f, thighL = -85f, shinL = 85f,
                  thighR = 20f, shinR = 90f, armL = -80f, armR = -76f,
                  forearmL = -30f, forearmR = -28f, footL = 0f, footR = 55f, head = -34f },
        // 后腿放平、躯干继续压低
        new Key { lean = 70f, bodyY = -0.54f, thighL = 55f, shinL = 40f,
                  thighR = 50f, shinR = 45f, armL = -50f, armR = -48f,
                  forearmL = -98f, forearmR = -96f, footL = 60f, footR = 70f, head = -58f },
        // 标准俯卧：双腿向后平铺、双前臂撑地、头抬起瞄准
        new Key { lean = 82f, bodyY = -0.66f, thighL = 84f, shinL = 8f,
                  thighR = 84f, shinR = 8f, armL = -40f, armR = -38f,
                  forearmL = -132f, forearmR = -130f, footL = 88f, footR = 88f, head = -82f }
    };

    /// <summary>卧倒整条链约 0.7 秒；起身略快，倒放同一套关键帧。</summary>
    public const float ProneDownRate = 1.45f;
    public const float ProneUpRate = 1.6f;

    public const float CrouchRate = 7f;
    public const float KneelRate = 6f;
    public const float CrouchLean = 12f;

    public const float CrouchThighFront = -66f;
    public const float CrouchThighFrontKneel = -84f;
    public const float CrouchShinFront = 96f;
    public const float CrouchShinFrontKneel = 86f;
    public const float CrouchThighBack = -66f;
    public const float CrouchThighBackKneel = -4f;
    public const float CrouchShinBack = 96f;
    public const float CrouchShinBackKneel = 102f;

    public const float CrouchDrop = -0.32f;
    public const float CrouchDropKneel = -0.46f;

    public static Key SampleProne(float phase)
    {
        phase = Mathf.Clamp01(phase);

        int i = 0;
        while (i < ProneStops.Length - 2 && phase > ProneStops[i + 1]) i++;

        float span = Mathf.Max(0.0001f, ProneStops[i + 1] - ProneStops[i]);
        float t = Mathf.Clamp01((phase - ProneStops[i]) / span);
        t = t * t * (3f - 2f * t);

        Key a = ProneKeys[i];
        Key b = ProneKeys[i + 1];

        return new Key
        {
            lean = Mathf.Lerp(a.lean, b.lean, t),
            bodyY = Mathf.Lerp(a.bodyY, b.bodyY, t),
            thighL = Mathf.Lerp(a.thighL, b.thighL, t),
            shinL = Mathf.Lerp(a.shinL, b.shinL, t),
            thighR = Mathf.Lerp(a.thighR, b.thighR, t),
            shinR = Mathf.Lerp(a.shinR, b.shinR, t),
            armL = Mathf.Lerp(a.armL, b.armL, t),
            armR = Mathf.Lerp(a.armR, b.armR, t),
            forearmL = Mathf.Lerp(a.forearmL, b.forearmL, t),
            forearmR = Mathf.Lerp(a.forearmR, b.forearmR, t),
            footL = Mathf.Lerp(a.footL, b.footL, t),
            footR = Mathf.Lerp(a.footR, b.footR, t),
            head = Mathf.Lerp(a.head, b.head, t)
        };
    }

    /// <summary>
    /// 蹲姿：站着不动时单膝跪地——前腿大腿端平、小腿竖直踩实，
    /// 后腿大腿近乎竖直、小腿折回贴地；一迈步就过渡回低姿蹚步。
    /// </summary>
    public static void SampleCrouch(float kneelWeight,
        out float thighFront, out float shinFront,
        out float thighBack, out float shinBack, out float drop)
    {
        thighFront = Mathf.Lerp(CrouchThighFront, CrouchThighFrontKneel, kneelWeight);
        shinFront = Mathf.Lerp(CrouchShinFront, CrouchShinFrontKneel, kneelWeight);
        thighBack = Mathf.Lerp(CrouchThighBack, CrouchThighBackKneel, kneelWeight);
        shinBack = Mathf.Lerp(CrouchShinBack, CrouchShinBackKneel, kneelWeight);
        drop = Mathf.Lerp(CrouchDrop, CrouchDropKneel, kneelWeight);
    }
}
