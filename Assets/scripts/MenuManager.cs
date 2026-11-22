using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public void StartGame()
    {
        // OYNA butonuna basılınca hemen üstünde TEKLİ / ÇİFTLİ seçeneklerini açar
        MultiplayerManager mp = MultiplayerManager.EnsureInstance();
        if (mp != null)
        {
            mp.TogglePlayModeSelector();
            return;
        }

        SceneManager.LoadScene(1);
    }

    public void StartSinglePlayerGame()
    {
        SceneManager.LoadScene(1);
    }

    public void QuitGame()
    {
        Debug.Log("Oyundan cikildi!");
        Application.Quit();
    }
}