using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerSpawner : MonoBehaviour
{
    [Header("4 Monster Prefabs")]
    public GameObject[] monsterPrefabs = new GameObject[4];
    public double[] monsterCosts = { 10d, 25d, 40d, 35d };

    [Header("Placement")]
    public LayerMask groundLayer;
    public Transform crosshairIndicator;
    public float navMeshSampleRadius = 1.5f;

    private int currentMonsterIndex;
    private Camera mainCam;
    private InfiniteMapGenerator map;
    private readonly List<Button> buttons = new();

    void Start()
    {
        mainCam = Camera.main;
        map = FindFirstObjectByType<InfiniteMapGenerator>();
        BindUIButtons();
        UpdateButtonVisuals();
    }

    void Update()
    {
        HandleMouseAim();
        HandleSwitching();

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
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
        UpdateButtonVisuals();
    }

    private void BindUIButtons()
    {
        GameObject root = GameObject.Find("HUD Monsters");
        if (root == null) return;

        Button[] found = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < found.Length && i < 4; i++)
        {
            int captured = i;
            buttons.Add(found[i]);
            found[i].onClick.AddListener(() => SelectMonster(captured));
        }
    }

    private void UpdateButtonVisuals()
    {
        for (int i = 0; i < buttons.Count; i++)
            buttons[i].transform.localScale = Vector3.one * (i == currentMonsterIndex ? 1.12f : 1f);
    }

    private void HandleMouseAim()
    {
        if (mainCam == null || Mouse.current == null) return;
        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer) && IsValidSpawn(hit.point))
        {
            if (crosshairIndicator != null)
            {
                crosshairIndicator.gameObject.SetActive(true);
                crosshairIndicator.position = hit.point;
            }
        }
        else if (crosshairIndicator != null) crosshairIndicator.gameObject.SetActive(false);
    }

    private void SpawnMonster()
    {
        if (mainCam == null || Mouse.current == null) return;
        if (monsterPrefabs == null || currentMonsterIndex >= monsterPrefabs.Length) return;
        GameObject prefab = monsterPrefabs[currentMonsterIndex];
        if (prefab == null) return;

        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, groundLayer)) return;
        if (!IsValidSpawn(hit.point)) return;

        double cost = currentMonsterIndex < monsterCosts.Length ? monsterCosts[currentMonsterIndex] : 0d;
        if (HeroDoomGameManager.Instance != null && !HeroDoomGameManager.Instance.SpendGold(cost)) return;

        Vector3 spawnPoint = hit.point;
        if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, navMeshSampleRadius, NavMesh.AllAreas))
            spawnPoint = navHit.position;

        GameObject go = Instantiate(prefab, spawnPoint, Quaternion.identity);
        go.name = $"Monster_{currentMonsterIndex + 1}_{prefab.name}";
        go.tag = "Enemy";

        MonsterAI ai = go.GetComponent<MonsterAI>();
        if (ai == null) ai = go.AddComponent<MonsterAI>();
        ai.ConfigureBySlot(currentMonsterIndex);

        CapturePillarManager.Instance?.TryAssignGuard(ai, spawnPoint);
    }

    private bool IsValidSpawn(Vector3 point)
    {
        if (map == null) return true;
        return map.IsInsideWorld(point) && !map.IsWaterAtWorldPosition(point);
    }
}
