using UnityEngine;

/// <summary>
/// Ensures the camera has a valid near clip plane before Lightship reads it.
/// Attach this to the Main Camera (AR Camera) in your scene.
/// </summary>
[DefaultExecutionOrder(-1000)] // Run very early, before Lightship
[RequireComponent(typeof(Camera))]
public class EnsureNearClip : MonoBehaviour
{
    [SerializeField] private float minNearClip = 0.1f;

    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        EnforceNearClip();
    }

    private void OnEnable()
    {
        EnforceNearClip();
    }

    private void Start()
    {
        EnforceNearClip();
    }

    // Also check in LateUpdate for the first few frames in case something resets it
    private int _frameCount = 0;
    private void LateUpdate()
    {
        if (_frameCount < 30)
        {
            EnforceNearClip();
            _frameCount++;
        }
    }

    private void EnforceNearClip()
    {
        if (_cam != null && _cam.nearClipPlane < minNearClip)
        {
            _cam.nearClipPlane = minNearClip;
            Debug.Log($"[EnsureNearClip] Set camera near clip to {minNearClip}");
        }
    }
}
