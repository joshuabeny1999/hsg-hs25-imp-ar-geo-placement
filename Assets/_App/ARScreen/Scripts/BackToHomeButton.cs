using UnityEngine;
using UnityEngine.SceneManagement;

public class BackToHomeButton : MonoBehaviour
{
        public void GoBack()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Debug.LogWarning("BackToHomeButton.GoBack ignored in edit mode. Enter Play Mode to navigate.");
            return;
        }
#endif
        SceneManager.LoadScene("Home");
    }
}