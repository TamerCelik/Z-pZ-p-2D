using System.Collections.Generic;
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
    public GameObject jumpButtonObj;

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
    private PlatformMovement standingPlatform;
    private Collider2D playerCollider;
    private readonly HashSet<Collider2D> ignoredPassThroughPlatforms = new HashSet<Collider2D>();
    private readonly List<Collider2D> tempCleanupList = new List<Collider2D>();

    // Zıplama & iniş esneme (Squash & Stretch) animasyonu ve iz zamanlayıcısı
    private float baseAbsScaleX = 1.8f;
    private float baseScaleY = 1.42f;
    private float facingSign = 1f;
    private Vector2 visualStretch = Vector2.one;
    private bool wasGroundedForSquash = true;
    private float superTrailTimer = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        if (rb != null) baseGravityScale = rb.gravityScale;

        baseAbsScaleX = Mathf.Abs(transform.localScale.x);
        baseScaleY = transform.localScale.y;
        facingSign = transform.localScale.x < 0f ? -1f : 1f;

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
        if (jumpButtonObj == null) jumpButtonObj = GameObject.Find("Btn_Zirla");

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

        int buttonLayout = PlayerPrefs.GetInt("ButtonLayout", 0);
        ApplyButtonLayoutPositions(buttonLayout);
    }

    private void ApplyButtonLayoutPositions(int buttonLayout)
    {
        float leftBtnX = (buttonLayout == 1) ? 24f : -124f;
        float rightBtnX = (buttonLayout == 1) ? 124f : -24f;
        float jumpBtnX = (buttonLayout == 1) ? -112f : 112f;

        if (leftButtonObj != null)
        {
            RectTransform rt = leftButtonObj.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(leftBtnX, rt.anchoredPosition.y);
        }

        if (rightButtonObj != null)
        {
            RectTransform rt = rightButtonObj.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(rightBtnX, rt.anchoredPosition.y);
        }

        if (jumpButtonObj != null)
        {
            RectTransform rt = jumpButtonObj.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(jumpBtnX, rt.anchoredPosition.y);
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

        bool rocketActive = GameManager.Instance != null && GameManager.Instance.IsRocketBoosting;
        rb.gravityScale = rocketActive ? 0f : (baseGravityScale * speedRatio);

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

        // 4. ZEMİN KONTROLÜ & KAYAN PLATFORM TAKİBİ
        bool superFeetActive = GameManager.Instance != null && GameManager.Instance.IsSuperFeetActive;
        UpdatePassThroughPlatforms(superFeetActive && !rocketActive);

        Collider2D groundHit = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);
        yerdeMi = groundHit != null && !ignoredPassThroughPlatforms.Contains(groundHit);
        standingPlatform = (yerdeMi && groundHit != null) ? groundHit.GetComponentInParent<PlatformMovement>() : null;

        if (rocketActive)
        {
            if (transform.parent != null) transform.SetParent(null);
            float targetY = GameManager.Instance.rocketTargetY;
            float newY = Mathf.Lerp(transform.position.y, targetY, Mathf.Clamp01(Time.deltaTime * 6.5f));
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            rb.linearVelocity = new Vector2(moveInput * currentMoveSpeed * 1.15f, 0f);
        }
        else
        {
            // Karakter kayan platformun üzerindeyken platformun anlık yatay hızını doğrudan ekle
            float platformCarryVx = (yerdeMi && standingPlatform != null && standingPlatform.isMovingHorizontal)
                ? standingPlatform.CurrentHorizontalVelocity
                : 0f;
            rb.linearVelocity = new Vector2((moveInput * currentMoveSpeed) + platformCarryVx, rb.linearVelocity.y);
        }

        // 2. YÖN ÇEVİRME & ZIPLAMA ESNEME (SQUASH & STRETCH) ANİMASYONU
        if (moveInput > 0.01f) facingSign = 1f;
        else if (moveInput < -0.01f) facingSign = -1f;

        if (yerdeMi && !wasGroundedForSquash && rb.linearVelocity.y <= 0.1f)
        {
            // Yere inişte tatlı ezilme (squash)
            visualStretch = new Vector2(1.22f, 0.80f);
        }
        wasGroundedForSquash = yerdeMi;

        visualStretch = Vector2.Lerp(visualStretch, Vector2.one, Mathf.Clamp01(Time.deltaTime * 12f));
        transform.localScale = new Vector3(
            facingSign * baseAbsScaleX * visualStretch.x,
            baseScaleY * visualStretch.y,
            transform.localScale.z);

        // Kuş ayağı ile yükselirken arkasından parıltılı rüzgar/yıldız izi bırak
        if (superFeetActive && !rocketActive && rb.linearVelocity.y > 0.8f)
        {
            superTrailTimer -= Time.deltaTime;
            if (superTrailTimer <= 0f)
            {
                superTrailTimer = 0.055f;
                JumpDustEffect.SpawnSuperJumpTrail(ayakNoktasi != null ? ayakNoktasi.position : transform.position);
            }
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

        // 4. ZEMİN TOZU & TEK ZIPLAMA GÜVENCESİ
        JumpDustEffect.NotifyGroundState(ayakNoktasi.position, yerdeMi, rb.linearVelocity.y);

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

            bool isSuperJump = GameManager.Instance != null && GameManager.Instance.IsSuperFeetActive;
            float superFeetMult = isSuperJump ? GameManager.Instance.superFeetJumpMultiplier : 1f;
            float currentJumpForce = jumpForce * Mathf.Sqrt(speedRatio) * superFeetMult;
            rb.AddForce(Vector2.up * currentJumpForce, ForceMode2D.Impulse);

            // Zıplama anında dikey uzama (stretch) animasyonu + partikül patlaması
            Vector3 feetPos = ayakNoktasi != null ? ayakNoktasi.position : transform.position;
            if (isSuperJump)
            {
                visualStretch = new Vector2(0.70f, 1.38f);
                JumpDustEffect.SpawnSuperJumpBurst(feetPos);
            }
            else
            {
                visualStretch = new Vector2(0.80f, 1.24f);
                JumpDustEffect.SpawnJumpDust(feetPos);
            }

            if (audioSource != null && jumpSound != null)
            {
                audioSource.PlayOneShot(jumpSound, ziplamaSesiSiddeti);
            }
        }
    }

    /// <summary>
    /// Kuş ayağı aktifken yukarı zıplarken (veya platformun içindeyken) platformların içinden
    /// takılmadan geçmeyi, aşağı inerken ise üstlerine sağlamca konmayı sağlar.
    /// </summary>
    private void UpdatePassThroughPlatforms(bool enablePassThrough)
    {
        if (playerCollider == null) return;

        if (!enablePassThrough)
        {
            if (ignoredPassThroughPlatforms.Count > 0)
            {
                foreach (Collider2D col in ignoredPassThroughPlatforms)
                {
                    if (col != null) Physics2D.IgnoreCollision(playerCollider, col, false);
                }
                ignoredPassThroughPlatforms.Clear();
            }
            return;
        }

        float playerBottomY = playerCollider.bounds.min.y;
        bool rising = rb != null && rb.linearVelocity.y > 0.12f;

        Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, 2.8f, zeminKatmani);
        for (int i = 0; i < nearby.Length; i++)
        {
            Collider2D pCol = nearby[i];
            if (pCol == null || pCol == playerCollider) continue;

            float platformTopY = pCol.bounds.max.y;
            bool shouldPass = rising || (playerBottomY < platformTopY - 0.12f);

            if (shouldPass)
            {
                if (!ignoredPassThroughPlatforms.Contains(pCol))
                {
                    Physics2D.IgnoreCollision(playerCollider, pCol, true);
                    ignoredPassThroughPlatforms.Add(pCol);
                }
            }
            else
            {
                if (ignoredPassThroughPlatforms.Contains(pCol))
                {
                    Physics2D.IgnoreCollision(playerCollider, pCol, false);
                    ignoredPassThroughPlatforms.Remove(pCol);
                }
            }
        }

        // Yok olmuş veya uzaklaşmış platformları listeden temizle
        if (ignoredPassThroughPlatforms.Count > 0)
        {
            tempCleanupList.Clear();
            foreach (Collider2D col in ignoredPassThroughPlatforms)
            {
                if (col == null)
                {
                    tempCleanupList.Add(col);
                }
                else if (!rising && playerBottomY >= col.bounds.max.y - 0.10f)
                {
                    Physics2D.IgnoreCollision(playerCollider, col, false);
                    tempCleanupList.Add(col);
                }
            }
            for (int i = 0; i < tempCleanupList.Count; i++)
            {
                ignoredPassThroughPlatforms.Remove(tempCleanupList[i]);
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
        if (GameManager.Instance != null && GameManager.Instance.IsRocketBoosting)
        {
            return;
        }

        if (collision.gameObject.CompareTag("Platform"))
        {
            // Platforma temas edildiğinde zıplama hakkını tazelemeye izin ver (kayma takibi CurrentHorizontalVelocity ile yapılır)
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
            PlatformMovement pm = collision.gameObject.GetComponent<PlatformMovement>();
            if (pm != null && pm == standingPlatform)
            {
                standingPlatform = null;
            }
        }
    }
}
