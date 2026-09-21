using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

[Serializable]
public class MonsterHUDSlot
{
    public Button button;
    public Image image;
    public Sprite normalSprite;
    public Sprite selectedSprite;
}

public class MonsterHUDController : MonoBehaviour
{
    [Header("Spawner")]
    public PlayerSpawner playerSpawner;

    [Header("Monster Buttons - thứ tự 1, 2, 3, 4")]
    public MonsterHUDSlot[] slots = new MonsterHUDSlot[4];

    [Header("Selected Visual")]
    [Range(1f, 1.25f)] public float selectedScale = 1.06f;

    private UnityAction[] clickActions;

    void Awake()
    {
        if (playerSpawner == null)
            playerSpawner = FindFirstObjectByType<PlayerSpawner>();

        clickActions = new UnityAction[slots.Length];

        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            MonsterHUDSlot slot = slots[i];

            if (slot == null || slot.button == null)
                continue;

            clickActions[i] = () => SelectMonster(index);
            slot.button.onClick.AddListener(clickActions[i]);
        }
    }

    void OnEnable()
    {
        if (playerSpawner == null)
            playerSpawner = FindFirstObjectByType<PlayerSpawner>();

        if (playerSpawner != null)
        {
            playerSpawner.SelectedMonsterChanged += Refresh;
            Refresh(playerSpawner.SelectedMonsterIndex);
        }
        else
        {
            Refresh(0);
        }
    }

    void OnDisable()
    {
        if (playerSpawner != null)
            playerSpawner.SelectedMonsterChanged -= Refresh;
    }

    void OnDestroy()
    {
        if (clickActions == null) return;

        for (int i = 0; i < slots.Length && i < clickActions.Length; i++)
        {
            if (slots[i] != null &&
                slots[i].button != null &&
                clickActions[i] != null)
            {
                slots[i].button.onClick.RemoveListener(clickActions[i]);
            }
        }
    }

    public void SelectMonster(int index)
    {
        if (playerSpawner != null)
            playerSpawner.SelectMonster(index);
        else
            Refresh(index);
    }

    public void Refresh(int selectedIndex)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            MonsterHUDSlot slot = slots[i];
            if (slot == null) continue;

            bool selected = i == selectedIndex;

            if (slot.image != null)
            {
                Sprite sprite = selected
                    ? slot.selectedSprite
                    : slot.normalSprite;

                if (sprite != null)
                    slot.image.sprite = sprite;
            }

            if (slot.button != null)
            {
                slot.button.transform.localScale =
                    Vector3.one * (selected ? selectedScale : 1f);
            }
        }
    }
}
