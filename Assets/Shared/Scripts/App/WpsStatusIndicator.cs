using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Changes the color of an Image component to green when WPS is ready.
/// Attach this to a UI panel/image that should indicate WPS status.
/// Uses event-based updates instead of polling every frame.
/// </summary>
public class WpsStatusIndicator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The GeoObjectSpawner to monitor for WPS status")]
    [SerializeField] private GeoObjectSpawner geoObjectSpawner;

    [Header("Colors")]
    [SerializeField] private Color notReadyColor = Color.red;
    [SerializeField] private Color readyColor = Color.green;

    [Header("Target")]
    [Tooltip("The Image component to change color. If not set, will try to get from this GameObject.")]
    [SerializeField] private Image targetImage;

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (geoObjectSpawner == null)
            geoObjectSpawner = FindFirstObjectByType<GeoObjectSpawner>();
    }

    private void OnEnable()
    {
        if (geoObjectSpawner != null)
            geoObjectSpawner.OnWpsReadyChanged += OnWpsReadyChanged;

        // Set initial color
        UpdateColor(geoObjectSpawner != null && geoObjectSpawner.IsWpsReady);
    }

    private void OnDisable()
    {
        if (geoObjectSpawner != null)
            geoObjectSpawner.OnWpsReadyChanged -= OnWpsReadyChanged;
    }

    private void OnWpsReadyChanged(bool isReady)
    {
        UpdateColor(isReady);
    }

    private void UpdateColor(bool isReady)
    {
        if (targetImage == null) return;
        targetImage.color = isReady ? readyColor : notReadyColor;
    }
}
