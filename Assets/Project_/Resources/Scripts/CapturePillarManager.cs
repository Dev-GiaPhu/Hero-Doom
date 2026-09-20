using System.Collections.Generic;
using UnityEngine;

public class CapturePillarManager : MonoBehaviour
{
    public static CapturePillarManager Instance { get; private set; }

    [Header("Pillars")]
    [Min(1)] public int pillarCount = 3;
    public float captureRadius = 4f;
    public float goldBonusPerCapturedPillar = 0.25f;
    public float relocateInterval = 120f;
    public float edgePadding = 12f;
    public InfiniteMapGenerator map;

    private readonly List<CapturePillar> pillars = new();
    private float relocateTimer;
    private GameObject pillarVisualPrefab;

    public float GoldMultiplier
    {
        get
        {
            int captured = 0;
            foreach (CapturePillar p in pillars)
                if (p != null && p.IsCaptured) captured++;
            return 1f + captured * goldBonusPerCapturedPillar;
        }
    }

    void Awake()
    {
        Instance = this;
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        pillarVisualPrefab = Resources.Load<GameObject>("Prefabs/Environment/pillar");
    }

    void Start()
    {
        SpawnAll();
        relocateTimer = relocateInterval;
    }

    void Update()
    {
        relocateTimer -= Time.deltaTime;
        if (relocateTimer <= 0f)
        {
            SpawnAll();
            relocateTimer = Mathf.Max(10f, relocateInterval);
        }
    }

    private void SpawnAll()
    {
        foreach (CapturePillar p in pillars)
        {
            if (p == null) continue;
            foreach (MonsterAI guard in new List<MonsterAI>(p.guards))
                if (guard != null) guard.ClearGuard(p);
            Destroy(p.gameObject);
        }
        pillars.Clear();

        if (map == null) return;

        for (int i = 0; i < pillarCount; i++)
        {
            if (!map.TryGetRandomSafeGround(edgePadding, out Vector3 pos)) continue;

            GameObject go;
            if (pillarVisualPrefab != null) go = Instantiate(pillarVisualPrefab, pos, Quaternion.identity);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.transform.position = pos;
                go.transform.localScale = new Vector3(0.7f, 2f, 0.7f);
            }

            go.name = $"EnergyPillar_{i + 1}";
            CapturePillar pillar = go.AddComponent<CapturePillar>();
            pillar.captureRadius = captureRadius;
            pillar.goldBonus = goldBonusPerCapturedPillar;

            SphereCollider trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = captureRadius;
            trigger.center = Vector3.zero;

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            pillars.Add(pillar);
        }
    }

    public CapturePillar FindClosestCaptured(Vector3 position)
    {
        CapturePillar best = null;
        float bestSqr = float.MaxValue;
        foreach (CapturePillar p in pillars)
        {
            if (p == null || !p.IsCaptured) continue;
            float sqr = (p.transform.position - position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = p; }
        }
        return best;
    }

    public void TryAssignGuard(MonsterAI monster, Vector3 position)
    {
        if (monster == null) return;
        foreach (CapturePillar p in pillars)
        {
            if (p != null && p.IsInside(position))
            {
                p.RegisterGuard(monster);
                return;
            }
        }
    }
}
