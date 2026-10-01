using UnityEngine;

[System.Serializable]
public sealed class HeroUpgradeUtilityProfile
{
    [Tooltip("Utility weight when the Hero is missing health.")][Range(0f, 2f)] public float survivalNeed;
    [Tooltip("Utility weight when enemies are close enough to threaten the Hero.")][Range(0f, 2f)] public float closeThreat;
    [Tooltip("Utility weight when several enemies are close together.")][Range(0f, 2f)] public float enemyCrowding;
    [Tooltip("Utility weight for nearby ranged enemies.")][Range(0f, 2f)] public float rangedThreat;
    [Tooltip("Utility weight for enemies already weakened by the Hero.")][Range(0f, 2f)] public float weakenedEnemies;
    [Tooltip("Utility weight for available experience and progress toward the next level.")][Range(0f, 2f)] public float experienceUrgency;
    [Tooltip("Utility weight when a captured objective is near enough to contest.")][Range(0f, 2f)] public float objectivePressure;
    [Tooltip("Utility weight that rewards a compatible existing offensive build.")][Range(0f, 2f)] public float offensiveSynergy;

    public float Score(float[] state)
    {
        if (state == null || state.Length < 8) return 0f;
        return survivalNeed * state[0] + closeThreat * state[1] + enemyCrowding * state[2] +
               rangedThreat * state[3] + weakenedEnemies * state[4] + experienceUrgency * state[5] +
               objectivePressure * state[6] + offensiveSynergy * state[7];
    }
}

[CreateAssetMenu(menuName = "Hero Doom/Hero Upgrade")]
public class HeroUpgradeDefinition : ScriptableObject
{
    public HeroUpgradeCategory category;
    public HeroUpgradeType type;
    public string displayName;
    public Sprite icon;
    [TextArea(2, 4)] public string description;
    [Header("Hero AI utility weights")]
    public HeroUpgradeUtilityProfile utility = new();
}

public enum HeroUpgradeCategory
{
    Stat,
    Skill
}
