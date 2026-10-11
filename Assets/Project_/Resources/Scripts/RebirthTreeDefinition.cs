using System;
using System.Collections.Generic;
using UnityEngine;

public enum RebirthTreeBonus
{
    StartingGold,
    GoldIncome,
    MonsterHealth,
    MonsterDamage,
    ArmyCapacity,
    AutoSummonSpeed,
    SummonCostDiscount,
    SpawnProtection,
    MonsterMoveSpeed,
    MonsterAttackSpeed
}

[Serializable]
public class RebirthTreeNodeDefinition
{
    [Tooltip("Mã ổn định của node. Không đổi mã sau khi đã phát hành để tiến trình đã mua không bị sai.")]
    public string id;
    [Tooltip("Tên hiển thị trên node và bảng thông tin.")]
    public string displayName;
    [TextArea(2, 4), Tooltip("Mô tả ngắn, nói rõ hiệu lực vĩnh viễn của node.")]
    public string description;
    [Tooltip("Số điểm Rebirth cần để mua node này.")]
    [Min(1)] public int cost = 1;
    [Tooltip("Các node phải sở hữu trước. Danh sách rỗng dùng cho node gốc.")]
    public int[] prerequisites = Array.Empty<int>();
    [Tooltip("Loại chỉ số được tăng.")]
    public RebirthTreeBonus bonus;
    [Tooltip("Giá trị cộng thêm. Các bonus theo phần trăm dùng 0.1 = 10%; Army Capacity dùng số nguyên.")]
    public float value;
    [Tooltip("Icon hiển thị trên cây nâng cấp.")]
    public Sprite icon;
}

[CreateAssetMenu(menuName = "Hero Doom/Rebirth Tree Definition", fileName = "Rebirth Tree Definition")]
public sealed class RebirthTreeDefinition : ScriptableObject
{
    [Tooltip("Các node theo thứ tự của cây. Prerequisites dùng chỉ số trong danh sách này.")]
    public List<RebirthTreeNodeDefinition> nodes = new();

    public RebirthTreeNodeDefinition GetNode(int index) =>
        index >= 0 && index < nodes.Count ? nodes[index] : null;
}
