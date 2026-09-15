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
    [Header("Cấu Hình Chunk (Macro Chunk)")]
    [Tooltip("Kích thước của Chunk lớn nhất (VD: nếu prefab to nhất là 4, để maxChunkSize = 4)")]
    public float maxChunkSize = 4f; 
    public int radius = 5;
    public Transform player;
    public int seed = 0;
    public List<GroundPrefabData> groundPrefabs;

    private int activeSeed;
    private Dictionary<Vector2Int, GameObject> activeChunks = new Dictionary<Vector2Int, GameObject>();
    private Vector3 lastCenterPosition = Vector3.zero;
    private int lastRadius = -1;
    private Vector2Int lastCenterChunk = new Vector2Int(int.MaxValue, int.MaxValue);

    void Start()
    {
        if (groundPrefabs == null || groundPrefabs.Count == 0)
        {
            Debug.LogError("[InfiniteMap - Start] Lỗi: Danh sách groundPrefabs đang trống! Vui lòng gán prefab vào list.");
            return;
        }

        activeSeed = (seed == 0) ? UnityEngine.Random.Range(1, 999999) : seed;
        Debug.Log($"[InfiniteMap] Khởi tạo map với Seed: {activeSeed}");
        
        UpdateMap();
    }

    void Update()
    {
        if (groundPrefabs == null || groundPrefabs.Count == 0) return;

        if (player != null)
        {
            lastCenterPosition = player.position;
        }

        Vector2Int currentCenterChunk = GetChunkCoordFromPosition(lastCenterPosition);

        if (currentCenterChunk != lastCenterChunk || radius != lastRadius)
        {
            try
            {
                UpdateMap();
                lastCenterChunk = currentCenterChunk;
                lastRadius = radius;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[InfiniteMap - Update] Lỗi runtime khi cập nhật map: {e.Message}\nStacktrace: {e.StackTrace}");
            }
        }
    }

    private void UpdateMap()
    {
        Vector2Int centerChunk = GetChunkCoordFromPosition(lastCenterPosition);
        List<Vector2Int> chunksInRadius = new List<Vector2Int>();

        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                if (x * x + z * z <= radius * radius) 
                {
                    chunksInRadius.Add(new Vector2Int(centerChunk.x + x, centerChunk.y + z));
                }
            }
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();
        foreach (var chunk in activeChunks)
        {
            if (!chunksInRadius.Contains(chunk.Key))
            {
                chunksToRemove.Add(chunk.Key);
            }
        }

        foreach (var chunkCoord in chunksToRemove)
        {
            if (activeChunks[chunkCoord] != null)
            {
                Destroy(activeChunks[chunkCoord]);
            }
            activeChunks.Remove(chunkCoord);
        }

        foreach (var chunkCoord in chunksInRadius)
        {
            if (!activeChunks.ContainsKey(chunkCoord))
            {
                GenerateMacroChunk(chunkCoord);
            }
        }
    }

    private void GenerateMacroChunk(Vector2Int coord)
    {
        try
        {
            int chunkHash = coord.x * 73856093 ^ coord.y * 19349663 ^ activeSeed * 83492791;
            System.Random prng = new System.Random(chunkHash);
            
            // Tạo 1 object cha rỗng để chứa các prefab con bên trong Macro Chunk này
            GameObject macroChunkParent = new GameObject($"MacroChunk_{coord.x}_{coord.y}");
            macroChunkParent.transform.SetParent(this.transform);
            
            Vector3 centerPosition = new Vector3(coord.x * maxChunkSize, 0, coord.y * maxChunkSize);
            macroChunkParent.transform.position = centerPosition;

            // Bắt đầu thuật toán đệ quy QuadTree để phủ kín không gian
            SubdivideAndSpawn(macroChunkParent.transform, centerPosition, maxChunkSize, prng);

            activeChunks.Add(coord, macroChunkParent);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[InfiniteMap - GenerateMacroChunk] Lỗi khi tạo chunk tại ({coord.x}, {coord.y}): {e.Message}\nStacktrace: {e.StackTrace}");
        }
    }

    private void SubdivideAndSpawn(Transform parentChunk, Vector3 currentCenter, float currentSize, System.Random prng)
    {
        // Chống đệ quy vô hạn nếu size quá nhỏ
        if (currentSize < 0.1f) return; 

        List<GroundPrefabData> validPrefabs = new List<GroundPrefabData>();
        float totalWeight = 0f;

        // Chỉ lấy các prefab có kích thước vừa hoặc nhỏ hơn khoảng trống hiện tại
        foreach (var gp in groundPrefabs)
        {
            if (gp.size <= currentSize && gp.prefab != null)
            {
                validPrefabs.Add(gp);
                totalWeight += gp.spawnChance;
            }
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning($"[InfiniteMap] Lỗi: Không có prefab nào phù hợp với size <= {currentSize} để lấp kín khoảng trống tại {currentCenter}");
            return;
        }

        double randomValue = prng.NextDouble() * totalWeight;
        GroundPrefabData selectedData = validPrefabs[0];

        foreach (var gp in validPrefabs)
        {
            randomValue -= gp.spawnChance;
            if (randomValue <= 0)
            {
                selectedData = gp;
                break;
            }
        }

        // Nếu prefab được chọn to bằng khoảng trống hiện tại -> Sinh ra luôn
        if (Mathf.Approximately(selectedData.size, currentSize))
        {
            GameObject go = Instantiate(selectedData.prefab, currentCenter, Quaternion.identity);
            go.transform.SetParent(parentChunk);
        }
        else
        {
            // Nếu prefab được chọn nhỏ hơn (vd: size 2 nhưng khoảng trống là 4)
            // Chia khoảng trống hiện tại thành 4 ô vuông nhỏ hơn (QuadTree) và gọi đệ quy
            float halfSize = currentSize / 2f;
            float offset = currentSize / 4f;

            Vector3[] quadrantOffsets = new Vector3[]
            {
                new Vector3(offset, 0, offset),   // Góc trên phải
                new Vector3(offset, 0, -offset),  // Góc dưới phải
                new Vector3(-offset, 0, offset),  // Góc trên trái
                new Vector3(-offset, 0, -offset)  // Góc dưới trái
            };

            foreach (var off in quadrantOffsets)
            {
                SubdivideAndSpawn(parentChunk, currentCenter + off, halfSize, prng);
            }
        }
    }

    private Vector2Int GetChunkCoordFromPosition(Vector3 position)
    {
        int x = Mathf.RoundToInt(position.x / maxChunkSize);
        int z = Mathf.RoundToInt(position.z / maxChunkSize);
        return new Vector2Int(x, z);
    }
}