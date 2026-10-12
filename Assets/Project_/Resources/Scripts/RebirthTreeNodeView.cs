using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>View component placed on every authored Rebirth Tree node.</summary>
public sealed class RebirthTreeNodeView : MonoBehaviour, IPointerEnterHandler
{
    [Min(0)] public int nodeIndex;
    public Button button;
    public Image frame;
    public Image icon;
    public TMP_Text title;
    public TMP_Text cost;
    [HideInInspector] public RebirthTreeController controller;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(Purchase);
            button.onClick.AddListener(Purchase);
        }
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(Purchase);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        controller?.ShowNodeDetails(nodeIndex);
    }

    public void Refresh(RebirthTreeNodeDefinition node, bool owned, bool canBuy, bool prerequisitesMet)
    {
        if (node == null) return;
        if (icon != null) icon.sprite = node.icon;
        if (title != null) title.text = node.displayName;
        if (cost != null) cost.text = owned ? "OWNED" : prerequisitesMet ? $"{node.cost} P" : "LOCKED";
        if (button != null) button.interactable = canBuy;
        if (frame != null)
        {
            frame.color = owned
                ? new Color(0.22f, 0.63f, 0.48f, 1f)
                : prerequisitesMet
                    ? new Color(0.48f, 0.31f, 0.12f, 1f)
                    : new Color(0.10f, 0.15f, 0.18f, 1f);
        }
    }

    public void Purchase()
    {
        controller?.TryPurchase(nodeIndex);
    }
}
