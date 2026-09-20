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
    [Range(0f, 4f)] public float spawnWaterClearance = 1.25f;

    private int currentMonsterIndex;

    private Camera mainCam;
    private InfiniteMapGenerator map;

    private readonly List<Button> buttons = new();
    private readonly List<Image> buttonImages = new();

    private readonly Sprite[] normalIcons = new Sprite[4];
    private readonly Sprite[] selectedIcons = new Sprite[4];

    void Start()
    {
        mainCam = Camera.main;
        map = FindFirstObjectByType<InfiniteMapGenerator>();

        LoadMonsterIcons();
        BindUIButtons();
        UpdateButtonVisuals();
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
            {
                return;
            }

            SpawnMonster();
        }
    }

    private void HandleSwitching()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame)
            SelectMonster(0);

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
            SelectMonster(1);

        if (Keyboard.current.digit3Key.wasPressedThisFrame)
            SelectMonster(2);

        if (Keyboard.current.digit4Key.wasPressedThisFrame)
            SelectMonster(3);
    }

    public void SelectMonster(int index)
    {
        if (index < 0 || index >= 4) return;

        currentMonsterIndex = index;
        UpdateButtonVisuals();
    }

    private void LoadMonsterIcons()
    {
        Sprite[] sprites =
            Resources.LoadAll<Sprite>("Image/icon monsters");

        Dictionary<string, Sprite> byName =
            new Dictionary<string, Sprite>();

        foreach (Sprite sprite in sprites)
        {
            if (sprite != null)
                byName[sprite.name] = sprite;
        }

        string[] normalNames =
        {
            "icon monsters_0",
            "icon monsters_1",
            "icon monsters_2",
            "icon monsters_3"
        };

        // Mapping đúng theo yêu cầu:
        // Button 1 -> _4
        // Button 2 -> _6
        // Button 3 -> _5
        // Button 4 -> _7
        string[] selectedNames =
        {
            "icon monsters_4",
            "icon monsters_6",
            "icon monsters_5",
            "icon monsters_7"
        };

        for (int i = 0; i < 4; i++)
        {
            byName.TryGetValue(normalNames[i], out normalIcons[i]);
            byName.TryGetValue(selectedNames[i], out selectedIcons[i]);
        }
    }

    private void BindUIButtons()
    {
        buttons.Clear();
        buttonImages.Clear();

        GameObject root = GameObject.Find("HUD Monsters");
        if (root == null) return;

        for (int i = 0; i < root.transform.childCount && buttons.Count < 4; i++)
        {
            Transform child = root.transform.GetChild(i);
            Button button = child.GetComponent<Button>();

            if (button == null)
                continue;

            int capturedIndex = buttons.Count;

            buttons.Add(button);
            buttonImages.Add(button.targetGraphic as Image ?? button.GetComponent<Image>());

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(
                () => SelectMonster(capturedIndex));
        }
    }

    private void UpdateButtonVisuals()
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            bool selected =
                i == currentMonsterIndex;

            Image image =
                i < buttonImages.Count
                    ? buttonImages[i]
                    : null;

            if (image != null)
            {
                Sprite desired =
                    selected
                        ? selectedIcons[i]
                        : normalIcons[i];

                if (desired != null)
                    image.sprite = desired;
            }

            buttons[i].transform.localScale =
                selected
                    ? Vector3.one * 1.06f
                    : Vector3.one;
        }
    }

    private void HandleMouseAim()
    {
        if (mainCam == null || Mouse.current == null)
            return;

        Ray ray =
            mainCam.ScreenPointToRay(
                Mouse.current.position.ReadValue());

        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                500f,
                groundLayer) &&
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
        if (mainCam == null || Mouse.current == null)
            return;

        if (monsterPrefabs == null ||
            currentMonsterIndex >= monsterPrefabs.Length)
        {
            return;
        }

        GameObject prefab =
            monsterPrefabs[currentMonsterIndex];

        if (prefab == null)
            return;

        Ray ray =
            mainCam.ScreenPointToRay(
                Mouse.current.position.ReadValue());

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                500f,
                groundLayer))
        {
            return;
        }

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
            {
                return;
            }

            spawnPoint = navHit.position;
        }

        double cost =
            currentMonsterIndex < monsterCosts.Length
                ? monsterCosts[currentMonsterIndex]
                : 0d;

        if (HeroDoomGameManager.Instance != null &&
            !HeroDoomGameManager.Instance.SpendGold(cost))
        {
            return;
        }

        GameObject go =
            Instantiate(
                prefab,
                spawnPoint,
                Quaternion.identity);

        go.name =
            $"Monster_{currentMonsterIndex + 1}_{prefab.name}";

        go.tag = "Enemy";

        MonsterAI ai =
            go.GetComponent<MonsterAI>();

        if (ai == null)
            ai = go.AddComponent<MonsterAI>();

        ai.ConfigureBySlot(currentMonsterIndex);

        CapturePillarManager.Instance?.TryAssignGuard(
            ai,
            spawnPoint);
    }

    private bool IsValidSpawn(Vector3 point)
    {
        if (map == null)
            return true;

        return
            map.IsInsideWorld(point) &&
            map.IsSafeDistanceFromWater(
                point,
                spawnWaterClearance);
    }
}
