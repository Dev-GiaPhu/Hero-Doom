using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerSpawner : MonoBehaviour
{
    [Header("Monster 1 - Basic Melee")]
    [Tooltip("Kéo prefab quái #1 vào đây.")]
    public GameObject monster1Prefab;
    public double monster1Cost = 10d;

    [Header("Monster 2 - Mage")]
    [Tooltip("Kéo prefab quái #2 vào đây.")]
    public GameObject monster2Prefab;
    public double monster2Cost = 25d;

    [Header("Monster 3 - Tank")]
    [Tooltip("Kéo ĐÚNG prefab Tank vào đây. Không tự động đoán prefab.")]
    public GameObject monster3Prefab;
    public double monster3Cost = 40d;

    [Header("Monster 4 - Assassin")]
    [Tooltip("Kéo ĐÚNG prefab Assassin vào đây. Không tự động đoán prefab.")]
    public GameObject monster4Prefab;
    public double monster4Cost = 35d;

    [Header("Selection")]
    [SerializeField, Range(0, 3)]
    private int currentMonsterIndex = 0;

    [Header("Placement")]
    public LayerMask groundLayer;
    public Transform crosshairIndicator;
    public float navMeshSampleRadius = 1.5f;
    [Range(0f, 4f)] public float spawnWaterClearance = 1.25f;

    public int SelectedMonsterIndex => currentMonsterIndex;
    public event Action<int> SelectedMonsterChanged;

    private Camera mainCam;
    private InfiniteMapGenerator map;

    void Start()
    {
        mainCam = Camera.main;
        map = FindFirstObjectByType<InfiniteMapGenerator>();
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

            SpawnMonster();
        }
    }

    private void HandleSwitching()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectMonster(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectMonster(1);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) SelectMonster(2);
        if (Keyboard.current.digit4Key.wasPressedThisFrame) SelectMonster(3);
    }

    public void SelectMonster(int index)
    {
        if (index < 0 || index >= 4) return;

        currentMonsterIndex = index;
        SelectedMonsterChanged?.Invoke(currentMonsterIndex);
    }

    private GameObject GetSelectedPrefab()
    {
        return currentMonsterIndex switch
        {
            0 => monster1Prefab,
            1 => monster2Prefab,
            2 => monster3Prefab,
            3 => monster4Prefab,
            _ => null
        };
    }

    private double GetSelectedCost()
    {
        return currentMonsterIndex switch
        {
            0 => monster1Cost,
            1 => monster2Cost,
            2 => monster3Cost,
            3 => monster4Cost,
            _ => 0d
        };
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
                crosshairIndicator.position = hit.point;
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

        GameObject prefab = GetSelectedPrefab();

        if (prefab == null)
        {
            Debug.LogWarning(
                $"[HeroDoom] Monster slot {currentMonsterIndex + 1} chưa được gán prefab trong PlayerSpawner Inspector.");
            return;
        }

        MonsterAI prefabAI = prefab.GetComponent<MonsterAI>();

        if (prefabAI == null)
        {
            Debug.LogError(
                $"[HeroDoom] Prefab {prefab.name} chưa có MonsterAI. Gắn MonsterAI trực tiếp lên prefab để role/stats hiện trong Inspector.");
            return;
        }

        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer))
            return;

        if (!IsValidSpawn(hit.point))
            return;

        Vector3 spawnPoint = hit.point;

        if (NavMesh.SamplePosition(
                hit.point,
                out NavMeshHit navHit,
                navMeshSampleRadius,
                NavMesh.AllAreas))
        {
            if (map != null &&
                !map.IsSafeDistanceFromWater(
                    navHit.position,
                    spawnWaterClearance))
                return;

            spawnPoint = navHit.position;
        }

        double cost = GetSelectedCost();

        if (HeroDoomGameManager.Instance != null &&
            !HeroDoomGameManager.Instance.SpendGold(cost))
            return;

        GameObject go = Instantiate(prefab, spawnPoint, Quaternion.identity);
        go.name = $"Monster_{currentMonsterIndex + 1}_{prefab.name}";
        go.tag = "Enemy";

        MonsterAI ai = go.GetComponent<MonsterAI>();

        CapturePillarManager.Instance?.TryAssignGuard(ai, spawnPoint);
    }

    private bool IsValidSpawn(Vector3 point)
    {
        if (map == null) return true;

        return
            map.IsInsideWorld(point) &&
            map.IsSafeDistanceFromWater(point, spawnWaterClearance);
    }
}
