using System;
using System.Linq;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
#endif

public class ClippingChanger : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    private const float ClipStep = 100f;
    private const float MinNearClip = 0.01f;

#if ENABLE_INPUT_SYSTEM
    private KeyControl volumeDownKeyControl;
    private KeyControl volumeUpKeyControl;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
    private static readonly KeyCode AndroidVolumeDown = (KeyCode)25;
    private static readonly KeyCode AndroidVolumeUp = (KeyCode)24;
#endif

#if ENABLE_INPUT_SYSTEM
    private void OnEnable()
    {
        CacheKeyboardKeys();
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }
#endif

    private void Update()
    {
        if (targetCamera == null)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        HandleInputSystemVolumeKeys();
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        HandleLegacyVolumeKeys();
#endif
    }

    private void ApplyClipDelta(float delta)
    {
        targetCamera.nearClipPlane = Mathf.Max(MinNearClip, targetCamera.nearClipPlane + delta);
        Debug.Log($"ClippingChanger: Near clip -> {targetCamera.nearClipPlane:F2}");
    }

#if ENABLE_INPUT_SYSTEM
    private void HandleInputSystemVolumeKeys()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (volumeDownKeyControl == null || volumeDownKeyControl.device != keyboard ||
            volumeUpKeyControl == null || volumeUpKeyControl.device != keyboard)
        {
            CacheKeyboardKeys();
        }

        if (volumeDownKeyControl != null && volumeDownKeyControl.wasPressedThisFrame)
        {
            ApplyClipDelta(-ClipStep);
        }
        else if (volumeUpKeyControl != null && volumeUpKeyControl.wasPressedThisFrame)
        {
            ApplyClipDelta(ClipStep);
        }
    }
#endif

#if ENABLE_INPUT_SYSTEM
    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Keyboard)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                case InputDeviceChange.Enabled:
                    CacheKeyboardKeys();
                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.Disabled:
                    volumeDownKeyControl = null;
                    volumeUpKeyControl = null;
                    break;
            }
        }
    }

    private void CacheKeyboardKeys()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            volumeDownKeyControl = null;
            volumeUpKeyControl = null;
            return;
        }

        volumeDownKeyControl = FindKeyByNames(keyboard, "volumedown", "volume_down", "volume down");
        volumeUpKeyControl = FindKeyByNames(keyboard, "volumeup", "volume_up", "volume up");
    }

    private static KeyControl FindKeyByNames(Keyboard keyboard, params string[] candidateNames)
    {
        foreach (var key in keyboard.allKeys)
        {
            foreach (var name in candidateNames)
            {
                if (string.Equals(key.name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key.displayName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key.shortDisplayName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }
        }

        return null;
    }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
    private void HandleLegacyVolumeKeys()
    {
#if UNITY_ANDROID
        if (Input.GetKeyDown(AndroidVolumeDown))
        {
            ApplyClipDelta(-ClipStep);
        }
        else if (Input.GetKeyDown(AndroidVolumeUp))
        {
            ApplyClipDelta(ClipStep);
        }
#endif
    }
#endif
}
