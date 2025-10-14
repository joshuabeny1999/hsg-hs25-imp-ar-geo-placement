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
            if (slider.minValue == 0f) slider.minValue = 0f;
            if (slider.maxValue < 20f) slider.maxValue = 20f;

            slider.value = spawner.cubeHeightMeters;
            slider.onValueChanged.AddListener(OnHeightChanged);
            UpdateLabel(slider.value);
        }
    }

    void OnHeightChanged(float h)
    {
        if (spawner != null)
        {
            spawner.SetCubeHeightMeters(h);
            UpdateLabel(h);
        }
    }

    void UpdateLabel(float h)
    {
        if (valueText != null) valueText.text = $"{h:0.0} m";
    }
}