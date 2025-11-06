using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InfoRow : MonoBehaviour
{
    [SerializeField] TMP_Text labelText;
    [SerializeField] TMP_Text valueText;
    [SerializeField] LayoutElement labelLayout;
    [SerializeField] LayoutGroup horizontalLayoutGroup;

    [SerializeField] float labelPreferredWidth = 300f;

    [Header("Font Sizes")]
    [SerializeField] float titleFontSize = 42f;
    [SerializeField] float subtitleFontSize = 42f;
    [SerializeField] float rowFontSize = 30f;

    [Header("Top Padding (px)")]
    [SerializeField] int titleTopPadding = 32;
    [SerializeField] int subtitleTopPadding = 8;
    [SerializeField] int defaultTopPadding = 2;

    void OnValidate()
    {
        if (labelLayout) labelLayout.preferredWidth = labelPreferredWidth;
        if (labelText)
        {
            labelText.alignment = TextAlignmentOptions.MidlineRight;
        }
        if (valueText) valueText.alignment = TextAlignmentOptions.MidlineLeft;
    }

    public void Set(string label, string value)
    {
        SetTopPadding(defaultTopPadding);
        if (labelText)
        {
            labelText.text = label ?? "";
            labelText.fontSize = rowFontSize;
            labelText.fontStyle = FontStyles.Normal;
        }

        if (labelLayout)
        {
            labelLayout.preferredWidth = labelPreferredWidth;
            labelLayout.flexibleWidth = 0;
        }

        if (valueText)
        {
            valueText.gameObject.SetActive(true);
            valueText.text = value ?? "";
            valueText.fontSize = rowFontSize;
            valueText.fontStyle = FontStyles.Normal;
        }
    }

    public void SetTitle(string title)
    {
        SetTopPadding(titleTopPadding);

        if (labelText)
        {
            labelText.text = title ?? "";
            labelText.fontSize = titleFontSize;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (labelLayout)
        {
            labelLayout.preferredWidth = -1;
            labelLayout.flexibleWidth = 1;
        }

        if (valueText) valueText.gameObject.SetActive(false);
    }

    public void SetSubtitle(string title)
    {
        SetTopPadding(subtitleTopPadding);

        if (labelText)
        {
            labelText.text = title ?? "";
            labelText.fontSize = subtitleFontSize;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (labelLayout)
        {
            labelLayout.preferredWidth = -1;
            labelLayout.flexibleWidth = 1;
        }

        if (valueText) valueText.gameObject.SetActive(false);
    }

    private void SetTopPadding(int top)
    {
        if (!horizontalLayoutGroup) horizontalLayoutGroup = GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayoutGroup == null) return;

        var p = horizontalLayoutGroup.padding;
        p.top = top;
        horizontalLayoutGroup.padding = p;
        horizontalLayoutGroup.SetLayoutHorizontal(); // optional refresh
    }
}