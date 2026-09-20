using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class GroundPrefabData
{
    public GameObject prefab;
    [Tooltip("Kích thước của prefab (vd: 4 cho loại to, 2 cho loại nhỏ. Nên là các số chia hết cho nhau như 1, 2, 4, 8)")]
    public float size = 4f;
    [Range(0, 100f)]
    [Tooltip("Tỉ lệ xuất hiện trên thang 100")]
    public float spawnChance = 50f;
}

public class InfiniteMapGenerator : MonoBehaviour
{
    [Header("Render Theo Camera")]
    [Tooltip("Camera dùng làm tâm render map. Nếu bỏ trống sẽ tự dùng Camera.main.")]
    public Camera renderCamera;
    [Tooltip("Bán kính chunk được giữ quanh vùng camera đang nhìn.")]
    [Min(1)] public int radius = 14;

    [Header("Cấu Hình Chunk")]
    [Tooltip("Kích thước Macro Chunk. Water prefab nên có footprint đúng bằng kích thước này.")]
    public float maxChunkSize = 4f;
    public int seed = 1;
    public List<GroundPrefabData> groundPrefabs;

    [Header("River")]
    [Tooltip("Prefab nước 4x4. Mỗi ô sông dùng đúng prefab này.")]
    public GameObject waterPrefab;
    [Tooltip("Y của mặt nước.")]
    public float waterLevel = -0.35f;
    [Tooltip("Bề rộng sông tính theo số chunk mỗi phía quanh tim sông.")]
    [Range(0, 5)] public int riverHalfWidth = 1;
    [Tooltip("Độ uốn của sông, tính theo số chunk.")]
    [Range(0f, 20f)] public float riverAmplitude = 5f;
    [Tooltip("Tần số uốn. Nhỏ hơn = khúc sông dài và tự nhiên hơn.")]
    [Range(0.005f, 0.5f)] public float riverFrequency = 0.08f;
    [Tooltip("Thêm một lớp uốn phụ để đường sông bớt giống sóng sin.")]
    [Range(0f, 10f)] public float secondaryAmplitude = 2f;
    [Range(0.005f, 0.5f)] public float secondaryFrequency = 0.035f;

    private int activeSeed;
    private readonly Dictionary<Vector2Int, GameObject> activeChunks = new();
    private Vector2Int lastCenterChunk = new(int.MaxValue, int.MaxValue);
    private int lastRadius = -1;

    void Start()
    {
        if (groundPrefabs == null || groundPrefabs.Count == 0)
        {
            Debug.LogError("[InfiniteMap] groundPrefabs đang trống.");
            return;
        }

        if (renderCamera == null) renderCamera = Camera.main;
        activeSeed = seed == 0 ? Random.Range(1, 999999) : seed;
        UpdateMap(true);
    }

    void Update()
    {
        if (groundPrefabs == null || groundPrefabs.Count == 0) return;

        Vector2Int currentCenterChunk = GetCameraCenterChunk();
        if (currentCenterChunk != lastCenterChunk || radius != lastRadius)
            UpdateMap(false);
    }

