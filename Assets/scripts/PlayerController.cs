using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Hareket Ayarları")]
    public float moveSpeed = 5f;
    public float jumpForce = 12f;
    private Rigidbody2D rb;

    [Header("Zemin Kontrolü")]
    public Transform ayakNoktasi;
    public float ayakCapi = 0.3f;
    public LayerMask zeminKatmani;
    private bool yerdeMi;

    [Header("Ses Efektleri")]
    public AudioClip jumpSound;
    public AudioClip damageSound;
    private AudioSource audioSource;

    [Range(0f, 10f)]
    public float ziplamaSesiSiddeti = 1f;

    [Range(0f, 10f)]
    public float hasarSesiSiddeti = 1f;

    [Header("Dinamik Ekran Sınırları")]
    [Tooltip("Karakterin kenarlardan taşmaması için yarım genişlik payı")]
    public float playerWidthOffset = 0.35f;
    private float minX = -2.75f;
    private float maxX = 2.75f;

    // Mobil girdiler için değişkenler
    private float mobileMoveInput = 0f;
    private bool mobileJumpRequested = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();
        CalculateScreenBounds();
    }

    /// <summary>
    /// Ekranın en boy oranına göre karakterin sağ/sol sınırlarını dinamik belirler.
    /// Farklı telefon modellerinde karakterin ekrandan taşmasını engeller.
    /// </summary>
    private void CalculateScreenBounds()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            float camHalfWidth = mainCam.orthographicSize * mainCam.aspect;
            minX = -camHalfWidth + playerWidthOffset;
            maxX = camHalfWidth - playerWidthOffset;
        }
        else
        {
            minX = -2.75f;
            maxX = 2.75f;
        }
    }

    void Update()
    {
        // 1. HAREKET (Klavye ve Dokunmatik Kontrolleri Birleştirme)
        float keyboardInput = Input.GetAxisRaw("Horizontal");
        float moveInput = Mathf.Abs(mobileMoveInput) > 0.01f ? mobileMoveInput : keyboardInput;

        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);

        // 2. YÖN ÇEVİRME
        if (moveInput > 0)
        {
            transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);
        }
        else if (moveInput < 0)
        {
            transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);
        }

        // 3. EKRAN SINIRLAMA (Dinamik Mobil ve PC Uyumlu)
        float sinirliX = Mathf.Clamp(transform.position.x, minX, maxX);
        transform.position = new Vector2(sinirliX, transform.position.y);

        // 4. ZEMİN KONTROLÜ
        yerdeMi = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);

        // 5. ZIPLAMA VE SES (Klavye Space veya Mobil Buton)
        bool jumpInput = Input.GetButtonDown("Jump") || mobileJumpRequested;
        mobileJumpRequested = false; // Tek karelik zıplama isteğini tüket

        if (jumpInput && yerdeMi)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

            if (audioSource != null && jumpSound != null)
            {
                audioSource.PlayOneShot(jumpSound, ziplamaSesiSiddeti);
            }
        }
    }

    /// <summary>
    /// Mobil dokunmatik butonlar (Sol/Sağ) tarafından çağrılır.
    /// </summary>
    public void SetMobileMoveInput(float direction)
    {
        mobileMoveInput = direction;
    }

    /// <summary>
    /// Mobil dokunmatik zıpla butonu tarafından çağrılır.
    /// </summary>
    public void TriggerMobileJump()
    {
        mobileJumpRequested = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("DeadZone"))
        {
            if (audioSource != null && damageSound != null)
            {
                audioSource.PlayOneShot(damageSound, hasarSesiSiddeti);
            }

            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm != null) gm.TakeDamage();
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform")) transform.SetParent(collision.transform);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform")) transform.SetParent(null);
    }
}
