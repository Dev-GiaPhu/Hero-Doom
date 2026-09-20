using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[System.Serializable]
public class GroundPrefabData
{
    public GameObject prefab;
    public float size = 4f;
    [Range(0, 100f)] public float spawnChance = 50f;
}

public class InfiniteMapGenerator : MonoBehaviour
{
    [Header("Render Theo Camera")]
    public Camera renderCamera;
    [Min(1)] public int radius = 14;

    [Header("Finite Seeded World")]
    [Tooltip("Nửa kích thước map tính bằng macro chunk.")]
    [Min(8)] public int worldHalfExtentChunks = 32;
    public float maxChunkSize = 4f;
    public int seed = 1;
    public List<GroundPrefabData> groundPrefabs;

    [Header("Water - giữ các thông số đã tune")]
    public GameObject waterPrefab;
    public float waterLevel = -0.35f;
    [Range(0, 5)] public int riverHalfWidth = 1;
    [Range(0f, 20f)] public float riverAmplitude = 5f;
    [Range(0.005f, 0.5f)] public float riverFrequency = 0.08f;
    [Range(0f, 10f)] public float secondaryAmplitude = 2f;
    [Range(0.005f, 0.5f)] public float secondaryFrequency = 0.035f;

    [Header("Finite Water Segment")]
    [Tooltip("Nửa chiều dài đoạn nước theo số macro chunk. Nước không còn chạy xuyên vô hạn cả map.")]
    [Min(3)] public int waterSegmentHalfLengthChunks = 7;
    [Tooltip("Số lối đất cắt ngang đoạn nước để AI/player có chỗ đi qua.")]
    [Range(0, 4)] public int waterCrossingCount = 2;
    [Tooltip("Nửa bề rộng mỗi lối qua, tính theo macro chunk.")]
    [Range(0, 2)] public int crossingHalfWidthChunks = 0;
    [Tooltip("NavMesh được lùi khỏi mép nước để agent không cạ trigger nước rồi chết.")]
    [Range(0f, 1.5f)] public float shoreNavPadding = 0.7f;
    [Tooltip("Khoảng cách an toàn mặc định khi tìm vị trí spawn gần nước.")]
    [Range(0f, 4f)] public float safeSpawnWaterClearance = 1.5f;

    public event Action MapChanged;
    public float WorldHalfSize => worldHalfExtentChunks * maxChunkSize;

    private int activeSeed;
    private Vector2Int waterSegmentCenter;
    private readonly Dictionary<Vector2Int, GameObject> activeChunks = new();
    private Vector2Int lastCenterChunk = new(int.MaxValue, int.MaxValue);
    private int lastRadius = -1;

    void Awake()
    {
        activeSeed = seed == 0 ? 1 : seed;
        BuildWaterLayout();
    }

    void Start()
    {
        if (renderCamera == null) renderCamera = Camera.main;

        if (seed == 0)
        {
            activeSeed = UnityEngine.Random.Range(1, 999999);
            seed = activeSeed;
            BuildWaterLayout();
        }

        UpdateMap(true);
    }

    void Update()
    {
        Vector2Int center = GetCameraCenterChunk();
        if (center != lastCenterChunk || radius != lastRadius)
            UpdateMap(false);
    }

    private void BuildWaterLayout()
    {
        // Giữ vùng nước gần khu trung tâm nhưng không khóa toàn bộ bản đồ.
        // Seed chỉ làm lệch vị trí một lượng vừa phải để map vẫn chơi được.
        int maxOffset = Mathf.Max(2, worldHalfExtentChunks / 5);
        System.Random prng = new System.Random(activeSeed * 92821 + 173);
        waterSegmentCenter = new Vector2Int(
            prng.Next(-maxOffset, maxOffset + 1),
            prng.Next(-maxOffset, maxOffset + 1));
    }

