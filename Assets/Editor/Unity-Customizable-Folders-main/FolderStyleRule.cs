using System;
using UnityEngine;

[Serializable]
public class FolderStyleRule
{
    public bool enabled = true;

    [Tooltip("Folder Name matches every folder with that name. Full Path matches one exact Assets/... path.")]
    public FolderMatchMode matchMode = FolderMatchMode.FolderName;

    [Tooltip("Folder name (e.g. Scripts) or full asset path (e.g. Assets/Scripts).")]
    public string match = "Scripts";

    public Color color = new Color(0.3f, 0.7f, 1f, 1f);

    [Tooltip("Optional badge drawn on the folder. Leave empty for tint only.")]
    public Texture2D icon;

    public bool Matches(string folderPath, string folderName)
    {
        if (!enabled || string.IsNullOrWhiteSpace(match))
            return false;

        switch (matchMode)
        {
            case FolderMatchMode.FolderName:
                return string.Equals(folderName, match.Trim(), StringComparison.OrdinalIgnoreCase);
            case FolderMatchMode.FullPath:
                return string.Equals(folderPath, match.Trim().Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }
}

public enum FolderMatchMode
{
    FolderName = 0,
    FullPath = 1,
}
