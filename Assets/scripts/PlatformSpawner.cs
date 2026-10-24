using UnityEngine;

public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    [Header("Mesafe Ayarları")]
    [Tooltip("Platformlar arası dikey mesafe (Akıcı zıplama için 3f idealdir)")]
    public float platformDistance = 3f;

    [Tooltip("Ekranın sağ ve sol kenar sınırları")]
    public float xLimit = 2.4f;

    [Header("Ulaşılabilirlik Kuralı")]
    [Tooltip("Bir sonraki platformun önceki platforma göre maksimum yatay sapma mesafesi")]
    public float maxHorizontalStep = 1.8f;

    // Son üretilen platformu hafızada tutuyoruz
    private GameObject lastSpawnedPlatform;

    void Start()
    {
        // Oyun başlar başlamaz ilk platformu üret
        SpawnPlatform();
    }

    void Update()
    {
        // Eğer son platform yoksa veya belirlenen mesafe kadar aşağı indiyse yenisini üret
        if (lastSpawnedPlatform == null ||
            transform.position.y - lastSpawnedPlatform.transform.position.y >= platformDistance)
        {
            SpawnPlatform();
        }
    }

    void SpawnPlatform()
    {
        float nextX;

        if (lastSpawnedPlatform != null)
        {
            float prevX = lastSpawnedPlatform.transform.position.x;
            float step = Random.Range(-maxHorizontalStep, maxHorizontalStep);

            // Kenarlara sıkışmayı önleme: Platform kenardaysa merkeze yönlendir
            if (prevX > xLimit * 0.65f)
            {
                step = Random.Range(-maxHorizontalStep, 0.2f);
            }
            else if (prevX < -xLimit * 0.65f)
            {
                step = Random.Range(-0.2f, maxHorizontalStep);
            }

            nextX = Mathf.Clamp(prevX + step, -xLimit, xLimit);
        }
        else
        {
            nextX = Random.Range(-1f, 1f);
        }

        Vector2 spawnPos = new Vector2(nextX, transform.position.y);
        int randomIndex = Random.Range(0, platformPrefabs.Length);

        lastSpawnedPlatform = Instantiate(platformPrefabs[randomIndex], spawnPos, Quaternion.identity);
    }
}
