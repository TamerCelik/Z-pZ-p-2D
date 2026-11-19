using UnityEngine;

/// <summary>
/// Platformların dengeli, erişilebilir ve asla üst üste binmeyecek şekilde
/// dinamik olarak üretilmesini sağlayan spawner sınıfı.
/// </summary>
public class PlatformSpawner : MonoBehaviour
{
    [Header("Genel / Yedek Platform Prefabları")]
    public GameObject[] platformPrefabs;

    [Header("Döngüye Göre Platform Prefabları (Sabah / Öğle / Akşam / Gece / Uzay)")]
    [Tooltip("0 - 280 skor arası Sabah (Çimenli & Papatyalı) platformları")]
    public GameObject[] sabahPrefabs;
    [Tooltip("280 - 520 skor arası Öğle (Altın Çayır & Bal Köprü) platformları")]
    public GameObject[] oglePrefabs;
    [Tooltip("520 - 760 skor arası Akşam / Gün Batımı (Pembe Bulut & Kehribar) platformları")]
    public GameObject[] aksamPrefabs;
    [Tooltip("760 - 1200 skor arası Gece (Parlayan Mantarlı Ay Taşı & Gece Bulutu) platformları")]
    public GameObject[] gecePrefabs;
    [Tooltip("1200+ skor Uzay & Galaksi (Nebula Kristal & Yıldız Geçidi) platformları")]
    public GameObject[] uzayPrefabs;

    private SkyCycleManager skyCycleManager;

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

    [Header("Nadir Can İkonu (1000+ Skor Ödülü)")]
    [Tooltip("Can ikonu sprite'ı")]
    public Sprite heartSprite;
    [Range(0f, 1f)]
    [Tooltip("1000 skordan sonra platformda can çıkma olasılığı (%1.5)")]
    public float heartSpawnChance = 0.015f;
    [Tooltip("İki can arasında minimum platform sayısı")]
    public int minPlatformsBetweenHearts = 20;
    private int platformsSinceLastHeart = 20;

    private GameObject lastSpawnedPlatform;
    private float nextSpawnDistance = 1.7f;
    private int consecutiveSameSideCount = 0;
    private float lastDirection = 0f;

    void Start()
    {
        skyCycleManager = Object.FindAnyObjectByType<SkyCycleManager>();
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
        GameObject selectedPrefab = SelectPlatformPrefabForCurrentCycle();
        if (selectedPrefab == null) return;

        lastSpawnedPlatform = Instantiate(selectedPrefab, spawnPos, Quaternion.identity);

        // Skor arttıkça hareketli platform oranı %10'dan %60'a kadar kademeli artar
        float progress = Mathf.Clamp01(GameManager.currentScore / 1000f);
        float currentMovingChance = Mathf.Lerp(baslangicHareketSans, maksimumHareketSans, progress);

        PlatformMovement pm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
        if (pm != null && Random.value < currentMovingChance)
        {
            float randomSpeed = Random.Range(1.6f, 2.3f);
            float randomDist = Random.Range(0.6f, 1.0f);
            pm.SetupMovement(true, randomSpeed, randomDist);
        }

        // 1000 skordan sonra nadir olarak platform üzerinde can ikonu üret
        bool spawnedHeart = false;
        if (GameManager.currentScore >= 1000f && platformsSinceLastHeart >= minPlatformsBetweenHearts)
        {
            if (Random.value < heartSpawnChance)
            {
                SpawnHeartPickup(lastSpawnedPlatform);
                platformsSinceLastHeart = 0;
                spawnedHeart = true;
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

        // Aynı platformda can yoksa belirli aralıklarla coin meyvesi üret
        TrySpawnPlatformCoin(lastSpawnedPlatform, spawnedHeart);

        // Bir sonraki platform için güvenli ve rahat dikey mesafe belirle (1.6f - 2.1f)
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
    }

    /// <summary>
    /// Mevcut gökyüzü döngüsü skoruna göre Sabah / Öğle / Akşam / Gece / Uzay prefab havuzundan
    /// uygun platformu seçer (geçiş eşiklerinde yumuşak olasılık karışımı uygular).
    /// </summary>
    public GameObject SelectPlatformPrefabForCurrentCycle()
    {
        if (skyCycleManager == null)
        {
            skyCycleManager = Object.FindAnyObjectByType<SkyCycleManager>();
        }

        float cycleScore = skyCycleManager != null
            ? skyCycleManager.GetCurrentMappedScore()
            : GameManager.currentScore;

        GameObject[] primaryPool = GetPhasePool(cycleScore);
        GameObject chosen = PickRandomFromPool(primaryPool);
        if (chosen != null) return chosen;

        return PickRandomFromPool(platformPrefabs);
    }

    private GameObject[] GetPhasePool(float score)
    {
        // Geçiş bölgelerinde (±25 skor) iki evre arasında yumuşak geçiş
        if (score < 280f)
        {
            if (score > 255f && Random.value < Mathf.InverseLerp(255f, 280f, score) && HasValidItem(oglePrefabs))
                return oglePrefabs;
            return HasValidItem(sabahPrefabs) ? sabahPrefabs : platformPrefabs;
        }
        if (score < 520f)
        {
            if (score > 495f && Random.value < Mathf.InverseLerp(495f, 520f, score) && HasValidItem(aksamPrefabs))
                return aksamPrefabs;
            return HasValidItem(oglePrefabs) ? oglePrefabs : platformPrefabs;
        }
        if (score < 760f)
        {
            if (score > 735f && Random.value < Mathf.InverseLerp(735f, 760f, score) && HasValidItem(gecePrefabs))
                return gecePrefabs;
            return HasValidItem(aksamPrefabs) ? aksamPrefabs : platformPrefabs;
        }
        if (score < 1200f)
        {
            if (score > 1170f && Random.value < Mathf.InverseLerp(1170f, 1200f, score) && HasValidItem(uzayPrefabs))
                return uzayPrefabs;
            return HasValidItem(gecePrefabs) ? gecePrefabs : platformPrefabs;
        }

        // Sürekli döngü aktifse 1760-2000 şafak evresinde tekrar sabaha karışır
        if (skyCycleManager != null && skyCycleManager.useContinuousLoop && score >= 1760f)
        {
            if (Random.value < Mathf.InverseLerp(1760f, 1980f, score) && HasValidItem(sabahPrefabs))
                return sabahPrefabs;
        }

        if (HasValidItem(uzayPrefabs)) return uzayPrefabs;
        if (HasValidItem(gecePrefabs)) return gecePrefabs;
        return platformPrefabs;
    }

    private static bool HasValidItem(GameObject[] pool)
    {
        if (pool == null || pool.Length == 0) return false;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null) return true;
        }
        return false;
    }

