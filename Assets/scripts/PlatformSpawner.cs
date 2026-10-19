using UnityEngine;

public class PlatformSpawner : MonoBehaviour
{
    public GameObject[] platformPrefabs;

    // Platformlar arasý SABÝT mesafe (Örn: 4 metre)
    // Hýz ne olursa olsun bu mesafe korunacak.
    public float platformDistance = 4f;

    public float xLimit = 2.5f;

    // Son üretilen platformu hafýzada tutuyoruz
    private GameObject lastSpawnedPlatform;

    void Start()
    {
        // Oyun baþlar baþlamaz ilk platformu üret
        SpawnPlatform();
    }

    void Update()
    {
        // Eðer son üretilen platform yoksa (yok olduysa) veya
        // Son platform, Spawner'dan yeterince (platformDistance kadar) aþaðý indiyse:
        if (lastSpawnedPlatform == null ||
            transform.position.y - lastSpawnedPlatform.transform.position.y >= platformDistance)
        {
            SpawnPlatform();
        }
    }

    void SpawnPlatform()
    {
        float randomX = Random.Range(-xLimit, xLimit);
        // Spawner'ýn olduðu yükseklikte üret
        Vector2 spawnPos = new Vector2(randomX, transform.position.y);

        int randomIndex = Random.Range(0, platformPrefabs.Length);

        // Yeni üretilen platformu "lastSpawnedPlatform" deðiþkenine kaydet
        lastSpawnedPlatform = Instantiate(platformPrefabs[randomIndex], spawnPos, Quaternion.identity);
    }
}