    private void UpdateMap(bool force)
    {
        Vector2Int centerChunk = ClampChunk(GetCameraCenterChunk());
        if (!force && centerChunk == lastCenterChunk && radius == lastRadius) return;

        HashSet<Vector2Int> wanted = new();
        for (int x = -radius; x <= radius; x++)
        for (int z = -radius; z <= radius; z++)
        {
            Vector2Int c = new Vector2Int(centerChunk.x + x, centerChunk.y + z);
            if (IsChunkInsideWorld(c)) wanted.Add(c);
        }

        List<Vector2Int> remove = new();
        foreach (var pair in activeChunks)
            if (!wanted.Contains(pair.Key)) remove.Add(pair.Key);

        foreach (Vector2Int coord in remove)
        {
            if (activeChunks[coord] != null) Destroy(activeChunks[coord]);
            activeChunks.Remove(coord);
        }

        foreach (Vector2Int coord in wanted)
            if (!activeChunks.ContainsKey(coord)) GenerateMacroChunk(coord);

        lastCenterChunk = centerChunk;
        lastRadius = radius;
        MapChanged?.Invoke();
    }

    private void GenerateMacroChunk(Vector2Int coord)
    {
        GameObject parent = new GameObject($"MacroChunk_{coord.x}_{coord.y}");
        parent.transform.SetParent(transform);
        Vector3 center = ChunkToWorld(coord);
        parent.transform.position = center;

        if (IsRiver(coord) && waterPrefab != null)
        {
            GameObject water = Instantiate(
                waterPrefab,
                new Vector3(center.x, waterLevel, center.z),
                Quaternion.identity,
                parent.transform);

            water.name = $"Water_{coord.x}_{coord.y}";
            SetLayerRecursively(water, LayerMask.NameToLayer("Water"));
        }
        else
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0) parent.layer = groundLayer;

            CreateWalkableCollider(parent, coord);

