using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives an Editor-authored Rebirth Tree panel. It only updates authored controls;
/// no tree UI is built at runtime.
/// </summary>
public sealed class RebirthTreeController : MonoBehaviour
{
    [Header("Data")]
    public SummonerUpgrades upgrades;
    public RebirthTreeDefinition definition;

    [Header("Authored panel")]
    public GameObject popup;
    public Button openButton;
    public Button closeButton;
    public TMP_Text pointLabel;
    public TMP_Text detailTitle;
    public TMP_Text detailDescription;
    public TMP_Text detailStatus;
    public RebirthTreeNodeView[] nodeViews = System.Array.Empty<RebirthTreeNodeView>();
    public Image[] connectors = System.Array.Empty<Image>();
    [Tooltip("Node đầu/cuối của từng đường nối, cùng thứ tự với Connectors.")]
    public int[] connectorFromNodes = System.Array.Empty<int>();
    public int[] connectorToNodes = System.Array.Empty<int>();

    private int selectedNode;

    private void Awake()
    {
        if (upgrades == null) upgrades = FindFirstObjectByType<SummonerUpgrades>();
        if (definition == null && upgrades != null) definition = upgrades.rebirthTree;
        if (openButton != null) openButton.onClick.AddListener(TogglePopup);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePopup);
        if (popup != null) popup.SetActive(false);
    }

    private void OnEnable()
    {
        if (upgrades != null) upgrades.Changed += Refresh;
    }

    private void Start()
    {
        if (upgrades == null) upgrades = FindFirstObjectByType<SummonerUpgrades>();
        if (upgrades != null)
        {
            upgrades.Changed -= Refresh;
            upgrades.Changed += Refresh;
        }
        Refresh();
    }

    private void OnDisable()
    {
        if (upgrades != null) upgrades.Changed -= Refresh;
    }

    private void OnDestroy()
    {
        if (openButton != null) openButton.onClick.RemoveListener(TogglePopup);
        if (closeButton != null) closeButton.onClick.RemoveListener(ClosePopup);
    }

    public void TogglePopup()
    {
        if (popup == null) return;
        popup.SetActive(!popup.activeSelf);
        if (popup.activeSelf)
        {
            popup.transform.SetAsLastSibling();
            Refresh();
        }
    }

    public void ClosePopup()
    {
        if (popup != null) popup.SetActive(false);
    }

    public void TryPurchase(int nodeIndex)
    {
        if (upgrades == null) upgrades = FindFirstObjectByType<SummonerUpgrades>();
        if (upgrades == null || !upgrades.TryPurchaseRebirthNode(nodeIndex)) return;
        selectedNode = nodeIndex;
        Refresh();
    }

    public void ShowNodeDetails(int nodeIndex)
    {
        selectedNode = Mathf.Max(0, nodeIndex);
        RefreshDetails();
    }

    public void Refresh()
    {
        if (upgrades == null) upgrades = FindFirstObjectByType<SummonerUpgrades>();
        if (upgrades == null) return;
        if (definition == null || definition != upgrades.rebirthTree)
            definition = upgrades.rebirthTree;
        if (definition == null) return;
        RebirthTreeNodeDefinition selected = definition.GetNode(selectedNode) ?? definition.GetNode(0);
        if (pointLabel != null)
            pointLabel.text = $"POINTS  {upgrades.RebirthPoints}  |  COST  {(selected != null ? selected.cost : 0)}";
        foreach (RebirthTreeNodeView view in nodeViews)
        {
            if (view == null) continue;
            view.controller = this;
            RebirthTreeNodeDefinition node = definition.GetNode(view.nodeIndex);
            view.Refresh(node, upgrades.IsRebirthNodePurchased(view.nodeIndex),
                upgrades.CanPurchaseRebirthNode(view.nodeIndex), upgrades.AreRebirthPrerequisitesMet(view.nodeIndex));
        }

        for (int i = 0; i < connectors.Length; i++)
        {
            if (connectors[i] == null) continue;
            int from = i < connectorFromNodes.Length ? connectorFromNodes[i] : -1;
            int to = i < connectorToNodes.Length ? connectorToNodes[i] : -1;
            bool completed = upgrades.IsRebirthNodePurchased(from) && upgrades.IsRebirthNodePurchased(to);
            bool started = upgrades.IsRebirthNodePurchased(from);
            connectors[i].color = completed
                ? new Color(0.30f, 0.92f, 0.66f, 0.92f)
                : started
                    ? new Color(0.44f, 0.70f, 0.60f, 0.65f)
                    : new Color(0.26f, 0.34f, 0.37f, 0.56f);
        }
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (upgrades == null || definition == null) return;
        RebirthTreeNodeDefinition node = definition.GetNode(selectedNode) ?? definition.GetNode(0);
        if (node == null) return;
        if (detailTitle != null) detailTitle.text = node.displayName;
        if (detailDescription != null) detailDescription.text = node.description;
        if (detailStatus != null)
        {
            detailStatus.text = upgrades.IsRebirthNodePurchased(selectedNode)
                ? "PURCHASED • PERMANENT"
                : upgrades.CanPurchaseRebirthNode(selectedNode)
                    ? $"AVAILABLE • COST {node.cost} POINT"
                    : upgrades.AreRebirthPrerequisitesMet(selectedNode)
                        ? $"NEED {node.cost} POINT"
                        : "LOCKED • BUY PREVIOUS NODE";
        }
    }
}
