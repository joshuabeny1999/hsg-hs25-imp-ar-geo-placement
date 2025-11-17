using UnityEngine;

public static class VibrationService
{
    public static void TriggerLoadVibration(int durationMs = 250)
    {

        long vibrationDuration = (int)Mathf.Clamp(durationMs, 1000f, 5000f);
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null)
            {
                Debug.Log("[NearbyProjectsListController] Vibrate skipped: no activity");
                return;
            }

            var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            if (vibrator == null)
            {
                Debug.Log("[NearbyProjectsListController] Vibrate skipped: no vibrator service");
                return;
            }

            var version = new AndroidJavaClass("android.os.Build$VERSION");
            int sdkInt = version.GetStatic<int>("SDK_INT");
            if (sdkInt >= 26)
            {
                var vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");
                int amplitude = vibrationEffect.GetStatic<int>("DEFAULT_AMPLITUDE");
                var effect = vibrationEffect.CallStatic<AndroidJavaObject>("createOneShot", vibrationDuration, amplitude);
                vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", (long)durationMs);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[NearbyProjectsListController] Vibrate failed {ex.Message}");
        }
#else
        Handheld.Vibrate();
#endif
    }
}
