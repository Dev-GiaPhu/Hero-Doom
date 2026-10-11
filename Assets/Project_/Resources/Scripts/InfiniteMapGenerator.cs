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

[System.Serializable]
public class NoisePrefabChoice
{
    [Tooltip("Prefab sẽ được rải. Để trống thì mục này không tham gia chọn.")]
    public GameObject prefab;
    [Tooltip("Trọng số chọn prefab trong pool. Số lớn xuất hiện thường xuyên hơn.")]
    [Min(0f)] public float weight = 1f;
    [Tooltip("Hệ số nhân thêm vào scale ngẫu nhiên chung.")]
    [Range(0.1f, 4f)] public float scaleMultiplier = 1f;
}

[System.Serializable]
public class NoisePrefabLayer
{
    [Tooltip("Tên ghi chú cho lớp noise này. Thứ tự danh sách luôn chạy từ vùng noise tối đến sáng.")]
    public string label = "Noise Layer";
    [Tooltip("Độ rộng của dải noise mà lớp này chiếm. Các dải được nối liền, không có khoảng trống hay chồng lấn.")]
    [Min(0.01f)] public float bandWeight = 1f;
    [Tooltip("Prefab chỉ dành cho lớp này. Nếu để trống, hệ thống chọn ngẫu nhiên từ pool prefab chung.")]
    public List<NoisePrefabChoice> prefabOverrides = new();
}

public class InfiniteMapGenerator : MonoBehaviour
{
    [Header("Render Theo Camera")]
    [Tooltip("Camera dùng để xác định các macro chunk nào cần hiện quanh màn hình.")]
    public Camera renderCamera;
    [Tooltip("Bán kính chunk được tạo quanh camera. Tăng sẽ nhìn xa hơn nhưng tốn hiệu năng hơn.")]
    [Min(1)] public int radius = 14;

    [Header("Finite Seeded World")]
    [Tooltip("Nửa kích thước toàn map theo macro chunk. Giá trị 32 tạo map 65 x 65 chunk.")]
    [Min(8)] public int worldHalfExtentChunks = 32;
    [Tooltip("Kích thước một macro chunk theo world unit. Chỉ đổi trước khi bắt đầu làm map.")]
    public float maxChunkSize = 4f;
    [Tooltip("Seed quyết định bố cục map. Đặt 0 để tạo seed ngẫu nhiên cho mỗi lượt Play; Inspector vẫn giữ số 0 khi dừng game.")]
    public int seed = 1;
    [Tooltip("Các prefab nền đất được chọn để tạo mỗi macro chunk đất.")]
    public List<GroundPrefabData> groundPrefabs;

    [Header("Terrain Noise - quyết định đất và nước")]
    [Tooltip("Bật để noise nền quyết định map: giá trị thấp/tối là nước, giá trị cao/sáng là đất. Thay thế hệ sông cũ.")]
    public bool useTerrainNoise = true;
    [Tooltip("Tần số noise nền trên tọa độ thế giới. Số nhỏ tạo lục địa lớn; số lớn tạo nhiều đảo và bờ cong hơn.")]
    [Min(0.001f)] public float terrainNoiseFrequency = 0.025f;
    [Tooltip("Số lớp noise nền chồng lên nhau. 2-4 tạo đường bờ tự nhiên mà vẫn dễ di chuyển.")]
    [Range(1, 6)] public int terrainNoiseOctaves = 2;
    [Tooltip("Độ yếu dần của lớp noise nền sau. 0.5 là mặc định cân bằng.")]
    [Range(0.1f, 0.9f)] public float terrainNoisePersistence = 0.5f;
    [Tooltip("Tần số nhân cho lớp noise nền sau.")]
    [Range(1.1f, 4f)] public float terrainNoiseLacunarity = 2f;
    [Tooltip("Tỷ lệ đất mong muốn trên toàn map. Tăng = nhiều đất hơn; giảm = nhiều nước hơn. Noise vẫn tạo hình tự nhiên, không chia map thành ô đều.")]
    [Range(0.1f, 0.9f)] public float landCoverage = 0.56f;
    [Tooltip("Dịch mẫu noise nền mà không thay seed.")]
    public Vector2 terrainNoiseOffset;
    [Tooltip("Bẻ cong tọa độ noise trước khi lấy mẫu. Bật để đường bờ không lặp lại các mảng Perlin giống nhau.")]
    public bool useTerrainDomainWarp = true;
    [Tooltip("Tần số của lớp dùng để bẻ cong noise nền. Giá trị lớn hơn tạo biến dạng nhỏ, dày hơn.")]
    [Min(0.001f)] public float terrainWarpFrequency = 0.055f;
    [Tooltip("Độ mạnh bẻ cong đường bờ tính theo world unit. 8-20 thường cho đảo và vịnh tự nhiên.")]
    [Range(0f, 40f)] public float terrainWarpStrength = 14f;
    [Tooltip("Tỷ lệ trộn lớp chi tiết vào noise nền. Tăng nhẹ để bờ đất có vịnh/eo đất hơn, không nên quá cao.")]
    [Range(0f, 0.6f)] public float terrainDetailBlend = 0.22f;
    [Tooltip("Tần số lớp chi tiết của terrain noise.")]
    [Min(0.001f)] public float terrainDetailFrequency = 0.075f;
    [Tooltip("Số octave của lớp chi tiết.")]
    [Range(1, 4)] public int terrainDetailOctaves = 2;
    [Tooltip("Luôn nối các cụm đất bị tách bằng hành lang đất hẹp. Hero và quái sẽ luôn có đường đi giữa các mảng đất.")]
    public bool guaranteeConnectedLand = true;
    [Tooltip("Bề rộng tối thiểu của hành lang đất nối các cụm, tính theo macro chunk.")]
    [Range(1, 3)] public int landConnectionWidth = 1;

