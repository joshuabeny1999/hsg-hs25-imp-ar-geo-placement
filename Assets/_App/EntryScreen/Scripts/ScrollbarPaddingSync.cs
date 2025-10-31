using UnityEngine;
using UnityEngine.UI;

public class ScrollbarPaddingSync : MonoBehaviour
{
    [Header("Refs")]
    public ScrollRect scrollRect;                 // Your ScrollRect
    public VerticalLayoutGroup contentLayout;     // The VerticalLayoutGroup on Content

    [Header("Padding (px)")]
    public int rightWhenHidden = 50;              // e.g. 50 when no scrollbar
    public int rightWhenVisible = 100;            // e.g. 100 when scrollbar shows

    [Header("Tuning")]
    public float epsilon = 0.5f;                  // tolerance for float comparisons

    bool _lastVisible;

    void Awake()
    {
        if (!scrollRect) scrollRect = GetComponent<ScrollRect>();
    }

    void OnEnable()                     { UpdatePadding(force: true); }
    void LateUpdate()                   { UpdatePadding(); }
    void OnRectTransformDimensionsChange() { UpdatePadding(); } // reacts to rotations/resizes

    bool ShouldShowVertical()
    {
        if (!scrollRect || !scrollRect.vertical || !scrollRect.content || !scrollRect.viewport)
            return false;

        float contentH = scrollRect.content.rect.height;
        float viewH    = scrollRect.viewport.rect.height;
        return (contentH - viewH) > epsilon;     // scrollbar needed?
    }

    void UpdatePadding(bool force = false)
    {
        bool visible = ShouldShowVertical();
        if (force || visible != _lastVisible)
        {
            var p = contentLayout.padding;
            p.right = visible ? rightWhenVisible : rightWhenHidden;
            contentLayout.padding = p;

            // make layout react immediately
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            _lastVisible = visible;
        }
    }
}
