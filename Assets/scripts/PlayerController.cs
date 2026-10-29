using UnityEngine;

/// <summary>
/// Karakter kontrolcüsü: Yatay hareket, zıplama ve oyun fiziğini yönetir.
/// - Ekran sarma (Screen Wrap): Sol kenardan çıkınca sağdan, sağdan çıkınca soldan belirir.
/// - Tek zıplama kuralı: Havada veya yükselirken asla üst üste zıplayamaz; sadece yere indiğinde zıplayabilir.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("Hareket Ayarları")]
    [Tooltip("Yatay hareket hızı")]
    public float moveSpeed = 5.5f;
    [Tooltip("Zıplama gücü")]
    public float jumpForce = 5.2f;
    private Rigidbody2D rb;
    private float baseGravityScale = 1.5f;

    [Header("Oynanış Hissi (Game Feel)")]
    [Tooltip("Platformdan düştükten sonra zıplamaya izin verilen tolerans süresi (saniye)")]
    public float coyoteTime = 0.12f;
    private float coyoteTimeCounter;

    [Tooltip("Yere inmeden önce basılan zıplama girdisinin hatırlanma süresi (saniye)")]
    public float jumpBufferTime = 0.12f;
    private float jumpBufferCounter;

    [Header("Kontrol Modu (0: Butonlar, 1: Jiroskop/Eğim)")]
    public int controlMode = 0;
    [Tooltip("Jiroskop eğim hassasiyeti")]
    public float tiltSensitivity = 3f;

    [Header("Mobil Buton Referansları")]
    public GameObject leftButtonObj;
    public GameObject rightButtonObj;

    [Header("Zemin Kontrolü & Zıplama Kilidi")]
    public Transform ayakNoktasi;
    public float ayakCapi = 0.28f;
    public LayerMask zeminKatmani;
    private bool yerdeMi;
    private bool canJump = true; // Üst üste zıplamayı kesin olarak engeller

    [Header("Ses Efektleri")]
    public AudioClip jumpSound;
    public AudioClip damageSound;
    private AudioSource audioSource;

    [Range(0f, 10f)]
    public float ziplamaSesiSiddeti = 1f;

    [Range(0f, 10f)]
    public float hasarSesiSiddeti = 1f;

    [Header("Ekran Sarma (Screen Wrap) Ayarları")]
    [Tooltip("Karakterin ekran kenarından tamamen çıkıp diğer taraftan belireceği eşik payı")]
    public float wrapOffset = 0.45f;
    private float wrapThreshold = 3.0f;
    private float lastAspect = 0f;

    // Mobil girdiler için değişkenler
    private float mobileMoveInput = 0f;
    private bool mobileJumpRequested = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null) baseGravityScale = rb.gravityScale;

        audioSource = GetComponent<AudioSource>();

        // Kayıtlı kontrol modunu yükle
        controlMode = PlayerPrefs.GetInt("ControlMode", 0);
        ApplyControlModeUI();

        CalculateScreenBounds();
    }

    public void ApplyControlModeUI()
    {
        if (leftButtonObj == null) leftButtonObj = GameObject.Find("Btn_Sol");
        if (rightButtonObj == null) rightButtonObj = GameObject.Find("Btn_Sag");

        if (controlMode == 1) // Jiroskop / Eğim Modu
        {
            if (leftButtonObj != null) leftButtonObj.SetActive(false);
            if (rightButtonObj != null) rightButtonObj.SetActive(false);
        }
        else // Standart Buton Modu
        {
            if (leftButtonObj != null) leftButtonObj.SetActive(true);
            if (rightButtonObj != null) rightButtonObj.SetActive(true);
        }
    }

    private void CalculateScreenBounds()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            float camHalfWidth = mainCam.orthographicSize * mainCam.aspect;
            wrapThreshold = camHalfWidth + wrapOffset;
            lastAspect = mainCam.aspect;
        }
        else
        {
            wrapThreshold = 3.0f;
        }
    }

    void Update()
    {
        // Ekran boyutu veya oranı değişirse sınırları güncelle
        Camera mainCam = Camera.main;
        if (mainCam != null && Mathf.Abs(mainCam.aspect - lastAspect) > 0.005f)
        {
            CalculateScreenBounds();
        }

        // Oyun hızlandıkça dikeyde ve yatayda çevikliği artıran dinamik katsayı
        float speedRatio = (GameManager.globalPlatformSpeed > 0f) ? (GameManager.globalPlatformSpeed / 1.7f) : 1f;
        speedRatio = Mathf.Clamp(speedRatio, 1f, 1.41f);

        rb.gravityScale = baseGravityScale * speedRatio;

        // 1. HAREKET (Tuşlar, Klavye veya Jiroskop Eğim)
        float moveInput = 0f;

        if (controlMode == 1) // Jiroskop Modu
        {
            float tilt = Input.acceleration.x * tiltSensitivity;
            if (Mathf.Abs(tilt) < 0.05f) tilt = 0f;

            float keyboardInput = Input.GetAxisRaw("Horizontal");
            moveInput = Mathf.Abs(keyboardInput) > 0.01f ? keyboardInput : Mathf.Clamp(tilt, -1f, 1f);
        }
        else // Buton Modu
        {
            float keyboardInput = Input.GetAxisRaw("Horizontal");
            moveInput = Mathf.Abs(mobileMoveInput) > 0.01f ? mobileMoveInput : keyboardInput;
        }

        float currentMoveSpeed = moveSpeed * Mathf.Sqrt(speedRatio);
        rb.linearVelocity = new Vector2(moveInput * currentMoveSpeed, rb.linearVelocity.y);

        // 2. YÖN ÇEVİRME
        if (moveInput > 0)
        {
            transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);
        }
        else if (moveInput < 0)
        {
            transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, 1);
        }

        // 3. EKRAN SARMA (SCREEN WRAP - SOLDAN GİR SAĞDAN ÇIK / SAĞDAN GİR SOLDAN ÇIK)
        if (transform.position.x > wrapThreshold)
        {
            transform.position = new Vector3(-wrapThreshold + 0.1f, transform.position.y, transform.position.z);
        }
        else if (transform.position.x < -wrapThreshold)
        {
            transform.position = new Vector3(wrapThreshold - 0.1f, transform.position.y, transform.position.z);
        }

        // 4. ZEMİN KONTROLÜ & TEK ZIPLAMA GÜVENCESİ
        yerdeMi = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);

        // Karakter yere inip durduğunda veya aşağı düşerken zemine bastığında zıplama hakkı yenilenir
        if (yerdeMi && rb.linearVelocity.y <= 0.1f)
        {
            canJump = true;
            coyoteTimeCounter = coyoteTime;
        }
        else
        {
            coyoteTimeCounter -= Time.deltaTime;
        }

        // 5. ZIPLAMA GİRDİSİ & JUMP BUFFERING
        bool jumpPressed = Input.GetButtonDown("Jump") || mobileJumpRequested;
        mobileJumpRequested = false;

        if (jumpPressed)
        {
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpBufferCounter -= Time.deltaTime;
        }

        // Kesin Zıplama Koşulu:
        // - Zıplama tuşuna basılmış olmalı (buffer > 0)
        // - Karakter zeminde veya coyote time içinde olmalı
        // - canJump aktif olmalı (havada zıplamayı kilitler)
        // - Karakter halihazırda yukarı doğru fırlıyor olmamalı (üst üste zıplamayı kesin olarak engeller)
        if (jumpBufferCounter > 0f && coyoteTimeCounter > 0f && canJump && rb.linearVelocity.y <= 0.15f)
        {
            canJump = false; // Zıplama hakkını tüket, yere basana kadar tekrar zıplayamaz!
            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f;

            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);

            float currentJumpForce = jumpForce * Mathf.Sqrt(speedRatio);
            rb.AddForce(Vector2.up * currentJumpForce, ForceMode2D.Impulse);

            if (audioSource != null && jumpSound != null)
            {
                audioSource.PlayOneShot(jumpSound, ziplamaSesiSiddeti);
            }
        }
    }

    public void SetMobileMoveInput(float direction)
    {
        mobileMoveInput = direction;
    }

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
        if (collision.gameObject.CompareTag("Platform"))
        {
            transform.SetParent(collision.transform);

            // Platforma temas edildiğinde zıplama hakkını tazelemeye izin ver
            if (rb.linearVelocity.y <= 0.1f)
            {
                canJump = true;
            }
        }
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Platform"))
        {
            transform.SetParent(null);
        }
    }
}
