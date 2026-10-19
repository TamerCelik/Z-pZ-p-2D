using UnityEngine;

public class PlayerController : MonoBehaviour
{
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

    // --- YENİ EKLENEN KISIM: SES AYARLARI ---
    [Range(0f, 10f)] // Editörde 0 ile 3 arasında kaydırma çubuğu çıkarır
    public float ziplamaSesiSiddeti = 1f; // 1 normal, 0.5 yarım, 2 iki katı ses

    [Range(0f, 10f)]
    public float hasarSesiSiddeti = 1f;
    // ----------------------------------------

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        // 1. HAREKET
        float moveInput = Input.GetAxis("Horizontal");
        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);

        // 2. YÖN ÇEVİRME
        if (moveInput > 0) transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);
        else if (moveInput < 0) transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);

        // 3. SINIRLAMA
        float sinirliX = Mathf.Clamp(transform.position.x, -2.75f, 2.75f);
        transform.position = new Vector2(sinirliX, transform.position.y);

        // 4. ZEMİN KONTROLÜ
        yerdeMi = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);

        // 5. ZIPLAMA VE SES
        if (Input.GetButtonDown("Jump") && yerdeMi)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

            if (audioSource != null && jumpSound != null)
            {
                // Sesi belirlediğimiz şiddetle çal
                audioSource.PlayOneShot(jumpSound, ziplamaSesiSiddeti);
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("DeadZone"))
        {
            if (audioSource != null && damageSound != null)
            {
                // Hasar sesini belirlediğimiz şiddetle çal
                audioSource.PlayOneShot(damageSound, hasarSesiSiddeti);
            }

            GameManager gm = FindObjectOfType<GameManager>();
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