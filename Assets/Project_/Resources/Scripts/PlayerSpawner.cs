using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSpawner : MonoBehaviour
{
    [Header("--- Cấu Hình Triệu Hồi ---")]
    public GameObject[] monsterPrefabs;
    public LayerMask groundLayer;
    public Transform crosshairIndicator; 
    
    private int currentMonsterIndex = 0;
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;
    }

    void Update()
    {
        HandleMouseAim();
        HandleSwitching();
        
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            SpawnMonster();
        }
    }

    private void HandleMouseAim()
    {
        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundLayer))
        {
            if (crosshairIndicator != null)
            {
                crosshairIndicator.position = hit.point;
            }
        }
    }

    private void HandleSwitching()
    {
        if (Keyboard.current.digit1Key.wasPressedThisFrame) currentMonsterIndex = 0;
        if (Keyboard.current.digit2Key.wasPressedThisFrame) currentMonsterIndex = 1;
        if (Keyboard.current.digit3Key.wasPressedThisFrame) currentMonsterIndex = 2;
    }

    private void SpawnMonster()
    {
        Ray ray = mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, groundLayer))
        {
            if (currentMonsterIndex < monsterPrefabs.Length && monsterPrefabs[currentMonsterIndex] != null)
            {
                Instantiate(monsterPrefabs[currentMonsterIndex], hit.point, Quaternion.identity);
            }
        }
    }
}