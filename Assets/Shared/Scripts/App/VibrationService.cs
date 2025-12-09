using UnityEngine;

public static class VibrationService
{
    public static void TriggerLoadVibration(int durationMs = 50)
    {
        long vibrationDuration = (long)Mathf.Clamp(durationMs, 50f, 5000f);
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            if (activity == null)
            {
                return;
            }

            AndroidJavaClass versionClass = new AndroidJavaClass("android.os.Build$VERSION");
            int sdkInt = versionClass.GetStatic<int>("SDK_INT");
            AndroidJavaObject vibrator = null;
            
            if (sdkInt >= 31)
            {
                AndroidJavaObject vibratorManager = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager");
                if (vibratorManager != null)
                {
                    vibrator = vibratorManager.Call<AndroidJavaObject>("getDefaultVibrator");
                }
            }
            
            if (vibrator == null)
            {
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            
            if (vibrator == null)
            {
                return;
            }

            bool hasVibrator = vibrator.Call<bool>("hasVibrator");
            
            if (!hasVibrator) return;

            if (sdkInt >= 26)
            {
                AndroidJavaClass vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot", vibrationDuration, 255);
                
                // SDK 33+ needs VibrationAttributes
                if (sdkInt >= 33)
                {
                    AndroidJavaClass attrBuilderClass = new AndroidJavaClass("android.os.VibrationAttributes$Builder");
                    AndroidJavaObject attrBuilder = new AndroidJavaObject("android.os.VibrationAttributes$Builder");
                    attrBuilder.Call<AndroidJavaObject>("setUsage", 17); // USAGE_TOUCH = 17
                    AndroidJavaObject attributes = attrBuilder.Call<AndroidJavaObject>("build");
                    vibrator.Call("vibrate", effect, attributes);
                }
                else
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                vibrator.Call("vibrate", vibrationDuration);
            }
        }
        catch (System.Exception ex)
        {
            Debug.Log($"VIBRATE ERROR: {ex.Message}");
        }
#else
        Handheld.Vibrate();
#endif
    }
}
