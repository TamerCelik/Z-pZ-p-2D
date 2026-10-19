using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public void StartGame()
    {
        // "1" numaralý sahneyi yükle (Birazdan ayarlayacaðýz)
        SceneManager.LoadScene(1);
    }

    public void QuitGame()
    {
        Debug.Log("Oyundan Çýkýldý!");
        Application.Quit();
    }
}