    [Header("Structure Noise - rải công trình trên đất")]
    [Tooltip("Bật để noise thứ hai chỉ rải prefab công trình/trang trí lên macro chunk đã là đất. Chunk nước không bao giờ nhận công trình.")]
    public bool enableNoisePrefabScatter;
    [Tooltip("Số dải noise liên tiếp từ tối đến sáng. Lớp 0 luôn là dải thấp nhất; tăng/giảm số này sẽ tự thêm/xóa cấu hình lớp.")]
    [Min(1)] public int noiseLayerCount = 3;
    [Tooltip("Pool mặc định dùng ngẫu nhiên cho mọi lớp chưa có prefab riêng.")]
    public List<NoisePrefabChoice> defaultNoisePrefabPool = new();
    [Tooltip("Cấu hình theo thứ tự từ noise thấp/tối đến noise cao/sáng. Mỗi lớp có thể gán pool prefab riêng.")]
    public List<NoisePrefabLayer> noiseLayers = new() { new NoisePrefabLayer { label = "Dark" }, new NoisePrefabLayer { label = "Mid" }, new NoisePrefabLayer { label = "Light" } };

    [Header("Noise Settings")]
    [Tooltip("Tần số Perlin trên tọa độ thế giới. Số nhỏ tạo mảng lớn; số lớn tạo vùng đổi lớp nhanh hơn.")]
    [Min(0.001f)] public float scatterNoiseFrequency = 0.08f;
    [Tooltip("Số octave chồng để noise có thêm chi tiết. 1 là mượt; 3-5 thường tạo vùng tự nhiên.")]
    [Range(1, 8)] public int noiseOctaves = 3;
    [Tooltip("Biên độ mỗi octave tiếp theo. 0.5 nghĩa là octave sau yếu bằng một nửa octave trước.")]
    [Range(0.1f, 0.9f)] public float noisePersistence = 0.5f;
    [Tooltip("Tần số nhân cho mỗi octave. Giá trị 2 thường phù hợp.")]
    [Range(1.1f, 4f)] public float noiseLacunarity = 2f;
    [Tooltip("Dịch mẫu noise theo trục X/Z mà không di chuyển prefab.")]
    public Vector2 noiseOffset;
    [Tooltip("Độ ảnh hưởng của noise lên xác suất rải. 0 = mật độ đều trong từng lớp; 1 = vùng noise sáng có xác suất cao hơn.")]
    [Range(0f, 1f)] public float noiseDensityInfluence = 0.25f;

    [Header("Placement Settings")]
    [Tooltip("Khoảng cách mục tiêu giữa các điểm thử rải. Tăng số này để công trình thưa hơn; hệ thống vẫn thử các góc chunk để không bị thiếu công trình vì hành lang giữa map.")]
    [Min(0.25f)] public float scatterSpacing = 2.5f;
    [Tooltip("Xác suất một điểm thử tạo prefab trước khi áp dụng ảnh hưởng mật độ noise.")]
    [Range(0f, 1f)] public float scatterChance = 0.22f;
    [Tooltip("Giới hạn số prefab tối đa trong mỗi macro chunk đất. Mặc định 1 để công trình không dày đặc.")]
    [Min(0)] public int maxScatterPerChunk = 1;
    [Tooltip("Độ lệch ngẫu nhiên bên trong mỗi ô, theo tỷ lệ kích thước ô. 0 đặt đúng tâm ô.")]
    [Range(0f, 0.49f)] public float cellJitter = 0.35f;
    [Tooltip("Khoảng lùi khỏi mép chunk để prefab không tràn sang chunk khác.")]
    [Min(0f)] public float scatterEdgePadding = 0.25f;
    [Tooltip("Độ cao cộng thêm sau khi đặt lên mặt đất.")]
    public float scatterGroundOffset = 0.02f;
    [Tooltip("Xoay ngẫu nhiên prefab quanh trục thẳng đứng.")]
    public bool randomizeYaw = true;
    [Tooltip("Góc xoay cố định khi tắt xoay ngẫu nhiên.")]
    public float fixedYaw;
    [Tooltip("Scale đồng đều ngẫu nhiên cho từng prefab; hệ số riêng của prefab sẽ được nhân thêm.")]
    public Vector2 randomScale = new(0.85f, 1.15f);