            int chunkHash = coord.x * 73856093 ^ coord.y * 19349663 ^ activeSeed * 83492791;
            SubdivideAndSpawn(parent.transform, center, maxChunkSize, new System.Random(chunkHash));
        }

        activeChunks.Add(coord, parent);
    }

    private void CreateWalkableCollider(GameObject parent, Vector2Int coord)
    {
        float pad = Mathf.Clamp(shoreNavPadding, 0f, maxChunkSize * 0.4f);

        bool waterEast = IsRiver(coord + Vector2Int.right);
        bool waterWest = IsRiver(coord + Vector2Int.left);
        bool waterNorth = IsRiver(coord + Vector2Int.up);
        bool waterSouth = IsRiver(coord + Vector2Int.down);

        float shrinkX = (waterEast ? pad : 0f) + (waterWest ? pad : 0f);
        float shrinkZ = (waterNorth ? pad : 0f) + (waterSouth ? pad : 0f);

        float shiftX = (waterWest ? pad * 0.5f : 0f) - (waterEast ? pad * 0.5f : 0f);
        float shiftZ = (waterSouth ? pad * 0.5f : 0f) - (waterNorth ? pad * 0.5f : 0f);

        BoxCollider walkable = parent.AddComponent<BoxCollider>();
        walkable.center = new Vector3(shiftX, -0.05f, shiftZ);
        walkable.size = new Vector3(
            Mathf.Max(0.5f, maxChunkSize - shrinkX),
            0.1f,
            Mathf.Max(0.5f, maxChunkSize - shrinkZ));
    }

    private void SubdivideAndSpawn(Transform parentChunk, Vector3 center, float size, System.Random prng)
    {
        if (size < 0.1f || groundPrefabs == null) return;

        List<GroundPrefabData> valid = new();
        float total = 0f;

        foreach (GroundPrefabData gp in groundPrefabs)
        {
            if (gp != null && gp.prefab != null && gp.size <= size && gp.spawnChance > 0f)
            {
                valid.Add(gp);
                total += gp.spawnChance;
            }
        }

        if (valid.Count == 0 || total <= 0f) return;

        double roll = prng.NextDouble() * total;
        GroundPrefabData selected = valid[0];

        foreach (GroundPrefabData gp in valid)
        {
            roll -= gp.spawnChance;
            if (roll <= 0)
            {
                selected = gp;
                break;
            }
        }

        if (Mathf.Approximately(selected.size, size))
        {
            GameObject instance = Instantiate(selected.prefab, center, Quaternion.identity, parentChunk);
            SetLayerRecursively(instance, LayerMask.NameToLayer("Ground"));
            return;
        }

        float half = size * 0.5f;
        float offset = size * 0.25f;

        SubdivideAndSpawn(parentChunk, center + new Vector3( offset, 0,  offset), half, prng);
        SubdivideAndSpawn(parentChunk, center + new Vector3( offset, 0, -offset), half, prng);
        SubdivideAndSpawn(parentChunk, center + new Vector3(-offset, 0,  offset), half, prng);
        SubdivideAndSpawn(parentChunk, center + new Vector3(-offset, 0, -offset), half, prng);
    }

    // Tên method được giữ để không phá code cũ, nhưng giờ đây đây là một đoạn nước HỮU HẠN.
    public bool IsRiver(Vector2Int coord)
    {
        int halfLength = Mathf.Clamp(waterSegmentHalfLengthChunks, 3, Mathf.Max(3, worldHalfExtentChunks - 3));
        int localX = coord.x - waterSegmentCenter.x;

        if (Mathf.Abs(localX) > halfLength)
            return false;

        if (IsCrossingColumn(localX, halfLength))
            return false;

        float phaseA = activeSeed * 0.01731f;
        float phaseB = activeSeed * 0.04127f;

        float centerZ =
            waterSegmentCenter.y +
            Mathf.Sin(localX * riverFrequency + phaseA) * riverAmplitude +
            Mathf.Sin(localX * secondaryFrequency + phaseB) * secondaryAmplitude;

        int centerChunkZ = Mathf.RoundToInt(centerZ);
        return Mathf.Abs(coord.y - centerChunkZ) <= riverHalfWidth;
    }

    private bool IsCrossingColumn(int localX, int halfLength)
    {
        int count = Mathf.Clamp(waterCrossingCount, 0, 4);
        if (count == 0) return false;

        int usableMin = -halfLength + 2;
        int usableMax = halfLength - 2;
        if (usableMax <= usableMin) return false;

        for (int i = 0; i < count; i++)
        {
            float t = (i + 1f) / (count + 1f);
            int baseX = Mathf.RoundToInt(Mathf.Lerp(usableMin, usableMax, t));

            int hash = activeSeed * 92821 + i * 68917 + 31;
            float jitter01 = Mathf.Abs(Mathf.Sin(hash * 0.0001234f));
            int jitter = Mathf.RoundToInt(Mathf.Lerp(-1f, 1f, jitter01));

            int crossingX = Mathf.Clamp(baseX + jitter, usableMin, usableMax);
            if (Mathf.Abs(localX - crossingX) <= crossingHalfWidthChunks)
                return true;
        }

        return false;
    }

    public bool IsWaterAtWorldPosition(Vector3 world)
    {
        return IsRiver(WorldToChunk(world));
    }

    public bool IsSafeDistanceFromWater(Vector3 world, float clearance)
    {
        if (IsWaterAtWorldPosition(world)) return false;
        if (clearance <= 0f) return true;

        const int samples = 12;
        for (int i = 0; i < samples; i++)
        {
            float angle = (Mathf.PI * 2f * i) / samples;
            Vector3 p = world + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * clearance;
            if (IsWaterAtWorldPosition(p)) return false;
        }

        return true;
    }

    public bool IsInsideWorld(Vector3 world)
    {
        return Mathf.Abs(world.x) <= WorldHalfSize && Mathf.Abs(world.z) <= WorldHalfSize;
    }

    public Vector3 ClampInsideWorld(Vector3 world, float padding = 0.5f)
    {
        float limit = Mathf.Max(0f, WorldHalfSize - Mathf.Max(0f, padding));
        world.x = Mathf.Clamp(world.x, -limit, limit);
        world.z = Mathf.Clamp(world.z, -limit, limit);
        return world;
    }

    public bool TryGetSafeGround(Vector3 near, float radiusWorld, out Vector3 position)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int groundMask = groundLayer >= 0 ? 1 << groundLayer : 0;

        for (int i = 0; i < 48; i++)
        {
            Vector2 circle = i == 0 ? Vector2.zero : UnityEngine.Random.insideUnitCircle * radiusWorld;
            Vector3 candidate = near + new Vector3(circle.x, 0f, circle.y);

            candidate.x = Mathf.Clamp(candidate.x, -WorldHalfSize + maxChunkSize, WorldHalfSize - maxChunkSize);
            candidate.z = Mathf.Clamp(candidate.z, -WorldHalfSize + maxChunkSize, WorldHalfSize - maxChunkSize);

            if (!IsSafeDistanceFromWater(candidate, safeSpawnWaterClearance))
                continue;

            if (groundMask != 0 && Physics.Raycast(candidate + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, groundMask))
            {
                if (!IsSafeDistanceFromWater(hit.point, safeSpawnWaterClearance))
                    continue;

                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2f, NavMesh.AllAreas) &&
                    IsSafeDistanceFromWater(navHit.position, safeSpawnWaterClearance))
                {
                    position = navHit.position;
                    return true;
                }

                position = hit.point;
                return true;
            }
        }

        Vector2Int origin = WorldToChunk(near);
        int maxRing = Mathf.CeilToInt(radiusWorld / maxChunkSize) + 5;

        for (int ring = 0; ring <= maxRing; ring++)
        for (int x = -ring; x <= ring; x++)
        for (int z = -ring; z <= ring; z++)
        {
            Vector2Int c = new Vector2Int(origin.x + x, origin.y + z);
            if (!IsChunkInsideWorld(c) || IsRiver(c)) continue;

            Vector3 candidate = ChunkToWorld(c);
            if (!IsSafeDistanceFromWater(candidate, safeSpawnWaterClearance))
                continue;

            position = candidate;
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    public bool TryGetRandomSafeGround(float edgePadding, out Vector3 position)
    {
        float edge = Mathf.Max(maxChunkSize, edgePadding);
        float range = Mathf.Max(maxChunkSize, WorldHalfSize - edge);

        for (int i = 0; i < 100; i++)
        {
            Vector3 candidate = new Vector3(
                UnityEngine.Random.Range(-range, range),
                0f,
                UnityEngine.Random.Range(-range, range));

            if (!IsSafeDistanceFromWater(candidate, safeSpawnWaterClearance))
                continue;

            if (TryGetSafeGround(candidate, maxChunkSize * 2f, out position))
                return true;
        }

        position = Vector3.zero;
        return false;
    }

    public Vector2Int WorldToChunk(Vector3 world)
    {
        return new Vector2Int(
            Mathf.RoundToInt(world.x / maxChunkSize),
            Mathf.RoundToInt(world.z / maxChunkSize));
    }

    public Vector3 ChunkToWorld(Vector2Int coord)
    {
        return new Vector3(coord.x * maxChunkSize, 0f, coord.y * maxChunkSize);
    }

    private bool IsChunkInsideWorld(Vector2Int c)
    {
        return Mathf.Abs(c.x) <= worldHalfExtentChunks && Mathf.Abs(c.y) <= worldHalfExtentChunks;
    }

    private Vector2Int ClampChunk(Vector2Int c)
    {
        return new Vector2Int(
            Mathf.Clamp(c.x, -worldHalfExtentChunks, worldHalfExtentChunks),
            Mathf.Clamp(c.y, -worldHalfExtentChunks, worldHalfExtentChunks));
    }

    private Vector2Int GetCameraCenterChunk()
    {
        if (renderCamera == null) renderCamera = Camera.main;
        if (renderCamera == null) return Vector2Int.zero;

        Ray ray = renderCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Vector3 point = renderCamera.transform.position;

        if (plane.Raycast(ray, out float distance))
            point = ray.GetPoint(distance);

        return WorldToChunk(point);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null || layer < 0) return;

        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
