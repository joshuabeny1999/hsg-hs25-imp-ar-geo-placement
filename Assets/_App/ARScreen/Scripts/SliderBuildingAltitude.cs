using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SliderBuildingAltitude : MonoBehaviour
{
    public GeoObjectSpawner spawner; // Referenz im Inspector setzen
    public Slider slider;                      // deinen Slider referenzieren
    public TextMeshProUGUI valueText; 
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (slider == null) slider = GetComponent<Slider>();
        if (spawner != null && slider != null)
        {
            slider.onValueChanged.AddListener(OnHeightChanged);
            UpdateLabel(slider.value);
        }
    }

    void OnHeightChanged(float a)
    {
        if (spawner != null)
        {
            spawner.SetBuildingAltitudeMeters(a);
        }
    }
        void UpdateLabel(float h)
    {
        if (valueText != null) valueText.text = $"{h:0.0} m";
    }
}
