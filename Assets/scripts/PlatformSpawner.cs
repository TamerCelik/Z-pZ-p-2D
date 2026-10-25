using UnityEngine;

public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    [Header("Mesafe Ayarları (Rastgele & Ulaşılabilir)")]
    [Tooltip("Platformlar arası minimum dikey mesafe")]
    public float minDistance = 1.3f;

    [Tooltip("Platformlar arası maksimum dikey mesafe (tek zıplayışta 2 platform geçmeyi önler)")]
    public float maxDistance = 1.8f;

    [Tooltip("Ekranın sağ ve sol kenar sınırları (Ekran boyutuna göre otomatik ayarlanır)")]
    public float xLimit = 2.0f;

    [Header("Geniş ve Rastgele Yatay Dağılım")]
    [Tooltip("Bir sonraki platformun önceki platforma göre maksimum yatay sapma mesafesi")]
    public float maxHorizontalStep = 2.4f;

    [Tooltip("Platformların tam üst üste binmesini önlemek için minimum yatay mesafe")]
    public float minHorizontalStep = 0.6f;

    [Header("Dinamik Zorluk (Hareketli Platformlar)")]
    [Tooltip("Oyun başında hareketli platform şansı (%10)")]
    public float baslangicHareketSans = 0.10f;
    [Tooltip("1000 puanda ulaşılan maksimum hareketli platform şansı (%60)")]
    public float maksimumHareketSans = 0.60f;

    private GameObject lastSpawnedPlatform;
    private float nextSpawnDistance = 1.5f;

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
            // Platform yarım genişliği payı düşülerek TAMAMI ekranda kalacak şekilde geniş sınır
            xLimit = Mathf.Clamp(camHalfWidth - 0.7f, 1.2f, 2.3f);
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

        float nextX;

        if (lastSpawnedPlatform != null)
        {
            float prevX = lastSpawnedPlatform.transform.position.x;

            // Rastgele yön seç (Sol veya Sağ), tekdüzelikten kurtar
            float direction = (Random.value > 0.5f) ? 1f : -1f;

            // Eğer platform ekranın çok kenarındaysa ters yöne gitmeye meyilli olsun
            if (prevX > xLimit * 0.75f)
            {
                direction = -1f;
            }
            else if (prevX < -xLimit * 0.75f)
            {
                direction = 1f;
            }

            // Hem minHorizontalStep hem maxHorizontalStep kullanarak ekran genelinde geniş dağılım sağla
            float offset = Random.Range(minHorizontalStep, maxHorizontalStep) * direction;
            nextX = Mathf.Clamp(prevX + offset, -xLimit, xLimit);
        }
        else
        {
            nextX = Random.Range(-xLimit * 0.5f, xLimit * 0.5f);
        }

        Vector2 spawnPos = new Vector2(nextX, transform.position.y);
        int randomIndex = Random.Range(0, platformPrefabs.Length);

        lastSpawnedPlatform = Instantiate(platformPrefabs[randomIndex], spawnPos, Quaternion.identity);

        // Skor arttıkça hareketli platform oranı %10'dan %60'a kadar dinamik artar
        float progress = Mathf.Clamp01(GameManager.currentScore / 1000f);
        float currentMovingChance = Mathf.Lerp(baslangicHareketSans, maksimumHareketSans, progress);

        PlatformMovement pm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
        if (pm != null && Random.value < currentMovingChance)
        {
            float randomSpeed = Random.Range(1.6f, 2.4f);
            float randomDist = Random.Range(0.7f, 1.2f);
            pm.SetupMovement(true, randomSpeed, randomDist);

            SpriteRenderer sr = lastSpawnedPlatform.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0.65f, 0.95f, 1.0f); // Hareketli olduğunu belli eden şık neon mavi tonu
            }
        }

        // Bir sonraki platform için rastgele yeni dikey mesafe belirle (1.3f - 1.8f)
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
    }
}