    [Header("Ground Snap and Avoidance")]
    [Tooltip("Bật để bắn ray xuống mặt đất và đặt prefab theo độ cao/normal bề mặt.")]
    public bool snapToGround;
    [Tooltip("Layer được xem là mặt đất khi Snap To Ground bật. Nên chọn Ground.")]
    public LayerMask scatterGroundMask = ~0;
    [Tooltip("Độ cao bắt đầu bắn ray từ trên mặt đất.")]
    [Min(1f)] public float groundRayStartHeight = 20f;
    [Tooltip("Bỏ qua điểm nếu ray không tìm được mặt đất. Tắt để dùng mặt phẳng y của chunk làm dự phòng.")]
    public bool skipWhenGroundNotFound;
    [Tooltip("Xoay trục lên của prefab theo normal mặt đất khi Snap To Ground bật.")]
    public bool alignToGroundNormal;
    [Tooltip("Bật để bỏ qua điểm bị chiếm bởi collider trong Avoidance Mask.")]
    public bool avoidOccupiedPoints;
    [Tooltip("Các layer collider khiến điểm rải bị bỏ qua. Để Nothing nếu không muốn kiểm tra.")]
    public LayerMask avoidanceMask;
    [Tooltip("Bán kính kiểm tra va chạm quanh điểm rải.")]
    [Min(0.05f)] public float avoidanceRadius = 0.6f;

    [Header("Structure Navigation Blocking")]
    [Tooltip("Tạo NavMeshObstacle cho mỗi công trình sinh từ noise để Hero và quái không đi xuyên qua nó.")]
    public bool structuresBlockNavigation = true;
    [Tooltip("Khoảng trống cộng thêm quanh công trình khi chặn NavMesh.")]
    [Range(0f, 2f)] public float structureNavObstaclePadding = 0.2f;
    [Tooltip("Khoảng cách tối thiểu giữa hai công trình. Kiểm tra cả các macro chunk đang hiển thị.")]
    [Min(0.25f)] public float structureMinimumSeparation = 4f;
    [Tooltip("Nửa bề rộng hành lang chữ thập được chừa trống ở tâm mỗi chunk đất. Hành lang này giữ đường đi giữa các chunk không bị công trình bít.")]
    [Range(0.1f, 2f)] public float structurePathHalfWidth = 0.75f;
    [Tooltip("Khoảng lùi thêm từ footprint công trình đến nước. Hệ thống kiểm tra cả vòng ngoài footprint để công trình sát bờ không tràn xuống sông.")]
    [Range(0f, 3f)] public float structureWaterClearance = 0.45f;
    [Tooltip("Layer đặt cho collider chặn đường. Nếu project chưa có layer này, object vẫn được NavMeshObstacle chặn.")]
    public string structureLayerName = "Obstacle";

    [Header("Editor Preview")]
    [Tooltip("Số chunk về mỗi hướng khi tạo preview trong Editor. Giá trị 2 tạo tối đa 5 x 5 chunk.")]
    [Range(0, 8)] public int previewChunkRadius = 2;

    // Migration nội bộ: các scene tạo trước Structure Noise v2 giữ giá trị dày của bản cũ.
    [SerializeField, HideInInspector] private int structureNoiseVersion;

    [Header("Water Visual")]
    [Tooltip("Prefab hiển thị nước cho các macro chunk nước.")]
    public GameObject waterPrefab;
    [Tooltip("Độ cao của bề mặt nước.")]
    public float waterLevel = -0.35f;
    // Thuật toán sông cũ không còn được dùng. Terrain Noise hiện quyết định toàn bộ đất và nước.
    [Tooltip("NavMesh được lùi khỏi mép nước để agent không cạ trigger nước rồi chết.")]
    [Range(0f, 1.5f)] public float shoreNavPadding = 0.7f;
    [Tooltip("Khoảng cách an toàn mặc định khi tìm vị trí spawn gần nước.")]
    [Range(0f, 4f)] public float safeSpawnWaterClearance = 1.5f;

    public event Action MapChanged;
    public float WorldHalfSize => worldHalfExtentChunks * maxChunkSize;

    private int activeSeed;
    private int lastConfiguredSeed = int.MinValue;
    private readonly HashSet<Vector2Int> landChunks = new();
    private readonly Dictionary<Vector2Int, GameObject> activeChunks = new();
    private Vector2Int lastCenterChunk = new(int.MaxValue, int.MaxValue);
    private int lastRadius = -1;

    private const string NoisePreviewName = "__Noise Prefab Preview (Editor Only)";

