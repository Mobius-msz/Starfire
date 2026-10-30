using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时 HUD：生命 / 体温 / 信念条、弹药、准星、任务提示、标题与结算界面。
/// 使用引擎内置 IMGUI（OnGUI）绘制，不依赖 UGUI 包，也无需任何 Prefab 或美术资源。
/// 设计分辨率 1600 × 900，按屏幕尺寸整体缩放。
/// </summary>
public class HUDController : MonoBehaviour
{
    private const float RefWidth = 1600f;
    private const float RefHeight = 900f;

    private static readonly Color Cream = new Color(0.93f, 0.9f, 0.82f);
    private static readonly Color Gold = new Color(0.96f, 0.8f, 0.46f);

    private Font uiFont;
    private Texture2D frostTexture;
    private Texture2D damageTexture;
    private Texture2D topShadeTexture;
    private Texture2D bloodTexture;

    private GUIStyle objectiveStyle;
    private GUIStyle timerStyle;
    private GUIStyle weatherStyle;
    private GUIStyle messageStyle;
    private GUIStyle promptStyle;
    private GUIStyle healthLabelStyle;
    private GUIStyle tempLabelStyle;
    private GUIStyle faithLabelStyle;
    private GUIStyle ammoStyle;
    private GUIStyle reserveStyle;
    private GUIStyle weaponStyle;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle storyStyle;
    private GUIStyle historyStyle;
    private GUIStyle controlsStyle;
    private GUIStyle startStyle;
    private GUIStyle footerStyle;
    private GUIStyle endTitleStyle;
    private GUIStyle endBodyStyle;
    private GUIStyle restartStyle;
    private GUIStyle briefingTitleStyle;
    private GUIStyle briefingHeaderStyle;
    private GUIStyle briefingBodyStyle;
    private GUIStyle briefingHintStyle;
    private GUIStyle briefingFooterStyle;
    private GUIStyle victoryTitleStyle;
    private GUIStyle victoryStatsStyle;
    private GUIStyle victoryHistoryStyle;
    private GUIStyle victoryHintStyle;
    private GUIStyle allyStyle;
    private GUIStyle evacStyle;
    private GUIStyle feedStyle;
    private GUIStyle feedNameRightStyle;
    private GUIStyle feedNameLeftStyle;
    private GUIStyle feedTagStyle;
    private GUIStyle scoreStyle;
    private GUIStyle killBannerStyle;
    private GUIStyle levelStyle;
    private GUIStyle levelSelectedStyle;

    // ── 暂停菜单（ESC：左侧四分之一屏） ──
    private bool pauseVisible;
    private string pauseStats = "";
    private Texture pauseBackdrop;
    private int pauseAction;
    private GUIStyle pauseTitleStyle;
    private GUIStyle pauseLevelStyle;
    private GUIStyle pauseStatStyle;
    private GUIStyle pauseButtonStyle;
    private GUIStyle pauseHintStyle;
    private Texture2D pauseButtonNormal;
    private Texture2D pauseButtonHover;
    private Texture2D pauseButtonActive;

    private bool stylesReady;

    private int selectedLevel = 1;
    public int SelectedLevel => selectedLevel;

    // ── 战斗反馈（命中、击杀信息、战功） ──
    private struct FeedLine
    {
        public string text;       // 消息行：回血、阵亡提示等
        public float born;
        // 击杀行按「击杀者 · 武器图标 · 被击杀者」排版
        public bool kill;
        public string killer;
        public string victim;
        public Color killerColor;
        public int weaponKind;    // 0 步枪 / 1 驳壳枪 / 2 大刀
        public bool headshot;
    }

    private readonly List<FeedLine> feed = new List<FeedLine>();
    private float hitMarkerTimer;
    private bool hitMarkerHeadshot;
    private bool hitMarkerKill;
    private float killBannerTimer;
    private string killBannerText = "";
    private bool killBannerHeadshot;
    private int score;
    private int headshotKills;

    public int Score => score;
    public int HeadshotKills => headshotKills;

    private Health health;
    private ColdSystem cold;
    private FaithSystem faith;
    private Hanyang88Rifle rifle;
    private Pistol pistol;
    private PlayerMovement movement;
    private WeaponController weapon;
    private GrenadeSystem grenades;
    private CameraRig cameraRig;

    private bool hudVisible;
    private bool titleVisible;
    private bool endVisible;

    // 战前简报（历史背景）与胜利结算（主角敬礼 + 顶部得分）
    private bool briefingVisible;
    private string briefingTitle = "";
    private string briefingStory = "";
    private string briefingHistory = "";
    private string briefingImpact = "";
    private string briefingObjective = "";

    private bool victoryVisible;
    private string victoryTitle = "";
    private string victoryStats = "";
    private string victoryHistory = "";
    private string victoryHint = "";
    private float victoryShownAt;

    private string objective = "";
    private float evacProgress = -1f;
    private float fadeAlpha;

    // 任务指引：目标点上的菱形标记 / 出屏时的边缘箭头 + 距离
    private Transform waypointTarget;
    private Vector3 waypointPosition;
    private bool waypointActive;
    private string waypointLabel = "";
    private GUIStyle waypointStyle;
    private GUIStyle waypointShadowStyle;
    private Texture2D waypointArrow;
    private string timer = "";
    private string weatherText = "";
    private string message = "";
    private float messageUntil;
    private string prompt = "";
    private float promptProgress;
    private bool promptVisible;

    private string healthText = "生命 100";
    private string tempText = "体温 100%";
    private string faithText = "信念 0 / 100";
    private string staminaText = "体力 100%";
    private string ammoText = "弹匣 30 / 30";
    private string reserveText = "备弹 30";
    private string stanceText = "站姿";
    private string grenadeText = "手榴弹 无";
    private float ammoFill = 1f;
    private Color ammoColor = new Color(0.98f, 0.88f, 0.62f);
    private float healthFill = 1f;
    private float tempFill = 1f;
    private float faithFill;
    private Color tempColor = new Color(0.45f, 0.68f, 0.9f);
    private float staminaFill = 1f;
    private Color staminaColor = new Color(0.55f, 0.84f, 0.46f);

    private float damageFlash;
    private float lowHealthBlend;
    private float bloodAlpha;
    private float bloodHold;
    private float startAlpha = 1f;

    private string endTitle = "";
    private string endBody = "";
    private bool endVictory;

    // ───────────────────────────── 构建与绑定 ─────────────────────────────

