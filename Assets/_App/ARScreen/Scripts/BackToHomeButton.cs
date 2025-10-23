using UnityEngine;
using UnityEngine.SceneManagement;

public class BackToHomeButton : MonoBehaviour
{
    [SerializeField] private string homeSceneName = "Home"; // set your list scene name

    public void GoBack()
    {
        SceneManager.LoadScene(homeSceneName);
    }
}