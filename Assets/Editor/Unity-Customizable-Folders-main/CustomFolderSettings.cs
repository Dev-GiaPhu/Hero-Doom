using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "CustomFolderSettings",
    menuName = "Custom Folders/Settings")]
public class CustomFolderSettings : ScriptableObject
{
    [Tooltip("First matching enabled rule wins.")]
    public List<FolderStyleRule> rules = new List<FolderStyleRule>();

    public bool TryGetRule(string folderPath, string folderName, out FolderStyleRule rule)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            FolderStyleRule candidate = rules[i];
            if (candidate != null && candidate.Matches(folderPath, folderName))
            {
                rule = candidate;
                return true;
            }
        }

        rule = null;
        return false;
    }
}
