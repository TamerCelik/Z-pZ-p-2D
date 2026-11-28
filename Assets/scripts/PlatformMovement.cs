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
    private Collider2D platformCollider;
    private PlatformEffector2D oneWayEffector;

    /// <summary>
    /// Platformun anlık yatay kayma hızı (karakterin platformla birlikte kayabilmesi için).
    /// </summary>
    public float CurrentHorizontalVelocity { get; private set; }

    /// <summary>
    /// Düello modunda her iki cihazda birebir aynı olan deterministik platform sıra numarası (-1: spawner dışı).
    /// </summary>
    public int PlatformIndex { get; private set; } = -1;

    private bool useDeterministicDuelClock = false;

    public void SetPlatformIndex(int index)
    {
        PlatformIndex = index;
    }

    public void AssignDuelDeterministicData(int index, float phaseOffset)
    {
        PlatformIndex = index;
        randomOffset = phaseOffset;
        useDeterministicDuelClock = true;
        screenLimit = 1.55f;
    }

    void Awake()
    {
        platformCollider = GetComponent<Collider2D>();
    }

    void Start()
    {
        startX = transform.position.x;
        if (!useDeterministicDuelClock)
        {
            // Farklı platformlar aynı anda aynı yöne gitmesin diye rastgele faz farkı
            randomOffset = Random.Range(0f, 6.28f);
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            if (!useDeterministicDuelClock)
            {
                float camHalfWidth = cam.orthographicSize * cam.aspect;
                screenLimit = Mathf.Clamp(camHalfWidth - 0.7f, 1.0f, 2.3f);
            }

            // Platform ekranın en alt sınırını tamamen geçtikten sonra silinir
            destroyY = cam.transform.position.y - cam.orthographicSize - 0.95f;
        }

        SyncOneWayEffector();
    }

    void FixedUpdate()
    {
        SyncOneWayEffector();
    }

    /// <summary>
    /// Kuş Ayağı güçlendirmesi aktifken platformu Unity'nin yerel PlatformEffector2D bileşeniyle
    /// tek yönlü (alttan geçilir, üstten düşüldüğünde kesinlikle tutar) hale getirir.
    /// </summary>
    private void SyncOneWayEffector()
    {
        if (platformCollider == null) return;

        bool enableOneWay = GameManager.Instance != null
            && GameManager.Instance.IsSuperFeetActive
            && !GameManager.Instance.IsRocketBoosting;

        if (enableOneWay)
        {
            if (oneWayEffector == null)
            {
                oneWayEffector = GetComponent<PlatformEffector2D>();
                if (oneWayEffector == null)
                {
                    oneWayEffector = gameObject.AddComponent<PlatformEffector2D>();
                }
                oneWayEffector.useOneWay = true;
                oneWayEffector.useOneWayGrouping = false;
                oneWayEffector.useColliderMask = false;
                oneWayEffector.surfaceArc = 170f;
                oneWayEffector.rotationalOffset = 0f;
                oneWayEffector.useSideFriction = false;
                oneWayEffector.useSideBounce = false;
            }

            if (!oneWayEffector.enabled) oneWayEffector.enabled = true;
            if (!platformCollider.usedByEffector) platformCollider.usedByEffector = true;
        }
        else
        {
            if (platformCollider.usedByEffector) platformCollider.usedByEffector = false;
            if (oneWayEffector != null && oneWayEffector.enabled) oneWayEffector.enabled = false;
        }
    }

    void Update()
    {
        SyncOneWayEffector();
        // 1. Dikey Hareket (GameManager'ın oyun ilerledikçe artan platform hızı)
        transform.Translate(Vector3.down * GameManager.globalPlatformSpeed * Time.deltaTime, Space.World);

        // 2. Yatay Hareket (Sağa - Sola yumuşak salınım)
        if (isMovingHorizontal)
        {
            float prevX = transform.position.x;
            float clock = useDeterministicDuelClock ? MultiplayerManager.DuelMatchElapsedTime : Time.time;
            float newX = startX + Mathf.Sin((clock * horizontalSpeed) + randomOffset) * horizontalDistance;
            newX = Mathf.Clamp(newX, -screenLimit, screenLimit);
            transform.position = new Vector3(newX, transform.position.y, transform.position.z);
            CurrentHorizontalVelocity = Time.deltaTime > 0.0001f ? (newX - prevX) / Time.deltaTime : 0f;
        }
        else
        {
            CurrentHorizontalVelocity = 0f;
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
