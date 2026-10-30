using UnityEngine;

/// <summary>
/// 视角切换（V）：第一人称 ⇄ 第三人称。
///
/// 第三人称是越肩视角：相机沿准星方向退后 3.6 米，
/// 碰到墙 / 战壕壁 / 桥栏会立刻收进来，不会穿模。
/// 第一人称时交还给 PlayerMovement 控制眼高，所以蹲下、趴下的镜头仍然平滑。
/// </summary>
[DisallowMultipleComponent]
public class CameraRig : MonoBehaviour
{
    [SerializeField] private float shoulderOffset = 0.62f;
    // 三档镜头距离：近 → 中 → 远，按 T 循环切换
    [SerializeField] private float[] thirdPersonDistances = { 2.4f, 3.6f, 5.0f };
    [SerializeField] private int defaultDistanceIndex = 1;
    [SerializeField] private float distanceLerpSpeed = 4.5f;
    [SerializeField] private float minDistance = 0.9f;
    [SerializeField] private float switchSpeed = 8f;

    private static readonly string[] DistanceNames = { "近", "中", "远" };

    private Camera viewCamera;
    private PlayerMovement movement;
    private PlayerShadowBody body;
    private WeaponController weapon;
    private Transform rifleView;
    private Transform pistolView;
    private Transform knifeView;
    private HUDController hud;

    private bool thirdPerson;
    private float blend;
    private float switchCooldown;
    private int distanceIndex;
    private float currentDistance;

    public bool IsThirdPerson => thirdPerson;

    public void Initialize(Camera cameraRef, PlayerMovement movementRef, PlayerShadowBody bodyRef,
                           WeaponController weaponRef, Transform rifleRef, Transform pistolRef,
                           Transform knifeRef, HUDController hudRef)
    {
        viewCamera = cameraRef;
        movement = movementRef;
        body = bodyRef;
        weapon = weaponRef;
        rifleView = rifleRef;
        pistolView = pistolRef;
        knifeView = knifeRef;
        hud = hudRef;

        distanceIndex = Mathf.Clamp(defaultDistanceIndex, 0, thirdPersonDistances.Length - 1);
        currentDistance = thirdPersonDistances[distanceIndex];
    }

    private void Update()
    {
        switchCooldown -= Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.V) && switchCooldown <= 0f)
        {
            SetThirdPerson(!thirdPerson);
        }

        // 第三人称下按 T 循环切换近 / 中 / 远三个镜头距离
        if (Input.GetKeyDown(KeyCode.T) && thirdPerson)
        {
            CycleDistance();
        }
    }

    /// <summary>切换一级镜头距离，最近的下一档是更远的那档。</summary>
    public void CycleDistance()
    {
        SetDistanceIndex(distanceIndex + 1);
    }

    public void SetDistanceIndex(int index, bool announce = true)
    {
        if (thirdPersonDistances == null || thirdPersonDistances.Length == 0) return;

        distanceIndex = ((index % thirdPersonDistances.Length) + thirdPersonDistances.Length)
                        % thirdPersonDistances.Length;

        if (!announce || hud == null) return;

        string name = distanceIndex < DistanceNames.Length
            ? DistanceNames[distanceIndex]
            : (distanceIndex + 1).ToString();
        hud.ShowMessage("镜头距离 · " + name, 1.6f);
    }

    /// <summary>调试 / 截图用：跳过推拉，直接落到当前档位。</summary>
    public void SnapDistance()
    {
        if (thirdPersonDistances == null || thirdPersonDistances.Length == 0) return;
        currentDistance = thirdPersonDistances[
            Mathf.Clamp(distanceIndex, 0, thirdPersonDistances.Length - 1)];
    }


    public void SetThirdPerson(bool value)
    {
        if (thirdPerson == value) return;

        thirdPerson = value;
        switchCooldown = 0.25f;

        if (body != null) body.SetVisible(value);
    }

    private void LateUpdate()
    {
        if (viewCamera == null) return;

        blend = Mathf.MoveTowards(blend, thirdPerson ? 1f : 0f, switchSpeed * Time.deltaTime);

        // 换档时镜头平滑推拉，不是"啪"地跳过去
        float targetDistance = thirdPersonDistances[Mathf.Clamp(distanceIndex, 0,
            thirdPersonDistances.Length - 1)];
        currentDistance = Mathf.Lerp(currentDistance, targetDistance,
            1f - Mathf.Exp(-distanceLerpSpeed * Time.deltaTime));

        // 第三人称时收起第一人称武器模型
        ApplyViewModelVisibility(blend > 0.35f);

        if (blend <= 0.001f)
        {
            // 完全回到第一人称：位置交还给 PlayerMovement
            return;
        }

        float eye = movement != null ? movement.EyeHeight : 1.6f;
        Vector3 firstPerson = new Vector3(0f, eye, 0f);

        Vector3 pivot = transform.root.position + Vector3.up * (eye + 0.08f);
        Quaternion rotation = viewCamera.transform.rotation;

        Vector3 desired = pivot
                          + rotation * new Vector3(shoulderOffset, 0f, -currentDistance);

        Vector3 direction = desired - pivot;
        float distance = direction.magnitude;

        if (distance > 0.01f)
        {
            distance = ClampByObstacles(pivot, direction / distance, distance);
            desired = pivot + direction.normalized * distance;
        }

        Vector3 localDesired = transform.root.InverseTransformPoint(desired);
        float t = blend * blend * (3f - 2f * blend);

        // 平时平滑，靠近遮挡时直接收进来，避免穿墙
        transform.localPosition = Vector3.Lerp(firstPerson, localDesired, t);
    }

    private float ClampByObstacles(Vector3 origin, Vector3 direction, float distance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(origin, 0.26f, direction, distance, ~0,
                                                  QueryTriggerInteraction.Ignore);

        float allowed = distance;
        Transform playerRoot = transform.root;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].distance <= 0.01f) continue;
            if (hits[i].collider.transform.IsChildOf(playerRoot)) continue;

            if (hits[i].distance < allowed)
            {
                allowed = hits[i].distance - 0.08f;
            }
        }

        return Mathf.Clamp(allowed, minDistance, distance);
    }

    private void ApplyViewModelVisibility(bool hide)
    {
        // 投弹、结算演出会把枪模整体收起来，这时候不能按武器槽又放回来
        if (weapon != null && weapon.ViewModelHidden) hide = true;

        if (rifleView != null)
        {
            bool show = !hide && (weapon == null || weapon.IsRifle);
            if (rifleView.gameObject.activeSelf != show) rifleView.gameObject.SetActive(show);
        }

        if (pistolView != null)
        {
            bool show = !hide && weapon != null && weapon.IsPistol;
            if (pistolView.gameObject.activeSelf != show) pistolView.gameObject.SetActive(show);
        }

        if (knifeView != null)
        {
            bool show = !hide && weapon != null && weapon.IsKnife;
            if (knifeView.gameObject.activeSelf != show) knifeView.gameObject.SetActive(show);
        }
    }
}
