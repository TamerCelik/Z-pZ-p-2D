using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static float globalPlatformSpeed = 4f;

    [Header("Mobil Performans ve Optimizasyon")]
    [Tooltip("Mobil cihazlarda sabit FPS hedefi (60 FPS akıcı ve pil dostudur)")]
    public int targetFPS = 60;
    [Tooltip("Hasar alındığında telefonun hafifçe titremesi")]
    public bool enableVibration = true;

    [Header("Hız Ayarları")]
    public float baslangicHizi = 4f;
    [Range(0f, 0.5f)] public float hizlanmaMiktari = 0.05f;
    public float maksimumHiz = 8f;

    [Header("UI Objeleri")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI healthText;
    public GameObject gameOverPanel;
    public GameObject mobileControlsPanel;
    public TextMeshProUGUI finalScoreText;
    public TextMeshProUGUI highScoreText;

    [Header("Oyun Ayarları")]
    public int health = 3;
    public Transform respawnPoint;
    public GameObject player;

    [Header("Güvenli Platform Ayarları")]
    public GameObject guvenliPlatformPrefab;
    public float platformMesafeAyari = 0.5f;

    private float score;
    private bool isGameActive = true;

    void Start()
    {
        // 1. MOBİL PERFORMANS VE PİL AYARLARI
        QualitySettings.vSyncCount = 0; // vSync kapatılarak targetFrameRate'in mobilde devreye girmesi sağlanır
        Application.targetFrameRate = targetFPS;
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // Oyun esnasında telefon ekranının kararmasını engeller

        globalPlatformSpeed = baslangicHizi;
        score = 0;
        isGameActive = true;
        UpdateHealthUI();
        Time.timeScale = 1;

        GuvenliPlatformOlustur();
    }

    void Update()
    {
        if (!isGameActive && Input.GetKeyDown(KeyCode.R)) RestartGame();

        if (isGameActive)
        {
            score += Time.deltaTime * 10;
            if (scoreText != null) scoreText.text = "Skor: " + (int)score;

            if (globalPlatformSpeed < maksimumHiz)
            {
                globalPlatformSpeed += Time.deltaTime * hizlanmaMiktari;
            }
        }
    }

    public void TakeDamage()
    {
        if (!isGameActive) return;

        health--;
        UpdateHealthUI();

        // Hasar anında hafif mobil titreşim (Haptic Feedback)
        if (enableVibration)
        {
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        if (health <= 0) GameOver();
        else
        {
            player.transform.position = respawnPoint.position;
            player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        }
    }

    void GuvenliPlatformOlustur()
    {
        if (guvenliPlatformPrefab != null && respawnPoint != null)
        {
            Vector2 platformKonumu = new Vector2(respawnPoint.position.x, respawnPoint.position.y - platformMesafeAyari);
            Instantiate(guvenliPlatformPrefab, platformKonumu, Quaternion.identity);
        }
    }

    void GameOver()
    {
        isGameActive = false;
        if (gameOverPanel != null) gameOverPanel.SetActive(true);

        // Mobil butonları GameOver anında otomatik gizle
        if (mobileControlsPanel != null)
        {
            mobileControlsPanel.SetActive(false);
        }
        else
        {
            MobileButton[] mobileButtons = Object.FindObjectsByType<MobileButton>(FindObjectsSortMode.None);
            foreach (var btn in mobileButtons)
            {
                btn.gameObject.SetActive(false);
            }
        }

        int currentScore = (int)score;
        int highScore = PlayerPrefs.GetInt("HighScore", 0);

        if (currentScore > highScore)
        {
            highScore = currentScore;
            PlayerPrefs.SetInt("HighScore", highScore);
            PlayerPrefs.Save(); // Mobilde skorun anında kalıcı belleğe yazılmasını garantiler
        }

        if (finalScoreText != null) finalScoreText.text = "SKORUN: " + currentScore;
        if (highScoreText != null) highScoreText.text = "EN İYİ: " + highScore;

        Time.timeScale = 0;
    }

    public void RestartGame()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMenu()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(0);
    }

    void UpdateHealthUI()
    {
        if (healthText != null) healthText.text = "Can: " + health;
    }
}
