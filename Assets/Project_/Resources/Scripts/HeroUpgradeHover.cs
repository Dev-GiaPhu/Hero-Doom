using UnityEngine;
using UnityEngine.EventSystems;

public class HeroUpgradeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public HeroUpgradeTooltip tooltip;
    public HeroUpgradeType type;

    public void OnPointerEnter(PointerEventData eventData)
    {
        tooltip?.Show(type, eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        tooltip?.PositionAtPointer(eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        tooltip?.Hide();
    }
}
