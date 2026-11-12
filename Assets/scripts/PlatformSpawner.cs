using UnityEngine;

/// <summary>
/// Platformların dengeli, erişilebilir ve asla üst üste binmeyecek şekilde
/// dinamik olarak üretilmesini sağlayan spawner sınıfı.
/// </summary>
public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    [Header("Mesafe Ayarları (Çakışmayı Önleyen Güvenli Aralık)")]
    [Tooltip("Platformlar arası minimum dikey mesafe (üst üste binmeyi engeller)")]
    public float minDistance = 1.6f;

    [Tooltip("Platformlar arası maksimum dikey mesafe (ulaşılabilir zıplama sınırı)")]
    public float maxDistance = 2.1f;

    [Tooltip("Ekranın sağ ve sol kenar sınırları (Ekran boyutuna göre otomatik ayarlanır)")]
    public float xLimit = 2.0f;

    [Header("Yatay Dağılım ve Ayrım")]
    [Tooltip("Platformların yatayda birbirinden en az ne kadar uzakta olması gerektiği")]
    public float minHorizontalStep = 1.0f;

    [Tooltip("Platformların yatayda bir sonraki adımda maksimum sapma mesafesi")]
    public float maxHorizontalStep = 2.2f;

    [Header("Dinamik Zorluk (Hareketli Platformlar)")]
    [Tooltip("Oyun başında hareketli platform şansı (%10)")]
    public float baslangicHareketSans = 0.10f;
    [Tooltip("1000 puanda ulaşılan maksimum hareketli platform şansı (%60)")]
    public float maksimumHareketSans = 0.60f;

    [Header("Özel Platform Çeşitleri (Trambolin & Kırılan)")]
    [Tooltip("Platform üzerinde trambolin/yay belirme şansı (%15)")]
    [Range(0f, 1f)]
    public float springSpawnChance = 0.15f;

    [Tooltip("Kırılan/çatlak platform çıkma şansı (%20)")]
    [Range(0f, 1f)]
    public float breakableSpawnChance = 0.20f;

    [Tooltip("Trambolin boşta durma sprite'ı")]
    public Sprite trampolineIdleSprite;

    [Tooltip("Trambolin zıplama sprite'ı")]
    public Sprite trampolineJumpSprite;

    [Tooltip("Kırılan platform çatlak sprite'ı (opsiyonel)")]
    public Sprite breakableHitSprite;

    [Tooltip("Kırılan platform parçalanma sprite'ı (opsiyonel)")]
    public Sprite breakableBrokenSprite;

    private bool lastWasBreakable = false;
    private int spawnedPlatformCount = 0;

    [Header("Nadir Can İkonu (1000+ Skor Ödülü)")]
    [Tooltip("Can ikonu sprite'ı")]
    public Sprite heartSprite;
    [Range(0f, 1f)]
    [Tooltip("1000 skordan sonra platformda can çıkma olasılığı (%5)")]
    public float heartSpawnChance = 0.05f;
    [Tooltip("İki can arasında minimum platform sayısı")]
    public int minPlatformsBetweenHearts = 7;
    private int platformsSinceLastHeart = 10;

    private GameObject lastSpawnedPlatform;
    private float nextSpawnDistance = 1.7f;
    private int consecutiveSameSideCount = 0;
    private float lastDirection = 0f;

    void Start()
    {
        CalculateLimits();
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
        SpawnPlatform();
    }

    /// <summary>
    /// Ekranın en-boy oranına göre platformların geniş ve güvenli sınırlarını hesaplar.
    /// </summary>
    private void CalculateLimits()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            float camHalfWidth = cam.orthographicSize * cam.aspect;
            // Platform yarım genişliği payı düşülerek TAMAMI ekranda kalacak şekilde sınır
            xLimit = Mathf.Clamp(camHalfWidth - 0.75f, 1.2f, 2.3f);
        }
    }

    void Update()
    {
        if (lastSpawnedPlatform == null ||
            transform.position.y - lastSpawnedPlatform.transform.position.y >= nextSpawnDistance)
        {
            SpawnPlatform();
        }
    }

    void SpawnPlatform()
    {
        CalculateLimits();

        float chosenX = 0f;
        bool foundSafePosition = false;

        // Doğal ve Çeşitli Konumlandırma (Monoton Çaprazlığı Önler):
        // Bazen düz tırmanış, bazen orta şerit, bazen hafif adımlar, bazen geniş geçişler
        if (lastSpawnedPlatform != null)
        {
            float prevX = lastSpawnedPlatform.transform.position.x;
            
            PlatformMovement prevPm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
            float movingMargin = (prevPm != null && prevPm.isMovingHorizontal) ? prevPm.horizontalDistance : 0f;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float candidateX = 0f;
                // Formasyon Tipleri:
                // 0: Düz / Hafif kayık tırmanış (çaprazlığı kesin olarak kırar!)
                // 1: Orta hat (ekranın merkezine dönüş)
                // 2: Dengeli yatay adım (rastgele sol ya da sağ)
                // 3: Çapraz atlayış (ters tarafa geçiş)
                int patternType = Random.Range(0, 4);

                // Eğer son 2 platform üst üste aynı kenardaysa 3. platformu ortaya veya ters tarafa yönlendir
                if (consecutiveSameSideCount >= 2)
                {
                    patternType = (Random.value > 0.4f) ? 1 : 3;
                }

                switch (patternType)
                {
                    case 0: // Düz tırmanış / minik sapma (-0.55f ile +0.55f arası)
                        candidateX = prevX + Random.Range(-0.55f, 0.55f);
                        break;
                    case 1: // Ekran ortası (-0.45f ile +0.45f)
                        candidateX = Random.Range(-0.45f, 0.45f);
                        break;
                    case 2: // Orta boy adım (rastgele sağ veya sol)
                        float stepDir = (Random.value > 0.5f) ? 1f : -1f;
                        candidateX = prevX + (Random.Range(0.85f + movingMargin, 1.55f) * stepDir);
                        break;
                    case 3: // Geniş geçiş (karşı tarafa)
                    default:
                        float crossDir = (prevX >= 0) ? -1f : 1f;
                        candidateX = prevX + (Random.Range(1.35f + movingMargin, maxHorizontalStep) * crossDir);
                        break;
                }

                // Sınır dışına taşarsa duvardan içeriye doğru sektir
                if (candidateX > xLimit)
                {
                    candidateX = xLimit - (candidateX - xLimit);
                }
                else if (candidateX < -xLimit)
                {
                    candidateX = -xLimit + (-xLimit - candidateX);
                }

                candidateX = Mathf.Clamp(candidateX, -xLimit, xLimit);

                // Fiziksel çakışma kontrolü
                Collider2D hit = Physics2D.OverlapBox(new Vector2(candidateX, transform.position.y), new Vector2(2.1f, 1.2f), 0f);
                if (hit == null)
                {
                    chosenX = candidateX;
                    foundSafePosition = true;
                    break;
                }
            }
        }

        // Eğer hiçbir güvenli konum bulunamazsa dengeli rastgele dağıt
        if (!foundSafePosition)
        {
            float[] possibleSlots = new float[] { -xLimit * 0.7f, 0f, xLimit * 0.7f, Random.Range(-xLimit, xLimit) };
            chosenX = possibleSlots[Random.Range(0, possibleSlots.Length)];
        }

        // Aynı kenarda kalma takibi (duvara sıkışmayı engeller)
        if (Mathf.Abs(chosenX) > 0.6f)
        {
            float side = Mathf.Sign(chosenX);
            if (side == lastDirection) consecutiveSameSideCount++;
            else
            {
                lastDirection = side;
                consecutiveSameSideCount = 1;
            }
        }
        else
        {
            consecutiveSameSideCount = 0;
            lastDirection = 0f;
        }

        Vector2 spawnPos = new Vector2(chosenX, transform.position.y);
        int randomIndex = Random.Range(0, platformPrefabs.Length);

        lastSpawnedPlatform = Instantiate(platformPrefabs[randomIndex], spawnPos, Quaternion.identity);
        spawnedPlatformCount++;

        // Skor arttıkça hareketli platform oranı %10'dan %60'a kadar kademeli artar
        float progress = Mathf.Clamp01(GameManager.currentScore / 1000f);
        float currentMovingChance = Mathf.Lerp(baslangicHareketSans, maksimumHareketSans, progress);

        PlatformMovement pm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
        bool isMoving = false;

        if (pm != null && Random.value < currentMovingChance)
        {
            isMoving = true;
            float randomSpeed = Random.Range(1.6f, 2.3f);
            float randomDist = Random.Range(0.6f, 1.0f);
            pm.SetupMovement(true, randomSpeed, randomDist);

            SpriteRenderer sr = lastSpawnedPlatform.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0.65f, 0.95f, 1.0f); // Hareketli olduğunu belli eden şık neon mavi tonu
            }
        }

        // Özel Platform Türleri Belirleme (İlk 3 platform her zaman sabit ve güvenlidir)
        if (spawnedPlatformCount > 3)
        {
            // 1. Kırılan Platform: İki kırılan platform üst üste gelemez, hareketli olanlar kırılan yapılmaz
            if (!isMoving && !lastWasBreakable && Random.value < breakableSpawnChance)
            {
                MakePlatformBreakable(lastSpawnedPlatform);
                lastWasBreakable = true;
            }
            else
            {
                lastWasBreakable = false;

                // 2. Trambolin / Yay Platformu: Kırılan platforma konmaz, normal veya hareketli platform üzerine yerleşir
                if (Random.value < springSpawnChance)
                {
                    SpawnTrampoline(lastSpawnedPlatform);
                }
            }
        }
        else
        {
            lastWasBreakable = false;
        }

        // 1000 skordan sonra nadir olarak platform üzerinde can ikonu üret
        if (GameManager.currentScore >= 1000f && platformsSinceLastHeart >= minPlatformsBetweenHearts)
        {
            if (Random.value < heartSpawnChance)
            {
                SpawnHeartPickup(lastSpawnedPlatform);
                platformsSinceLastHeart = 0;
            }
            else
            {
                platformsSinceLastHeart++;
            }
        }
        else
        {
            platformsSinceLastHeart++;
        }

        // Bir sonraki platform için güvenli ve rahat dikey mesafe belirle (1.6f - 2.1f)
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
    }

    /// <summary>
    /// Platformun üzerine toplanabilir bir can ikonu yerleştirir.
    /// </summary>
    void SpawnHeartPickup(GameObject platform)
    {
        GameObject heartObj = new GameObject("HealthPickup");
        heartObj.transform.position = new Vector3(platform.transform.position.x, platform.transform.position.y + 0.65f, 0f);

        SpriteRenderer sr = heartObj.AddComponent<SpriteRenderer>();
        if (heartSprite != null)
        {
            sr.sprite = heartSprite;
        }
        sr.sortingOrder = 5;
        heartObj.transform.localScale = new Vector3(0.55f, 0.55f, 1f);

        CircleCollider2D col = heartObj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        heartObj.AddComponent<HealthPickup>();

        heartObj.transform.SetParent(platform.transform);
    }

    /// <summary>
    /// Platformu kırılan / çatlak platform haline getirir.
    /// Karakter bastığında hafif titrer ve kısa süre sonra kırılarak düşer.
    /// </summary>
    void MakePlatformBreakable(GameObject platform)
    {
        if (platform == null) return;

        BreakablePlatform bp = platform.AddComponent<BreakablePlatform>();
        bp.brokenSprite = breakableBrokenSprite;

        SpriteRenderer sr = platform.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (breakableHitSprite != null)
            {
                sr.sprite = breakableHitSprite;
            }
            else
            {
                // Görsel olarak oyuncunun kırılabilir olduğunu anlayabilmesi için sıcak çatlak/ahşap tonu
                sr.color = new Color(1f, 0.78f, 0.65f, 1f);
            }
        }
    }

    /// <summary>
    /// Platformun üzerine trambolin / yay yerleştirir.
    /// Karakter üzerine bastığında süper zıplama ile fırlar.
    /// </summary>
    void SpawnTrampoline(GameObject platform)
    {
        if (platform == null) return;

        GameObject trampObj = new GameObject("Trampoline");

        // Platformun üst yüzeyini hesapla
        Collider2D col = platform.GetComponent<Collider2D>();
        float surfaceY = (col != null) ? col.bounds.max.y : (platform.transform.position.y + 0.35f);

        // Platformun tam ortasında ve üst yüzeyinde dursun
        trampObj.transform.position = new Vector3(platform.transform.position.x, surfaceY + 0.15f, 0f);

        SpriteRenderer sr = trampObj.AddComponent<SpriteRenderer>();
        if (trampolineIdleSprite != null)
        {
            sr.sprite = trampolineIdleSprite;
        }
        sr.sortingOrder = 4; // Platformun üstünde net görünsün

        // Trambolin bileşeni
        SpringTrampoline spring = trampObj.AddComponent<SpringTrampoline>();
        spring.idleSprite = trampolineIdleSprite;
        spring.jumpSprite = trampolineJumpSprite;
        spring.bounceMultiplier = 1.95f;

        // Tetikleyici (Trigger) Collider
        BoxCollider2D boxCol = trampObj.AddComponent<BoxCollider2D>();
        boxCol.isTrigger = true;
        boxCol.size = new Vector2(0.28f, 0.22f);
        boxCol.offset = new Vector2(0f, 0.05f);

        // Trambolini platforma bağla (Platform hareket ederse onunla gitsin)
        trampObj.transform.SetParent(platform.transform, true);

        // Dünyada uygun boyutta görünmesi için (~0.65 birim genişlik)
        Vector3 parentScale = platform.transform.lossyScale;
        float targetWidth = 0.65f;
        float targetHeight = 0.65f;
        trampObj.transform.localScale = new Vector3(
            targetWidth / Mathf.Max(0.01f, Mathf.Abs(parentScale.x)),
            targetHeight / Mathf.Max(0.01f, Mathf.Abs(parentScale.y)),
            1f
        );
    }
}