    private void OnValidate()
    {
        if (structureNoiseVersion < 2)
        {
            // Chỉ chạy một lần để scene cũ nhận preset công trình thưa; sau đó người dùng tự tune Inspector.
            scatterSpacing = 2.5f;
            scatterChance = 0.22f;
            maxScatterPerChunk = 1;
            structureMinimumSeparation = 4f;
            structuresBlockNavigation = true;
            structureNoiseVersion = 2;
        }
        noiseLayerCount = Mathf.Clamp(noiseLayerCount, 1, 16);
        if (noiseLayers == null) noiseLayers = new List<NoisePrefabLayer>();
        while (noiseLayers.Count < noiseLayerCount)
            noiseLayers.Add(new NoisePrefabLayer { label = $"Layer {noiseLayers.Count + 1}" });
        while (noiseLayers.Count > noiseLayerCount)
            noiseLayers.RemoveAt(noiseLayers.Count - 1);
        foreach (NoisePrefabLayer layer in noiseLayers)
        {
            if (layer == null) continue;
            layer.bandWeight = Mathf.Max(0.01f, layer.bandWeight);
            if (layer.prefabOverrides == null) layer.prefabOverrides = new List<NoisePrefabChoice>();
        }
        if (defaultNoisePrefabPool == null) defaultNoisePrefabPool = new List<NoisePrefabChoice>();
        if (scatterSpacing < 0.25f) scatterSpacing = 0.25f;
        if (randomScale.y < randomScale.x) randomScale.y = randomScale.x;
        terrainNoiseOctaves = Mathf.Clamp(terrainNoiseOctaves, 1, 6);
        terrainDetailOctaves = Mathf.Clamp(terrainDetailOctaves, 1, 4);
        landCoverage = Mathf.Clamp(landCoverage, 0.1f, 0.9f);
        terrainNoiseFrequency = Mathf.Max(0.001f, terrainNoiseFrequency);
        terrainWarpFrequency = Mathf.Max(0.001f, terrainWarpFrequency);
        terrainDetailFrequency = Mathf.Max(0.001f, terrainDetailFrequency);
        landConnectionWidth = Mathf.Clamp(landConnectionWidth, 1, 3);
        maxScatterPerChunk = Mathf.Max(0, maxScatterPerChunk);
    }

    private void Awake()
    {
        OnValidate();
        ApplyConfiguredSeed();
        BuildTerrainLayout();
    }

    void Start()
    {
#if UNITY_EDITOR
        ClearNoisePrefabPreview();
#endif
        if (renderCamera == null) renderCamera = Camera.main;

        UpdateMap(true);
    }

    void Update()
    {
        Vector2Int center = GetCameraCenterChunk();
        if (center != lastCenterChunk || radius != lastRadius)
            UpdateMap(false);
    }

    /// <summary>
    /// Gọi từ Inspector khi đang Play để áp dụng ngay các thông số noise, đất/nước,
    /// prefab và mật độ công trình cho các chunk đang được hiển thị.
    /// </summary>
    public void RefreshMapFromInspector()
    {
        OnValidate();
        if (!Application.isPlaying)
            return;

        ApplyConfiguredSeed();
        BuildTerrainLayout();

        foreach (GameObject chunk in activeChunks.Values)
        {
            if (chunk == null) continue;
            // Destroy chạy cuối frame; tắt ngay để NavMesh và collider không còn tính chunk cũ.
            chunk.SetActive(false);
            Destroy(chunk);
        }
        activeChunks.Clear();
        lastCenterChunk = new Vector2Int(int.MaxValue, int.MaxValue);
        lastRadius = -1;
        UpdateMap(true);
    }

    private void ApplyConfiguredSeed()
    {
        if (seed == 0)
        {
            // Seed ngẫu nhiên chỉ sống trong lần Play hiện tại, không ghi ngược vào
            // field seed đã serialize. Đổi slider khi đang dùng seed 0 vẫn giữ map đó.
            if (activeSeed <= 0 || lastConfiguredSeed != 0)
                activeSeed = UnityEngine.Random.Range(1, 999999);
        }
        else
        {
            activeSeed = seed;
        }
        lastConfiguredSeed = seed;
    }

    private void BuildTerrainLayout()
    {
        landChunks.Clear();
        int extent = Mathf.Max(8, worldHalfExtentChunks);
        if (!useTerrainNoise)
        {
            for (int x = -extent; x <= extent; x++)
            for (int z = -extent; z <= extent; z++)
                landChunks.Add(new Vector2Int(x, z));
            return;
        }

        List<KeyValuePair<Vector2Int, float>> samples = new();
        for (int x = -extent; x <= extent; x++)
        for (int z = -extent; z <= extent; z++)
        {
            Vector2Int coord = new Vector2Int(x, z);
            samples.Add(new KeyValuePair<Vector2Int, float>(coord, SampleTerrainNoise(ChunkToWorld(coord))));
        }

        // Chọn đúng phần trăm noise cao nhất thay vì dùng một threshold cố định.
        // Vì phân bố Perlin thay đổi theo seed, cách này khiến Land Coverage trong
        // Inspector luôn có ý nghĩa ổn định giữa các seed.
        samples.Sort((a, b) => b.Value.CompareTo(a.Value));
        int requestedLand = Mathf.Clamp(Mathf.RoundToInt(samples.Count * landCoverage), 1, samples.Count);
        for (int i = 0; i < requestedLand; i++)
            landChunks.Add(samples[i].Key);

        // Trung tâm luôn là đất để game có một điểm spawn/objective an toàn.
        landChunks.Add(Vector2Int.zero);
        if (guaranteeConnectedLand)
            ConnectAllLandRegions(extent);
    }

