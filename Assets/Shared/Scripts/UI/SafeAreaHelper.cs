using UnityEngine;

namespace Shared.Scripts.UI
{
    /// <summary>
    /// Adjusts a RectTransform to fit within the device's safe area (notch, rounded corners).
    /// Attach this script to a full-screen UI Panel (child of Canvas).
    /// All other UI elements should live inside that Panel.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaHelper : MonoBehaviour
    {
        [Header("Safe Area Padding (in pixels)")]
        [Tooltip("Extra padding added inside the safe area (useful to keep UI from screen edges).")]
        public Vector4 extraPadding = new Vector4(16, 16, 16, 16); 
        // left, top, right, bottom

        private RectTransform _rectTransform;
        private Rect _lastSafeArea = new Rect(0, 0, 0, 0);
        private Vector2Int _lastScreenSize = Vector2Int.zero;

        void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            ApplySafeArea();
        }

        void Update()
        {
            // In Editor mode, keep updating when resizing window or switching device
            if (Application.isEditor)
                ApplySafeArea();
        }

        private void ApplySafeArea()
        {
            var safeArea = Screen.safeArea;

            // Reapply when safe area or resolution changes
            if (safeArea == _lastSafeArea && 
                _lastScreenSize.x == Screen.width && 
                _lastScreenSize.y == Screen.height)
                return;

            _lastSafeArea = safeArea;
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);

            // Apply extra padding (subtract from the usable area)
            safeArea.xMin += extraPadding.x;
            safeArea.yMin += extraPadding.w;
            safeArea.xMax -= extraPadding.z;
            safeArea.yMax -= extraPadding.y;

            // Convert to normalized anchor coordinates
            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            // Apply to RectTransform
            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;

            Debug.Log($"[SafeAreaHelper] Applied safe area: {safeArea}, padding={extraPadding}");
        }
    }
}