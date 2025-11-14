using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIHeightSlider : MonoBehaviour
{
    public GeoObjectSpawner spawner; // Referenz im Inspector setzen
    public Slider slider;                      // deinen Slider referenzieren
    public TextMeshProUGUI valueText;                    

    void Start()
    {
        if (slider == null) slider = GetComponent<Slider>();
        if (spawner != null && slider != null)
        {
            // sinnvolle Defaults
            if (slider.minValue < 0.5f) slider.minValue = 0.5f;
            if (slider.maxValue < 30f) slider.maxValue = 30f;

            slider.value = spawner.objectHeightMeters;
            slider.onValueChanged.AddListener(OnHeightChanged);
            UpdateLabel(slider.value);
        }
    }

    void OnHeightChanged(float h)
    {
        if (spawner != null)
        {
            spawner.SetObjectHeightMeters(h);
            UpdateLabel(h);
        }
    }

    void UpdateLabel(float h)
    {
        if (valueText != null) valueText.text = $"{h:0.0} m";
    }
}