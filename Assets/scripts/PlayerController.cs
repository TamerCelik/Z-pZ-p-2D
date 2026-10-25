using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Hareket Ayarları")]
    public float moveSpeed = 7.5f;
    public float jumpForce = 8.5f;
    private Rigidbody2D rb;

    [Header("Oynanış Hissi (Game Feel)")]
    [Tooltip("Platformdan düştükten sonra zıplamaya izin verilen tolerans süresi (saniye)")]
    public float coyoteTime = 0.15f;
    private float coyoteTimeCounter;

    [Tooltip("Yere inmeden önce basılan zıplama girdisinin hatırlanma süresi (saniye)")]
    public float jumpBufferTime = 0.15f;
    private float jumpBufferCounter;

    [Header("Kontrol Modu (0: Butonlar, 1: Jiroskop/Eğim)")]
    [Tooltip("Kontrol tipi PlayerPrefs'ten otomatik yüklenir. İstenirse editörden test için değiştirilebilir.")]
    public int controlMode = 0;
    [Tooltip("Jiroskop eğim hassasiyeti (2.5 - 3.5 idealdir)")]
    public float tiltSensitivity = 3f;

    [Header("Mobil Buton Referansları (Jiroskopta Otomatik Gizlenir)")]
    public GameObject leftButtonObj;
    public GameObject rightButtonObj;

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
    [Tooltip("0 olduğunda karakterin tam yarısı ekran çizgisini geçer.")]
    public float playerWidthOffset = 0f;
    private float minX = -2.75f;
    private float maxX = 2.75f;
    private float lastAspect = 0f;

    // Mobil girdiler için değişkenler
    private float mobileMoveInput = 0f;
    private bool mobileJumpRequested = false;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();

        // Kayıtlı kontrol modunu yükle (Varsayılan: 0 - Butonlar)
        controlMode = PlayerPrefs.GetInt("ControlMode", 0);
        ApplyControlModeUI();

        CalculateScreenBounds();
    }

    /// <summary>
    /// Kontrol moduna göre Sol ve Sağ butonları gizler/açar.
    /// Jiroskop modunda Sol/Sağ gizlenir; Zıplama butonu ekranda kalır!
    /// </summary>
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
            minX = -camHalfWidth + playerWidthOffset;
            maxX = camHalfWidth - playerWidthOffset;
            lastAspect = mainCam.aspect;
        }
        else
        {
            minX = -2.75f;
            maxX = 2.75f;
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

        // 1. HAREKET (Tuşlar, Klavye veya Jiroskop Eğim)
        float moveInput = 0f;

        if (controlMode == 1) // Jiroskop Modu
        {
            float tilt = Input.acceleration.x * tiltSensitivity;
            // Küçük el titremelerini filtrelemek için ölü bölge (deadzone)
            if (Mathf.Abs(tilt) < 0.05f) tilt = 0f;

            // Editörde ve PC testinde klavye desteğini koru
            float keyboardInput = Input.GetAxisRaw("Horizontal");
            moveInput = Mathf.Abs(keyboardInput) > 0.01f ? keyboardInput : Mathf.Clamp(tilt, -1f, 1f);
        }
        else // Buton Modu
        {
            float keyboardInput = Input.GetAxisRaw("Horizontal");
            moveInput = Mathf.Abs(mobileMoveInput) > 0.01f ? mobileMoveInput : keyboardInput;
        }

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

        // 3. EKRAN SINIRLAMA
        float sinirliX = Mathf.Clamp(transform.position.x, minX, maxX);
        transform.position = new Vector2(sinirliX, transform.position.y);

        // 4. ZEMİN KONTROLÜ & COYOTE TIME
        yerdeMi = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);

        if (yerdeMi)
        {
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

        // Zıplama koşulu: Hem buffer geçerli, hem de coyote time içinde
        if (jumpBufferCounter > 0f && coyoteTimeCounter > 0f)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f;

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
