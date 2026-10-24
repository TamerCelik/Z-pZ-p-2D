using UnityEngine;

public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    [Header("Mesafe Ayarları")]
    [Tooltip("Platformlar arası dikey mesafe (2.0f - 2.4f zıplamak için idealdir)")]
    public float platformDistance = 2.2f;

    [Tooltip("Ekranın sağ ve sol kenar sınırları (Ekran boyutuna göre otomatik ayarlanır)")]
    public float xLimit = 1.4f;

    [Header("Ulaşılabilirlik Kuralı")]
    [Tooltip("Bir sonraki platformun önceki platforma göre maksimum yatay sapma mesafesi")]
    public float maxHorizontalStep = 1.2f;

    private GameObject lastSpawnedPlatform;

    void Start()
    {
        CalculateLimits();
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
            transform.position.y - lastSpawnedPlatform.transform.position.y >= platformDistance)
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
            float step = Random.Range(-maxHorizontalStep, maxHorizontalStep);

            // Eğer platform ekranın sağına/soluna çok yakınsa, bir sonrakini merkeze doğru üret
            if (prevX > xLimit * 0.4f)
            {
                step = Random.Range(-maxHorizontalStep, 0.1f);
            }
            else if (prevX < -xLimit * 0.4f)
            {
                step = Random.Range(-0.1f, maxHorizontalStep);
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
    }
}
