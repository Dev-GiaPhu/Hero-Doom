using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InfiniteMapGenerator))]
public sealed class InfiniteMapNoiseScatterEditor : Editor
{
    private static bool showWorld = true;
    private static bool showTerrain = true;
    private static bool showStructures = true;
    private static bool showAdvancedTerrain;
    private static bool showAdvancedStructures;
    private static bool showPreview;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();

        Section("MAP HIỂN THỊ", ref showWorld);
        if (showWorld)
        {
            Draw("renderCamera");
            Draw("radius");
            Draw("worldHalfExtentChunks");
            Draw("maxChunkSize");
            Draw("seed");
            Draw("groundPrefabs");
            Draw("waterPrefab");
            Draw("waterLevel");
        }

        Section("ĐẤT VÀ NƯỚC", ref showTerrain);
        if (showTerrain)
        {
            Draw("useTerrainNoise");
            if (Property("useTerrainNoise").boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Chỉ cần chỉnh Tỷ Lệ Đất, Kích Thước Mảng và Độ Uốn Bờ. Map luôn nối các vùng đất để Hero và quái có đường đi.",
                    MessageType.Info);
                Draw("landCoverage");
                Draw("terrainNoiseFrequency");
                Draw("terrainWarpStrength");
                Draw("terrainDetailBlend");
                Draw("guaranteeConnectedLand");
                if (Property("guaranteeConnectedLand").boolValue)
                    Draw("landConnectionWidth");
            }

            Section("TÙY CHỈNH NOISE NÂNG CAO", ref showAdvancedTerrain);
            if (showAdvancedTerrain && Property("useTerrainNoise").boolValue)
            {
                Draw("terrainNoiseOctaves");
                Draw("terrainNoisePersistence");
                Draw("terrainNoiseLacunarity");
                Draw("useTerrainDomainWarp");
                if (Property("useTerrainDomainWarp").boolValue)
                    Draw("terrainWarpFrequency");
                Draw("terrainDetailFrequency");
                Draw("terrainDetailOctaves");
                Draw("terrainNoiseOffset");
            }
        }

        Section("CÔNG TRÌNH VÀ TRANG TRÍ", ref showStructures);
        if (showStructures)
        {
            Draw("enableNoisePrefabScatter");
            if (Property("enableNoisePrefabScatter").boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Prefab chỉ sinh trên đất. Hệ thống tự chừa đường đi, giữ khoảng cách giữa công trình và kiểm tra toàn footprint để không tràn ra nước.",
                    MessageType.Info);
                Draw("defaultNoisePrefabPool");
                Draw("noiseLayerCount");
                Draw("noiseLayers");
                Draw("scatterSpacing");
                Draw("scatterChance");
                Draw("maxScatterPerChunk");
                Draw("structureMinimumSeparation");
                Draw("structureWaterClearance");

                Section("TÙY CHỈNH CÔNG TRÌNH NÂNG CAO", ref showAdvancedStructures);
                if (showAdvancedStructures)
                {
                    Draw("scatterNoiseFrequency");
                    Draw("noiseOctaves");
                    Draw("noisePersistence");
                    Draw("noiseLacunarity");
                    Draw("noiseOffset");
                    Draw("noiseDensityInfluence");
                    Draw("cellJitter");
                    Draw("scatterEdgePadding");
                    Draw("scatterGroundOffset");
                    Draw("randomizeYaw");
                    if (!Property("randomizeYaw").boolValue)
                        Draw("fixedYaw");
                    Draw("randomScale");
                    Draw("snapToGround");
                    if (Property("snapToGround").boolValue)
                    {
                        Draw("scatterGroundMask");
                        Draw("groundRayStartHeight");
                        Draw("skipWhenGroundNotFound");
                        Draw("alignToGroundNormal");
                    }
                    Draw("avoidOccupiedPoints");
                    if (Property("avoidOccupiedPoints").boolValue)
                    {
                        Draw("avoidanceMask");
                        Draw("avoidanceRadius");
                    }
                    Draw("structuresBlockNavigation");
                    if (Property("structuresBlockNavigation").boolValue)
                    {
                        Draw("structureNavObstaclePadding");
                        Draw("structurePathHalfWidth");
                        Draw("structureLayerName");
                    }
                }
            }
        }

        Section("PREVIEW TRONG EDITOR", ref showPreview);
        if (showPreview)
        {
            Draw("previewChunkRadius");
            InfiniteMapGenerator map = (InfiniteMapGenerator)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying || !Property("enableNoisePrefabScatter").boolValue))
            {
                if (GUILayout.Button("Tạo Preview Công Trình"))
                    map.GenerateNoisePrefabPreview();
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Xóa Preview Công Trình"))
                    map.ClearNoisePrefabPreview();
            }
        }

        bool inspectorChanged = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        if (inspectorChanged && Application.isPlaying)
        {
            foreach (Object inspectedObject in targets)
                if (inspectedObject is InfiniteMapGenerator map)
                    map.RefreshMapFromInspector();
        }

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Live Update đang bật: thay đổi thông số sẽ tạo lại địa hình và công trình ngay.", MessageType.None);
    }

    private SerializedProperty Property(string name) => serializedObject.FindProperty(name);

    private void Draw(string name)
    {
        SerializedProperty property = Property(name);
        if (property != null)
            EditorGUILayout.PropertyField(property, true);
    }

    private static void Section(string title, ref bool expanded)
    {
        EditorGUILayout.Space(7f);
        expanded = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
    }

    [MenuItem("Hero Doom/Map/Generate Noise Prefab Preview", true)]
    private static bool ValidateGeneratePreviewMenu()
    {
        return Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<InfiniteMapGenerator>() != null;
    }

    [MenuItem("Hero Doom/Map/Generate Noise Prefab Preview")]
    private static void GeneratePreviewMenu()
    {
        Selection.activeGameObject.GetComponent<InfiniteMapGenerator>().GenerateNoisePrefabPreview();
    }
}
