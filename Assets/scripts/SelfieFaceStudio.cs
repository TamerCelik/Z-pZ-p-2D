using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Mağazadaki 750 Kirazla açılan "SELFIE YUZU" bölümü ve tam ekran Selfie Stüdyosu paneli.
/// Android ön kamera + PC webcam desteğiyle oyuncunun yüzünü oval kesip seçili karakterin
/// Koca Kafa (Big Head) portresine kalıcı olarak entegre eder.
/// </summary>
public class SelfieFaceStudio : MonoBehaviour
{
    private ShopMenuUI shopMenuUI;
    private Transform shopPanelTransform;
    private CharacterShopConfig config;
    private TMP_FontAsset uiFont;
    private Material uiFontMat;

    private GameObject shopBannerObj;
    private Image bannerIconImage;
    private TextMeshProUGUI bannerSubtitleText;
    private Image bannerActionBtnImage;
    private TextMeshProUGUI bannerActionBtnLabel;

    private GameObject studioPanelObj;
    private RawImage cameraFeedRawImage;
    private RectTransform cameraFeedRect;
    private Image ovalGuideImage;
    private GameObject previewStageObj;
    private Image previewCharBodyImage;
    private Image previewBigHeadImage;
    private TextMeshProUGUI studioStatusText;

    private Button captureButton;
    private Button saveButton;
    private Button retakeButton;
    private Button clearFaceButton;
    private Button closeStudioButton;

    private WebCamTexture webCamTexture;
    private bool activeCamIsFrontFacing = true;
    private Texture2D pendingCapturedTexture;
    private Sprite pendingCapturedSprite;
    private bool isInPreviewMode = false;
    private float charAnimTimer = 0f;
    private int charAnimFrame = 0;

    public bool IsStudioOpen => studioPanelObj != null && studioPanelObj.activeInHierarchy;

    private static readonly Color UnlockedAccent = new Color(0.78f, 0.30f, 0.88f, 1f);
    private static readonly Color BuyableAccent = new Color(0.95f, 0.62f, 0.08f, 1f);
    private static readonly Color LockedAccent = new Color(0.55f, 0.58f, 0.66f, 1f);
    private static readonly Color ActiveGreen = new Color(0.13f, 0.77f, 0.42f, 1f);
    private static readonly Color DangerCoral = new Color(0.90f, 0.32f, 0.34f, 1f);
    private static readonly Color SoftCardCream = new Color(1.00f, 0.96f, 0.99f, 0.98f);

    public void Initialize(ShopMenuUI owner, GameObject shopPanel, TMP_FontAsset font, Material fontMat)
    {
        shopMenuUI = owner;
        shopPanelTransform = shopPanel != null ? shopPanel.transform : null;
        uiFont = font;
        uiFontMat = fontMat;
        config = CharacterShopConfig.Load();

        if (shopPanelTransform == null) return;

        AdjustShopLayoutForSelfieBanner();
        BuildShopSelfieBanner();
        BuildStudioModalPanel();
        RefreshBannerUI();
    }

    private void OnEnable()
    {
        CharacterShopData.OnShopStateChanged += RefreshBannerUI;
    }

    private void OnDisable()
    {
        CharacterShopData.OnShopStateChanged -= RefreshBannerUI;
        StopCamera();
    }

    private void OnDestroy()
    {
        StopCamera();
        ClearPendingCapture();
    }

    private void Update()
    {
        if (studioPanelObj == null || !studioPanelObj.activeInHierarchy)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseStudio();
            return;
        }

