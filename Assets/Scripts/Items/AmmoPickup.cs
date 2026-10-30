using System.Collections;
using UnityEngine;

/// <summary>
/// 弹药补给包。
///
/// 策划案要求：每个补给包只给 3 发，且不能突破武器的备弹上限。
/// 拾取时如果玩家背包已满，补给包不会被消耗，可以留着以后再捡。
///
/// 使用方式：
///   1. 物体上挂 Collider，并勾选 Is Trigger；
///   2. 玩家标签设为 "Player"（或修改下方 playerTag）；
///   3. 把补给包拖进场景即可。
/// </summary>
[RequireComponent(typeof(Collider))]
public class AmmoPickup : MonoBehaviour
{
    /// <summary>补给种类：步枪子弹给汉阳造，弹夹给驳壳枪。</summary>
    public enum Kind
    {
        Rifle,
        Pistol
    }

    [Header("补给")]
    [Tooltip("每个补给包增加的备弹数：步枪 3 发，手枪弹夹 6 发")]
    [SerializeField] private int ammoAmount = 3;

    [Tooltip("拾取后多久重新出现。0 表示不刷新")]
    [SerializeField] private float respawnTime = 0f;

    [SerializeField] private Kind kind = Kind.Rifle;

    [Header("玩家识别")]
    [SerializeField] private string playerTag = "Player";

    [Header("表现")]
    [SerializeField] private AudioClip pickupClip;
    [SerializeField] private GameObject pickupEffect;
    [SerializeField] private GameObject visualRoot; // 拾取后要隐藏的模型

    private bool used;

    /// <summary>运行时配置补给包。</summary>
    public void Configure(int amount, float respawn)
    {
        ammoAmount = Mathf.Max(1, amount);
        respawnTime = Mathf.Max(0f, respawn);
    }

    /// <summary>在指定位置生成一个可拾取的步枪弹药掉落物。</summary>
    public static void SpawnPickup(Vector3 position, int amount = 3)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "AmmoDrop";
        go.transform.position = position;
        go.transform.localScale = new Vector3(0.3f, 0.2f, 0.22f);
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(0.82f, 0.66f, 0.24f),
                new Color(0.45f, 0.3f, 0.04f));
        }

        // 弹夹上的一道深色包布，远看比纯色方块更像战利品
        GameObject band = GameObject.CreatePrimitive(PrimitiveType.Cube);
        band.name = "DropStrap";
        band.transform.SetParent(go.transform, false);
        band.transform.localPosition = Vector3.zero;
        band.transform.localScale = new Vector3(1.06f, 0.32f, 1.06f);
        Renderer bandRenderer = band.GetComponent<Renderer>();
        if (bandRenderer != null)
        {
            bandRenderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(0.25f, 0.22f, 0.16f));
        }
        Collider bandCollider = band.GetComponent<Collider>();
        if (bandCollider != null) Object.Destroy(bandCollider);

        BoxCollider col = go.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;

        AmmoPickup pickup = go.AddComponent<AmmoPickup>();
        pickup.kind = Kind.Rifle;
        pickup.Configure(amount, 0f);
    }

    /// <summary>在指定位置生成一个手枪弹夹掉落物：立着的金属弹匣，口上露一发黄铜弹。</summary>
    public static void SpawnPistolPickup(Vector3 position, int amount = 6)
    {
        GameObject go = new GameObject("PistolMagDrop");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "PistolMagBody";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.11f, 0f);
        body.transform.localScale = new Vector3(0.07f, 0.21f, 0.13f);
        Renderer bodyRenderer = body.GetComponent<Renderer>();
        if (bodyRenderer != null)
        {
            bodyRenderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(0.3f, 0.31f, 0.34f), new Color(0.06f, 0.07f, 0.08f));
        }
        Collider bodyCollider = body.GetComponent<Collider>();
        if (bodyCollider != null) Object.Destroy(bodyCollider);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "PistolMagFloor";
        floor.transform.SetParent(go.transform, false);
        floor.transform.localPosition = new Vector3(0f, 0.008f, 0f);
        floor.transform.localScale = new Vector3(0.082f, 0.018f, 0.145f);
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        if (floorRenderer != null)
        {
            floorRenderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(0.5f, 0.51f, 0.54f), new Color(0.1f, 0.1f, 0.12f));
        }
        Collider floorCollider = floor.GetComponent<Collider>();
        if (floorCollider != null) Object.Destroy(floorCollider);

        GameObject round = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        round.name = "PistolMagRound";
        round.transform.SetParent(go.transform, false);
        round.transform.localPosition = new Vector3(0f, 0.228f, 0f);
        round.transform.localScale = new Vector3(0.045f, 0.06f, 0.045f);
        Renderer roundRenderer = round.GetComponent<Renderer>();
        if (roundRenderer != null)
        {
            roundRenderer.sharedMaterial = LevelBuilder.MakeMaterial(
                new Color(0.76f, 0.6f, 0.26f), new Color(0.28f, 0.19f, 0.05f));
        }
        Collider roundCollider = round.GetComponent<Collider>();
        if (roundCollider != null) Object.Destroy(roundCollider);

        BoxCollider col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, 0.12f, 0f);
        col.size = new Vector3(0.26f, 0.34f, 0.32f);

        AmmoPickup pickup = go.AddComponent<AmmoPickup>();
        pickup.kind = Kind.Pistol;
        pickup.Configure(amount, 0f);
    }

    private void Reset()
    {
        // 挂上组件时自动把碰撞体设为触发器
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (used) return;
        if (!other.CompareTag(playerTag)) return;

        int added;
        if (kind == Kind.Pistol)
        {
            Pistol pistol = other.GetComponentInParent<Pistol>();
            if (pistol == null) return;
            added = pistol.AddReserveAmmo(ammoAmount);
        }
        else
        {
            // 从玩家身上找到步枪组件（可能在父级）
            Hanyang88Rifle rifle = other.GetComponentInParent<Hanyang88Rifle>();
            if (rifle == null) return;
            added = rifle.AddReserveAmmo(ammoAmount);
        }

        // 严格判断：备弹已满则拾取失败，补给包留在原地
        if (added <= 0) return;

        used = true;

        if (pickupClip != null)
        {
            AudioSource.PlayClipAtPoint(pickupClip, transform.position);
        }

        if (pickupEffect != null)
        {
            Instantiate(pickupEffect, transform.position, Quaternion.identity);
        }

        SetVisible(false);

        if (respawnTime > 0f)
        {
            StartCoroutine(RespawnRoutine());
        }
        else
        {
            // 不刷新则直接销毁，避免留下空触发器
            Destroy(gameObject, 0.1f);
        }
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnTime);

        used = false;
        SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(visible);
        }
        else
        {
            // 没有指定模型根节点时，关闭所有渲染器
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = visible;
            }
        }
    }
}