    private float SampleTerrainNoise(Vector3 world)
    {
        float seedOffset = activeSeed * 0.1973f;
        float sampleX = world.x + terrainNoiseOffset.x;
        float sampleZ = world.z + terrainNoiseOffset.y;

        // Domain warp làm biến dạng tọa độ trước khi lấy Perlin. Vì vậy các đảo không còn
        // có cùng một dáng mây/noise lặp lại khi chỉ thay đổi seed hoặc vị trí.
        if (useTerrainDomainWarp && terrainWarpStrength > 0f)
        {
            float warpX = SampleTerrainFbm(sampleX + 31.7f, sampleZ - 17.3f, terrainWarpFrequency, 2, 0.5f, 2f, seedOffset) - 0.5f;
            float warpZ = SampleTerrainFbm(sampleX - 53.1f, sampleZ + 47.9f, terrainWarpFrequency, 2, 0.5f, 2f, -seedOffset) - 0.5f;
            sampleX += warpX * terrainWarpStrength;
            sampleZ += warpZ * terrainWarpStrength;
        }

        float broad = SampleTerrainFbm(sampleX, sampleZ, terrainNoiseFrequency, terrainNoiseOctaves,
            terrainNoisePersistence, terrainNoiseLacunarity, seedOffset);
        float detail = SampleTerrainFbm(sampleX + 101.3f, sampleZ - 79.1f, terrainDetailFrequency,
            terrainDetailOctaves, 0.52f, 2.1f, -seedOffset);
        return Mathf.Clamp01(Mathf.Lerp(broad, detail, terrainDetailBlend));
    }

    private static float SampleTerrainFbm(float worldX, float worldZ, float baseFrequency, int octaves,
        float persistence, float lacunarity, float seedOffset)
    {
        float amplitude = 1f;
        float frequency = Mathf.Max(0.001f, baseFrequency);
        float sum = 0f;
        float amplitudeSum = 0f;
        for (int octave = 0; octave < Mathf.Max(1, octaves); octave++)
        {
            sum += Mathf.PerlinNoise((worldX + seedOffset) * frequency, (worldZ - seedOffset) * frequency) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= Mathf.Clamp(persistence, 0.01f, 1f);
            frequency *= Mathf.Max(1.01f, lacunarity);
        }
        return amplitudeSum > 0f ? Mathf.Clamp01(sum / amplitudeSum) : 0.5f;
    }

    private void ConnectAllLandRegions(int extent)
    {
        HashSet<Vector2Int> connected = FloodLand(Vector2Int.zero, extent);
        if (connected.Count == 0)
        {
            landChunks.Add(Vector2Int.zero);
            connected.Add(Vector2Int.zero);
        }

        while (connected.Count < landChunks.Count)
        {
            Vector2Int from = Vector2Int.zero;
            foreach (Vector2Int candidate in landChunks)
                if (!connected.Contains(candidate)) { from = candidate; break; }

            Vector2Int target = FindNearestConnected(from, connected);
            CarveLandCorridor(from, target, extent);
            connected = FloodLand(Vector2Int.zero, extent);
        }
    }