    private static GameObject PickRandomFromPool(GameObject[] pool)
    {
        if (!HasValidItem(pool)) return null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            GameObject candidate = pool[Random.Range(0, pool.Length)];
            if (candidate != null) return candidate;
        }
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null) return pool[i];
        }
        return null;
    }

    [Header("Coin Meyve Ayarları")]
    [SerializeField] private float coinSpawnChance = 0.25f;
    [SerializeField] private int minPlatformsBetweenCoins = 6;
    [SerializeField] private float rareFruitScoreThreshold = 1000f;
    [SerializeField] private float rareFruitChance = 0.14f;
    private int platformsSinceLastCoin = 3;

    private void TrySpawnPlatformCoin(GameObject platform, bool platformHasHeart)
    {
        if (platform == null || platformHasHeart)
        {
            platformsSinceLastCoin++;
            return;
        }

        if (platformsSinceLastCoin < minPlatformsBetweenCoins)
        {
            platformsSinceLastCoin++;
            return;
        }

        if (Random.value >= coinSpawnChance)
        {
            platformsSinceLastCoin++;
            return;
        }

        platformsSinceLastCoin = 0;

        bool isRare = GameManager.currentScore >= rareFruitScoreThreshold && Random.value < rareFruitChance;
        int value = isRare ? 3 : 1;

        // %40 olasılıkla risk-ödül konumu: kenara kaydırılmış ve biraz daha yüksekte
        bool riskReward = Random.value < 0.40f;
        float worldX = riskReward
            ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.28f, 0.46f)
            : Random.Range(-0.32f, 0.32f);
        float worldY = riskReward
            ? Random.Range(0.88f, 1.12f)
            : Random.Range(0.62f, 0.78f);

        Vector3 pScale = platform.transform.lossyScale;
        Vector3 localOffset = new Vector3(
            worldX / Mathf.Max(0.01f, Mathf.Abs(pScale.x)),
            worldY / Mathf.Max(0.01f, Mathf.Abs(pScale.y)),
            0f);

        GameObject coinGo = new GameObject(isRare ? "Coin_Pineapple_3" : "Coin_Cherries_1");
        CoinPickup pickup = coinGo.AddComponent<CoinPickup>();
        pickup.Configure(value, isRare, platform.transform, localOffset);
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
}
