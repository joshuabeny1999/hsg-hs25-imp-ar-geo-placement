using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AccordionSection : MonoBehaviour
{
    [SerializeField] private Button headerButton;
    [SerializeField] private TMP_Text headerText;
    [SerializeField] private RectTransform content;
    [SerializeField] private GameObject caret; // optional

    [Header("Style")]
    public bool SubStyle = false;

    private bool _expanded = true;
    public Transform ContentRoot => content;

    private void Awake()
    {
        if (headerButton) headerButton.onClick.AddListener(Toggle);
    }

    public void SetTitle(string t)
    {
        if (headerText) headerText.text = t;
        // optional: apply sub-style (smaller font, lighter bg)
        if (SubStyle) headerText.fontSize -= 2;
    }

    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        content.gameObject.SetActive(_expanded);
        if (caret) caret.transform.localEulerAngles = new Vector3(0,0, _expanded ? 0 : -90);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
    }

    public void Toggle() => SetExpanded(!_expanded);
}