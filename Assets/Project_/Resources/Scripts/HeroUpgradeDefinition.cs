using UnityEngine;

[CreateAssetMenu(menuName = "Hero Doom/Hero Upgrade")]
public class HeroUpgradeDefinition : ScriptableObject
{
    public HeroUpgradeType type;
    public string displayName;
    public Sprite icon;
}
