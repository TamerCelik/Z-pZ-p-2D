using UnityEngine;

public class HealthPickup : MonoBehaviour
{
    [Header("Görsel Animasyon")]
    [Tooltip("Can ikonunun yukarı-aşağı hafif süzülme hızı")]
    public float bobSpeed = 3.5f;
    [Tooltip("Süzülme genliği")]
    public float bobAmount = 0.08f;
    [Tooltip("Hafif nabız/büyüyüp küçülme efekti")]
    public float pulseSpeed = 4f;

    private float startLocalY;
    private Vector3 baseScale;
    private bool isCollected = false;

    /// <summary>
    /// Düello modunda iki oyuncu arasında ortak olan deterministik item kimliği (-1: tek oyunculu).
    /// </summary>
    public int DuelItemId { get; set; } = -1;

    void Start()
    {
        startLocalY = transform.localPosition.y;
        baseScale = transform.localScale;
    }

    void Update()
    {
        // Platform üzerinde tatlı bir süzülme ve nabız animasyonu
        float newY = startLocalY + Mathf.Sin(Time.time * bobSpeed) * bobAmount;
        transform.localPosition = new Vector3(transform.localPosition.x, newY, transform.localPosition.z);

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.08f;
        transform.localScale = baseScale * pulse;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isCollected) return;

        // Karakter içine girdiğinde
        if (other.GetComponent<PlayerController>() != null || other.CompareTag("Player") || other.name.ToLower().Contains("karakter"))
        {
            isCollected = true;

            if (DuelItemId >= 0 && MultiplayerManager.IsDuelActive && MultiplayerManager.Instance != null)
            {
                MultiplayerManager.Instance.NotifyLocalItemCollected(DuelItemId);
            }

            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                gm.AddHealth(1);
            }

            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Düello modunda rakip bu canı aldığında yerel oyuncuya can eklemeden sahneden kaldırır.
    /// </summary>
    public void ConsumeRemotely()
    {
        if (isCollected) return;
        isCollected = true;
        JumpDustEffect.SpawnJumpDust(transform.position);
        Destroy(gameObject);
    }
}
