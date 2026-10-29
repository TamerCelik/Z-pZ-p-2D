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

        // 1. Akıllı konum seçimi: Önceki platformun konumuna göre ters veya uzak yönü hedefle
        if (lastSpawnedPlatform != null)
        {
            float prevX = lastSpawnedPlatform.transform.position.x;
            
            // Eğer önceki platform hareketliyse, hareket menzilini de hesaba kat
            PlatformMovement prevPm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
            float movingMargin = (prevPm != null && prevPm.isMovingHorizontal) ? prevPm.horizontalDistance : 0f;

            // Güvenli konum bulmak için 8 adede kadar akıllı deneme yap
            for (int attempt = 0; attempt < 8; attempt++)
            {
                // Tercihen önceki platformun zıt tarafına yönel
                float direction = (prevX > 0) ? -1f : 1f;
                if (attempt > 2)
                {
                    // Diğer yönleri de dene
                    direction = (Random.value > 0.5f) ? 1f : -1f;
                }

                float offset = Random.Range(minHorizontalStep + movingMargin, maxHorizontalStep) * direction;
                float candidateX = prevX + offset;

                // Sınır dışına taşarsa duvardan içeriye doğru sektir (kenarda yığılmayı önler)
                if (candidateX > xLimit)
                {
                    candidateX = xLimit - (candidateX - xLimit);
                }
                else if (candidateX < -xLimit)
                {
                    candidateX = -xLimit + (-xLimit - candidateX);
                }

                candidateX = Mathf.Clamp(candidateX, -xLimit, xLimit);

                // Yatayda önceki platformla doğrudan üst üste gelip gelmediğini kontrol et
                if (Mathf.Abs(candidateX - prevX) >= (minHorizontalStep * 0.85f))
                {
                    // Fiziksel çakışma kontrolü: Doğrudan o alanda başka bir collider var mı?
                    Collider2D hit = Physics2D.OverlapBox(new Vector2(candidateX, transform.position.y), new Vector2(2.4f, 1.3f), 0f);
                    if (hit == null)
                    {
                        chosenX = candidateX;
                        foundSafePosition = true;
                        break;
                    }
                }
            }
        }

        // Eğer hiçbir güvenli konum bulunamazsa ekranın ortasından veya rastgele bir aralıktan güvenli seç
        if (!foundSafePosition)
        {
            chosenX = (lastSpawnedPlatform != null && lastSpawnedPlatform.transform.position.x > 0)
                ? Random.Range(-xLimit, -0.3f)
                : Random.Range(0.3f, xLimit);
        }

        Vector2 spawnPos = new Vector2(chosenX, transform.position.y);
        int randomIndex = Random.Range(0, platformPrefabs.Length);

        lastSpawnedPlatform = Instantiate(platformPrefabs[randomIndex], spawnPos, Quaternion.identity);

        // Skor arttıkça hareketli platform oranı %10'dan %60'a kadar kademeli artar
        float progress = Mathf.Clamp01(GameManager.currentScore / 1000f);
        float currentMovingChance = Mathf.Lerp(baslangicHareketSans, maksimumHareketSans, progress);

        PlatformMovement pm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
        if (pm != null && Random.value < currentMovingChance)
        {
            float randomSpeed = Random.Range(1.6f, 2.3f);
            float randomDist = Random.Range(0.6f, 1.0f);
            pm.SetupMovement(true, randomSpeed, randomDist);

            SpriteRenderer sr = lastSpawnedPlatform.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0.65f, 0.95f, 1.0f); // Hareketli olduğunu belli eden şık neon mavi tonu
            }
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
}
