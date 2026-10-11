using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class RuntimeNavMeshSystem : MonoBehaviour
{
    public InfiniteMapGenerator map;
    public float rebuildDelay = 0.35f;
    public float verticalSize = 30f;

    private NavMeshData navData;
    private NavMeshDataInstance navInstance;
    private float dirtyTimer = -1f;
    private AsyncOperation buildOperation;
    private bool rebuildQueued;

    void Start()
    {
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        if (map != null) map.MapChanged += RequestRebuild;
        RequestRebuild();
    }

    void OnDestroy()
    {
        if (map != null) map.MapChanged -= RequestRebuild;
        if (navInstance.valid) navInstance.Remove();
    }

    public void RequestRebuild()
    {
        // Map có thể đổi trong khi NavMesh async build còn chạy. Giữ một lượt rebuild
        // kế tiếp thay vì bỏ qua thay đổi mới nhất, tránh đất đã hiện nhưng NavMesh cũ.
        if (buildOperation != null && !buildOperation.isDone)
        {
            rebuildQueued = true;
            return;
        }
        dirtyTimer = rebuildDelay;
    }

    void Update()
    {
        if (buildOperation != null)
        {
            if (!buildOperation.isDone) return;
            buildOperation = null;
            if (rebuildQueued)
            {
                rebuildQueued = false;
                dirtyTimer = rebuildDelay;
            }
        }
        if (dirtyTimer < 0f) return;
        dirtyTimer -= Time.deltaTime;
        if (dirtyTimer > 0f) return;
        dirtyTimer = -1f;
        Rebuild();
    }

    private void Rebuild()
    {
        if (map == null) return;
        if (buildOperation != null && !buildOperation.isDone)
        {
            rebuildQueued = true;
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) return;

        float worldSize = map.WorldHalfSize * 2f + map.maxChunkSize * 2f;
        Bounds bounds = new Bounds(Vector3.zero, new Vector3(worldSize, verticalSize, worldSize));

        List<NavMeshBuildSource> sources = new();
        List<NavMeshBuildMarkup> markups = new();
        NavMeshBuilder.CollectSources(bounds, 1 << groundLayer, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        if (navData == null)
        {
            navData = new NavMeshData(settings.agentTypeID);
            navData.position = Vector3.zero;
            navData.rotation = Quaternion.identity;
            navInstance = NavMesh.AddNavMeshData(navData);
        }

        buildOperation = NavMeshBuilder.UpdateNavMeshDataAsync(navData, settings, sources, bounds);
    }
}
