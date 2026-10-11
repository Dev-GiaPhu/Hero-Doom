using System.Collections.Generic;
using UnityEngine;

/// <summary>Authored collectible coin. Only monsters collect it; awards shared Summoner gold.</summary>
public sealed class GoldPickup : MonoBehaviour
{
    public static readonly HashSet<GoldPickup> Active = new();

    [Header("Gold")]
    [Min(1f)] public double goldValue = 10d;
    [Min(1f)] public float lifetime = 50f;
    [Min(0.1f)] public float pickupRadius = 1.35f;
    [Range(0f, 1.5f)] public float collectionDelay = .35f;

    [Header("Authored Visual")]
    public Transform coinVisual;
    [Min(0f)] public float spinDegreesPerSecond = 155f;
    [Min(0f)] public float hoverAmplitude = .16f;
    [Min(.1f)] public float hoverFrequency = 1.7f;

    public bool IsCollected { get; private set; }
    public bool CanCollect =>
        !IsCollected && gameObject.activeInHierarchy &&
        Time.time >= spawnedAt + collectionDelay;
    public MonsterAI ReservedBy { get; private set; }

    public bool CanClaim(MonsterAI monster) =>
        CanCollect && monster != null && monster.IsAlive &&
        (ReservedBy == null || !ReservedBy.IsAlive || ReservedBy == monster);

    public bool TryClaim(MonsterAI monster)
    {
        if (!CanClaim(monster)) return false;
        ReservedBy = monster;
        return true;
    }

    public void Release(MonsterAI monster)
    {
        if (ReservedBy == monster) ReservedBy = null;
    }
    private Vector3 visualRestPosition;
    private float remaining;
    private float phase;
    private float spawnedAt;

    private void Awake()
    {
        if (coinVisual != null) visualRestPosition = coinVisual.localPosition;
        phase = Mathf.Repeat(Mathf.Abs(GetInstanceID()) * .49f, Mathf.PI * 2f);
        remaining = Mathf.Max(1f, lifetime);
    }

    private void OnEnable()
    {
        IsCollected = false;
        ReservedBy = null;
        remaining = Mathf.Max(1f, lifetime);
        spawnedAt = Time.time;
        Active.Add(this);
    }
    private void OnDisable()
    {
        Active.Remove(this);
        if (ReservedBy != null) ReservedBy.ClearGoldPickup(this);
        ReservedBy = null;
    }

    private void Update()
    {
        if (coinVisual != null)
        {
            coinVisual.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
            Vector3 local = visualRestPosition;
            local.y += Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f + phase) * hoverAmplitude;
            coinVisual.localPosition = local;
        }

        remaining -= Time.deltaTime;
        if (remaining <= 0f) Destroy(gameObject);
    }

    public void Configure(double value, float despawnAfter)
    {
        goldValue = System.Math.Max(1d, value);
        lifetime = Mathf.Max(1f, despawnAfter);
        remaining = lifetime;
    }

    public bool TryCollect(MonsterAI collector)
    {
        if (!CanCollect || collector == null || !collector.IsAlive) return false;
        Vector3 delta = collector.transform.position - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > pickupRadius * pickupRadius) return false;

        HeroDoomGameManager game = HeroDoomGameManager.Instance;
        if (game == null) return false;

        IsCollected = true;
        Active.Remove(this);
        if (ReservedBy != null) ReservedBy.ClearGoldPickup(this);
        ReservedBy = null;
        game.AddWorldGold(goldValue);
        GameAudioController.Instance?.PlayGoldPickup();
        Destroy(gameObject);
        return true;
    }
}
