using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static float globalPlatformSpeed = 1.7f;
    public static float currentScore = 0f;

    [Header("Mobil Performans ve Optimizasyon")]
    [Tooltip("Mobil cihazlarda sabit FPS hedefi (90 FPS akıcıdır)")]
    public int targetFPS = 90;
    [Tooltip("Hasar alındığında telefonun hafifçe titremesi (Haptic Feedback)")]
    public bool enableVibration = true;

    [Header("Hız Ayarları (Kontrollü ve Dengeli)")]
    public float baslangicHizi = 1.7f;
    public float maksimumHiz = 2.4f;
    [Tooltip("Maksimum hıza ve zorluğa ulaşılacak hedef skor puanı (1000 ve sonrasında 2.4 hızında kalır)")]
    public float maxHizSkoru = 1000f;

    [Header("UI Objeleri")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI healthText;
    public GameObject gameOverPanel;
    public GameObject mobileControlsPanel;
    public TextMeshProUGUI finalScoreText;
    public TextMeshProUGUI highScoreText;

    [Header("Duraklatma (Pause) Menüsü")]
    public GameObject pausePanel;
    public GameObject pauseButton;
    public GameObject ayarlarSubPanel;
    public Slider sesSlider;
    public TextMeshProUGUI kontrolButonText;

    [Header("Ses Efektleri")]
    [Tooltip("Can alındığında çalan ses efekti")]
    public AudioClip canKazanmaSesi;
    private AudioSource audioSource;

    [Header("Oyun Ayarları")]
    public int health = 3;
    public Transform respawnPoint;
    public GameObject player;

    [Header("Güvenli Platform Ayarları")]
    public GameObject guvenliPlatformPrefab;
    public float platformMesafeAyari = 0.5f;

    private float score;
    private bool isGameActive = true;
    private bool isPaused = false;

    [Header("Coin Sistemi")]
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI tusYeriButonText;
    public static GameManager Instance { get; private set; }

    private AudioSource coinAudioSource;
    private Coroutine coinPunchCoroutine;

    [Header("Güçlendirmeler (Kalkan, Roket & Kuş Ayağı)")]
    [Tooltip("Koruyucu kalkanın aktif kalma süresi (saniye)")]
    public float shieldDuration = 12f;
    [Tooltip("Kuş ayağı (2 platform zıplama + içinden geçme) süresi (saniye)")]
    public float superFeetDuration = 12f;
    [Tooltip("Kuş ayağı aktifken zıplama kuvvet çarpanı (1.92 -> 2 tam platformu rahatça aşar)")]
    public float superFeetJumpMultiplier = 1.92f;
    public float rocketBoostDuration = 3.2f;
    public float rocketSpeedMultiplier = 3.1f;
    public float rocketTargetY = 1.15f;

    private bool hasShield = false;
    private float shieldTimer = 0f;
    private bool hasSuperFeet = false;
    private float superFeetTimer = 0f;
    private bool isRocketBoosting = false;
    private float rocketTimer = 0f;
    private float postRocketGraceTimer = 0f;
    private GameObject shieldVisualObj;
    private SpriteRenderer shieldVisualRenderer;
    private GameObject superFeetVisualObj;
    private SpriteRenderer superFeetVisualRenderer;
    private GameObject rocketFlameObj;
    private SpriteRenderer rocketFlameRenderer;

    public bool HasShield => hasShield;
    public bool IsSuperFeetActive => hasSuperFeet;
    public bool IsRocketBoosting => isRocketBoosting;

    void Awake()
    {
        Instance = this;
        QualitySettings.vSyncCount = 0; // vSync kapatılarak targetFrameRate'in mobilde devreye girmesi sağlanır
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Application.targetFrameRate = targetFPS;
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // Oyun esnasında telefon ekranının kararmasını engeller

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        coinAudioSource = gameObject.AddComponent<AudioSource>();
        coinAudioSource.playOnAwake = false;

        globalPlatformSpeed = baslangicHizi;
        score = 0;
        currentScore = 0;
        isGameActive = true;
        isPaused = false;
        UpdateHealthUI();
        UpdateCoinUI();
        Time.timeScale = 1;

        SpawnPlayerOnFirstPlatform();

        // Uzay evresi düşen meteor engel spawner'ını otomatik başlat
        if (Object.FindAnyObjectByType<SpaceMeteorSpawner>() == null)
        {
            gameObject.AddComponent<SpaceMeteorSpawner>();
        }

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (ayarlarSubPanel != null) ayarlarSubPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(true);
        if (mobileControlsPanel != null) mobileControlsPanel.SetActive(true);

        // Ses ayarını başlat
        float kayitliSes = PlayerPrefs.GetFloat("OyunSesi", 1f);
        AudioListener.volume = kayitliSes;
        if (sesSlider != null) sesSlider.value = kayitliSes;

        GuncelleKontrolText(PlayerPrefs.GetInt("ControlMode", 0));
        GuncelleTusYeriText(PlayerPrefs.GetInt("ButtonLayout", 0));
    }

    void Update()
    {
        if (!isGameActive && Input.GetKeyDown(KeyCode.R)) RestartGame();

        // Android geri tuşu veya Escape ile oyunu duraklat / devam ettir
        if (isGameActive && Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }

        if (isGameActive && !isPaused)
        {
            if (postRocketGraceTimer > 0f)
            {
                postRocketGraceTimer -= Time.deltaTime;
            }

            // 1000 puana kadar temel platform hızını pürüzsüzce maksimumHiz'a artır
            float hizOrani = Mathf.Clamp01(score / maxHizSkoru);
            float baseSpeed = Mathf.Lerp(baslangicHizi, maksimumHiz, hizOrani);

            if (isRocketBoosting)
            {
                rocketTimer -= Time.deltaTime;
                score += Time.deltaTime * 42f;
                globalPlatformSpeed = baseSpeed * rocketSpeedMultiplier;
                UpdateRocketVisuals();

                if (rocketTimer <= 0f)
                {
                    EndRocketBoost(baseSpeed);
                }
            }
            else
            {
                score += Time.deltaTime * 10f;
                globalPlatformSpeed = baseSpeed;
            }

            currentScore = score;
            if (scoreText != null) scoreText.text = "SKOR: " + (int)score;

            if (hasShield)
            {
                shieldTimer -= Time.deltaTime;
                if (shieldTimer <= 0f)
                {
                    hasShield = false;
                    shieldTimer = 0f;
                }
            }
            UpdateShieldVisuals();

            if (hasSuperFeet)
            {
                superFeetTimer -= Time.deltaTime;
                if (superFeetTimer <= 0f)
                {
                    hasSuperFeet = false;
                    superFeetTimer = 0f;
                }
            }
            UpdateSuperFeetVisuals();
        }
    }

    #region Pause Menü Fonksiyonları
    public void PauseGame()
    {
        if (!isGameActive) return;

        isPaused = true;
        Time.timeScale = 0f;

        if (pausePanel != null) pausePanel.SetActive(true);
        if (pauseButton != null) pauseButton.SetActive(false);
        if (ayarlarSubPanel != null) ayarlarSubPanel.SetActive(false);

        // Ses ve kontrol metinlerini hazırla
        float kayitliSes = PlayerPrefs.GetFloat("OyunSesi", 1f);
        if (sesSlider != null) sesSlider.value = kayitliSes;
        GuncelleKontrolText(PlayerPrefs.GetInt("ControlMode", 0));
        GuncelleTusYeriText(PlayerPrefs.GetInt("ButtonLayout", 0));
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;

        if (pausePanel != null) pausePanel.SetActive(false);
        if (ayarlarSubPanel != null) ayarlarSubPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(true);

        // Kontrol moduna göre butonları tazele
        PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null) pc.ApplyControlModeUI();
    }

    public void OpenSettingsSubPanel()
    {
        if (ayarlarSubPanel != null) ayarlarSubPanel.SetActive(true);
    }

    public void CloseSettingsSubPanel()
    {
        if (ayarlarSubPanel != null) ayarlarSubPanel.SetActive(false);
    }

    public void SetVolume(float volume)
    {
        AudioListener.volume = volume;
        PlayerPrefs.SetFloat("OyunSesi", volume);
        PlayerPrefs.Save();
    }


    private void GuncelleKontrolText(int mode)
    {
        if (kontrolButonText != null)
        {
            kontrolButonText.text = (mode == 1) ? "JIROSKOP" : "TUS";
        }
    }

    private void GuncelleTusYeriText(int layout)
    {
        if (tusYeriButonText != null)
        {
            tusYeriButonText.text = (layout == 1) ? "YON TUSLARI: SAG" : "YON TUSLARI: SOL";
        }
    }

    public void TusYeriniDegistir()
    {
        int current = PlayerPrefs.GetInt("ButtonLayout", 0);
        int next = (current == 0) ? 1 : 0;
        PlayerPrefs.SetInt("ButtonLayout", next);
        PlayerPrefs.Save();

        GuncelleTusYeriText(next);

        PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null)
        {
            pc.ApplyControlModeUI();
        }
    }
    #endregion

    /// <summary>
    /// Can pickup'ı toplandığında veya ödül kazanıldığında can ekler ve ses efektini çalar.
    /// </summary>
    public void AddHealth(int amount = 1)
    {
        health += amount;
        UpdateHealthUI();

        if (canKazanmaSesi != null)
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.PlayOneShot(canKazanmaSesi);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (enableVibration) Handheld.Vibrate();
#endif
    }

    public void ActivateShield()
    {
        hasShield = true;
        shieldTimer = shieldDuration;

        if (canKazanmaSesi != null)
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.pitch = 1.18f;
            audioSource.PlayOneShot(canKazanmaSesi, 0.7f);
            audioSource.pitch = 1f;
        }

        EnsureShieldVisual();
        if (shieldVisualObj != null) shieldVisualObj.SetActive(true);
    }

    public void ActivateSuperFeet()
    {
        hasSuperFeet = true;
        superFeetTimer = superFeetDuration;

        if (canKazanmaSesi != null)
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.pitch = 1.26f;
            audioSource.PlayOneShot(canKazanmaSesi, 0.78f);
            audioSource.pitch = 1f;
        }

        EnsureSuperFeetVisual();
        if (superFeetVisualObj != null) superFeetVisualObj.SetActive(true);
    }

    private void EnsureSuperFeetVisual()
    {
        if (superFeetVisualObj != null || player == null) return;

        superFeetVisualObj = new GameObject("SuperPigeonFeetVisual");
        superFeetVisualObj.transform.SetParent(player.transform, false);
        superFeetVisualObj.transform.localPosition = new Vector3(0f, -0.155f, 0f);

        Vector3 pScale = player.transform.localScale;
        float absX = Mathf.Max(0.01f, Mathf.Abs(pScale.x));
        float absY = Mathf.Max(0.01f, Mathf.Abs(pScale.y));
        const float targetWorldWidth = 0.62f;
        superFeetVisualObj.transform.localScale = new Vector3(
            targetWorldWidth / absX,
            targetWorldWidth / absY,
            1f);

        superFeetVisualRenderer = superFeetVisualObj.AddComponent<SpriteRenderer>();
        superFeetVisualRenderer.sprite = PowerUpPickup.GetSuperFeetSprite();
        SpriteRenderer playerSr = player.GetComponent<SpriteRenderer>();
        if (playerSr != null)
        {
            superFeetVisualRenderer.sortingLayerID = playerSr.sortingLayerID;
            superFeetVisualRenderer.sortingOrder = playerSr.sortingOrder + 1;
        }
        else
        {
            superFeetVisualRenderer.sortingOrder = 11;
        }
    }

    private void UpdateSuperFeetVisuals()
    {
        if (!hasSuperFeet)
        {
            if (superFeetVisualObj != null && superFeetVisualObj.activeSelf)
            {
                superFeetVisualObj.SetActive(false);
            }
            return;
        }

        EnsureSuperFeetVisual();
        if (superFeetVisualObj != null)
        {
            if (!superFeetVisualObj.activeSelf) superFeetVisualObj.SetActive(true);

            // Hafif komik yürüyüş/zıplama paytaklığı
            float waddle = Mathf.Sin(Time.time * 12f) * 6f;
            superFeetVisualObj.transform.localRotation = Quaternion.Euler(0f, 0f, waddle);

            if (superFeetVisualRenderer != null)
            {
                float alpha = (superFeetTimer <= 3f)
                    ? (0.28f + 0.72f * Mathf.Abs(Mathf.Sin(Time.time * 16f)))
                    : 1f;
                superFeetVisualRenderer.color = new Color(1f, 1f, 1f, alpha);
            }
        }
    }

    public void ActivateRocketBoost()
    {
        isRocketBoosting = true;
        rocketTimer = Mathf.Max(rocketTimer, rocketBoostDuration);

        if (canKazanmaSesi != null)
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.pitch = 1.32f;
            audioSource.PlayOneShot(canKazanmaSesi, 0.8f);
            audioSource.pitch = 1f;
        }

        if (player != null)
        {
            player.transform.SetParent(null);
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }

        EnsureRocketVisual();
        if (rocketFlameObj != null) rocketFlameObj.SetActive(true);
    }

    private void EndRocketBoost(float baseSpeed)
    {
        isRocketBoosting = false;
        rocketTimer = 0f;
        postRocketGraceTimer = 0.85f;
        globalPlatformSpeed = baseSpeed;

        if (rocketFlameObj != null) rocketFlameObj.SetActive(false);

        // Roket bittiğinde karakterin tam altına güvenli bir iniş platformu oluştur
        if (player != null)
        {
            PlatformSpawner spawner = Object.FindAnyObjectByType<PlatformSpawner>();
            GameObject prefabToSpawn = spawner != null
                ? spawner.SelectPlatformPrefabForCurrentCycle()
                : null;
            if (prefabToSpawn == null) prefabToSpawn = guvenliPlatformPrefab;

            if (prefabToSpawn != null)
            {
                Vector2 landingPos = new Vector2(player.transform.position.x, player.transform.position.y - 0.58f);
                Instantiate(prefabToSpawn, landingPos, Quaternion.identity);
            }

            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, -baseSpeed);
            }
        }
    }

    private void EnsureShieldVisual()
    {
        if (shieldVisualObj != null || player == null) return;

        shieldVisualObj = new GameObject("ShieldBubbleAura");
        shieldVisualObj.transform.SetParent(player.transform, false);
        shieldVisualObj.transform.localPosition = Vector3.zero;
        shieldVisualObj.transform.localScale = new Vector3(0.30f, 0.38f, 1f);

        shieldVisualRenderer = shieldVisualObj.AddComponent<SpriteRenderer>();
        shieldVisualRenderer.sprite = PowerUpPickup.GetShieldAuraSprite();
        SpriteRenderer playerSr = player.GetComponent<SpriteRenderer>();
        shieldVisualRenderer.sortingOrder = playerSr != null ? playerSr.sortingOrder + 2 : 12;
    }

    private void UpdateShieldVisuals()
    {
        if (!hasShield)
        {
            if (shieldVisualObj != null && shieldVisualObj.activeSelf)
            {
                shieldVisualObj.SetActive(false);
            }
            return;
        }

        EnsureShieldVisual();
        if (shieldVisualObj != null)
        {
            if (!shieldVisualObj.activeSelf) shieldVisualObj.SetActive(true);
            float pulse = 1f + Mathf.Sin(Time.time * 4.5f) * 0.05f;
            shieldVisualObj.transform.localScale = new Vector3(0.30f * pulse, 0.38f * pulse, 1f);
            if (shieldVisualRenderer != null)
            {
                float alphaPulse;
                if (shieldTimer <= 3f)
                {
                    // Kalkanın bitmesine son 3 saniye kala hızlı yanıp sönme uyarısı
                    alphaPulse = 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(Time.time * 16f));
                }
                else
                {
                    alphaPulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
                }
                shieldVisualRenderer.color = new Color(1f, 1f, 1f, alphaPulse);
            }
        }
    }

    private void EnsureRocketVisual()
    {
        if (rocketFlameObj != null || player == null) return;

        rocketFlameObj = new GameObject("RocketExhaustFlame");
        rocketFlameObj.transform.SetParent(player.transform, false);
        rocketFlameObj.transform.localPosition = new Vector3(0f, -0.16f, 0f);
        rocketFlameObj.transform.localScale = new Vector3(0.55f, 0.75f, 1f);

        rocketFlameRenderer = rocketFlameObj.AddComponent<SpriteRenderer>();
        rocketFlameRenderer.sprite = PowerUpPickup.GetRocketFlameSprite();
        SpriteRenderer playerSr = player.GetComponent<SpriteRenderer>();
        rocketFlameRenderer.sortingOrder = playerSr != null ? playerSr.sortingOrder - 1 : 4;
    }

    private void UpdateRocketVisuals()
    {
        EnsureRocketVisual();
        if (rocketFlameObj != null)
        {
            if (!rocketFlameObj.activeSelf) rocketFlameObj.SetActive(true);
            float flickerY = 0.68f + Mathf.Abs(Mathf.Sin(Time.time * 28f)) * 0.34f;
            float flickerX = 0.50f + Mathf.Abs(Mathf.Cos(Time.time * 22f)) * 0.14f;
            rocketFlameObj.transform.localScale = new Vector3(flickerX, flickerY, 1f);
        }
    }

    /// <summary>
    /// Meteor gibi anlık bir tehlike çarptığında kalkan aktifse oyuncuyu ışınlamadan yerinde korur.
    /// </summary>
    public bool TryAbsorbHitWithShield(Vector3 impactPos)
    {
        if (!hasShield) return false;

        hasShield = false;
        shieldTimer = 0f;
        if (shieldVisualObj != null) shieldVisualObj.SetActive(false);
        JumpDustEffect.SpawnJumpDust(impactPos);
        return true;
    }

    public void TakeDamage()
    {
        if (!isGameActive) return;

        // Roket uçuşu sırasında veya roket iniş koruması aktifken hasar alınmaz
        if (isRocketBoosting || postRocketGraceTimer > 0f)
        {
            if (player != null && respawnPoint != null && !isRocketBoosting)
            {
                player.transform.position = respawnPoint.position;
                Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
                GuvenliPlatformOlustur();
            }
            return;
        }

        // Koruyucu Kalkan (Baloncuk) aktifse can azaltmadan kalkanı tüket ve karakteri kurtar!
        if (hasShield)
        {
            hasShield = false;
            shieldTimer = 0f;
            if (shieldVisualObj != null) shieldVisualObj.SetActive(false);

            if (player != null)
            {
                JumpDustEffect.SpawnJumpDust(player.transform.position);
                if (respawnPoint != null)
                {
                    player.transform.SetParent(null);
                    player.transform.position = respawnPoint.position;
                    Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                    if (rb != null) rb.linearVelocity = Vector2.zero;
                }
            }
            GuvenliPlatformOlustur();
            return;
        }

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

    /// <summary>
    /// Oyun ilk başladığında spawner'ın ürettiği ilk platformu garantiler ve karakteri doğrudan onun üstüne yerleştirir.
    /// </summary>
    void SpawnPlayerOnFirstPlatform()
    {
        PlatformSpawner spawner = Object.FindAnyObjectByType<PlatformSpawner>();
        GameObject firstPlatform = spawner != null ? spawner.EnsureFirstPlatformSpawned() : null;

        if (firstPlatform == null)
        {
            GuvenliPlatformOlustur();
            return;
        }

        if (player == null) return;

        Physics2D.SyncTransforms();

        Collider2D platformCol = firstPlatform.GetComponentInChildren<Collider2D>();
        float platformTopY = platformCol != null
            ? platformCol.bounds.max.y
            : firstPlatform.transform.position.y + 0.2f;

        Collider2D playerCol = player.GetComponent<Collider2D>();
        float playerBottomOffset = playerCol != null
            ? (player.transform.position.y - playerCol.bounds.min.y)
            : 0.25f;

        player.transform.SetParent(null);
        player.transform.position = new Vector3(
            firstPlatform.transform.position.x,
            platformTopY + playerBottomOffset + 0.04f,
            player.transform.position.z
        );

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(0f, -globalPlatformSpeed);
            rb.angularVelocity = 0f;
        }

        Physics2D.SyncTransforms();
    }

    void GuvenliPlatformOlustur()
    {
        if (respawnPoint == null) return;

        PlatformSpawner spawner = Object.FindAnyObjectByType<PlatformSpawner>();
        GameObject prefabToSpawn = spawner != null
            ? spawner.SelectPlatformPrefabForCurrentCycle()
            : null;
        if (prefabToSpawn == null) prefabToSpawn = guvenliPlatformPrefab;

        if (prefabToSpawn != null)
        {
            Vector2 platformKonumu = new Vector2(respawnPoint.position.x, respawnPoint.position.y - platformMesafeAyari);
            Instantiate(prefabToSpawn, platformKonumu, Quaternion.identity);
        }
    }

    void UpdateHealthUI()
    {
        if (healthText != null) healthText.text = "CAN: " + health;
    }

    public void UpdateCoinUI()
    {
        if (coinText != null)
        {
            coinText.text = "COIN: " + CharacterShopData.GetCoins();
        }
    }

    /// <summary>
    /// Coin toplandığında HUD sayacını günceller, kısa ölçek vurgusu ve pitched ses efekti çalar.
    /// </summary>
    public void NotifyCoinCollected(int amount, AudioClip clip, bool isRare)
    {
        UpdateCoinUI();

        if (clip != null)
        {
            if (coinAudioSource == null)
            {
                coinAudioSource = gameObject.AddComponent<AudioSource>();
                coinAudioSource.playOnAwake = false;
            }

            coinAudioSource.pitch = isRare ? Random.Range(1.42f, 1.52f) : Random.Range(1.24f, 1.34f);
            coinAudioSource.PlayOneShot(clip, isRare ? 0.62f : 0.45f);
        }

        if (coinText != null && gameObject.activeInHierarchy)
        {
            if (coinPunchCoroutine != null) StopCoroutine(coinPunchCoroutine);
            coinPunchCoroutine = StartCoroutine(PunchCoinHudRoutine());
        }
    }

    private System.Collections.IEnumerator PunchCoinHudRoutine()
    {
        Transform target = coinText.transform.parent != null && coinText.transform.parent.name == "CoinBadge"
            ? coinText.transform.parent
            : coinText.transform;

        target.localScale = Vector3.one * 1.18f;
        float elapsed = 0f;
        const float duration = 0.16f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            target.localScale = Vector3.Lerp(Vector3.one * 1.18f, Vector3.one, t);
            yield return null;
        }
        target.localScale = Vector3.one;
        coinPunchCoroutine = null;
    }

    void GameOver()
    {
        isGameActive = false;
        globalPlatformSpeed = 0;

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (pauseButton != null) pauseButton.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);

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

        int currentScoreInt = (int)score;
        int highScore = PlayerPrefs.GetInt("HighScore", 0);

        if (currentScoreInt > highScore)
        {
            highScore = currentScoreInt;
            PlayerPrefs.SetInt("HighScore", highScore);
            PlayerPrefs.Save(); // Mobilde skorun anında kalıcı belleğe yazılmasını garantiler
        }

        if (finalScoreText != null) finalScoreText.text = "SKORUN: " + currentScoreInt;
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

        GuncelleKontrolText(next);

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