        if (!isInPreviewMode && webCamTexture != null && webCamTexture.isPlaying && webCamTexture.width > 16)
        {
            UpdateCameraFeedOrientation();
        }
        else if (isInPreviewMode)
        {
            UpdatePreviewCharacterAnimation();
        }
    }

    private void AdjustShopLayoutForSelfieBanner()
    {
        Transform statusTr = shopPanelTransform.Find("ShopStatusText");
        if (statusTr is RectTransform statusRt)
        {
            statusRt.anchoredPosition = new Vector2(0f, 158f);
        }

        Transform closeTr = shopPanelTransform.Find("Btn_ShopKapat");
        if (closeTr is RectTransform closeRt)
        {
            closeRt.anchoredPosition = new Vector2(0f, -264f);
        }
    }

    private void BuildShopSelfieBanner()
    {
        if (shopBannerObj != null) return;

        shopBannerObj = new GameObject("Card_SelfieStudio", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shopBannerObj.transform.SetParent(shopPanelTransform, false);

        RectTransform rt = shopBannerObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -198f);
        rt.sizeDelta = new Vector2(304f, 56f);

        Image bg = shopBannerObj.GetComponent<Image>();
        bg.color = SoftCardCream;

        Outline border = shopBannerObj.AddComponent<Outline>();
        border.effectColor = new Color(0.84f, 0.42f, 0.72f, 1f);
        border.effectDistance = new Vector2(2f, -2f);

        GameObject iconObj = new GameObject("SelfieIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObj.transform.SetParent(shopBannerObj.transform, false);
        RectTransform iconRt = iconObj.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = new Vector2(28f, 0f);
        iconRt.sizeDelta = new Vector2(44f, 44f);
        bannerIconImage = iconObj.GetComponent<Image>();
        bannerIconImage.preserveAspect = true;

        CreateLabel(shopBannerObj.transform, "SelfieTitle", "SELFIE YUZU", 15f,
            new Vector2(-12f, 10f), new Vector2(150f, 22f), new Color(0.36f, 0.16f, 0.34f, 1f), TextAlignmentOptions.Left);

        bannerSubtitleText = CreateLabel(shopBannerObj.transform, "SelfieSub", "KENDI YUZUNU EKLE", 10.5f,
            new Vector2(-12f, -11f), new Vector2(150f, 20f), new Color(0.54f, 0.35f, 0.48f, 1f), TextAlignmentOptions.Left);

        Button btn = CreateButton(shopBannerObj.transform, "SelfieActionButton", CharacterShopData.SelfieStudioPrice + " COIN",
            new Vector2(96f, 0f), new Vector2(96f, 36f), BuyableAccent, 13f, out bannerActionBtnImage, out bannerActionBtnLabel);
        btn.onClick.AddListener(OnShopSelfieBannerClicked);
    }

    private void BuildStudioModalPanel()
    {
        if (studioPanelObj != null) return;

        studioPanelObj = new GameObject("SelfieStudioPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        studioPanelObj.transform.SetParent(shopPanelTransform, false);

        RectTransform panelRt = studioPanelObj.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        Image dimBg = studioPanelObj.GetComponent<Image>();
        dimBg.color = new Color(0.11f, 0.08f, 0.19f, 0.94f);

        GameObject cardObj = new GameObject("StudioCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cardObj.transform.SetParent(studioPanelObj.transform, false);
        RectTransform cardRt = cardObj.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(326f, 520f);

        Image cardImg = cardObj.GetComponent<Image>();
        cardImg.color = new Color(0.99f, 0.95f, 0.98f, 1f);
        Outline cardOutline = cardObj.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.42f, 0.20f, 0.38f, 1f);
        cardOutline.effectDistance = new Vector2(3f, -3f);

        CreateLabel(cardObj.transform, "StudioTitle", "SELFIE STUDIOSU", 22f,
            new Vector2(0f, 224f), new Vector2(290f, 36f), new Color(0.32f, 0.14f, 0.34f, 1f), TextAlignmentOptions.Center);

        studioStatusText = CreateLabel(cardObj.transform, "StudioStatus", "YUZUNU CERCEVEYE ORTALA", 12f,
            new Vector2(0f, 192f), new Vector2(300f, 26f), new Color(0.48f, 0.28f, 0.44f, 1f), TextAlignmentOptions.Center);

        GameObject viewportObj = new GameObject("CameraViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewportObj.transform.SetParent(cardObj.transform, false);
        RectTransform vpRt = viewportObj.GetComponent<RectTransform>();
        vpRt.anchorMin = new Vector2(0.5f, 0.5f);
        vpRt.anchorMax = new Vector2(0.5f, 0.5f);
        vpRt.anchoredPosition = new Vector2(0f, 52f);
        vpRt.sizeDelta = new Vector2(236f, 236f);
        viewportObj.GetComponent<Image>().color = new Color(0.14f, 0.12f, 0.22f, 1f);

        Outline vpOutline = viewportObj.AddComponent<Outline>();
        vpOutline.effectColor = new Color(0.86f, 0.44f, 0.72f, 1f);
        vpOutline.effectDistance = new Vector2(2.5f, -2.5f);

        GameObject feedObj = new GameObject("CameraFeed", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        feedObj.transform.SetParent(viewportObj.transform, false);
        cameraFeedRect = feedObj.GetComponent<RectTransform>();
        cameraFeedRect.anchorMin = new Vector2(0.5f, 0.5f);
        cameraFeedRect.anchorMax = new Vector2(0.5f, 0.5f);
        cameraFeedRect.anchoredPosition = Vector2.zero;
        cameraFeedRect.sizeDelta = new Vector2(236f, 236f);
        cameraFeedRawImage = feedObj.GetComponent<RawImage>();

        GameObject guideObj = new GameObject("OvalGuide", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        guideObj.transform.SetParent(viewportObj.transform, false);
        RectTransform guideRt = guideObj.GetComponent<RectTransform>();
        guideRt.anchorMin = Vector2.zero;
        guideRt.anchorMax = Vector2.one;
        guideRt.offsetMin = Vector2.zero;
        guideRt.offsetMax = Vector2.zero;
        ovalGuideImage = guideObj.GetComponent<Image>();
        ovalGuideImage.sprite = CustomFaceHelper.CreateOvalGuideSprite();
        ovalGuideImage.raycastTarget = false;

        previewStageObj = new GameObject("BigHeadPreviewStage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        previewStageObj.transform.SetParent(viewportObj.transform, false);
        RectTransform stageRt = previewStageObj.GetComponent<RectTransform>();
        stageRt.anchorMin = Vector2.zero;
        stageRt.anchorMax = Vector2.one;
        stageRt.offsetMin = Vector2.zero;
        stageRt.offsetMax = Vector2.zero;
        previewStageObj.GetComponent<Image>().color = new Color(0.55f, 0.82f, 0.98f, 1f);

        GameObject charBodyObj = new GameObject("PreviewCharBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        charBodyObj.transform.SetParent(previewStageObj.transform, false);
        RectTransform cbRt = charBodyObj.GetComponent<RectTransform>();
        cbRt.anchorMin = new Vector2(0.5f, 0.5f);
        cbRt.anchorMax = new Vector2(0.5f, 0.5f);
        cbRt.anchoredPosition = new Vector2(0f, -26f);
        cbRt.sizeDelta = new Vector2(126f, 126f);
        previewCharBodyImage = charBodyObj.GetComponent<Image>();
        previewCharBodyImage.preserveAspect = true;

        GameObject headObj = new GameObject("PreviewBigHead", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        headObj.transform.SetParent(previewStageObj.transform, false);
        RectTransform headRt = headObj.GetComponent<RectTransform>();
        headRt.anchorMin = new Vector2(0.5f, 0.5f);
        headRt.anchorMax = new Vector2(0.5f, 0.5f);
        headRt.anchoredPosition = new Vector2(0f, 16f);
        headRt.sizeDelta = new Vector2(88f, 88f);
        previewBigHeadImage = headObj.GetComponent<Image>();
        previewBigHeadImage.preserveAspect = true;

        previewStageObj.SetActive(false);

        captureButton = CreateButton(cardObj.transform, "Btn_Capture", "SELFIE CEK",
            new Vector2(0f, -104f), new Vector2(236f, 44f), ActiveGreen, 17f, out _, out _);
        captureButton.onClick.AddListener(OnCaptureClicked);

        saveButton = CreateButton(cardObj.transform, "Btn_Save", "KAYDET VE KULLAN",
            new Vector2(0f, -104f), new Vector2(236f, 44f), ActiveGreen, 15f, out _, out _);
        saveButton.onClick.AddListener(OnSaveAndUseClicked);

        retakeButton = CreateButton(cardObj.transform, "Btn_Retake", "TEKRAR CEK",
            new Vector2(0f, -156f), new Vector2(236f, 38f), BuyableAccent, 14f, out _, out _);
        retakeButton.onClick.AddListener(OnRetakeClicked);

        clearFaceButton = CreateButton(cardObj.transform, "Btn_ClearFace", "ORIJINAL YUZE DON",
            new Vector2(0f, -156f), new Vector2(236f, 38f), DangerCoral, 13f, out _, out _);
        clearFaceButton.onClick.AddListener(OnClearFaceClicked);

        closeStudioButton = CreateButton(cardObj.transform, "Btn_CloseStudio", "KAPAT",
            new Vector2(0f, -214f), new Vector2(180f, 40f), UnlockedAccent, 15f, out _, out _);
        closeStudioButton.onClick.AddListener(CloseStudio);

        studioPanelObj.SetActive(false);
    }

    public void RefreshBannerUI()
    {
        if (shopBannerObj == null) return;

        bool unlocked = CharacterShopData.IsSelfieStudioUnlocked();
        bool faceActive = CharacterShopData.IsCustomFaceEnabled();
        int coins = CharacterShopData.GetCoins();
        bool affordable = coins >= CharacterShopData.SelfieStudioPrice;

        if (bannerIconImage != null)
        {
            Sprite savedFace = faceActive ? CharacterShopData.LoadCustomFaceSprite() : null;
            bannerIconImage.sprite = savedFace != null ? savedFace : CustomFaceHelper.CreateCameraBadgeSprite();
            bannerIconImage.color = Color.white;
        }

        if (bannerSubtitleText != null)
        {
            if (!unlocked) bannerSubtitleText.text = "KENDI YUZUNU EKLE";
            else if (faceActive) bannerSubtitleText.text = "KOCA KAFA AKTIF!";
            else bannerSubtitleText.text = "ACIK - SELFIE CEK";
        }

        if (bannerActionBtnImage != null)
        {
            bannerActionBtnImage.color = unlocked
                ? (faceActive ? ActiveGreen : UnlockedAccent)
                : (affordable ? BuyableAccent : LockedAccent);
        }

        if (bannerActionBtnLabel != null)
        {
            if (!unlocked)
            {
                bannerActionBtnLabel.text = affordable
                    ? CharacterShopData.SelfieStudioPrice + " AL"
                    : CharacterShopData.SelfieStudioPrice + " COIN";
            }
            else
            {
                bannerActionBtnLabel.text = faceActive ? "DUZENLE" : "STUDYO";
            }
        }
    }

    private void OnShopSelfieBannerClicked()
    {
        if (!CharacterShopData.IsSelfieStudioUnlocked())
        {
            bool ok = CharacterShopData.TryUnlockSelfieStudio(out bool newlyUnlocked, out bool insufficientFunds, out _);
            if (!ok && insufficientFunds)
            {
                if (shopMenuUI != null)
                    shopMenuUI.ShowExternalStatus(CharacterShopData.SelfieStudioPrice + " COIN GEREKLI!", new Color(0.86f, 0.22f, 0.28f, 1f), 0.66f);
                return;
            }

            if (newlyUnlocked && shopMenuUI != null)
            {
                shopMenuUI.ShowExternalStatus("SELFIE STUDIOSU ACILDI!", new Color(0.10f, 0.64f, 0.32f, 1f), 1.42f);
            }

            RefreshBannerUI();
        }

        OpenStudio();
    }

    public void OpenStudio()
    {
        if (studioPanelObj == null) return;
        studioPanelObj.SetActive(true);
        isInPreviewMode = false;
        ClearPendingCapture();
        SetStudioModeUI(previewMode: false);
        StartCoroutine(RequestPermissionAndStartCamera());
    }

    public void CloseStudio()
    {
        StopCamera();
        ClearPendingCapture();
        if (studioPanelObj != null) studioPanelObj.SetActive(false);
        RefreshBannerUI();
    }

    private IEnumerator RequestPermissionAndStartCamera()
    {
        if (studioStatusText != null) studioStatusText.text = "KAMERA ACILIYOR...";

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            float wait = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && wait < 8f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                if (studioStatusText != null) studioStatusText.text = "KAMERA IZNI VERILMEDI!";
                if (captureButton != null) captureButton.interactable = false;
                yield break;
            }
        }
#endif

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices == null || devices.Length == 0)
        {
            if (studioStatusText != null) studioStatusText.text = "CIHAZDA KAMERA BULUNAMADI!";
            if (captureButton != null) captureButton.interactable = false;
            yield break;
        }

        WebCamDevice chosen = devices[0];
        activeCamIsFrontFacing = devices[0].isFrontFacing;
        for (int i = 0; i < devices.Length; i++)
        {
            if (devices[i].isFrontFacing)
            {
                chosen = devices[i];
                activeCamIsFrontFacing = true;
                break;
            }
        }

        StopCamera();
        webCamTexture = new WebCamTexture(chosen.name, 640, 640, 30);
        if (cameraFeedRawImage != null)
        {
            cameraFeedRawImage.texture = webCamTexture;
            cameraFeedRawImage.color = Color.white;
        }
        webCamTexture.Play();

        float startWait = 0f;
        while (webCamTexture != null && webCamTexture.width <= 16 && startWait < 3f)
        {
            startWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (webCamTexture == null || webCamTexture.width <= 16)
        {
            if (studioStatusText != null) studioStatusText.text = "KAMERA BASLATILAMADI!";
            if (captureButton != null) captureButton.interactable = false;
            yield break;
        }

        UpdateCameraFeedOrientation();
        if (captureButton != null) captureButton.interactable = true;
        if (studioStatusText != null) studioStatusText.text = "YUZUNU OVAL CERCEVEYE ORTALA";
    }

    private void UpdateCameraFeedOrientation()
    {
        if (webCamTexture == null || cameraFeedRect == null || cameraFeedRawImage == null) return;

        int rotAngle = ((webCamTexture.videoRotationAngle % 360) + 360) % 360;
        cameraFeedRect.localEulerAngles = new Vector3(0f, 0f, -rotAngle);

        // 90 veya 270 derece döndürülmüş mobil kamera sensörlerinde ekranın yatay (selfie ayna) ekseni local Y,
        // dikey ekseni ise local X olur. Dönüş öncesi scaleX = -1 vermek görüntüyü telefonda baş aşağı (ters) çevirir!
        bool isQuarterTurn = (rotAngle == 90 || rotAngle == 270);
        float scaleX = 1f;
        float scaleY = webCamTexture.videoVerticallyMirrored ? -1f : 1f;
        if (activeCamIsFrontFacing)
        {
            if (isQuarterTurn)
                scaleY = -scaleY;
            else
                scaleX = -scaleX;
        }
        cameraFeedRect.localScale = new Vector3(scaleX, scaleY, 1f);

        float aspect = (float)webCamTexture.width / Mathf.Max(1, webCamTexture.height);
        const float box = 236f;
        if (aspect >= 1f)
            cameraFeedRect.sizeDelta = new Vector2(box * aspect, box);
        else
            cameraFeedRect.sizeDelta = new Vector2(box, box / Mathf.Max(0.1f, aspect));
    }

    private void OnCaptureClicked()
    {
        if (webCamTexture == null || !webCamTexture.isPlaying || webCamTexture.width <= 16)
            return;

        ClearPendingCapture();
        pendingCapturedTexture = CustomFaceHelper.CaptureOvalSelfie(webCamTexture, activeCamIsFrontFacing);
        if (pendingCapturedTexture == null) return;

        pendingCapturedSprite = Sprite.Create(
            pendingCapturedTexture,
            new Rect(0, 0, pendingCapturedTexture.width, pendingCapturedTexture.height),
            new Vector2(0.5f, 0.5f),
            100f);

        StopCamera();
        isInPreviewMode = true;

        if (previewBigHeadImage != null)
        {
            previewBigHeadImage.sprite = pendingCapturedSprite;
        }

        UpdatePreviewCharacterAnimation();
        SetStudioModeUI(previewMode: true);
        if (studioStatusText != null) studioStatusText.text = "HARIKA! KOCA KAFA ONIZLEMESI";
    }

    private void OnSaveAndUseClicked()
    {
        if (pendingCapturedTexture == null) return;

        byte[] png = pendingCapturedTexture.EncodeToPNG();
        CharacterShopData.SaveCustomFacePng(png);

        if (shopMenuUI != null)
        {
            shopMenuUI.ShowExternalStatus("SELFIE YUZU KAYDEDILDI!", ActiveGreen, 1.38f);
            shopMenuUI.RefreshUI();
        }

        CloseStudio();
    }

    private void OnRetakeClicked()
    {
        ClearPendingCapture();
        isInPreviewMode = false;
        SetStudioModeUI(previewMode: false);
        StartCoroutine(RequestPermissionAndStartCamera());
    }

    private void OnClearFaceClicked()
    {
        CharacterShopData.SetCustomFaceEnabled(false);
        if (shopMenuUI != null)
        {
            shopMenuUI.ShowExternalStatus("ORIJINAL KARAKTERE DONULDU", new Color(0.14f, 0.50f, 0.85f, 1f), 1.0f);
            shopMenuUI.RefreshUI();
        }
        CloseStudio();
    }

    private void SetStudioModeUI(bool previewMode)
    {
        if (cameraFeedRawImage != null) cameraFeedRawImage.gameObject.SetActive(!previewMode);
        if (ovalGuideImage != null) ovalGuideImage.gameObject.SetActive(!previewMode);
        if (previewStageObj != null) previewStageObj.SetActive(previewMode);

        if (captureButton != null) captureButton.gameObject.SetActive(!previewMode);
        if (saveButton != null) saveButton.gameObject.SetActive(previewMode);
        if (retakeButton != null) retakeButton.gameObject.SetActive(previewMode);

        bool canClear = !previewMode && CharacterShopData.IsCustomFaceEnabled();
        if (clearFaceButton != null) clearFaceButton.gameObject.SetActive(canClear);
    }

    private void UpdatePreviewCharacterAnimation()
    {
        if (previewCharBodyImage == null) return;
        if (config == null) config = CharacterShopConfig.Load();
        if (config == null) return;

        string selectedId = CharacterShopData.GetSelectedCharacterId();
        var visual = config.GetVisualEntry(selectedId);
        Sprite[] frames = visual != null ? visual.idleFrames : null;
        if (frames == null || frames.Length == 0) return;

        charAnimTimer += Time.unscaledDeltaTime * 10f;
        if (charAnimTimer >= 1f)
        {
            charAnimFrame = (charAnimFrame + 1) % frames.Length;
            charAnimTimer -= 1f;
        }
        previewCharBodyImage.sprite = frames[charAnimFrame];

        if (previewBigHeadImage != null)
        {
            float bob = Mathf.Sin(Time.unscaledTime * 6f) * 3f;
            previewBigHeadImage.rectTransform.anchoredPosition = new Vector2(0f, 16f + bob);
        }
    }

    private void StopCamera()
    {
        if (webCamTexture != null)
        {
            if (webCamTexture.isPlaying) webCamTexture.Stop();
            Destroy(webCamTexture);
            webCamTexture = null;
        }
        if (cameraFeedRawImage != null) cameraFeedRawImage.texture = null;
    }

    private void ClearPendingCapture()
    {
        if (pendingCapturedSprite != null)
        {
            Destroy(pendingCapturedSprite);
            pendingCapturedSprite = null;
        }
        if (pendingCapturedTexture != null)
        {
            Destroy(pendingCapturedTexture);
            pendingCapturedTexture = null;
        }
    }

    #region UI Builder Helpers
    private TextMeshProUGUI CreateLabel(
        Transform parent, string name, string text, float fontSize,
        Vector2 pos, Vector2 size, Color color, TextAlignmentOptions align)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (uiFont != null) tmp.font = uiFont;
        if (uiFontMat != null) tmp.fontSharedMaterial = uiFontMat;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private Button CreateButton(
        Transform parent, string name, string text,
        Vector2 pos, Vector2 size, Color bgColor, float fontSize,
        out Image btnImage, out TextMeshProUGUI btnLabel)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        btnImage = obj.GetComponent<Image>();
        btnImage.color = bgColor;

        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0.20f, 0.12f, 0.22f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = btnImage;

        GameObject lblObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lblObj.transform.SetParent(obj.transform, false);
        RectTransform lrt = lblObj.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(4f, 2f);
        lrt.offsetMax = new Vector2(-4f, -2f);

        btnLabel = lblObj.GetComponent<TextMeshProUGUI>();
        if (uiFont != null) btnLabel.font = uiFont;
        if (uiFontMat != null) btnLabel.fontSharedMaterial = uiFontMat;
        btnLabel.text = text;
        btnLabel.fontSize = fontSize;
        btnLabel.color = Color.white;
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.enableAutoSizing = true;
        btnLabel.fontSizeMin = 10f;
        btnLabel.fontSizeMax = fontSize;
        btnLabel.raycastTarget = false;

        return btn;
    }
    #endregion
}
