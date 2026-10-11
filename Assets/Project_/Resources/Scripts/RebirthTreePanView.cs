using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Editor-authored pan/zoom behaviour for the Rebirth Tree viewport.
/// It moves only the assigned content RectTransform and never creates UI at runtime.
/// </summary>
public sealed class RebirthTreePanView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    [Tooltip("Khung có RectMask2D. Node và đường nối chỉ hiện bên trong khung này.")]
    public RectTransform viewport;
    [Tooltip("Nội dung cây được kéo bên trong khung.")]
    public RectTransform content;
    [Range(0.5f, 1.5f), Tooltip("Độ nhạy khi kéo bản đồ cây.")]
    public float dragMultiplier = 1f;
    [Range(0.55f, 1f), Tooltip("Mức zoom nhỏ nhất khi cuộn chuột trên bản đồ cây.")]
    public float minZoom = 0.72f;
    [Range(1f, 1.8f), Tooltip("Mức zoom lớn nhất khi cuộn chuột trên bản đồ cây.")]
    public float maxZoom = 1.28f;
    [Range(0.02f, 0.25f), Tooltip("Mức thay đổi zoom cho mỗi nấc cuộn chuột.")]
    public float zoomStep = 0.10f;

    [Tooltip("Vị trí cây khi mở panel hoặc bấm Reset View.")]
    public Vector2 initialContentPosition = new Vector2(290f, 0f);

    private Canvas parentCanvas;

    private void Awake()
    {
        if (viewport == null) viewport = transform as RectTransform;
        if (content == null && transform.childCount > 0) content = transform.GetChild(0) as RectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
        ClampPosition();
    }

    public void OnBeginDrag(PointerEventData eventData) => ClampPosition();

    public void OnDrag(PointerEventData eventData)
    {
        if (content == null) return;
        float scale = parentCanvas != null ? Mathf.Max(0.01f, parentCanvas.scaleFactor) : 1f;
        content.anchoredPosition += eventData.delta * (dragMultiplier / scale);
        ClampPosition();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (content == null || Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
        float current = content.localScale.x;
        float next = Mathf.Clamp(current + Mathf.Sign(eventData.scrollDelta.y) * zoomStep, minZoom, maxZoom);
        content.localScale = new Vector3(next, next, 1f);
        ClampPosition();
    }

    public void ResetView()
    {
        if (content == null) return;
        content.localScale = Vector3.one;
        content.anchoredPosition = initialContentPosition;
        ClampPosition();
    }

    private void ClampPosition()
    {
        if (viewport == null || content == null) return;
        Vector2 viewportSize = viewport.rect.size;
        Vector2 contentSize = new Vector2(content.rect.width * content.localScale.x, content.rect.height * content.localScale.y);
        Vector2 allowed = new Vector2(Mathf.Max(0f, (contentSize.x - viewportSize.x) * 0.5f), Mathf.Max(0f, (contentSize.y - viewportSize.y) * 0.5f));
        Vector2 position = content.anchoredPosition;
        position.x = Mathf.Clamp(position.x, -allowed.x, allowed.x);
        position.y = Mathf.Clamp(position.y, -allowed.y, allowed.y);
        content.anchoredPosition = position;
    }
}