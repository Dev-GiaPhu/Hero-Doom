using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(CustomFolderSettings))]
public class CustomFolderSettingsEditor : Editor
{
    ReorderableList _rulesList;
    SerializedProperty _rulesProperty;

    void OnEnable()
    {
        _rulesProperty = serializedObject.FindProperty("rules");
        _rulesList = new ReorderableList(serializedObject, _rulesProperty, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Folder Styles"),
            elementHeightCallback = GetElementHeight,
            drawElementCallback = DrawElement,
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Copy this whole Custom Folders folder into any project — paths resolve automatically.\n" +
            "Prefer Match Mode: Folder Name when sharing rules across projects.\n" +
            "First matching enabled rule wins. Drag rules to change priority.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selected Folder", GUILayout.Height(28f)))
                AddSelectedFolder();

            if (GUILayout.Button("Ping Settings", GUILayout.Height(28f), GUILayout.Width(110f)))
                EditorGUIUtility.PingObject(target);
        }

        EditorGUILayout.Space(6f);
        _rulesList.DoLayoutList();

        serializedObject.ApplyModifiedProperties();
    }

    float GetElementHeight(int index)
    {
        return EditorGUIUtility.singleLineHeight * 5f + 18f;
    }

    void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty element = _rulesProperty.GetArrayElementAtIndex(index);
        SerializedProperty enabled = element.FindPropertyRelative("enabled");
        SerializedProperty matchMode = element.FindPropertyRelative("matchMode");
        SerializedProperty match = element.FindPropertyRelative("match");
        SerializedProperty color = element.FindPropertyRelative("color");
        SerializedProperty icon = element.FindPropertyRelative("icon");

        float line = EditorGUIUtility.singleLineHeight;
        float pad = 2f;
        Rect row = new Rect(rect.x, rect.y + 2f, rect.width, line);

        Rect enabledRect = new Rect(row.x, row.y, 18f, line);
        Rect labelRect = new Rect(row.x + 22f, row.y, row.width - 22f, line);
        enabled.boolValue = EditorGUI.Toggle(enabledRect, enabled.boolValue);
        EditorGUI.LabelField(labelRect, BuildTitle(matchMode, match), EditorStyles.boldLabel);

        row.y += line + pad;
        EditorGUI.PropertyField(row, matchMode, new GUIContent("Match Mode"));

        row.y += line + pad;
        EditorGUI.PropertyField(row, match, new GUIContent("Match"));

        row.y += line + pad;
        EditorGUI.PropertyField(row, color, new GUIContent("Color"));

        row.y += line + pad;
        EditorGUI.PropertyField(row, icon, new GUIContent("Icon Badge"));
    }

    static string BuildTitle(SerializedProperty matchMode, SerializedProperty match)
    {
        string mode = matchMode.enumDisplayNames[matchMode.enumValueIndex];
        string value = string.IsNullOrWhiteSpace(match.stringValue) ? "(empty)" : match.stringValue;
        return $"{mode}: {value}";
    }

    void AddSelectedFolder()
    {
        Object selected = Selection.activeObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog(
                "Custom Folders",
                "Select a folder in the Project window first.",
                "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
        {
            EditorUtility.DisplayDialog(
                "Custom Folders",
                "The current selection is not a folder.",
                "OK");
            return;
        }

        serializedObject.Update();
        int index = _rulesProperty.arraySize;
        _rulesProperty.arraySize++;
        SerializedProperty element = _rulesProperty.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("enabled").boolValue = true;
        element.FindPropertyRelative("matchMode").enumValueIndex = (int)FolderMatchMode.FullPath;
        element.FindPropertyRelative("match").stringValue = path.Replace('\\', '/');
        element.FindPropertyRelative("color").colorValue = new Color(0.3f, 0.7f, 1f, 1f);
        element.FindPropertyRelative("icon").objectReferenceValue = null;
        serializedObject.ApplyModifiedProperties();

        EditorUtility.SetDirty(target);
        Debug.Log($"Added custom folder style for '{path}'. Set a color/icon on the new rule.", target);
    }
}