    private void UpdateMap(bool force)
    {
        Vector2Int centerChunk = GetCameraCenterChunk();
        if (!force && centerChunk == lastCenterChunk && radius == lastRadius) return;

        HashSet<Vector2Int> wanted = new();
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                // Hình vuông phủ viewport tốt hơn hình tròn khi camera di chuyển ngang/dọc.
                wanted.Add(new Vector2Int(centerChunk.x + x, centerChunk.y + z));
            }
        }

        List<Vector2Int> remove = new();
        foreach (var pair in activeChunks)
            if (!wanted.Contains(pair.Key)) remove.Add(pair.Key);

        foreach (var coord in remove)
        {
            if (activeChunks[coord] != null) Destroy(activeChunks[coord]);
            activeChunks.Remove(coord);
        }

        foreach (var coord in wanted)
            if (!activeChunks.ContainsKey(coord)) GenerateMacroChunk(coord);

        lastCenterChunk = centerChunk;
        lastRadius = radius;
    }

    private void GenerateMacroChunk(Vector2Int coord)
    {
        GameObject parent = new($"MacroChunk_{coord.x}_{coord.y}");
        parent.transform.SetParent(transform);

        Vector3 center = new(coord.x * maxChunkSize, 0f, coord.y * maxChunkSize);
        parent.transform.position = center;

        if (IsRiver(coord) && waterPrefab != null)
        {
            Vector3 waterPosition = new(center.x, waterLevel, center.z);
            GameObject water = Instantiate(waterPrefab, waterPosition, Quaternion.identity, parent.transform);
            water.name = $"Water_{coord.x}_{coord.y}";
        }
        else
        {
            int chunkHash = coord.x * 73856093 ^ coord.y * 19349663 ^ activeSeed * 83492791;
            System.Random prng = new(chunkHash);
            SubdivideAndSpawn(parent.transform, center, maxChunkSize, prng);
        }

        activeChunks.Add(coord, parent);
    }

    // Mỗi cột X có đúng một tim sông. Tim chỉ thay đổi từ từ theo X,
    // vì vậy các water chunk luôn chạm cạnh nhau thay vì xuất hiện rải rác.
    private bool IsRiver(Vector2Int coord)
    {
        float phaseA = activeSeed * 0.01731f;
        float phaseB = activeSeed * 0.04127f;
        float riverCenter =
            Mathf.Sin(coord.x * riverFrequency + phaseA) * riverAmplitude +
            Mathf.Sin(coord.x * secondaryFrequency + phaseB) * secondaryAmplitude;

        int centerZ = Mathf.RoundToInt(riverCenter);
        return Mathf.Abs(coord.y - centerZ) <= riverHalfWidth;
    }

    private void SubdivideAndSpawn(Transform parentChunk, Vector3 currentCenter, float currentSize, System.Random prng)
    {
        if (currentSize < 0.1f) return;

        List<GroundPrefabData> valid = new();
        float totalWeight = 0f;
        foreach (var gp in groundPrefabs)
        {
            if (gp.prefab != null && gp.size <= currentSize)
            {
                valid.Add(gp);
                totalWeight += gp.spawnChance;
            }
        }

        if (valid.Count == 0 || totalWeight <= 0f) return;

        double value = prng.NextDouble() * totalWeight;
        GroundPrefabData selected = valid[0];
        foreach (var gp in valid)
        {
            value -= gp.spawnChance;
            if (value <= 0)
            {
                selected = gp;
                break;
            }
        }

        if (Mathf.Approximately(selected.size, currentSize))
        {
            Instantiate(selected.prefab, currentCenter, Quaternion.identity, parentChunk);
            return;
        }

        float half = currentSize * 0.5f;
        float offset = currentSize * 0.25f;
        SubdivideAndSpawn(parentChunk, currentCenter + new Vector3( offset, 0,  offset), half, prng);
        SubdivideAndSpawn(parentChunk, currentCenter + new Vector3( offset, 0, -offset), half, prng);
        SubdivideAndSpawn(parentChunk, currentCenter + new Vector3(-offset, 0,  offset), half, prng);
        SubdivideAndSpawn(parentChunk, currentCenter + new Vector3(-offset, 0, -offset), half, prng);
    }

    private Vector2Int GetCameraCenterChunk()
    {
        if (renderCamera == null) renderCamera = Camera.main;
        if (renderCamera == null) return Vector2Int.zero;

        // Lấy đúng điểm giữa màn hình chiếu xuống mặt phẳng map thay vì dùng vị trí camera,
        // nên camera nghiêng vẫn render quanh vùng người chơi đang thật sự nhìn thấy.
        Ray ray = renderCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane groundPlane = new(Vector3.up, Vector3.zero);
        Vector3 point = renderCamera.transform.position;

        if (groundPlane.Raycast(ray, out float distance))
            point = ray.GetPoint(distance);

        return new Vector2Int(
            Mathf.RoundToInt(point.x / maxChunkSize),
            Mathf.RoundToInt(point.z / maxChunkSize));
    }
}
