using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CustomFolderCustomization
{
    const string SettingsFileName = "CustomFolderSettings.asset";
    const string ScriptFileName = "CustomFolderCustomization.cs";

    static CustomFolderSettings _settings;
    static string _toolFolder;
    static double _nextSettingsRefreshTime;

    static CustomFolderCustomization()
    {
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowGUI;
        EditorApplication.projectChanged += InvalidateSettingsCache;
    }

    [MenuItem("Tools/Custom Folders/Open Settings")]
    public static void OpenOrCreateSettings()
    {
        CustomFolderSettings settings = LoadOrCreateSettings();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }

    static void InvalidateSettingsCache()
    {
        _settings = null;
        _toolFolder = null;
        _nextSettingsRefreshTime = 0d;
    }

    /// <summary>
    /// Wherever this tool folder was copied, settings live next to these scripts.
    /// </summary>
    static string GetToolFolder()
    {
        if (!string.IsNullOrEmpty(_toolFolder))
            return _toolFolder;

        string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(ScriptFileName) + " t:MonoScript");
        for (int i = 0; i < guids.Length; i++)
        {
            string scriptPath = AssetDatabase.GUIDToAssetPath(guids[i]).Replace('\\', '/');
            if (!string.Equals(Path.GetFileName(scriptPath), ScriptFileName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            _toolFolder = Path.GetDirectoryName(scriptPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(_toolFolder))
                return _toolFolder;
        }

        _toolFolder = "Assets/Editor/Custom Folders";
        return _toolFolder;
    }

    static string GetSettingsPath()
    {
        return GetToolFolder() + "/" + SettingsFileName;
    }

    static CustomFolderSettings GetSettings()
    {
        if (_settings != null)
            return _settings;

        if (EditorApplication.timeSinceStartup < _nextSettingsRefreshTime)
            return null;

        _nextSettingsRefreshTime = EditorApplication.timeSinceStartup + 1d;
        _settings = FindSettingsAsset();
        return _settings;
    }

    static CustomFolderSettings FindSettingsAsset()
    {
        CustomFolderSettings atToolPath =
            AssetDatabase.LoadAssetAtPath<CustomFolderSettings>(GetSettingsPath());
        if (atToolPath != null)
            return atToolPath;

        string[] guids = AssetDatabase.FindAssets("t:CustomFolderSettings");
        if (guids == null || guids.Length == 0)
            return null;

        return AssetDatabase.LoadAssetAtPath<CustomFolderSettings>(
            AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    static CustomFolderSettings LoadOrCreateSettings()
    {
        CustomFolderSettings settings = FindSettingsAsset();
        if (settings != null)
        {
            _settings = settings;
            return settings;
        }

        string toolFolder = GetToolFolder();
        EnsureAssetFolders(toolFolder);

        settings = ScriptableObject.CreateInstance<CustomFolderSettings>();
        settings.rules.Add(new FolderStyleRule
        {
            enabled = true,
            matchMode = FolderMatchMode.FolderName,
            match = "Scripts",
            color = new Color(0.3f, 0.7f, 1f, 1f),
        });

        string settingsPath = toolFolder + "/" + SettingsFileName;
        AssetDatabase.CreateAsset(settings, settingsPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _settings = settings;
        return settings;
    }

    static void EnsureAssetFolders(string assetFolderPath)
    {
        assetFolderPath = assetFolderPath.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(assetFolderPath))
            return;

        string[] parts = assetFolderPath.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            return;

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static void OnProjectWindowGUI(string guid, Rect rect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        CustomFolderSettings settings = GetSettings();
        if (settings == null || settings.rules == null || settings.rules.Count == 0)
            return;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
            return;

        string folderName = Path.GetFileName(path);
        if (!settings.TryGetRule(path, folderName, out FolderStyleRule rule))
            return;

        bool isGridView = rect.height > 20f;
        Rect folderIconRect = GetFolderIconRect(rect, isGridView);

        Texture folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
        if (folderIcon != null)
        {
            Color originalColor = GUI.color;
            GUI.color = rule.color;
            GUI.DrawTexture(folderIconRect, folderIcon, ScaleMode.ScaleToFit);
            GUI.color = originalColor;
        }

        if (rule.icon != null)
        {
            Rect subIconRect = GetSubIconRect(folderIconRect, isGridView);
            GUI.DrawTexture(subIconRect, rule.icon, ScaleMode.ScaleToFit);
        }
    }

    static Rect GetFolderIconRect(Rect selectionRect, bool isGridView)
    {
        if (isGridView)
        {
            float size = Mathf.Min(selectionRect.width, selectionRect.height - 14f);
            float x = selectionRect.x + (selectionRect.width - size) * 0.5f;
            return new Rect(x, selectionRect.y, size, size);
        }

        return new Rect(selectionRect.x, selectionRect.y, 16f, 16f);
    }

    static Rect GetSubIconRect(Rect folderRect, bool isGridView)
    {
        if (isGridView)
        {
            float badgeSize = folderRect.width * 0.35f;
            return new Rect(
                folderRect.xMax - badgeSize,
                folderRect.yMax - badgeSize,
                badgeSize,
                badgeSize);
        }

        return new Rect(folderRect.x + 5f, folderRect.y + 5f, 10f, 10f);
    }
}