    public void BuildShell()
    {
        uiFont = Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "DengXian", "SimSun", "Arial" }, 24);

        frostTexture = ProceduralTextures.FrostTexture();
        damageTexture = ProceduralTextures.RedVignetteTexture();
        topShadeTexture = ProceduralTextures.TopShadeTexture();
        bloodTexture = ProceduralTextures.BloodTexture();
    }

    public void Bind(Health healthRef, ColdSystem coldRef, FaithSystem faithRef, Hanyang88Rifle rifleRef)
    {
        health = healthRef;
        cold = coldRef;
        faith = faithRef;
        rifle = rifleRef;

        if (health != null) health.Damaged += OnDamaged;
    }

    public void BindMovement(PlayerMovement movementRef)
    {
        movement = movementRef;
    }

    public void BindWeapon(WeaponController weaponRef)
    {
        weapon = weaponRef;
    }

    public void BindPistol(Pistol pistolRef)
    {
        pistol = pistolRef;
    }

    public void BindGrenade(GrenadeSystem grenadeRef)
    {
        grenades = grenadeRef;
    }

    public void BindCamera(CameraRig rigRef)
    {
        cameraRig = rigRef;
    }

    private void OnDisable()
    {
        if (health != null) health.Damaged -= OnDamaged;
    }

    private void OnDamaged(DamageInfo info)
    {
        damageFlash = Mathf.Clamp01(damageFlash + 0.16f + info.Amount * 0.012f);

        // 中弹时往镜头边缘溅一层血，最多挂 3 秒就清干净
        bloodAlpha = Mathf.Clamp01(bloodAlpha + 0.22f + info.Amount * 0.008f);
        bloodHold = 0.75f;
    }

    // ───────────────────────────── 状态刷新 ─────────────────────────────

    private void Update()
    {
        damageFlash = Mathf.MoveTowards(damageFlash, 0f, 1.8f * Time.deltaTime);

        bool nearDeath = health != null && !health.IsDead && health.Current < 20f;
        lowHealthBlend = Mathf.MoveTowards(
            lowHealthBlend, nearDeath ? 1f : 0f, (nearDeath ? 1.1f : 2.4f) * Time.deltaTime);

        if (bloodHold > 0f) bloodHold -= Time.deltaTime;
        else bloodAlpha = Mathf.MoveTowards(bloodAlpha, 0f, 0.5f * Time.deltaTime);

        if (hitMarkerTimer > 0f) hitMarkerTimer -= Time.deltaTime;
        if (killBannerTimer > 0f) killBannerTimer -= Time.deltaTime;

        for (int i = feed.Count - 1; i >= 0; i--)
        {
            if (Time.time - feed[i].born > 5.5f) feed.RemoveAt(i);
        }

        if (health != null)
        {
            healthFill = Mathf.Clamp01(health.Current / Mathf.Max(1f, health.MaxHealth));
            healthText = "生命 " + Mathf.CeilToInt(health.Current);
        }

        if (cold != null)
        {
            float percent = cold.TemperaturePercent;
            tempFill = Mathf.Clamp01(percent / 100f);

            if (percent < 0f) tempText = "体温 失温 · 行动迟缓";
            else if (percent < 30f) tempText = "体温 " + Mathf.RoundToInt(percent) + "% · 严寒";
            else tempText = "体温 " + Mathf.RoundToInt(percent) + "%";

            tempColor = percent < 30f
                ? new Color(0.55f, 0.8f, 1f)
                : new Color(0.45f, 0.68f, 0.9f);
        }

        if (faith != null)
        {
            faithFill = faith.FaithNormalized;

            if (faith.IsBuffActive) faithText = "信念 燃烧中 · 减伤 50%";
            else if (faith.IsFull) faithText = "信念 已满 · 按 F 爆发";
            else faithText = "信念 " + Mathf.RoundToInt(faith.Faith)
                                + " / " + Mathf.RoundToInt(faith.MaxFaith);
        }

        bool rifleEquipped = weapon == null || weapon.IsRifle;

        if (weapon != null && weapon.IsPistol && pistol != null)
        {
            int capacity = Mathf.Max(1, pistol.MagazineCapacity);
            int ammo = pistol.AmmoInMagazine;

            ammoText = "弹匣 " + ammo + " / " + capacity;
            if (pistol.IsReloading)
            {
                reserveText = "换弹中…";
            }
            else if (pistol.ReserveAmmo <= 0)
            {
                reserveText = "备弹 0　从敌军身上缴获弹夹";
            }
            else
            {
                reserveText = "备弹 " + pistol.ReserveAmmo + "　R 换弹夹";
            }
            ammoFill = Mathf.Clamp01((float)ammo / capacity);

            if (ammo <= 0) ammoColor = new Color(0.9f, 0.3f, 0.24f);
            else if (ammoFill <= 0.34f) ammoColor = new Color(0.96f, 0.64f, 0.28f);
            else ammoColor = new Color(0.98f, 0.88f, 0.62f);
        }
        else if (!rifleEquipped)
        {
            ammoText = "近战 · 无需弹药";
            reserveText = "左键挥砍";
            ammoFill = 0f;
            ammoColor = new Color(0.86f, 0.84f, 0.76f);
        }
        else if (rifle != null)
        {
            int capacity = Mathf.Max(1, rifle.MagazineCapacity);
            int ammo = rifle.AmmoInMagazine;

            ammoText = "弹匣 " + ammo + " / " + capacity;
            reserveText = "备弹 " + rifle.ReserveAmmo;
            ammoFill = Mathf.Clamp01((float)ammo / capacity);

            if (ammo <= 0) ammoColor = new Color(0.9f, 0.3f, 0.24f);
            else if (ammoFill <= 0.3f) ammoColor = new Color(0.96f, 0.64f, 0.28f);
            else ammoColor = new Color(0.98f, 0.88f, 0.62f);
        }

        if (grenades != null)
        {
            grenadeText = grenades.Count > 0
                ? "手榴弹 ×" + grenades.Count + "　T 投掷"
                : "手榴弹 无　从敌军身上缴获";
        }

        if (movement != null)
        {
            switch (movement.CurrentStance)
            {
                case PlayerMovement.Stance.Prone:
                    stanceText = "卧姿";
                    break;
                case PlayerMovement.Stance.Crouching:
                    stanceText = "蹲姿";
                    break;
                default:
                    stanceText = "站姿";
                    break;
            }

            staminaFill = Mathf.Clamp01(movement.StaminaPercent / 100f);
            if (movement.IsExhausted)
            {
                staminaText = "体力 力竭 · 无法奔跑";
                staminaColor = new Color(0.9f, 0.34f, 0.24f);
            }
            else if (staminaFill < 0.35f)
            {
                staminaText = "体力 " + Mathf.RoundToInt(movement.StaminaPercent) + "% · 喘息";
                staminaColor = new Color(0.95f, 0.62f, 0.24f);
            }
            else
            {
                staminaText = "体力 " + Mathf.RoundToInt(movement.StaminaPercent) + "%";
                staminaColor = new Color(0.55f, 0.84f, 0.46f);
            }
        }

        if (titleVisible)
        {
            startAlpha = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.A)
                || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                selectedLevel = 1;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.D)
                     || Input.GetKeyDown(KeyCode.RightArrow))
            {
                selectedLevel = 2;
            }
        }
    }

    // ───────────────────────────── 绘制 ─────────────────────────────

    private void OnGUI()
    {
        if (!hudVisible && !titleVisible && !endVisible
            && !briefingVisible && !victoryVisible && !pauseVisible
            && fadeAlpha <= 0.001f) return;

        EnsureStyles();

        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(
            Screen.width / RefWidth, Screen.height / RefHeight, 1f));

        if (hudVisible) DrawHud();
        if (titleVisible) DrawTitle();
        if (briefingVisible) DrawBriefing();
        if (victoryVisible) DrawVictory();
        if (endVisible) DrawEnd();
        if (pauseVisible) DrawPauseMenu();

        if (fadeAlpha > 0.001f)
        {
            GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), Texture2D.whiteTexture);
        }

        GUI.matrix = previous;
        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (stylesReady) return;

        if (uiFont == null) uiFont = GUI.skin.font;

        objectiveStyle = MakeStyle(21, TextAnchor.UpperLeft, Cream);
        timerStyle = MakeStyle(34, TextAnchor.UpperCenter, Gold);
        weatherStyle = MakeStyle(18, TextAnchor.UpperCenter, new Color(0.78f, 0.76f, 0.68f));
        messageStyle = MakeStyle(27, TextAnchor.MiddleCenter, new Color(1f, 0.91f, 0.78f, 0f));
        promptStyle = MakeStyle(23, TextAnchor.MiddleCenter, new Color(0.98f, 0.84f, 0.55f));
        waypointStyle = MakeStyle(19, TextAnchor.UpperCenter, new Color(1f, 0.86f, 0.48f));
        waypointShadowStyle = MakeStyle(19, TextAnchor.UpperCenter, new Color(0f, 0f, 0f, 0.6f));
        healthLabelStyle = MakeStyle(19, TextAnchor.LowerLeft, Cream);
        tempLabelStyle = MakeStyle(19, TextAnchor.LowerLeft, Cream);
        faithLabelStyle = MakeStyle(19, TextAnchor.LowerLeft, Cream);
        ammoStyle = MakeStyle(28, TextAnchor.MiddleRight, new Color(0.98f, 0.88f, 0.62f));
        reserveStyle = MakeStyle(19, TextAnchor.MiddleRight, Cream);
        weaponStyle = MakeStyle(17, TextAnchor.MiddleRight, new Color(0.72f, 0.69f, 0.62f));

        titleStyle = MakeStyle(84, TextAnchor.MiddleCenter, Gold);
        subtitleStyle = MakeStyle(24, TextAnchor.MiddleCenter, new Color(0.78f, 0.72f, 0.6f));
        storyStyle = MakeStyle(20, TextAnchor.MiddleCenter, new Color(0.86f, 0.83f, 0.76f), true);
        historyStyle = MakeStyle(16, TextAnchor.MiddleCenter, new Color(0.63f, 0.6f, 0.53f), true);
        controlsStyle = MakeStyle(18, TextAnchor.MiddleCenter, new Color(0.7f, 0.68f, 0.62f), true);
        startStyle = MakeStyle(30, TextAnchor.MiddleCenter, Gold);
        footerStyle = MakeStyle(15, TextAnchor.MiddleCenter, new Color(0.5f, 0.48f, 0.44f));

        endTitleStyle = MakeStyle(70, TextAnchor.MiddleCenter, Gold);
        endBodyStyle = MakeStyle(25, TextAnchor.MiddleCenter, Cream, true);
        restartStyle = MakeStyle(26, TextAnchor.MiddleCenter, Gold);

        briefingTitleStyle = MakeStyle(52, TextAnchor.MiddleCenter, Gold);
        briefingHeaderStyle = MakeStyle(21, TextAnchor.UpperLeft, new Color(0.93f, 0.75f, 0.42f));
        briefingBodyStyle = MakeStyle(19, TextAnchor.UpperLeft,
            new Color(0.86f, 0.83f, 0.76f), true);
        briefingHintStyle = MakeStyle(28, TextAnchor.MiddleCenter, Gold);
        briefingFooterStyle = MakeStyle(16, TextAnchor.MiddleCenter, new Color(0.58f, 0.56f, 0.5f));

        victoryTitleStyle = MakeStyle(48, TextAnchor.MiddleCenter, Gold);
        victoryTitleStyle.fontStyle = FontStyle.Bold;
        victoryStatsStyle = MakeStyle(22, TextAnchor.MiddleCenter, new Color(0.92f, 0.87f, 0.74f));
        victoryHistoryStyle = MakeStyle(18, TextAnchor.MiddleCenter,
            new Color(0.8f, 0.77f, 0.7f), true);
        victoryHintStyle = MakeStyle(24, TextAnchor.MiddleCenter, Gold);

        allyStyle = MakeStyle(17, TextAnchor.UpperLeft, new Color(0.74f, 0.72f, 0.64f));
        evacStyle = MakeStyle(17, TextAnchor.UpperLeft, new Color(0.78f, 0.84f, 0.66f));
        feedStyle = MakeStyle(18, TextAnchor.MiddleLeft, Cream);
        feedStyle.richText = true;
        feedNameRightStyle = MakeStyle(18, TextAnchor.MiddleRight, Cream);
        feedNameLeftStyle = MakeStyle(18, TextAnchor.MiddleLeft, Cream);
        feedTagStyle = MakeStyle(15, TextAnchor.MiddleRight, new Color(1f, 0.42f, 0.33f));
        scoreStyle = MakeStyle(17, TextAnchor.MiddleRight, new Color(0.83f, 0.79f, 0.68f));
        killBannerStyle = MakeStyle(31, TextAnchor.MiddleCenter, Gold);
        killBannerStyle.fontStyle = FontStyle.Bold;

        levelStyle = MakeStyle(26, TextAnchor.MiddleCenter, new Color(0.55f, 0.53f, 0.48f));
        levelSelectedStyle = MakeStyle(28, TextAnchor.MiddleCenter, Gold);
        levelSelectedStyle.fontStyle = FontStyle.Bold;

        pauseTitleStyle = MakeStyle(40, TextAnchor.MiddleLeft, Gold);
        pauseTitleStyle.fontStyle = FontStyle.Bold;
        pauseLevelStyle = MakeStyle(18, TextAnchor.MiddleLeft, new Color(0.78f, 0.72f, 0.6f));
        pauseStatStyle = MakeStyle(21, TextAnchor.UpperLeft, Cream, true);
        pauseHintStyle = MakeStyle(15, TextAnchor.MiddleCenter, new Color(0.58f, 0.55f, 0.48f));

        pauseButtonNormal = SolidTexture(new Color(0.13f, 0.14f, 0.16f, 0.92f));
        pauseButtonHover = SolidTexture(new Color(0.52f, 0.15f, 0.11f, 0.96f));
        pauseButtonActive = SolidTexture(new Color(0.7f, 0.24f, 0.16f, 0.98f));

        pauseButtonStyle = MakeStyle(22, TextAnchor.MiddleCenter, Cream);
        pauseButtonStyle.normal.background = pauseButtonNormal;
        pauseButtonStyle.hover.background = pauseButtonHover;
        pauseButtonStyle.active.background = pauseButtonActive;
        pauseButtonStyle.hover.textColor = Gold;
        pauseButtonStyle.active.textColor = Gold;
        pauseButtonStyle.fontStyle = FontStyle.Bold;

        stylesReady = true;
    }

    private static Texture2D SolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    private GUIStyle MakeStyle(int size, TextAnchor anchor, Color color, bool wrap = false)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.font = uiFont;
        style.fontSize = size;
        style.alignment = anchor;
        style.wordWrap = wrap;
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.padding = new RectOffset(0, 0, 0, 0);
        style.margin = new RectOffset(0, 0, 0, 0);
        return style;
    }

    private void DrawHud()
    {
        // 冰冻边框
        if (cold != null && cold.FrostAlpha > 0.001f)
        {
            DrawFullTexture(frostTexture, new Color(1f, 1f, 1f, cold.FrostAlpha));
        }

        // 生命垂危：整屏褪成灰白，心跳式明暗
        if (lowHealthBlend > 0.001f)
        {
            float pulse = 0.86f + 0.14f * Mathf.Sin(Time.time * 3.4f);

            GUI.color = new Color(0.46f, 0.47f, 0.51f, lowHealthBlend * 0.52f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), Texture2D.whiteTexture);

            // 复用红色暗角贴图并染成深灰，只在低血时收窄视野
            DrawFullTexture(damageTexture,
                new Color(0.14f, 0.15f, 0.17f, lowHealthBlend * 0.9f * pulse));
        }

        // 受伤红边
        if (damageFlash > 0.001f)
        {
            DrawFullTexture(damageTexture, new Color(1f, 1f, 1f, damageFlash * 0.8f));
        }

        // 镜头上的血迹：贴图本身就把血斑画在四周，整屏铺开才不会变成大红花
        if (bloodAlpha > 0.001f)
        {
            GUI.color = new Color(1f, 1f, 1f, bloodAlpha * 0.8f);
            GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), bloodTexture,
                ScaleMode.StretchToFill, true);
        }

        GUI.color = Color.white;

        DrawCrosshair();
        DrawHitMarker();

        // 顶部压暗，亮天空下任务与倒计时也能读清
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(0f, 0f, RefWidth, 132f), topShadeTexture,
            ScaleMode.StretchToFill, true);

        GUI.Label(new Rect(34f, 28f, 900f, 32f), objective, objectiveStyle);

        if (AllyAI.AliveCount > 0)
        {
            GUI.Label(new Rect(34f, 62f, 400f, 24f),
                "战友 " + AllyAI.AliveCount + " / " + AllyAI.SquadSize, allyStyle);
        }

        // 主力渡江进度：只显示到炸桥为止
        if (evacProgress >= 0f)
        {
            Rect bar = new Rect(34f, 112f, 300f, 12f);

            GUI.Label(new Rect(34f, 86f, 420f, 22f),
                "大部队撤离　" + Mathf.RoundToInt(evacProgress * 100f) + "%", evacStyle);

            DrawBar(bar, evacProgress, new Color(0.56f, 0.75f, 0.38f));

            // 六批人各占一格，一眼看得出过了几批
            for (int i = 1; i < CrossingColumn.Batches; i++)
            {
                float x = bar.x + bar.width * i / CrossingColumn.Batches;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(x, bar.y, 2f, bar.height), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        GUI.Label(new Rect(0f, 22f, RefWidth, 48f), timer, timerStyle);

        if (!string.IsNullOrEmpty(weatherText))
        {
            GUI.Label(new Rect(0f, 70f, RefWidth, 24f), weatherText, weatherStyle);
        }

        DrawScore();
        DrawKillFeed();
        DrawKillBanner();

        SetAlpha(messageStyle, Mathf.Clamp01((messageUntil - Time.time) / 0.7f));
        GUI.Label(new Rect(250f, 240f, 1100f, 80f), message, messageStyle);

        // 状态区半透明底衬，避免武器模型或亮背景影响读字
        GUI.color = new Color(0f, 0f, 0f, 0.34f);
        GUI.DrawTexture(new Rect(1200f, 706f, 372f, 146f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(16f, 634f, 320f, 228f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 左下四条状态条
        DrawBar(new Rect(32f, 842f, 280f, 14f), healthFill, new Color(0.76f, 0.18f, 0.14f));
        GUI.Label(new Rect(32f, 812f, 420f, 24f), healthText, healthLabelStyle);

        DrawBar(new Rect(32f, 786f, 280f, 14f), tempFill, tempColor);
        GUI.Label(new Rect(32f, 756f, 420f, 24f), tempText, tempLabelStyle);

        DrawBar(new Rect(32f, 730f, 280f, 14f), faithFill, new Color(0.96f, 0.76f, 0.28f));
        GUI.Label(new Rect(32f, 700f, 420f, 24f), faithText, faithLabelStyle);

        DrawBar(new Rect(32f, 674f, 280f, 14f), staminaFill, staminaColor);
        GUI.Label(new Rect(32f, 644f, 420f, 24f), staminaText, healthLabelStyle);

        // 右下弹药
        ammoStyle.normal.textColor = ammoColor;
        GUI.Label(new Rect(1228f, 750f, 340f, 42f), ammoText, ammoStyle);
        DrawBar(new Rect(1228f, 800f, 340f, 6f), ammoFill, new Color(0.96f, 0.76f, 0.28f));
        GUI.Label(new Rect(1228f, 812f, 340f, 26f), reserveText, reserveStyle);
        GUI.Label(new Rect(1228f, 838f, 340f, 26f), grenadeText, reserveStyle);

        string weaponLabel = "汉阳造 · 栓动步枪";
        if (weapon != null)
        {
            if (weapon.IsKnife) weaponLabel = "大刀 · 近战";
            else if (weapon.IsPistol) weaponLabel = "驳壳枪 · 副武器";
            else if (weapon.IsAiming) weaponLabel = "汉阳造 · 机瞄";
        }

        if (cameraRig != null && cameraRig.IsThirdPerson) weaponLabel += " · 第三人称";

        GUI.Label(new Rect(1228f, 724f, 340f, 24f),
            weaponLabel + " · " + stanceText, weaponStyle);

        // 交互提示与进度
        if (promptVisible)
        {
            GUI.Label(new Rect(420f, 580f, 760f, 40f), prompt, promptStyle);

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(630f, 624f, 340f, 8f), Texture2D.whiteTexture);

            GUI.color = new Color(0.96f, 0.72f, 0.3f, 1f);
            GUI.DrawTexture(new Rect(630f, 624f, 340f * Mathf.Clamp01(promptProgress), 8f),
                Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        DrawWaypoint();
    }

    /// <summary>
    /// 任务指引：目标在世界坐标里就画个菱形章，出屏后改成屏幕边缘的箭头；
    /// 两种情况都带名字和直线距离，玩家不用再自己找"桥头 / 渡口"。
    /// </summary>
    private void DrawWaypoint()
    {
        if (!waypointActive) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 world = waypointTarget != null ? waypointTarget.position : waypointPosition;
        world.y += 1.4f;

        Vector3 screenPoint = cam.WorldToScreenPoint(world);
        float scaleX = Screen.width / RefWidth;
        float scaleY = Screen.height / RefHeight;
        if (scaleX <= 0f || scaleY <= 0f) return;

        // 世界坐标 → 参考分辨率坐标（IMGUI 的 y 轴朝下）
        float x = screenPoint.x / scaleX;
        float y = (Screen.height - screenPoint.y) / scaleY;
        bool behind = screenPoint.z <= 0f;

        if (behind)
        {
            x = RefWidth - x;
            y = RefHeight - y;
        }

        const float marginX = 110f;
        const float marginTop = 158f;
        const float marginBottom = 168f;

        bool onScreen = !behind
                        && x > marginX && x < RefWidth - marginX
                        && y > marginTop && y < RefHeight - marginBottom;

        int meters = Mathf.RoundToInt(
            Vector3.Distance(cam.transform.position, waypointTarget != null
                ? waypointTarget.position : waypointPosition));
        string label = waypointLabel + "  " + meters + "米";

        Matrix4x4 saved = GUI.matrix;

        if (onScreen)
        {
            // 目标就在视野里：镜头前一个菱形，名字挂在下面
            RotateGui(45f, new Vector2(x, y));
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(x - 8f, y - 8f, 16f, 16f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.82f, 0.36f, 0.95f);
            GUI.DrawTexture(new Rect(x - 6f, y - 6f, 12f, 12f), Texture2D.whiteTexture);
            GUI.matrix = saved;
            GUI.color = Color.white;

            Rect text = new Rect(Clamp(x - 200f, 8f, RefWidth - 408f), y + 14f, 400f, 24f);
            GUI.Label(new Rect(text.x + 1f, text.y + 1f, text.width, text.height), label, waypointShadowStyle);
            GUI.Label(text, label, waypointStyle);
        }
        else
        {
            // 目标在视野外：箭头贴着屏幕内沿指向它
            Vector2 center = new Vector2(RefWidth * 0.5f, RefHeight * 0.5f);
            Vector2 dir = new Vector2(x - center.x, y - center.y);
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(0f, 1f);
            dir.Normalize();

            float halfW = RefWidth * 0.5f - marginX;
            float halfH = RefHeight * 0.5f - Mathf.Max(marginTop, marginBottom);
            float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            float reach = Mathf.Min(tx, ty);

            Vector2 pos = center + dir * reach;

            if (waypointArrow == null) waypointArrow = MakeArrowTexture();

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;
            RotateGui(angle, pos);
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(pos.x - 17f, pos.y - 16f, 34f, 34f), waypointArrow);
            GUI.color = new Color(1f, 0.82f, 0.36f, 0.95f);
            GUI.DrawTexture(new Rect(pos.x - 16f, pos.y - 17f, 32f, 32f), waypointArrow);
            GUI.matrix = saved;
            GUI.color = Color.white;

            // 文字往屏幕内侧偏，别贴边被裁掉
            float textX = Clamp(pos.x - dir.x * 120f - 200f, 8f, RefWidth - 408f);
            float textY = Clamp(pos.y - dir.y * 70f - 12f, 8f, RefHeight - 34f);
            Rect text = new Rect(textX, textY, 400f, 24f);
            GUI.Label(new Rect(text.x + 1f, text.y + 1f, text.width, text.height), label, waypointShadowStyle);
            GUI.Label(text, label, waypointStyle);
        }

        GUI.matrix = saved;
        GUI.color = Color.white;
    }

    private static float Clamp(float value, float min, float max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// 按参考分辨率坐标绕轴心旋转。GUIUtility.RotateAroundPivot 的轴心是按屏幕像素解释的，
    /// 在 HUD 这套 1600×900 缩放矩阵下会把图形甩到别处，所以自己拼一个参考坐标系的旋转。
    /// </summary>
    private static void RotateGui(float degrees, Vector2 pivot)
    {
        GUI.matrix = GUI.matrix
                     * Matrix4x4.TRS(new Vector3(pivot.x, pivot.y, 0f),
                                     Quaternion.Euler(0f, 0f, degrees), Vector3.one)
                     * Matrix4x4.TRS(new Vector3(-pivot.x, -pivot.y, 0f),
                                     Quaternion.identity, Vector3.one);
    }

    /// <summary>朝上的小三角，画箭头时绕中心旋转。</summary>
    private static Texture2D MakeArrowTexture()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color clear = new Color(1f, 1f, 1f, 0f);
        Color solid = Color.white;

        for (int py = 0; py < size; py++)
        {
            float v = py / (float)(size - 1);
            float half = (1f - v) * size * 0.5f;

            for (int px = 0; px < size; px++)
            {
                bool inside = Mathf.Abs(px - (size - 1) * 0.5f) <= half;
                tex.SetPixel(px, py, inside ? solid : clear);
            }
        }

        tex.Apply();
        return tex;
    }

    private void DrawCrosshair()
    {
        // 机瞄时隐藏准星，避免遮住照门与准星
        if (weapon != null && weapon.IsRifle && weapon.IsAiming) return;

        float cx = RefWidth * 0.5f;
        float cy = RefHeight * 0.5f;

        GUI.color = new Color(1f, 1f, 1f, 0.82f);
        GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - 15f, 2f, 9f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy + 6f, 2f, 9f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 15f, cy - 1f, 9f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + 6f, cy - 1f, 9f, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    /// <summary>命中提示：四道斜刻线，爆头/击杀转红，击杀更大更久。</summary>
    private void DrawHitMarker()
    {
        if (hitMarkerTimer <= 0f) return;

        float life = hitMarkerKill ? 0.2f : 0.13f;
        float alpha = Mathf.Clamp01(hitMarkerTimer / life);

        Color color = Color.white;
        if (hitMarkerKill)
        {
            color = hitMarkerHeadshot
                ? new Color(1f, 0.28f, 0.2f, alpha)
                : new Color(1f, 0.42f, 0.32f, alpha);
        }
        else
        {
            color = new Color(1f, 1f, 1f, 0.85f * alpha);
        }

        float cx = RefWidth * 0.5f;
        float cy = RefHeight * 0.5f;
        float gap = hitMarkerKill ? 9f : 7f;
        float len = hitMarkerKill ? 14f : 9f;
        float thick = hitMarkerKill ? 3f : 2f;

        Matrix4x4 before = GUI.matrix;
        GUI.color = color;

        for (int i = 0; i < 4; i++)
        {
            GUI.matrix = before;
            RotateGui(45f + i * 90f, new Vector2(cx, cy));
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy - gap - len, thick, len),
                Texture2D.whiteTexture);
        }

        GUI.matrix = before;
        GUI.color = Color.white;
    }

    /// <summary>右上角战功：击杀得分与爆头数。</summary>
    private void DrawScore()
    {
        if (score <= 0 && headshotKills <= 0) return;

        GUI.Label(new Rect(RefWidth - 380f, 58f, 344f, 24f),
            "战功 " + score + "　爆头 " + headshotKills, scoreStyle);
    }

    /// <summary>右上角击杀信息，参考 CS2 的 kill feed：名字 · 武器图标 · 名字。</summary>
    private void DrawKillFeed()
    {
        if (feed.Count == 0) return;

        const float width = 380f;
        const float iconWidth = 48f;
        float right = RefWidth - 36f;
        float y = 92f;

        for (int i = 0; i < feed.Count; i++)
        {
            FeedLine line = feed[i];
            float age = Time.time - line.born;
            float alpha = age > 4.6f ? Mathf.Clamp01((5.5f - age) / 0.9f) : 1f;
            float left = right - width;

            GUI.color = new Color(0f, 0f, 0f, 0.42f * alpha);
            GUI.DrawTexture(new Rect(left, y, width, 30f), Texture2D.whiteTexture);

            GUI.color = new Color(0.74f, 0.2f, 0.14f, 0.85f * alpha);
            GUI.DrawTexture(new Rect(left, y, 4f, 30f), Texture2D.whiteTexture);

            // 文字与图标各自带 alpha，这里先把 GUI.color 还原成白色
            GUI.color = Color.white;

            if (line.kill)
            {
                float iconX = left + (width - iconWidth) * 0.5f;
                float tagWidth = line.headshot ? 44f : 0f;

                feedNameRightStyle.normal.textColor = Fade(line.killerColor, alpha);
                GUI.Label(new Rect(left + 12f, y + 3f, iconX - left - 22f, 26f),
                    line.killer, feedNameRightStyle);

                DrawWeaponIcon(new Rect(iconX, y + 4f, iconWidth, 22f),
                    line.weaponKind, alpha);

                float victimX = iconX + iconWidth + 10f;
                feedNameLeftStyle.normal.textColor = Fade(new Color(0.86f, 0.8f, 0.7f), alpha);
                GUI.Label(new Rect(victimX, y + 3f, right - victimX - tagWidth - 12f, 26f),
                    line.victim, feedNameLeftStyle);

                if (line.headshot)
                {
                    feedTagStyle.normal.textColor = Fade(new Color(1f, 0.42f, 0.33f), alpha);
                    GUI.Label(new Rect(right - tagWidth - 4f, y + 3f, tagWidth, 26f),
                        "爆头", feedTagStyle);
                }
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(left + 14f, y + 3f, width - 22f, 26f),
                    line.text, feedStyle);
            }

            y += 34f;
        }

        GUI.color = Color.white;
    }

    private static Color Fade(Color color, float alpha)
    {
        color.a *= alpha;
        return color;
    }

    /// <summary>
    /// 击杀信息中间的武器剪影：用几条矩形拼出汉阳造 / 驳壳枪 / 大刀，
    /// 不依赖贴图，缩放时也不会糊。
    /// </summary>
    private static void DrawWeaponIcon(Rect rect, int kind, float alpha)
    {
        Color steel = Fade(new Color(0.82f, 0.8f, 0.74f), alpha);
        Color wood = Fade(new Color(0.62f, 0.42f, 0.24f), alpha);
        Color dark = Fade(new Color(0.4f, 0.4f, 0.42f), alpha);
        Color bright = Fade(new Color(0.95f, 0.94f, 0.9f), alpha);
        Color brass = Fade(new Color(0.72f, 0.58f, 0.28f), alpha);

        float x = rect.x;
        float y = rect.y;
        float mid = y + rect.height * 0.5f - 6f;
        Color saved = GUI.color;

        if (kind == 1)
        {
            // 驳壳枪：长套筒、方弹仓、后倾的木握把
            GUI.color = steel;
            GUI.DrawTexture(new Rect(x + 11f, mid + 1f, 27f, 5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 36f, mid + 3f, 9f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 13f, mid + 6f, 11f, 3f), Texture2D.whiteTexture);
            GUI.color = bright;
            GUI.DrawTexture(new Rect(x + 35f, mid - 1f, 2f, 2f), Texture2D.whiteTexture);
            GUI.color = wood;
            GUI.DrawTexture(new Rect(x + 11f, mid + 7f, 6f, 11f), Texture2D.whiteTexture);
            GUI.color = dark;
            GUI.DrawTexture(new Rect(x + 16f, mid + 8f, 3f, 7f), Texture2D.whiteTexture);
        }
        else if (kind == 2)
        {
            // 大刀：一边是亮刃、一边是厚刀背，刀尖从刃口斜上去
            GUI.color = steel;
            GUI.DrawTexture(new Rect(x + 10f, mid + 2f, 30f, 5f), Texture2D.whiteTexture);
            GUI.color = bright;
            GUI.DrawTexture(new Rect(x + 10f, mid + 7f, 31f, 1.6f), Texture2D.whiteTexture);
            GUI.color = dark;
            GUI.DrawTexture(new Rect(x + 10f, mid + 1f, 31f, 1.6f), Texture2D.whiteTexture);
            // 刀尖
            GUI.color = steel;
            GUI.DrawTexture(new Rect(x + 34f, mid + 0.5f, 8f, 2.5f), Texture2D.whiteTexture);
            // 护手与刀柄
            GUI.color = brass;
            GUI.DrawTexture(new Rect(x + 8f, mid - 1f, 2.5f, 12f), Texture2D.whiteTexture);
            GUI.color = wood;
            GUI.DrawTexture(new Rect(x + 1f, mid + 3f, 7f, 4f), Texture2D.whiteTexture);
        }
        else
        {
            // 汉阳造：木托、机匣、长枪管与准星
            GUI.color = wood;
            GUI.DrawTexture(new Rect(x + 1f, mid + 4f, 12f, 6f), Texture2D.whiteTexture);
            GUI.color = steel;
            GUI.DrawTexture(new Rect(x + 12f, mid + 3f, 18f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 29f, mid + 4f, 18f, 2.4f), Texture2D.whiteTexture);
            GUI.color = dark;
            GUI.DrawTexture(new Rect(x + 31f, mid + 6f, 7f, 3f), Texture2D.whiteTexture);
            GUI.color = bright;
            GUI.DrawTexture(new Rect(x + 44f, mid + 1.5f, 2f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 25f, mid + 6f, 2f, 4f), Texture2D.whiteTexture);
        }

        GUI.color = saved;
    }

    /// <summary>击杀横幅：击杀 / 爆头击杀 + 战功。</summary>
    private void DrawKillBanner()
    {
        if (killBannerTimer <= 0f) return;

        float alpha = Mathf.Clamp01(killBannerTimer / 0.45f);
        Color color = killBannerHeadshot
            ? new Color(1f, 0.44f, 0.32f, alpha)
            : new Color(1f, 0.86f, 0.55f, alpha);

        killBannerStyle.normal.textColor = color;

        float elapsed = 1.5f - killBannerTimer;
        float rise = Mathf.Clamp01(elapsed / 0.25f) * 10f;

        GUI.Label(new Rect(400f, 152f - rise, 800f, 44f), killBannerText, killBannerStyle);
    }

    private void AddFeed(string text)
    {
        feed.Insert(0, new FeedLine { text = text, born = Time.time });
        if (feed.Count > 5) feed.RemoveAt(feed.Count - 1);
    }

    /// <summary>击杀行：击杀者 + 武器图标 + 被击杀者。</summary>
    private void AddKillFeed(string killer, Color killerColor, string victim,
                             string weaponName, bool headshot)
    {
        feed.Insert(0, new FeedLine
        {
            born = Time.time,
            kill = true,
            killer = killer,
            victim = victim,
            killerColor = killerColor,
            weaponKind = WeaponKind(weaponName),
            headshot = headshot
        });
        if (feed.Count > 5) feed.RemoveAt(feed.Count - 1);
    }

    private static int WeaponKind(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName)) return 0;
        if (weaponName.Contains("刀")) return 2;
        if (weaponName.Contains("驳") || weaponName.Contains("手")) return 1;
        return 0;
    }

    private static readonly Color PlayerNameColor = new Color(0.91f, 0.77f, 0.44f);
    private static readonly Color AllyNameColor = new Color(0.56f, 0.76f, 0.87f);

    /// <summary>玩家命中敌人：显示命中提示。</summary>
    public void ReportHit(bool headshot)
    {
        hitMarkerTimer = 0.13f;
        hitMarkerHeadshot = headshot;
        hitMarkerKill = false;
    }

    /// <summary>玩家击杀敌人：计入战功，显示击杀横幅与红色命中提示。</summary>
    public void ReportKill(bool headshot, string weaponName)
    {
        int gain = headshot ? 150 : 100;
        score += gain;
        if (headshot) headshotKills++;

        string weapon = string.IsNullOrEmpty(weaponName) ? "汉阳造" : weaponName;
        AddKillFeed("你", PlayerNameColor, "敌军", weapon, headshot);

        killBannerText = (headshot ? "爆头击杀" : "击杀") + "　+" + gain;
        killBannerHeadshot = headshot;
        killBannerTimer = 1.5f;

        hitMarkerTimer = 0.2f;
        hitMarkerHeadshot = headshot;
        hitMarkerKill = true;
    }

    /// <summary>战友击杀敌人：只进击杀信息，不计入玩家战功。</summary>
    public void ReportAllyKill(string allyName, string weaponName)
    {
        string name = string.IsNullOrEmpty(allyName) ? "战友" : allyName;
        string weapon = string.IsNullOrEmpty(weaponName) ? "汉阳造" : weaponName;
        AddKillFeed(name, AllyNameColor, "敌军", weapon, false);
    }

    /// <summary>战友阵亡。</summary>
    public void ReportAllyDown(string allyName)
    {
        string name = string.IsNullOrEmpty(allyName) ? "战友" : allyName;
        AddFeed("<color=#D46A5A>敌军</color>  ▸  <color=#E4DDCB>"
                + name + " 阵亡</color>");
    }

    /// <summary>冲锋号振奋期间杀敌回血。</summary>
    public void ReportHeal(float amount)
    {
        AddFeed("<color=#7FD48A>振奋 · 杀敌回血 +" + Mathf.RoundToInt(amount) + "</color>");
    }

    /// <summary>重新开始一局时清空战斗反馈。</summary>
    public void ResetCombatFeedback()
    {
        feed.Clear();
        hitMarkerTimer = 0f;
        killBannerTimer = 0f;
        killBannerText = "";
        score = 0;
        headshotKills = 0;
        damageFlash = 0f;
        lowHealthBlend = 0f;
        bloodAlpha = 0f;
        bloodHold = 0f;
    }

    private static void DrawBar(Rect rect, float fill, Color color)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        if (fill > 0f)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y,
                rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
    }

    private static void DrawFullTexture(Texture2D texture, Color color)
    {
        if (texture == null) return;

        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight),
            texture, ScaleMode.StretchToFill, true);
        GUI.color = previous;
    }

    private static void DrawOverlay(Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private static void SetAlpha(GUIStyle style, float alpha)
    {
        Color color = style.normal.textColor;
        color.a = alpha;
        style.normal.textColor = color;
    }

    private void DrawTitle()
    {
        DrawOverlay(new Color(0.035f, 0.04f, 0.045f, 0.95f));

        bool level2 = selectedLevel == 2;

        GUI.Label(new Rect(200f, 70f, 1200f, 116f), "星火：渡", titleStyle);
        GUI.Label(new Rect(200f, 192f, 1200f, 40f),
            level2 ? "1935 年 · 赤水河 · 川黔边境" : "1934 年冬 · 湘江 · 界首渡口",
            subtitleStyle);

        string story = level2
            ? "1935 年，红军在川黔边境四次渡过赤水河。\n" +
              "在敌人重兵的缝隙里来回穿插，把追兵拖得筋疲力尽。\n\n" +
              "你是这支队伍里的尖兵。没有防线可守，只有不停奔跑：\n" +
              "打完一枪就换一个地方，谁在原地恋战，\n" +
              "谁就会被合围的炮火吞掉。"
            : "1934 年 11 月底，中央红军在湘江边陷入重围。\n" +
              "界首浮桥是大部队唯一的生路，你们要顶住追兵，\n" +
              "让队伍一批批渡过江去——桥在，人就还有活路。\n\n" +
              "你是红三军团的一名普通战士。敌军炮火昼夜不停，\n" +
              "弹药只有二十发——打光了，就只能从阵亡敌军身上搜刮。";

        GUI.Label(new Rect(200f, 258f, 1200f, 210f), story, storyStyle);

        string history = level2
            ? "四渡赤水是运动战的神来之笔：三个多月里四次渡河，\n" +
              "在几十万敌军的缝隙中穿插，最终跳出包围圈，为长征的胜利赢得了主动权。"
            : "湘江战役是长征途中最惨烈的一战：红军由八万余人锐减至三万余人，\n" +
              "却保住了革命的火种，为两个月后遵义会议的伟大转折留下了火种。";

        GUI.Label(new Rect(200f, 468f, 1200f, 64f), history, historyStyle);

        GUI.Label(new Rect(100f, 556f, 1400f, 96f),
            "WASD 移动　Shift 奔跑　Space 跳跃　C 蹲下　Z 趴下　Q / E 左右探头　V 视角切换　Tab 火把\n" +
            "左键 射击 / 挥刀　右键 机瞄 / 突刺　滚轮 切换武器　R 装填　T 手榴弹　F 信念爆发",
            controlsStyle);

        GUI.Label(new Rect(330f, 668f, 440f, 44f),
            (level2 ? "▶ " : "") + "一 · 血战湘江", level2 ? levelStyle : levelSelectedStyle);
        GUI.Label(new Rect(830f, 668f, 440f, 44f),
            (level2 ? "" : "▶ ") + "二 · 四渡赤水", level2 ? levelSelectedStyle : levelStyle);

        GUI.Label(new Rect(500f, 716f, 600f, 32f), "A / D 选择关卡", footerStyle);

        SetAlpha(startStyle, startAlpha);
        GUI.Label(new Rect(500f, 756f, 600f, 50f), "按 Enter 开始", startStyle);

        GUI.Label(new Rect(400f, 842f, 800f, 30f),
            "程序化原型 · 无外部美术与音频资源", footerStyle);
    }

    private void DrawBriefing()
    {
        DrawOverlay(new Color(0.025f, 0.03f, 0.035f, 0.95f));

        GUI.Label(new Rect(200f, 52f, 1200f, 72f), briefingTitle, briefingTitleStyle);

        GUI.color = new Color(0.84f, 0.67f, 0.32f, 0.8f);
        GUI.DrawTexture(new Rect(440f, 138f, 720f, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(230f, 166f, 1140f, 96f), briefingStory, briefingBodyStyle);

        GUI.Label(new Rect(230f, 278f, 1140f, 32f), "【历史背景】", briefingHeaderStyle);
        GUI.Label(new Rect(230f, 314f, 1140f, 96f), briefingHistory, briefingBodyStyle);

        GUI.Label(new Rect(230f, 424f, 1140f, 32f), "【历史影响】", briefingHeaderStyle);
        GUI.Label(new Rect(230f, 460f, 1140f, 96f), briefingImpact, briefingBodyStyle);

        GUI.Label(new Rect(230f, 570f, 1140f, 32f), "【作战目标】", briefingHeaderStyle);
        GUI.Label(new Rect(230f, 606f, 1140f, 72f), briefingObjective, briefingBodyStyle);

        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 3.2f);
        briefingHintStyle.normal.textColor = new Color(0.96f, 0.82f, 0.5f, pulse);
        GUI.Label(new Rect(400f, 744f, 800f, 44f), "按 Enter 出发", briefingHintStyle);

        GUI.Label(new Rect(400f, 826f, 800f, 28f),
            "WASD 移动　左键射击　E 交互　G 炸桥　Enter 继续", briefingFooterStyle);
    }

    private void DrawVictory()
    {
        float age = Time.time - victoryShownAt;
        float fade = Mathf.Clamp01(age / 0.9f);
        float historyFade = Mathf.Clamp01((age - 4.9f) / 1.2f);
        float hintFade = Mathf.Clamp01((age - 6.4f) / 0.8f);

        // 顶部：战功与战绩
        GUI.color = new Color(0.02f, 0.025f, 0.03f, 0.7f * fade);
        GUI.DrawTexture(new Rect(0f, 0f, RefWidth, 178f), Texture2D.whiteTexture);

        GUI.color = new Color(0.84f, 0.67f, 0.32f, 0.7f * fade);
        GUI.DrawTexture(new Rect(0f, 176f, RefWidth, 2f), Texture2D.whiteTexture);

        GUI.color = new Color(1f, 1f, 1f, fade);
        GUI.Label(new Rect(80f, 34f, 1440f, 64f), victoryTitle, victoryTitleStyle);
        GUI.Label(new Rect(80f, 108f, 1440f, 34f), victoryStats, victoryStatsStyle);
        GUI.color = Color.white;

        // 底部：历史影响与重开提示
        if (historyFade > 0.001f)
        {
            GUI.color = new Color(0.02f, 0.025f, 0.03f, 0.62f * historyFade);
            GUI.DrawTexture(new Rect(0f, 714f, RefWidth, 186f), Texture2D.whiteTexture);

            GUI.color = new Color(0.84f, 0.67f, 0.32f, 0.6f * historyFade);
            GUI.DrawTexture(new Rect(0f, 714f, RefWidth, 2f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, historyFade);
            GUI.Label(new Rect(200f, 734f, 1200f, 62f), victoryHistory, victoryHistoryStyle);
            GUI.color = Color.white;
        }

        if (hintFade > 0.001f)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 3.2f);
            victoryHintStyle.normal.textColor =
                new Color(0.96f, 0.82f, 0.5f, hintFade * pulse);
            GUI.Label(new Rect(400f, 840f, 800f, 40f), victoryHint, victoryHintStyle);
        }
    }

    private void DrawEnd()
    {
        DrawOverlay(new Color(0.02f, 0.02f, 0.025f, 0.9f));

        endTitleStyle.normal.textColor = endVictory
            ? new Color(0.96f, 0.82f, 0.5f)
            : new Color(0.82f, 0.32f, 0.26f);

        GUI.Label(new Rect(200f, 260f, 1200f, 110f), endTitle, endTitleStyle);
        GUI.Label(new Rect(250f, 400f, 1100f, 220f), endBody, endBodyStyle);
        GUI.Label(new Rect(400f, 700f, 800f, 50f), "按 Enter 重新开始", restartStyle);
    }

    // ───────────────────────────── 暂停菜单 ─────────────────────────────

    private void DrawPauseMenu()
    {
        // 虚化背景：暂停瞬间的小尺寸快照直接拉伸铺满，双线性插值就是模糊
        GUI.color = Color.white;
        if (pauseBackdrop != null)
        {
            GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), pauseBackdrop,
                ScaleMode.StretchToFill);
        }

        GUI.color = new Color(0.02f, 0.025f, 0.035f, 0.5f);
        GUI.DrawTexture(new Rect(0f, 0f, RefWidth, RefHeight), Texture2D.whiteTexture);

        float panelWidth = RefWidth * 0.25f;

        // 左侧面板
        GUI.color = new Color(0.045f, 0.05f, 0.06f, 0.93f);
        GUI.DrawTexture(new Rect(0f, 0f, panelWidth, RefHeight), Texture2D.whiteTexture);

        // 右缘金线 + 顶部红条
        GUI.color = new Color(0.8f, 0.6f, 0.26f, 0.85f);
        GUI.DrawTexture(new Rect(panelWidth - 2f, 0f, 2f, RefHeight), Texture2D.whiteTexture);
        GUI.color = new Color(0.56f, 0.11f, 0.09f, 0.96f);
        GUI.DrawTexture(new Rect(0f, 0f, panelWidth, 92f), Texture2D.whiteTexture);
        GUI.color = new Color(0.8f, 0.6f, 0.26f, 0.9f);
        GUI.DrawTexture(new Rect(0f, 92f, panelWidth, 2f), Texture2D.whiteTexture);

        GUI.Label(new Rect(28f, 16f, panelWidth - 52f, 52f), "暂　停", pauseTitleStyle);
        GUI.Label(new Rect(30f, 64f, panelWidth - 56f, 26f), "战斗已停止 · 按 ESC 继续", pauseLevelStyle);

        // 战功数据
        GUI.color = new Color(0.8f, 0.6f, 0.26f, 0.55f);
        GUI.DrawTexture(new Rect(28f, 128f, panelWidth - 56f, 1.5f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(28f, 142f, panelWidth - 56f, 26f), "战　绩", pauseLevelStyle);
        GUI.Label(new Rect(28f, 172f, panelWidth - 56f, 250f), pauseStats, pauseStatStyle);

        // 按钮
        float buttonWidth = panelWidth - 56f;
        float buttonHeight = 56f;
        float gap = 12f;
        float y = RefHeight - (buttonHeight + gap) * 4f - 34f;

        if (GUI.Button(new Rect(28f, y, buttonWidth, buttonHeight), "继续战斗", pauseButtonStyle))
        {
            pauseAction = 1;
        }
        y += buttonHeight + gap;

        if (GUI.Button(new Rect(28f, y, buttonWidth, buttonHeight), "重新开始", pauseButtonStyle))
        {
            pauseAction = 2;
        }
        y += buttonHeight + gap;

        if (GUI.Button(new Rect(28f, y, buttonWidth, buttonHeight), "选择战役", pauseButtonStyle))
        {
            pauseAction = 3;
        }
        y += buttonHeight + gap;

        if (GUI.Button(new Rect(28f, y, buttonWidth, buttonHeight), "退出游戏", pauseButtonStyle))
        {
            pauseAction = 4;
        }

        GUI.Label(new Rect(20f, RefHeight - 28f, panelWidth - 40f, 20f),
            "ESC 继续　·　点击按钮操作", pauseHintStyle);
    }

    // ───────────────────────────── 对外接口 ─────────────────────────────

    public void SetHudVisible(bool visible)
    {
        hudVisible = visible;
    }

    // ───────────────────────── 暂停菜单 ─────────────────────────

    /// <summary>打开 / 关闭暂停菜单。backdrop 是暂停瞬间的低分辨率快照，拉伸回全屏即为虚化。</summary>
    public void ShowPauseMenu(bool visible, string stats, Texture backdrop)
    {
        pauseVisible = visible;
        pauseStats = stats ?? "";
        pauseBackdrop = backdrop;
        if (!visible) pauseAction = 0;
        if (visible) EnsureStyles();
    }

    /// <summary>取出玩家在菜单里点的按钮：0 无、1 继续、2 重新开始、3 选择战役、4 退出。</summary>
    public int ConsumePauseAction()
    {
        int action = pauseAction;
        pauseAction = 0;
        return action;
    }

    public bool PauseVisible => pauseVisible;

    public void SetObjective(string text)
    {
        objective = text;
    }

    /// <summary>全屏黑场，0 = 透明、1 = 全黑；演出换机位时用。</summary>
    public void SetFade(float alpha)
    {
        fadeAlpha = Mathf.Clamp01(alpha);
    }

    /// <summary>设置任务目标点（跟着物体走），名字会显示在标记旁。</summary>
    public void SetWaypoint(Transform target, string label)
    {
        waypointTarget = target;
        waypointActive = target != null;
        waypointLabel = label;
    }

    /// <summary>设置任务目标点（固定坐标）。</summary>
    public void SetWaypoint(Vector3 position, string label)
    {
        waypointTarget = null;
        waypointPosition = position;
        waypointActive = true;
        waypointLabel = label;
    }

    public void ClearWaypoint()
    {
        waypointTarget = null;
        waypointActive = false;
    }

    /// <summary>大部队撤离进度，0~1；传负数隐藏（第二关、炸桥之后）。</summary>
    public void SetEvacuation(float progress)
    {
        evacProgress = progress < 0f ? -1f : Mathf.Clamp01(progress);
    }

    public void SetTimerText(string text)
    {
        timer = text;
    }

    public void SetWeatherText(string text)
    {
        weatherText = text;
    }

    public void ShowMessage(string text, float duration)
    {
        message = text;
        messageUntil = Time.time + duration;
    }

    public void SetPrompt(string text, float progress)
    {
        prompt = text;
        promptProgress = progress;
        promptVisible = !string.IsNullOrEmpty(text);
    }

    public void ShowTitlePanel()
    {
        titleVisible = true;
    }

    public void SetSelectedLevel(int level)
    {
        selectedLevel = Mathf.Clamp(level, 1, 2);
    }

    public void HideTitlePanel()
    {
        titleVisible = false;
    }

    public void ShowEnd(bool victory, string title, string body)
    {
        endVisible = true;
        endVictory = victory;
        endTitle = title;
        endBody = body;
    }

    /// <summary>战前简报：故事、真实历史背景、历史影响与本关目标。</summary>
    public void ShowBriefing(string title, string story, string history,
                             string impact, string objective)
    {
        briefingVisible = true;
        briefingTitle = title;
        briefingStory = story;
        briefingHistory = history;
        briefingImpact = impact;
        briefingObjective = objective;

        titleVisible = false;
        endVisible = false;
        victoryVisible = false;
    }

    public void HideBriefing()
    {
        briefingVisible = false;
    }

    /// <summary>胜利结算：顶部战绩条，主体画面留给敬礼的主角。</summary>
    public void ShowVictory(string title, string stats, string history, string hint)
    {
        victoryVisible = true;
        victoryShownAt = Time.time;
        victoryTitle = title;
        victoryStats = stats;
        victoryHistory = history;
        victoryHint = hint;

        titleVisible = false;
        briefingVisible = false;
        endVisible = false;
    }
}
