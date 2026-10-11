using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mine with limited capacity, miner reservations and recharge. Each work cycle
/// produces an authored GoldPickup rather than directly awarding gold.
/// </summary>
public sealed class GoldMine : MonoBehaviour
{
    public static readonly HashSet<GoldMine> Active = new();

    [Header("Capacity and Yield")]
    [Min(1)] public int maxWorkers = 3;
    [Min(1f)] public double initialReserve = 280d;
    [Min(1f)] public double goldPerWork = 12d;
    [Min(.25f)] public float workDuration = 2.5f;
    [Min(.5f)] public float workRadius = 2.5f;
    [Min(1f)] public float rechargeDelay = 38f;
    [Tooltip("0 = indefinitely recharge; 1+ = limited number of refills.")]
    [Min(0)] public int maxRecharges = 0;

    [Header("Authored Visual")]
    public Transform oreVisual;
    [Min(0f)] public float idleSpinSpeed = 15f;
    [Min(0f)] public float bobAmplitude = .06f;
    [Min(.1f)] public float bobFrequency = .65f;
    [Range(.05f, 1f)] public float depletedVisualScale = .4f;

    private readonly HashSet<MonsterAI> workers = new();
    private readonly List<MonsterAI> invalidWorkers = new();
    private float rechargeRemaining;
    private double reserve;
    private int recharges;
    private Vector3 visualPosition;
    private Vector3 visualScale;

    public double RemainingGold => reserve;
    public bool IsDepleted => reserve < 1d;
    public int WorkerCount { get { CleanupWorkers(); return workers.Count; } }

    private void Awake()
    {
        reserve = System.Math.Max(1d, initialReserve);
        if (oreVisual != null) { visualPosition = oreVisual.localPosition; visualScale = oreVisual.localScale; }
    }

    private void OnEnable() { Active.Add(this); }
    private void OnDisable() { Active.Remove(this); ReleaseAll(); }

    private void Update()
    {
        if (IsDepleted)
        {
            rechargeRemaining -= Time.deltaTime;
            if (rechargeRemaining <= 0f && (maxRecharges == 0 || recharges < maxRecharges))
            {
                reserve = System.Math.Max(1d, initialReserve);
                recharges++;
            }
        }

        if (oreVisual != null)
        {
            oreVisual.Rotate(0f, idleSpinSpeed * Time.deltaTime, 0f, Space.World);
            Vector3 pos = visualPosition;
            pos.y += Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f + GetInstanceID()) * bobAmplitude;
            oreVisual.localPosition = pos;
            oreVisual.localScale = Vector3.Lerp(
                oreVisual.localScale,
                visualScale * (IsDepleted ? depletedVisualScale : 1f),
                Time.deltaTime * 4f);
        }
    }

    public bool CanWork(MonsterAI worker)
    {
        if (worker == null || !worker.IsAlive || IsDepleted) return false;
        CleanupWorkers();
        return workers.Contains(worker) || workers.Count < Mathf.Max(1, maxWorkers);
    }

    public bool Reserve(MonsterAI worker)
    {
        if (!CanWork(worker)) return false;
        workers.Add(worker);
        return true;
    }

    public void Release(MonsterAI worker)
    {
        if (worker != null) workers.Remove(worker);
    }

    public bool Harvest(MonsterAI worker)
    {
        if (!workers.Contains(worker) || !CanWork(worker)) return false;

        Vector3 delta = worker.transform.position - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > workRadius * workRadius) return false;

        GoldMineManager manager = GoldMineManager.Instance;
        if (manager == null) return false;
        double amount = System.Math.Min(reserve, System.Math.Max(1d, goldPerWork));
        if (!manager.SpawnMinedGold(worker, this, amount)) return false;

        reserve -= amount;
        if (reserve < 1d)
        {
            reserve = 0d;
            rechargeRemaining = Mathf.Max(1f, rechargeDelay);
            ReleaseAll();
        }
        return true;
    }

    private void CleanupWorkers()
    {
        invalidWorkers.Clear();
        foreach (MonsterAI worker in workers)
            if (worker == null || !worker.IsAlive || worker.AssignedGoldMine != this)
                invalidWorkers.Add(worker);
        foreach (MonsterAI worker in invalidWorkers) workers.Remove(worker);
        invalidWorkers.Clear();
    }

    private void ReleaseAll()
    {
        foreach (MonsterAI worker in workers)
            if (worker != null) worker.ClearGoldMine(this);
        workers.Clear();
    }
}
