using UnityEngine;
using UnityEngine.UI;

public class ClippingChanger : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float step = 100f;
    [SerializeField] private float minFar = 5f;
    [SerializeField] private float maxFar = 5000f;

    [SerializeField] private Button add;
    [SerializeField] private Button subtract;

    private void OnEnable()
    {
        if (add != null)
        {
            add.onClick.AddListener(IncreaseClip);
        }

        if (subtract != null)
        {
            subtract.onClick.AddListener(DecreaseClip);
        }
    }

    private void OnDisable()
    {
        if (add != null)
        {
            add.onClick.RemoveListener(IncreaseClip);
        }

        if (subtract != null)
        {
            subtract.onClick.RemoveListener(DecreaseClip);
        }
    }

    private void IncreaseClip() => AdjustFarClip(step);
    private void DecreaseClip() => AdjustFarClip(-step);

    private void AdjustFarClip(float delta)
    {
        if (targetCamera == null)
        {
            Debug.LogWarning("ClippingChanger: No target camera set.");
            return;
        }

        float newFar = Mathf.Clamp(targetCamera.farClipPlane + delta, minFar, maxFar);
        if (Mathf.Approximately(newFar, targetCamera.farClipPlane))
        {
            return;
        }

        targetCamera.farClipPlane = newFar;

        if (targetCamera.nearClipPlane >= targetCamera.farClipPlane)
        {
            targetCamera.nearClipPlane = Mathf.Max(0.01f, targetCamera.farClipPlane - 0.1f);
        }

        Debug.Log($"ClippingChanger: Far clip -> {targetCamera.farClipPlane:F2}");
        VibrationService.TriggerLoadVibration();
    }
}
