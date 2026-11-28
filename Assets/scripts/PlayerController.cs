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

    [Header("Kontrol Modu (0: Yön Tuşları, 2: Joystick)")]
    public int controlMode = 0;

    [Header("Mobil Buton Referansları")]
    public GameObject leftButtonObj;
    public GameObject rightButtonObj;
    public GameObject jumpButtonObj;
    public GameObject bottomBarObj;
    private MobileAnalogStick analogStick;

    // Alt ölüm çizgisi (DeadZone): Tuş modunda alt barın üstünde, Analog modunda ekran bitiminin altında konumlanır
    private Transform bottomDeadZoneTr;
    private Collider2D bottomDeadZoneCol;
    private float defaultDeadZoneY = -5.3f;
    private bool deadZoneCached = false;

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

    public bool IsGrounded => yerdeMi;
    public PlatformMovement CurrentStandingPlatform => standingPlatform;

    // Zıplama & iniş esneme (Squash & Stretch) animasyonu ve iz zamanlayıcısı
    private float baseAbsScaleX = 1.8f;
    private float baseScaleY = 1.42f;
    private float facingSign = 1f;
    private Vector2 visualStretch = Vector2.one;
    private bool wasGroundedForSquash = true;
    private float superTrailTimer = 0f;

    // Platform sağ/sol kenarında görsel poz (fizik veya mekaniğe kesinlikle dokunmaz)
    private SpriteRenderer playerSpriteRenderer;
    private Sprite[] cornerHangFrames;
    private string loadedCornerCharId;
    private float cornerAnimTimer = 0f;
    private bool wasInCornerPose = false;
    private Sprite lastNormalSprite;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        playerSpriteRenderer = GetComponent<SpriteRenderer>();
        LoadCornerHangSprites();
        if (rb != null)
        {
            baseGravityScale = rb.gravityScale;
            // Süper zıplamadan hızlı düşerken ince platformların içinden geçmeyi (tunneling) kesin olarak önle
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        baseAbsScaleX = Mathf.Abs(transform.localScale.x);
        baseScaleY = transform.localScale.y;
        facingSign = transform.localScale.x < 0f ? -1f : 1f;

        audioSource = GetComponent<AudioSource>();

        // Kayıtlı kontrol modunu yükle (0 = Yön Tuşları, 2 = Joystick)
        controlMode = KawaiiControlSelectorUI.GetNormalizedControlMode();
        ApplyControlModeUI();

        CalculateScreenBounds();
    }

    private void LoadCornerHangSprites()
    {
        string charId = CharacterShopData.GetSelectedCharacterId();
        loadedCornerCharId = charId;
        Texture2D tex = Resources.Load<Texture2D>("Characters/corner_hang_" + charId);
        if (tex == null)
        {
            tex = Resources.Load<Texture2D>("Characters/corner_hang_pink_man");
        }
        if (tex == null) return;

        int frameSize = tex.height;
        if (frameSize <= 0) return;
        int frameCount = Mathf.Max(1, tex.width / frameSize);
        cornerHangFrames = new Sprite[frameCount];

        // Pink Man 114 PPU'da tam oturuyor; Ninja Frog, Mask Dude ve Virtual Guy çizimleri daha kompakt olduğu için
        // daha düşük PPU (daha büyük dünya boyutu) ile normal karakter boyutlarıyla birebir eşitlenir.
        float cornerPpu = 114f;
        if (charId == CharacterShopData.NinjaFrogId)
        {
            cornerPpu = 88f;
        }
        else if (charId == CharacterShopData.VirtualGuyId)
        {
            cornerPpu = 98f;
        }
        else if (charId == CharacterShopData.MaskDudeId)
        {
            cornerPpu = 90f;
        }

        Vector2 cornerPivot = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < frameCount; i++)
        {
            cornerHangFrames[i] = Sprite.Create(
                tex,
                new Rect(i * frameSize, 0f, frameSize, frameSize),
                cornerPivot,
                cornerPpu);
        }
    }

    public void ApplyControlModeUI()
    {
        if (leftButtonObj == null) leftButtonObj = FindUiObjectByName("Btn_Sol");
        if (rightButtonObj == null) rightButtonObj = FindUiObjectByName("Btn_Sag");
        if (jumpButtonObj == null) jumpButtonObj = FindUiObjectByName("Btn_Zirla");
        if (bottomBarObj == null) bottomBarObj = FindUiObjectByName("BottomBar");

        if (controlMode != KawaiiControlSelectorUI.ModeJoystick)
        {
            controlMode = KawaiiControlSelectorUI.ModeButtons;
        }

        mobileMoveInput = 0f;

        if (controlMode == KawaiiControlSelectorUI.ModeJoystick) // Joystick Modu: Alt bar ve tüm butonlar kalkar, solda Analog görünür
        {
            if (bottomBarObj != null) bottomBarObj.SetActive(false);
            if (leftButtonObj != null) leftButtonObj.SetActive(false);
            if (rightButtonObj != null) rightButtonObj.SetActive(false);
            if (jumpButtonObj != null) jumpButtonObj.SetActive(false);

            EnsureAnalogStickCreated();
            if (analogStick != null)
            {
                analogStick.gameObject.SetActive(true);
                analogStick.ResetStickImmediate();
            }
            UpdateDeadZoneForControlMode();
            return;
        }

        // Yön Tuşları Modu (0): Alt bar ve butonlar açık, analog kapalı
        if (analogStick != null) analogStick.gameObject.SetActive(false);
        if (bottomBarObj != null) bottomBarObj.SetActive(true);
        if (jumpButtonObj != null) jumpButtonObj.SetActive(true);
        if (leftButtonObj != null) leftButtonObj.SetActive(true);
        if (rightButtonObj != null) rightButtonObj.SetActive(true);

        int buttonLayout = PlayerPrefs.GetInt("ButtonLayout", 0);
        ApplyButtonLayoutPositions(buttonLayout);
        UpdateDeadZoneForControlMode();
    }

    public void HideAllMobileControls()
    {
        mobileMoveInput = 0f;
        if (leftButtonObj != null) leftButtonObj.SetActive(false);
        if (rightButtonObj != null) rightButtonObj.SetActive(false);
        if (jumpButtonObj != null) jumpButtonObj.SetActive(false);
        if (analogStick != null) analogStick.gameObject.SetActive(false);
    }

    private void EnsureAnalogStickCreated()
    {
        if (analogStick != null) return;

        Transform canvasTr = null;
        int siblingIdx = -1;
        if (bottomBarObj != null && bottomBarObj.transform.parent != null)
        {
            canvasTr = bottomBarObj.transform.parent;
            siblingIdx = bottomBarObj.transform.GetSiblingIndex() + 1;
        }
        else if (jumpButtonObj != null && jumpButtonObj.transform.parent != null)
        {
            canvasTr = jumpButtonObj.transform.parent;
            siblingIdx = jumpButtonObj.transform.GetSiblingIndex();
        }
        else
        {
            Canvas c = Object.FindFirstObjectByType<Canvas>();
            if (c != null) canvasTr = c.transform;
        }

        if (canvasTr != null)
        {
            analogStick = MobileAnalogStick.CreateUnderCanvas(canvasTr, siblingIdx);
        }
    }

    private static GameObject FindUiObjectByName(string objName)
    {
        GameObject active = GameObject.Find(objName);
        if (active != null) return active;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int c = 0; c < canvases.Length; c++)
        {
            if (canvases[c] == null) continue;
            Transform[] children = canvases[c].GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == objName)
                {
                    return children[i].gameObject;
                }
            }
        }
        return null;
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

    private void CacheBottomDeadZoneIfNeeded()
    {
        if (deadZoneCached && bottomDeadZoneTr != null) return;

        GameObject[] deadZones = GameObject.FindGameObjectsWithTag("DeadZone");
        for (int i = 0; i < deadZones.Length; i++)
        {
            if (deadZones[i] != null && deadZones[i].transform.position.y < 0f)
            {
                bottomDeadZoneTr = deadZones[i].transform;
                bottomDeadZoneCol = deadZones[i].GetComponent<Collider2D>();
                defaultDeadZoneY = bottomDeadZoneTr.position.y;
                deadZoneCached = true;
                break;
            }
        }
    }

    private void UpdateDeadZoneForControlMode()
    {
        CacheBottomDeadZoneIfNeeded();
        if (bottomDeadZoneTr == null) return;

        Vector3 pos = bottomDeadZoneTr.position;
        if (controlMode == KawaiiControlSelectorUI.ModeJoystick)
        {
            Camera mainCam = Camera.main;
            float screenBottomY = mainCam != null
                ? (mainCam.transform.position.y - mainCam.orthographicSize)
                : -5f;

            float playerHeight = 0.54f;
            if (playerCollider is BoxCollider2D boxCol)
            {
                playerHeight = Mathf.Clamp(boxCol.size.y * Mathf.Abs(baseScaleY), 0.40f, 0.75f);
            }
            else if (playerCollider != null && playerCollider.bounds.size.y > 0.05f)
            {
                playerHeight = Mathf.Clamp(playerCollider.bounds.size.y, 0.40f, 0.75f);
            }

            float dzHalfHeight = 0.5f;
            if (bottomDeadZoneCol != null && bottomDeadZoneCol.bounds.extents.y > 0.01f)
            {
                dzHalfHeight = bottomDeadZoneCol.bounds.extents.y;
            }

            // Karakter ekranın alt bitimini tamamen geçtiğinde (üst kısmı ekran altına indiğinde) ölüm çizgisine değsin
            float targetDeadZoneTopY = screenBottomY - playerHeight;
            bottomDeadZoneTr.position = new Vector3(pos.x, targetDeadZoneTopY - dzHalfHeight, pos.z);
        }
        else
        {
            bottomDeadZoneTr.position = new Vector3(pos.x, defaultDeadZoneY, pos.z);
        }

        Physics2D.SyncTransforms();
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

        UpdateDeadZoneForControlMode();
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

        // 1. HAREKET (Yön Tuşları, Joystick veya Klavye)
        float keyboardInput = Input.GetAxisRaw("Horizontal");
        float moveInput = Mathf.Abs(mobileMoveInput) > 0.01f ? mobileMoveInput : keyboardInput;

        float currentMoveSpeed = moveSpeed * Mathf.Sqrt(speedRatio);

        // 4. ZEMİN KONTROLÜ & KAYAN PLATFORM TAKİBİ
        bool superFeetActive = GameManager.Instance != null && GameManager.Instance.IsSuperFeetActive;

        Collider2D groundHit = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);
        if (groundHit != null && superFeetActive && playerCollider != null)
        {
            // Tek yönlü platformun altından yukarı geçerken zemine basmış sayma
            if (rb.linearVelocity.y > 0.15f || playerCollider.bounds.min.y < groundHit.bounds.center.y)
            {
                groundHit = null;
            }
        }
        yerdeMi = groundHit != null;
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
            visualStretch = new Vector2(1.14f, 0.88f);
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

    void LateUpdate()
    {
        if (loadedCornerCharId != CharacterShopData.GetSelectedCharacterId())
        {
            LoadCornerHangSprites();
        }
        if (playerSpriteRenderer == null || cornerHangFrames == null || cornerHangFrames.Length == 0)
            return;

        bool rocketActive = GameManager.Instance != null && GameManager.Instance.IsRocketBoosting;
        bool inCornerPose = false;
        bool onRightSide = false;

        if (!rocketActive && playerCollider != null)
        {
            Bounds pb = playerCollider.bounds;
            Collider2D platCol = Physics2D.OverlapBox(
                pb.center,
                new Vector2(pb.size.x + 0.24f, pb.size.y + 0.04f),
                0f,
                zeminKatmani);

            if (platCol == null && ayakNoktasi != null)
            {
                platCol = Physics2D.OverlapCircle(ayakNoktasi.position, ayakCapi, zeminKatmani);
            }

            if (platCol != null)
            {
                Bounds gb = platCol.bounds;
                float px = transform.position.x;
                float py = transform.position.y;

                // Platformun ÜSTÜNDEYKEN (köşesinde bile olsa) GİRMESİN:
                // Karakterin alt ucu (pb.min.y) platformun üst yüzeyinden (gb.max.y) aşağıda olmalı
                // ve karakter yatayda tamamen platformun sağ veya sol yanına çıkmış olmalı.
                bool notOnTopOfPlatform = pb.min.y < (gb.max.y - 0.05f);
                bool sideVerticalMatch = notOnTopOfPlatform && (py >= gb.min.y - 0.16f) && (py <= gb.max.y + 0.12f);

                if (sideVerticalMatch)
                {
                    if (px >= gb.max.x + 0.04f)
                    {
                        inCornerPose = true;
                        onRightSide = true;
                    }
                    else if (px <= gb.min.x - 0.04f)
                    {
                        inCornerPose = true;
                        onRightSide = false;
                    }
                }
            }
        }

        if (inCornerPose)
        {
            if (!IsCornerSprite(playerSpriteRenderer.sprite) && playerSpriteRenderer.sprite != null)
            {
                lastNormalSprite = playerSpriteRenderer.sprite;
            }
            wasInCornerPose = true;

            cornerAnimTimer += Time.deltaTime * 7f;
            int frameIdx = Mathf.FloorToInt(cornerAnimTimer) % cornerHangFrames.Length;
            playerSpriteRenderer.sprite = cornerHangFrames[frameIdx];

            // transform.localScale.x veya mekaniğe dokunmadan sadece sprite yönünü platforma çevir
            bool scaleIsNegative = transform.localScale.x < 0f;
            playerSpriteRenderer.flipX = onRightSide ? scaleIsNegative : !scaleIsNegative;
        }
        else if (wasInCornerPose)
        {
            wasInCornerPose = false;
            playerSpriteRenderer.flipX = false;
            if (IsCornerSprite(playerSpriteRenderer.sprite) && lastNormalSprite != null)
            {
                playerSpriteRenderer.sprite = lastNormalSprite;
            }
        }
    }

    private bool IsCornerSprite(Sprite s)
    {
        if (s == null || cornerHangFrames == null) return false;
        for (int i = 0; i < cornerHangFrames.Length; i++)
        {
            if (cornerHangFrames[i] == s) return true;
        }
        return false;
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
