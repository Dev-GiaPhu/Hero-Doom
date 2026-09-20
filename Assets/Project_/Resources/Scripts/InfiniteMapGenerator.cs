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
    [Tooltip("Nửa kích thước map tính bằng macro chunk. 32 với chunk 4 = map rộng 256 world units.")]
    [Min(8)] public int worldHalfExtentChunks = 32;
    public float maxChunkSize = 4f;
    public int seed = 1;
    public List<GroundPrefabData> groundPrefabs;

    [Header("River")]
    public GameObject waterPrefab;
    public float waterLevel = -0.35f;
    [Range(0, 5)] public int riverHalfWidth = 1;
    [Range(0f, 20f)] public float riverAmplitude = 5f;
    [Range(0.005f, 0.5f)] public float riverFrequency = 0.08f;
    [Range(0f, 10f)] public float secondaryAmplitude = 2f;
    [Range(0.005f, 0.5f)] public float secondaryFrequency = 0.035f;

    public event Action MapChanged;
    public float WorldHalfSize => worldHalfExtentChunks * maxChunkSize;

    private int activeSeed;
    private readonly Dictionary<Vector2Int, GameObject> activeChunks = new();
    private Vector2Int lastCenterChunk = new(int.MaxValue, int.MaxValue);
    private int lastRadius = -1;

    void Awake()
    {
        activeSeed = seed == 0 ? 1 : seed;
    }

    void Start()
    {
        if (renderCamera == null) renderCamera = Camera.main;
        if (seed == 0)
        {
            activeSeed = UnityEngine.Random.Range(1, 999999);
            seed = activeSeed;
        }
        UpdateMap(true);
    }

    void Update()
    {
        Vector2Int center = GetCameraCenterChunk();
        if (center != lastCenterChunk || radius != lastRadius) UpdateMap(false);
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
            GameObject water = Instantiate(waterPrefab, new Vector3(center.x, waterLevel, center.z), Quaternion.identity, parent.transform);
            water.name = $"Water_{coord.x}_{coord.y}";
            SetLayerRecursively(water, LayerMask.NameToLayer("Water"));
        }
        else
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0) parent.layer = groundLayer;

            // Procedural floor prefabs are visual-only. The macro collider is the authoritative
            // walkable surface, so river chunks have no ground collider/NavMesh underneath them.
            BoxCollider walkable = parent.AddComponent<BoxCollider>();
            walkable.center = new Vector3(0f, -0.05f, 0f);
            walkable.size = new Vector3(maxChunkSize, 0.1f, maxChunkSize);

            int chunkHash = coord.x * 73856093 ^ coord.y * 19349663 ^ activeSeed * 83492791;
            SubdivideAndSpawn(parent.transform, center, maxChunkSize, new System.Random(chunkHash));
        }

        activeChunks.Add(coord, parent);
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
            if (roll <= 0) { selected = gp; break; }
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

    public bool IsRiver(Vector2Int coord)
    {
        float phaseA = activeSeed * 0.01731f;
        float phaseB = activeSeed * 0.04127f;
        float center = Mathf.Sin(coord.x * riverFrequency + phaseA) * riverAmplitude +
                       Mathf.Sin(coord.x * secondaryFrequency + phaseB) * secondaryAmplitude;
        return Mathf.Abs(coord.y - Mathf.RoundToInt(center)) <= riverHalfWidth;
    }

    public bool IsWaterAtWorldPosition(Vector3 world)
    {
        return IsRiver(WorldToChunk(world));
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
        int groundMask = 1 << LayerMask.NameToLayer("Ground");
        for (int i = 0; i < 36; i++)
        {
            Vector2 circle = i == 0 ? Vector2.zero : UnityEngine.Random.insideUnitCircle * radiusWorld;
            Vector3 candidate = near + new Vector3(circle.x, 0f, circle.y);
            candidate.x = Mathf.Clamp(candidate.x, -WorldHalfSize + maxChunkSize, WorldHalfSize - maxChunkSize);
            candidate.z = Mathf.Clamp(candidate.z, -WorldHalfSize + maxChunkSize, WorldHalfSize - maxChunkSize);
            if (IsWaterAtWorldPosition(candidate)) continue;

            if (Physics.Raycast(candidate + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, groundMask))
            {
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                    position = navHit.position;
                else
                    position = hit.point;
                return true;
            }
        }

        Vector2Int origin = WorldToChunk(near);
        for (int ring = 0; ring <= Mathf.CeilToInt(radiusWorld / maxChunkSize) + 3; ring++)
        for (int x = -ring; x <= ring; x++)
        for (int z = -ring; z <= ring; z++)
        {
            Vector2Int c = new Vector2Int(origin.x + x, origin.y + z);
            if (!IsChunkInsideWorld(c) || IsRiver(c)) continue;
            position = ChunkToWorld(c);
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    public bool TryGetRandomSafeGround(float edgePadding, out Vector3 position)
    {
        float edge = Mathf.Max(maxChunkSize, edgePadding);
        float range = Mathf.Max(maxChunkSize, WorldHalfSize - edge);
        for (int i = 0; i < 80; i++)
        {
            Vector3 candidate = new Vector3(UnityEngine.Random.Range(-range, range), 0f, UnityEngine.Random.Range(-range, range));
            if (IsWaterAtWorldPosition(candidate)) continue;
            if (TryGetSafeGround(candidate, maxChunkSize * 2f, out position)) return true;
        }
        position = Vector3.zero;
        return false;
    }

    public Vector2Int WorldToChunk(Vector3 world)
    {
        return new Vector2Int(Mathf.RoundToInt(world.x / maxChunkSize), Mathf.RoundToInt(world.z / maxChunkSize));
    }

    public Vector3 ChunkToWorld(Vector2Int coord) => new Vector3(coord.x * maxChunkSize, 0f, coord.y * maxChunkSize);

    private bool IsChunkInsideWorld(Vector2Int c)
    {
        return Mathf.Abs(c.x) <= worldHalfExtentChunks && Mathf.Abs(c.y) <= worldHalfExtentChunks;
    }

    private Vector2Int ClampChunk(Vector2Int c)
    {
        return new Vector2Int(Mathf.Clamp(c.x, -worldHalfExtentChunks, worldHalfExtentChunks),
                              Mathf.Clamp(c.y, -worldHalfExtentChunks, worldHalfExtentChunks));
    }

    private Vector2Int GetCameraCenterChunk()
    {
        if (renderCamera == null) renderCamera = Camera.main;
        if (renderCamera == null) return Vector2Int.zero;

        Ray ray = renderCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Vector3 point = renderCamera.transform.position;
        if (plane.Raycast(ray, out float distance)) point = ray.GetPoint(distance);
        return WorldToChunk(point);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null || layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }
}
