using UnityEngine;

public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    [Header("Mesafe Ayarları (Rastgele & Ulaşılabilir)")]
    [Tooltip("Platformlar arası minimum dikey mesafe")]
    public float minDistance = 1.4f;

    [Tooltip("Platformlar arası maksimum dikey mesafe (karakterin zıplama yüksekliğini aşmayacak şekilde güvenli tutulur)")]
    public float maxDistance = 2.0f;

    [Tooltip("Ekranın sağ ve sol kenar sınırları (Ekran boyutuna göre otomatik ayarlanır)")]
    public float xLimit = 1.4f;

    [Header("Ulaşılabilirlik Kuralı")]
    [Tooltip("Bir sonraki platformun önceki platforma göre maksimum yatay sapma mesafesi")]
    public float maxHorizontalStep = 1.2f;

    private GameObject lastSpawnedPlatform;
    private float nextSpawnDistance = 1.8f;

    void Start()
    {
        CalculateLimits();
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
        SpawnPlatform();
    }

    /// <summary>
    /// Ekranın en-boy oranına göre platformların ASLA ekrandan taşmayacağı güvenli sınırları hesaplar.
    /// </summary>
    private void CalculateLimits()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            float camHalfWidth = cam.orthographicSize * cam.aspect;
            // Platform yarım genişliği payı (0.8f) düşülerek platformun TAMAMI ekranın içinde tutulur
            xLimit = Mathf.Clamp(camHalfWidth - 0.8f, 0.8f, 2.2f);
            maxHorizontalStep = Mathf.Min(maxHorizontalStep, xLimit * 1.1f);
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

            // Dikey mesafe ne kadar yüksekse, oyuncunun yetişebilmesi için yatay sapmayı o kadar dengeli tut
            float heightRatio = Mathf.InverseLerp(minDistance, maxDistance, nextSpawnDistance);
            float currentMaxStep = Mathf.Lerp(maxHorizontalStep, maxHorizontalStep * 0.75f, heightRatio);

            float step = Random.Range(-currentMaxStep, currentMaxStep);

            // Eğer platform ekranın sağına/soluna çok yakınsa, bir sonrakini merkeze doğru üret
            if (prevX > xLimit * 0.4f)
            {
                step = Random.Range(-currentMaxStep, 0.1f);
            }
            else if (prevX < -xLimit * 0.4f)
            {
                step = Random.Range(-0.1f, currentMaxStep);
            }

            nextX = Mathf.Clamp(prevX + step, -xLimit, xLimit);
        }
        else
        {
            nextX = Random.Range(-0.5f, 0.5f);
        }

        Vector2 spawnPos = new Vector2(nextX, transform.position.y);
        int randomIndex = Random.Range(0, platformPrefabs.Length);

        lastSpawnedPlatform = Instantiate(platformPrefabs[randomIndex], spawnPos, Quaternion.identity);

        // Bir sonraki platform için rastgele ve güvenli yeni bir mesafe belirle
        nextSpawnDistance = Random.Range(minDistance, maxDistance);
    }
}