    private HashSet<Vector2Int> FloodLand(Vector2Int start, int extent)
    {
        HashSet<Vector2Int> visited = new();
        if (!landChunks.Contains(start)) return visited;
        Queue<Vector2Int> queue = new();
        queue.Enqueue(start);
        visited.Add(start);
        Vector2Int[] steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int step in steps)
            {
                Vector2Int next = current + step;
                if (Mathf.Abs(next.x) > extent || Mathf.Abs(next.y) > extent || !landChunks.Contains(next) || !visited.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        return visited;
    }

    private static Vector2Int FindNearestConnected(Vector2Int from, HashSet<Vector2Int> connected)
    {
        int bestDistance = int.MaxValue;
        Vector2Int best = Vector2Int.zero;
        foreach (Vector2Int candidate in connected)
        {
            int distance = Mathf.Abs(candidate.x - from.x) + Mathf.Abs(candidate.y - from.y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    private void CarveLandCorridor(Vector2Int from, Vector2Int to, int extent)
    {
        Vector2Int current = from;
        int directionX = Math.Sign(to.x - current.x);
        while (current.x != to.x)
        {
            AddLandCorridorCell(current, extent);
            current.x += directionX;
        }
        int directionZ = Math.Sign(to.y - current.y);
        while (current.y != to.y)
        {
            AddLandCorridorCell(current, extent);
            current.y += directionZ;
        }
        AddLandCorridorCell(to, extent);
    }

    private void AddLandCorridorCell(Vector2Int center, int extent)
    {
        int radius = Mathf.Max(0, landConnectionWidth - 1);
        for (int x = -radius; x <= radius; x++)
        for (int z = -radius; z <= radius; z++)
        {
            Vector2Int cell = center + new Vector2Int(x, z);
            if (Mathf.Abs(cell.x) <= extent && Mathf.Abs(cell.y) <= extent)
                landChunks.Add(cell);
        }
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

        if (!IsLandChunk(coord))
        {
            if (waterPrefab != null)
            {
                GameObject water = Instantiate(
                    waterPrefab,
                    new Vector3(center.x, waterLevel, center.z),
                    Quaternion.identity,
                    parent.transform);

                water.name = $"Water_{coord.x}_{coord.y}";
                SetLayerRecursively(water, LayerMask.NameToLayer("Water"));
            }
            CreateWaterKillTrigger(parent.transform, center);
        }
        else
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0) parent.layer = groundLayer;

            CreateWalkableCollider(parent, coord);

            int chunkHash = coord.x * 73856093 ^ coord.y * 19349663 ^ activeSeed * 83492791;
            SubdivideAndSpawn(parent.transform, center, maxChunkSize, new System.Random(chunkHash));
            if (enableNoisePrefabScatter)
                SpawnNoisePrefabs(parent.transform, center, maxChunkSize, chunkHash, false);
        }

        activeChunks.Add(coord, parent);
    }

    private void CreateWaterKillTrigger(Transform parent, Vector3 center)
    {
        GameObject trigger = new GameObject("Water Kill Trigger");
        trigger.transform.SetParent(parent, true);
        trigger.transform.position = new Vector3(center.x, waterLevel + 0.45f, center.z);

        int waterLayer = LayerMask.NameToLayer("Water");
        if (waterLayer >= 0) trigger.layer = waterLayer;

        BoxCollider box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = new Vector3(maxChunkSize * 0.96f, 2.5f, maxChunkSize * 0.96f);

        trigger.AddComponent<WaterHazard>();
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

    private void SpawnNoisePrefabs(Transform parentChunk, Vector3 center, float chunkSize, int chunkHash, bool editorPreview)
    {
        if (parentChunk == null || noiseLayerCount <= 0 || maxScatterPerChunk <= 0 || scatterChance <= 0f)
            return;

        System.Random random = new System.Random(chunkHash);
        // Luôn thử các góc chunk: tâm và các trục giữa được chừa cho đường đi,
        // nên chỉ một điểm thử ở tâm sẽ làm cả chunk không bao giờ có công trình.
        int cellsPerSide = Mathf.Max(3, Mathf.CeilToInt(chunkSize / Mathf.Max(0.25f, scatterSpacing)));
        float cellSize = chunkSize / cellsPerSide;
        float totalLayerWeight = 0f;
        foreach (NoisePrefabLayer layer in noiseLayers)
            if (layer != null) totalLayerWeight += Mathf.Max(0.01f, layer.bandWeight);
        if (totalLayerWeight <= 0f) totalLayerWeight = noiseLayerCount;

        int spawned = 0;
        List<Vector3> localStructurePositions = new();
        float half = chunkSize * 0.5f;
        for (int ix = 0; ix < cellsPerSide && spawned < maxScatterPerChunk; ix++)
        for (int iz = 0; iz < cellsPerSide && spawned < maxScatterPerChunk; iz++)
        {
            float jitterX = ((float)random.NextDouble() * 2f - 1f) * cellJitter;
            float jitterZ = ((float)random.NextDouble() * 2f - 1f) * cellJitter;
            float localX = -half + (ix + 0.5f + jitterX) * cellSize;
            float localZ = -half + (iz + 0.5f + jitterZ) * cellSize;
            localX = Mathf.Clamp(localX, -half + scatterEdgePadding, half - scatterEdgePadding);
            localZ = Mathf.Clamp(localZ, -half + scatterEdgePadding, half - scatterEdgePadding);
            Vector3 position = new(center.x + localX, center.y + scatterGroundOffset, center.z + localZ);

            float noise = SampleFractalNoise(position.x, position.z);
            int layerIndex = GetNoiseLayer(noise, totalLayerWeight);
            NoisePrefabLayer layer = layerIndex >= 0 && layerIndex < noiseLayers.Count ? noiseLayers[layerIndex] : null;
            List<NoisePrefabChoice> pool = GetPrefabPool(layer);
            NoisePrefabChoice choice = ChoosePrefab(pool, random);
            if (choice == null || choice.prefab == null) continue;

            float density = Mathf.Lerp(1f, noise, noiseDensityInfluence);
            if (random.NextDouble() > scatterChance * density) continue;

            float scale = Mathf.Lerp(randomScale.x, randomScale.y, (float)random.NextDouble()) * Mathf.Max(0.01f, choice.scaleMultiplier);
            float footprintRadius = GetPrefabFootprintRadius(choice.prefab) * scale + structureNavObstaclePadding;
            if (IsInsideReservedPath(position, center, footprintRadius))
                continue;
            if (!HasWaterClearance(position, footprintRadius + structureWaterClearance))
                continue;

            Vector3 normal = Vector3.up;
            if (snapToGround && Physics.Raycast(position + Vector3.up * groundRayStartHeight, Vector3.down, out RaycastHit groundHit,
                    groundRayStartHeight * 2f, scatterGroundMask, QueryTriggerInteraction.Ignore))
            {
                position = groundHit.point + groundHit.normal * scatterGroundOffset;
                normal = groundHit.normal;
            }
            else if (snapToGround && skipWhenGroundNotFound)
            {
                continue;
            }

            if (avoidOccupiedPoints && avoidanceMask.value != 0 &&
                Physics.CheckSphere(position + Vector3.up * avoidanceRadius, avoidanceRadius, avoidanceMask, QueryTriggerInteraction.Ignore))
                continue;

            if (!HasStructureClearance(position, editorPreview, localStructurePositions))
                continue;

            float yaw = randomizeYaw ? (float)random.NextDouble() * 360f : fixedYaw;
            Quaternion rotation = alignToGroundNormal && snapToGround
                ? Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f)
                : Quaternion.Euler(0f, yaw, 0f);
            GameObject instance = InstantiateNoisePrefab(choice.prefab, position, rotation, parentChunk, editorPreview);
            if (instance == null) continue;
            instance.transform.localScale *= scale;
            if (!editorPreview && structuresBlockNavigation)
                AddStructureNavigationBlocker(instance);
            localStructurePositions.Add(position);
            spawned++;
        }
    }

    private bool IsInsideReservedPath(Vector3 position, Vector3 chunkCenter, float footprintRadius)
    {
        float localX = Mathf.Abs(position.x - chunkCenter.x);
        float localZ = Mathf.Abs(position.z - chunkCenter.z);
        float clearance = structurePathHalfWidth + Mathf.Max(0f, footprintRadius);
        // Mỗi chunk giữ một trục ngang và dọc liên thông với chunk đất bên cạnh.
        return localX < clearance || localZ < clearance;
    }

    private bool HasWaterClearance(Vector3 position, float radius)
    {
        // Dùng bán kính footprint đã gồm padding nên cả góc prefab cũng phải nằm trong đất.
        // Các điểm mẫu viền giúp chặn công trình tràn sang chunk nước liền kề.
        if (IsWaterAtWorldPosition(position))
            return false;

        float checkedRadius = Mathf.Max(0f, radius);
        if (checkedRadius <= 0.01f)
            return true;

        const int sampleCount = 16;
        for (int i = 0; i < sampleCount; i++)
        {
            float angle = i * Mathf.PI * 2f / sampleCount;
            Vector3 edge = position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * checkedRadius;
            if (IsWaterAtWorldPosition(edge))
                return false;
        }
        return true;
    }

    private static float GetPrefabFootprintRadius(GameObject prefab)
    {
        if (prefab == null) return 0.5f;
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 0.5f;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return Mathf.Max(0.25f, Mathf.Max(bounds.extents.x, bounds.extents.z));
    }

    private void AddStructureNavigationBlocker(GameObject structure)
    {
        if (structure == null) return;
        Renderer[] renderers = structure.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        if (bounds.size.x < 0.05f || bounds.size.z < 0.05f) return;

        GameObject blocker = new GameObject("Navigation Blocker");
        blocker.transform.SetParent(structure.transform, true);
        blocker.transform.position = new Vector3(bounds.center.x, structure.transform.position.y, bounds.center.z);
        blocker.transform.rotation = Quaternion.identity;
        int obstacleLayer = LayerMask.NameToLayer(structureLayerName);
        if (obstacleLayer >= 0) blocker.layer = obstacleLayer;

        float height = Mathf.Max(1f, bounds.size.y);
        Vector3 size = new Vector3(
            bounds.size.x + structureNavObstaclePadding * 2f,
            height,
            bounds.size.z + structureNavObstaclePadding * 2f);
        BoxCollider collider = blocker.AddComponent<BoxCollider>();
        collider.size = size;
        collider.center = new Vector3(0f, height * 0.5f, 0f);

        NavMeshObstacle obstacle = blocker.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = collider.center;
        obstacle.size = size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        obstacle.carvingMoveThreshold = 0.01f;
        obstacle.carvingTimeToStationary = 0f;
    }

    private bool HasStructureClearance(Vector3 position, bool editorPreview, List<Vector3> localStructurePositions)
    {
        if (structureMinimumSeparation <= 0f) return true;
        float minimumSquared = structureMinimumSeparation * structureMinimumSeparation;
        foreach (Vector3 otherPosition in localStructurePositions)
        {
            Vector3 delta = otherPosition - position;
            delta.y = 0f;
            if (delta.sqrMagnitude < minimumSquared) return false;
        }
        if (editorPreview) return true;
        foreach (GameObject chunk in activeChunks.Values)
        {
            if (chunk == null) continue;
            foreach (NavMeshObstacle obstacle in chunk.GetComponentsInChildren<NavMeshObstacle>(true))
            {
                Vector3 delta = obstacle.transform.position - position;
                delta.y = 0f;
                if (delta.sqrMagnitude < minimumSquared) return false;
            }
        }
        return true;
    }

    private float SampleFractalNoise(float worldX, float worldZ)
    {
        float amplitude = 1f;
        float frequency = Mathf.Max(0.001f, scatterNoiseFrequency);
        float sum = 0f;
        float amplitudeSum = 0f;
        float seedOffset = (activeSeed == 0 ? seed : activeSeed) * 0.1731f;
        for (int octave = 0; octave < noiseOctaves; octave++)
        {
            float x = (worldX + noiseOffset.x + seedOffset) * frequency;
            float z = (worldZ + noiseOffset.y - seedOffset) * frequency;
            sum += Mathf.PerlinNoise(x, z) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= noisePersistence;
            frequency *= noiseLacunarity;
        }
        return amplitudeSum > 0f ? Mathf.Clamp01(sum / amplitudeSum) : 0.5f;
    }

    private int GetNoiseLayer(float noise, float totalWeight)
    {
        float target = Mathf.Clamp01(noise) * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < noiseLayerCount && i < noiseLayers.Count; i++)
        {
            float weight = noiseLayers[i] == null ? 1f : Mathf.Max(0.01f, noiseLayers[i].bandWeight);
            cumulative += weight;
            if (target <= cumulative || i == noiseLayerCount - 1)
                return i;
        }
        return Mathf.Max(0, noiseLayerCount - 1);
    }

    private List<NoisePrefabChoice> GetPrefabPool(NoisePrefabLayer layer)
    {
        if (layer != null && layer.prefabOverrides != null && layer.prefabOverrides.Exists(item => item != null && item.prefab != null && item.weight > 0f))
            return layer.prefabOverrides;
        return defaultNoisePrefabPool;
    }

    private static NoisePrefabChoice ChoosePrefab(List<NoisePrefabChoice> pool, System.Random random)
    {
        if (pool == null) return null;
        float total = 0f;
        foreach (NoisePrefabChoice item in pool)
            if (item != null && item.prefab != null && item.weight > 0f) total += item.weight;
        if (total <= 0f) return null;
        double roll = random.NextDouble() * total;
        foreach (NoisePrefabChoice item in pool)
        {
            if (item == null || item.prefab == null || item.weight <= 0f) continue;
            roll -= item.weight;
            if (roll <= 0d) return item;
        }
        return null;
    }

    private static GameObject InstantiateNoisePrefab(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, bool editorPreview)
    {
#if UNITY_EDITOR
        if (editorPreview && !Application.isPlaying)
        {
            GameObject preview = UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (preview != null)
            {
                preview.transform.SetPositionAndRotation(position, rotation);
                UnityEditor.Undo.RegisterCreatedObjectUndo(preview, "Generate Noise Prefab Preview");
            }
            return preview;
        }
#endif
        return Instantiate(prefab, position, rotation, parent);
    }

#if UNITY_EDITOR
    public void GenerateNoisePrefabPreview()
    {
        if (Application.isPlaying || !enableNoisePrefabScatter)
        {
            Debug.LogWarning("Bật Noise Prefab Scatter và thoát Play Mode trước khi tạo preview.", this);
            return;
        }
        ClearNoisePrefabPreview();
        activeSeed = seed == 0 ? 1 : seed;
        BuildTerrainLayout();
        GameObject root = new GameObject(NoisePreviewName);
        root.transform.SetParent(transform, false);
        root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Create Noise Prefab Preview");
        int radius = Mathf.Clamp(previewChunkRadius, 0, 8);
        Vector2Int centerCoord = WorldToChunk(transform.position);
        for (int x = -radius; x <= radius; x++)
        for (int z = -radius; z <= radius; z++)
        {
            Vector2Int coord = centerCoord + new Vector2Int(x, z);
            if (!IsLandChunk(coord)) continue;
            Vector3 center = ChunkToWorld(coord);
            int chunkHash = coord.x * 73856093 ^ coord.y * 19349663 ^ (seed == 0 ? 1 : seed) * 83492791;
            SpawnNoisePrefabs(root.transform, center, maxChunkSize, chunkHash, true);
        }
        UnityEditor.Selection.activeGameObject = root;
        UnityEditor.SceneView.RepaintAll();
    }

    public void ClearNoisePrefabPreview()
    {
        Transform preview = transform.Find(NoisePreviewName);
        if (preview == null) return;
        if (!Application.isPlaying) UnityEditor.Undo.DestroyObjectImmediate(preview.gameObject);
        else Destroy(preview.gameObject);
    }
#endif

    [Tooltip("Trả về true khi macro chunk là đất trong layout noise hiện tại.")]
    public bool IsLandChunk(Vector2Int coord)
    {
        return landChunks.Contains(coord);
    }

    // Giữ tên method cũ để các script combat/spawn hiện có không bị phá.
    public bool IsRiver(Vector2Int coord)
    {
        return !IsLandChunk(coord);
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
