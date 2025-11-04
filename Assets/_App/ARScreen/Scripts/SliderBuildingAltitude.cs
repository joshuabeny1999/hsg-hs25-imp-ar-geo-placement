using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SliderBuildingAltitude : MonoBehaviour
{
    public GeoObjectSpawner spawner; // Referenz im Inspector setzen
    public Slider slider;                      // deinen Slider referenzieren
    public TextMeshProUGUI valueText; 
    private float _lastSliderValue;
    private bool _listenerRegistered;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (slider == null) slider = GetComponent<Slider>();
        if (spawner == null || slider == null)
            return;

        var currentAltitude = Mathf.Clamp((float)spawner.AltitudeMeters, slider.minValue, slider.maxValue);
        slider.SetValueWithoutNotify(currentAltitude);
        _lastSliderValue = currentAltitude;
        UpdateLabel(currentAltitude);

        slider.onValueChanged.AddListener(OnHeightChanged);
        _listenerRegistered = true;
    }

    void OnDestroy()
    {
        if (slider != null && _listenerRegistered)
        {
            slider.onValueChanged.RemoveListener(OnHeightChanged);
            _listenerRegistered = false;
        }
    }

    void OnHeightChanged(float a)
    {
        if (spawner == null)
            return;

        float delta = a - _lastSliderValue;
        if (!Mathf.Approximately(delta, 0f))
        {
            spawner.SetBuildingAltitudeMeters(delta);
        }

        _lastSliderValue = a;
        UpdateLabel(a);
    }
    void UpdateLabel(float a)
    {
        if (valueText != null) valueText.text = a.ToString() + " m"; 
    }
}
