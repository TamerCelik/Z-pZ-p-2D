using UnityEngine;

public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    [Header("Mesafe Ayarları (Rastgele & Ulaşılabilir)")]
    [Tooltip("Platformlar arası minimum dikey mesafe")]
    public float minDistance = 1.4f;

    [Tooltip("Platformlar arası maksimum dikey mesafe (tek zıplayışta 2 platform geçmeyi önler)")]
    public float maxDistance = 1.9f;

    [Tooltip("Ekranın sağ ve sol kenar sınırları (Ekran boyutuna göre otomatik ayarlanır)")]
    public float xLimit = 2.0f;

    [Header("Geniş ve Rastgele Yatay Dağılım")]
    [Tooltip("Bir sonraki platformun önceki platforma göre maksimum yatay sapma mesafesi")]
    public float maxHorizontalStep = 2.4f;

    [Tooltip("Platformların tam üst üste binmesini önlemek için minimum yatay mesafe")]
    public float minHorizontalStep = 0.6f;

    [Header("Hareketli Platformlar (Sağa - Sola Salınım)")]
    [Range(0f, 1f)]
    [Tooltip("Platformun sağa sola salınarak hareket etme olasılığı")]
    public float movingPlatformChance = 0.35f;

    private GameObject lastSpawnedPlatform;
    private float nextSpawnDistance = 1.6f;

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

        // Bazı platformları sağa-sola hareketli yap
        PlatformMovement pm = lastSpawnedPlatform.GetComponent<PlatformMovement>();
        if (pm != null && Random.value < movingPlatformChance)
        {
            float randomSpeed = Random.Range(1.8f, 2.6f);
            float randomDist = Random.Range(0.8f, 1.3f);
            pm.SetupMovement(true, randomSpeed, randomDist);

            SpriteRenderer sr = lastSpawnedPlatform.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = new Color(0.65f, 0.95f, 1.0f); // Hareketli olduğunu belli eden şık mavi tonu
            }
        }

        // Bir sonraki platform için rastgele yeni dikey mesafe belirle (1.4f - 1.9f)
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
    }
}
