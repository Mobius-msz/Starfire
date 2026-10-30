using UnityEngine;

/// <summary>命中部位。伤害倍率由武器侧配置，部位本身只负责"我是哪儿"。</summary>
public enum BodyZone
{
    Head,   // 头部：100 伤害
    Torso,  // 躯干：40 伤害
    Limb    // 四肢：20 伤害
}

/// <summary>
/// 挂在敌人的每一个碰撞体上（头、胸、四肢各一个 Collider）。
/// 武器射线命中后通过它判断打中了哪个部位。
///
/// 配置示例：
///   Enemy_Head  (CapsuleCollider) → HitZone.zone = Head
///   Enemy_Torso (CapsuleCollider) → HitZone.zone = Torso
///   Enemy_Leg   (CapsuleCollider) → HitZone.zone = Limb
/// 所有碰撞体的父物体上挂 Health 组件。
/// </summary>
[DisallowMultipleComponent]
public class HitZone : MonoBehaviour
{
    [Tooltip("该碰撞体代表的部位")]
    [SerializeField] private BodyZone zone = BodyZone.Torso;

    public BodyZone Zone
    {
        get => zone;
        set => zone = value;
    }

    private void Reset()
    {
        // 根据物体名称自动猜测部位，减少手工配置量
        string n = gameObject.name.ToLowerInvariant();
        if (n.Contains("head")) zone = BodyZone.Head;
        else if (n.Contains("leg") || n.Contains("arm") || n.Contains("limb")) zone = BodyZone.Limb;
        else zone = BodyZone.Torso;
    }
}
