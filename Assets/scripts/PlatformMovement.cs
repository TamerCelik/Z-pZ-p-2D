using UnityEngine;

public class PlatformMovement : MonoBehaviour
{
    [Header("Yatay Hareket (Sağa - Sola Salınım)")]
    [Tooltip("Platform yatayda sağa sola hareket etsin mi?")]
    public bool isMovingHorizontal = false;

    [Tooltip("Yatay salınım hızı")]
    public float horizontalSpeed = 2f;

    [Tooltip("Yatay salınım mesafesi (merkezden sağa ve sola)")]
    public float horizontalDistance = 1.0f;

    [Header("Yok Olma Sınırı (Ekranın En Altı)")]
    [Tooltip("Platformun barın arkasına tamamen gömüldükten sonra yok olacağı Y yüksekliği")]
    public float destroyY = -5.5f;

    private float startX;
    private float screenLimit = 2.0f;
    private float randomOffset;

    void Start()
    {
        startX = transform.position.x;
        // Farklı platformlar aynı anda aynı yöne gitmesin diye rastgele faz farkı
        randomOffset = Random.Range(0f, 6.28f);

        Camera cam = Camera.main;
        if (cam != null)
        {
            float camHalfWidth = cam.orthographicSize * cam.aspect;
            screenLimit = Mathf.Clamp(camHalfWidth - 0.7f, 1.0f, 2.3f);

            // Platform barın arkasında tamamen kaybolup ekranın en altına inince silinir
            destroyY = -cam.orthographicSize - 0.5f;
        }
    }

    void Update()
    {
        // 1. Dikey Hareket (GameManager'ın oyun ilerledikçe artan platform hızı)
        transform.Translate(Vector3.down * GameManager.globalPlatformSpeed * Time.deltaTime, Space.World);

        // 2. Yatay Hareket (Sağa - Sola yumuşak salınım)
        if (isMovingHorizontal)
        {
            float newX = startX + Mathf.Sin((Time.time * horizontalSpeed) + randomOffset) * horizontalDistance;
            newX = Mathf.Clamp(newX, -screenLimit, screenLimit);
            transform.position = new Vector3(newX, transform.position.y, transform.position.z);
        }

        // 3. Ekranın En Altına Gelince Yok Etme (Barın içine tamamen girdikten sonra oyuncu görmeden silinir)
        if (transform.position.y < destroyY)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Spawner tarafından üretildiğinde hareket özelliklerini ayarlar.
    /// </summary>
    public void SetupMovement(bool moveHorizontal, float speed = 2f, float distance = 1.0f)
    {
        isMovingHorizontal = moveHorizontal;
        horizontalSpeed = speed;
        horizontalDistance = distance;
        startX = transform.position.x;
    }
}
