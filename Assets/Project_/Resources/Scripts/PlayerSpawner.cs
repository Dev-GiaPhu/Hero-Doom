using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Monster 1 - Basic Melee")]
    [Tooltip("Kéo prefab quái #1 vào đây.")]
    public GameObject monster1Prefab;
    public double monster1Cost = 12d;

    [Header("Monster 2 - Mage")]
    [Tooltip("Kéo prefab quái #2 vào đây.")]
    public GameObject monster2Prefab;
    public double monster2Cost = 32d;

    [Header("Monster 3 - Tank")]
    [Tooltip("Kéo ĐÚNG prefab Tank vào đây. Không tự động đoán prefab.")]
    public GameObject monster3Prefab;
    public double monster3Cost = 68d;

    [Header("Monster 4 - Assassin")]
    [Tooltip("Kéo ĐÚNG prefab Assassin vào đây. Không tự động đoán prefab.")]
    public GameObject monster4Prefab;
    public double monster4Cost = 85d;

    [Header("Upgrades and automatic summoning")]
    public SummonerUpgrades upgrades;
    public Vector2 autoSpawnDistance = new Vector2(7f, 11f);
    [Range(1, 20)] public int autoSpawnAttempts = 8;

    [Header("Selection")]
    [SerializeField, Range(0, 3)]
    private int currentMonsterIndex = 0;

    [Header("Placement")]
    public LayerMask groundLayer;
    public Transform crosshairIndicator;

    [Tooltip("Độ cao cộng thêm cho crosshair so với điểm raycast trên mặt đất. Tăng nếu marker bị chìm dưới map.")]
    [Range(-1f, 3f)] public float crosshairHeightOffset = 0.12f;

    public float navMeshSampleRadius = 1.5f;
    [Range(0f, 4f)] public float spawnWaterClearance = 1.25f;

    public int SelectedMonsterIndex => currentMonsterIndex;
    public event Action<int> SelectedMonsterChanged;

    [Header("Spawn click feedback")]
    [Tooltip("Scale pulse khi triệu hồi thành công.")]
    [Min(0.05f)] public float crosshairPulseDuration = 0.18f;
    [Range(1f, 1.5f)] public float crosshairPulseScale = 1.16f;

    [Header("Thiếu vàng - phản hồi khi click triệu hồi")]
    [Tooltip("Màu flash nhẹ của crosshair khi click triệu hồi nhưng không đủ vàng.")]
    public Color insufficientGoldColor = new(1f, 0.18f, 0.2f, 1f);
    [Tooltip("Thời lượng flash đỏ và scale pulse khi thiếu vàng.")]
    [Range(0.08f, 0.8f)] public float insufficientGoldFeedbackDuration = 0.32f;
    [Tooltip("Scale lớn nhất của crosshair khi thiếu vàng.")]
    [Range(1f, 1.5f)] public float insufficientGoldPulseScale = 1.1f;
    [Tooltip("Âm lượng beep khi thiếu vàng.")]
    [Range(0f, 1f)] public float insufficientGoldSoundVolume = 0.22f;
    [Tooltip("Khoảng nghỉ tối thiểu giữa các beep để click liên tục không gây chói tai.")]
    [Range(0.05f, 1f)] public float insufficientGoldSoundCooldown = 0.16f;
    [Tooltip("Nguồn âm đã author sẵn. Để trống, hệ thống dùng AudioSource trên PlayerSpawner.")]
    public AudioSource feedbackAudioSource;
    [Tooltip("Có thể gán clip SFX riêng. Nếu để trống, game phát một beep ngắn được tạo sẵn.")]
    public AudioClip insufficientGoldSound;

    private Camera mainCam;
    private InfiniteMapGenerator map;
    private Vector3 crosshairBaseScale = Vector3.one;
    private Coroutine crosshairPulseRoutine;
    private Coroutine insufficientGoldFeedbackRoutine;
    private SpriteRenderer[] crosshairSprites;
    private Color[] crosshairBaseColors;
    private float nextInsufficientGoldSoundTime;
    private MonsterAI controlledMonster;

    void Start()
    {
        mainCam = Camera.main;
        map = FindFirstObjectByType<InfiniteMapGenerator>();
        CacheCrosshairVisual();
        SelectedMonsterChanged?.Invoke(currentMonsterIndex);
    }

    void Update()
    {
        HandleMouseAim();
        HandleSwitching();

        if (Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
                return;

            if (controlledMonster != null && controlledMonster.IsAlive) CommandControlledMonster();
            else SpawnMonster();
        }
    }

    private void HandleSwitching()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectMonster(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectMonster(1);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) SelectMonster(2);
        if (Keyboard.current.digit4Key.wasPressedThisFrame) SelectMonster(3);

        if (Keyboard.current.fKey.wasPressedThisFrame) CycleControlledMonster();
    }

    private void CycleControlledMonster()
    {
        // F is a true toggle: one press takes control, the next returns control to AI.
        if (controlledMonster != null)
        {
            controlledMonster.SetPlayerControlled(false);
            controlledMonster = null;
            return;
        }
        var alive = new System.Collections.Generic.List<MonsterAI>();
        foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
            if (monster != null && monster.IsAlive) alive.Add(monster);
        alive.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        if (alive.Count == 0) return;
        controlledMonster = alive[0];
        controlledMonster.SetPlayerControlled(true);
    }

    private void CommandControlledMonster()
    {
        if (mainCam == null || Mouse.current == null || controlledMonster == null) return;
        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer) && IsValidSpawn(hit.point))
            controlledMonster.SetPlayerMoveTarget(hit.point);
    }

    public void SelectMonster(int index)
    {
        if (index < 0 || index >= 4) return;
        if (upgrades != null && !upgrades.IsMonsterUnlocked(index)) return;

        currentMonsterIndex = index;
        SelectedMonsterChanged?.Invoke(currentMonsterIndex);
    }

    public void ResetControlForRebirth()
    {
        currentMonsterIndex=0;
        controlledMonster=null;
        SelectedMonsterChanged?.Invoke(currentMonsterIndex);
    }

    public GameObject GetPrefab(int slot)
    {
        return slot switch
        {
            0 => monster1Prefab,
            1 => monster2Prefab,
            2 => monster3Prefab,
            3 => monster4Prefab,
            _ => null
        };
    }

    public double GetCost(int slot)
    {
        double baseCost = slot switch
        {
            0 => monster1Cost,
            1 => monster2Cost,
            2 => monster3Cost,
            3 => monster4Cost,
            _ => 0d
        };
        return baseCost * (upgrades != null ? upgrades.RebirthSummonCostMultiplier : 1f);
    }

    private void HandleMouseAim()
    {
        if (mainCam == null || Mouse.current == null) return;

        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer) &&
            IsValidSpawn(hit.point))
        {
            if (crosshairIndicator != null)
            {
                crosshairIndicator.gameObject.SetActive(true);
                crosshairIndicator.position =
                    hit.point + Vector3.up * crosshairHeightOffset;
            }
        }
        else if (crosshairIndicator != null)
        {
            crosshairIndicator.gameObject.SetActive(false);
        }
    }

    private void SpawnMonster()
    {
        if (mainCam == null || Mouse.current == null) return;
        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer) || !IsValidSpawn(hit.point))
            return;

        HeroDoomGameManager manager = HeroDoomGameManager.Instance;
        if (manager == null || !manager.CanAfford(GetCost(currentMonsterIndex)))
        {
            PlayInsufficientGoldFeedback();
            return;
        }

        if (TrySpawn(currentMonsterIndex, hit.point, true))
        {
            PulseCrosshair();
        }
    }

    private void CacheCrosshairVisual()
    {
        if (crosshairIndicator == null) return;
        crosshairBaseScale = crosshairIndicator.localScale;
        crosshairSprites = crosshairIndicator.GetComponentsInChildren<SpriteRenderer>(true);
        crosshairBaseColors = new Color[crosshairSprites.Length];
        for (int i = 0; i < crosshairSprites.Length; i++)
            crosshairBaseColors[i] = crosshairSprites[i] != null ? crosshairSprites[i].color : Color.white;
    }

    private void PulseCrosshair()
    {
        if (crosshairIndicator == null) return;
        ResetCrosshairVisual();
        if (crosshairPulseRoutine != null) StopCoroutine(crosshairPulseRoutine);
        crosshairPulseRoutine = StartCoroutine(CrosshairPulseRoutine());
    }

    private IEnumerator CrosshairPulseRoutine()
    {
        float duration = Mathf.Max(0.05f, crosshairPulseDuration);
        float half = duration * 0.5f;
        float t = 0f;
        Vector3 peak = crosshairBaseScale * crosshairPulseScale;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            crosshairIndicator.localScale = Vector3.Lerp(crosshairBaseScale, peak, Mathf.SmoothStep(0f, 1f, t / half));
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            crosshairIndicator.localScale = Vector3.Lerp(peak, crosshairBaseScale, Mathf.SmoothStep(0f, 1f, t / half));
            yield return null;
        }

        crosshairIndicator.localScale = crosshairBaseScale;
        crosshairPulseRoutine = null;
    }

    private void PlayInsufficientGoldFeedback()
    {
        if (crosshairIndicator == null) return;
        if (crosshairSprites == null) CacheCrosshairVisual();
        if (crosshairPulseRoutine != null) StopCoroutine(crosshairPulseRoutine);
        if (insufficientGoldFeedbackRoutine != null) StopCoroutine(insufficientGoldFeedbackRoutine);
        insufficientGoldFeedbackRoutine = StartCoroutine(InsufficientGoldFeedbackRoutine());
        PlayInsufficientGoldSound();
    }

    private IEnumerator InsufficientGoldFeedbackRoutine()
    {
        float duration = Mathf.Max(0.08f, insufficientGoldFeedbackDuration);
        float elapsed = 0f;
        Vector3 peakScale = crosshairBaseScale * insufficientGoldPulseScale;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float pulse = Mathf.Sin(normalized * Mathf.PI);
            crosshairIndicator.localScale = Vector3.Lerp(crosshairBaseScale, peakScale, pulse);
            SetCrosshairColor(pulse);
            yield return null;
        }

        ResetCrosshairVisual();
        insufficientGoldFeedbackRoutine = null;
    }

    private void SetCrosshairColor(float redAmount)
    {
        if (crosshairSprites == null) return;
        for (int i = 0; i < crosshairSprites.Length; i++)
        {
            if (crosshairSprites[i] == null) continue;
            Color flash = insufficientGoldColor;
            flash.a = crosshairBaseColors[i].a;
            crosshairSprites[i].color = Color.Lerp(crosshairBaseColors[i], flash, redAmount);
        }
    }

    private void ResetCrosshairVisual()
    {
        if (crosshairIndicator != null) crosshairIndicator.localScale = crosshairBaseScale;
        if (crosshairSprites == null || crosshairBaseColors == null) return;
        for (int i = 0; i < crosshairSprites.Length; i++)
            if (crosshairSprites[i] != null) crosshairSprites[i].color = crosshairBaseColors[i];
    }

    private void PlayInsufficientGoldSound()
    {
        if (Time.unscaledTime < nextInsufficientGoldSoundTime || insufficientGoldSoundVolume <= 0f) return;
        nextInsufficientGoldSoundTime = Time.unscaledTime + insufficientGoldSoundCooldown;

        if (insufficientGoldSound == null)
        {
            GameAudioController.Instance?.PlayNoMoney();
            return;
        }

        if (feedbackAudioSource == null)
        {
            feedbackAudioSource = GetComponent<AudioSource>();
            if (feedbackAudioSource == null)
                feedbackAudioSource = gameObject.AddComponent<AudioSource>();

            feedbackAudioSource.playOnAwake = false;
            feedbackAudioSource.spatialBlend = 0f;
        }

        feedbackAudioSource.PlayOneShot(insufficientGoldSound, insufficientGoldSoundVolume);
    }

    public bool TryAutoSpawn(int slot, bool chargeGold)
    {
        HeroBotAI hero = HeroDoomGameManager.Instance != null ? HeroDoomGameManager.Instance.CurrentHero : null;
        if (upgrades != null && !upgrades.CanSpawn(slot)) return false;

        Vector3 anchor;
        if (hero != null && hero.IsAlive)
            anchor = hero.transform.position;
        else if (GoldMineManager.Instance != null)
            anchor = GoldMineManager.Instance.EconomyAnchor;
        else
        {
            anchor = Vector3.zero;
            foreach (MonsterAI monster in MonsterAI.ActiveMonsters)
                if (monster != null && monster.IsAlive) { anchor = monster.transform.position; break; }
        }

        for (int i = 0; i < autoSpawnAttempts; i++)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float distance = UnityEngine.Random.Range(autoSpawnDistance.x, Mathf.Max(autoSpawnDistance.x, autoSpawnDistance.y));
            Vector3 point = anchor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
            if (TrySpawn(slot, point, chargeGold)) return true;
        }
        return false;
    }

    public bool TrySpawn(int slot, Vector3 point, bool chargeGold)
    {
        if (slot < 0 || slot > 3 || (upgrades != null && (!upgrades.IsMonsterUnlocked(slot) || !upgrades.CanSpawn(slot)))) return false;
        GameObject prefab = GetPrefab(slot);
        if (prefab == null || prefab.GetComponent<MonsterAI>() == null) return false;
        if (map == null) map = FindFirstObjectByType<InfiniteMapGenerator>();
        if (!IsValidSpawn(point)) return false;
        if (!NavMesh.SamplePosition(point, out NavMeshHit navHit, navMeshSampleRadius, NavMesh.AllAreas)) return false;
        if (!IsValidSpawn(navHit.position)) return false;
        HeroDoomGameManager manager = HeroDoomGameManager.Instance;
        if (chargeGold && (manager == null || !manager.SpendGold(GetCost(slot)))) return false;
        GameObject go = Instantiate(prefab, navHit.position, Quaternion.identity);
        go.name = $"Monster_{slot + 1}_{prefab.name}";
        go.tag = "Enemy";
        MonsterAI ai = go.GetComponent<MonsterAI>();
        if (controlledMonster != null && !controlledMonster.IsAlive) controlledMonster = null;
        upgrades?.Apply(ai, slot);
        CapturePillarManager.Instance?.TryAssignGuard(ai, navHit.position);
        GameAudioController.Instance?.PlaySummon();
        return true;
    }

    private bool IsValidSpawn(Vector3 point)
    {
        if (map == null) return true;

        return
            map.IsInsideWorld(point) &&
            map.IsSafeDistanceFromWater(point, spawnWaterClearance);
    }
}

