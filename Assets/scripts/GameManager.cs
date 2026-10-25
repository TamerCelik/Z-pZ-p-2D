using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static float globalPlatformSpeed = 2.2f;

    [Header("Mobil Performans ve Optimizasyon")]
    [Tooltip("Mobil cihazlarda sabit FPS hedefi (90 FPS akıcıdır)")]
    public int targetFPS = 90;
    [Tooltip("Hasar alındığında telefonun hafifçe titremesi (Haptic Feedback)")]
    public bool enableVibration = true;

    [Header("Hız Ayarları")]
    public float baslangicHizi = 2.2f;
    public float maksimumHiz = 4.0f;
    [Tooltip("Maksimum hıza ulaşılacak hedef skor puanı (1000 ve sonrasında 4.0 hızında kalır)")]
    public float maxHizSkoru = 1000f;

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

    void Awake()
    {
        QualitySettings.vSyncCount = 0; // vSync kapatılarak targetFrameRate'in mobilde devreye girmesi sağlanır
    }

    void Start()
    {
        Application.targetFrameRate = targetFPS;
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // Oyun esnasında telefon ekranının kararmasını engeller

        globalPlatformSpeed = baslangicHizi;
        score = 0;
        isGameActive = true;
        UpdateHealthUI();
        Time.timeScale = 1;

        GuvenliPlatformOlustur();

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (mobileControlsPanel != null) mobileControlsPanel.SetActive(true);
    }

    void Update()
    {
        if (!isGameActive && Input.GetKeyDown(KeyCode.R)) RestartGame();

        if (isGameActive)
        {
            score += Time.deltaTime * 10;
            if (scoreText != null) scoreText.text = "Skor: " + (int)score;

            // 1000 puana kadar platform hızını pürüzsüzce 4.0'a artır, 1000 ve sonrasında 4.0'da sabitle
            float hizOrani = Mathf.Clamp01(score / maxHizSkoru);
            globalPlatformSpeed = Mathf.Lerp(baslangicHizi, maksimumHiz, hizOrani);
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
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        if (health <= 0)
        {
            GameOver();
        }
        else
        {
            if (player != null && respawnPoint != null)
            {
                player.transform.position = respawnPoint.position;
                Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
            }
            GuvenliPlatformOlustur();
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

    void UpdateHealthUI()
    {
        if (healthText != null) healthText.text = "Can: " + health;
    }

    void GameOver()
    {
        isGameActive = false;
        globalPlatformSpeed = 0;

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

    /// <summary>
    /// Kontrol modunu (0: Tuşlar <-> 1: Jiroskop) değiştirir ve anında uygular.
    /// UI butonlarının OnClick olayına atanabilir.
    /// </summary>
    public void KontrolModunuDegistir()
    {
        int current = PlayerPrefs.GetInt("ControlMode", 0);
        int next = (current == 0) ? 1 : 0;
        PlayerPrefs.SetInt("ControlMode", next);
        PlayerPrefs.Save();

        PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null)
        {
            pc.controlMode = next;
            pc.ApplyControlModeUI();
        }
    }

    public void RestartGame()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void GoToMenu()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene("MenuScene");
    }
}
