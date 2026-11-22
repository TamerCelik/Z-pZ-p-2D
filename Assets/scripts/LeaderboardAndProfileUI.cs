using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ana Menü (MenuScene) ve Oyun Sonu (SampleScene - GameOverPanel) ekranlarına:
/// - Ana Menü sağ üste: Seçili karakter avatarlı "Profil / Kullanıcı Adı" sekmesi
/// - Ana Menü sol üste & Game Over ekranına: Pixel-art Kupalı "Skor Tablosu" butonu
/// - Açılır "Oyuncu Profili (İsim Seç / Değiştir)" ve "Global Skor Tablosu (Top 50 + Senin Sıran)" pencerelerini kurar.
/// </summary>
public class LeaderboardAndProfileUI : MonoBehaviour
{
    private Canvas rootCanvas;
    private CharacterShopConfig shopConfig;
    private TMP_FontAsset arcadeFont;
    private Material arcadeFontMaterial;

    private Sprite roundedCardSprite;
    private Sprite roundedPillSprite;
    private Sprite btnGoldSprite;
    private Sprite btnPinkSprite;
    private Sprite btnCyanSprite;
    private Sprite btnRedSprite;
    private Sprite btnGreenSprite;
    private Sprite trophyIconSprite;
    private Sprite profileIconSprite;
    private Sprite leaderboardBtnBgSprite;

    // Ana menü sağ üst profil rozeti bileşenleri
    private Image topProfileAvatarImg;
    private Image topProfileSelfieOverlayImg;
    private TextMeshProUGUI topProfileNameText;
    private TextMeshProUGUI topProfileSubText;

    // Ana menü sol üst kupa rozeti bileşenleri
    private TextMeshProUGUI topTrophySubText;

    // Profil Penceresi (Modal)
    private GameObject profileModalObj;
    private Image profileModalAvatarImg;
    private Image profileModalSelfieOverlayImg;
    private TextMeshProUGUI profileModalCurrentNameText;
    private TextMeshProUGUI profileModalBadgeText;
    private Image profileModalBadgeBg;
    private TextMeshProUGUI profileModalScoreText;
    private TextMeshProUGUI profileModalRankText;
    private TMP_InputField usernameInputField;
    private Button saveUsernameBtn;
    private TextMeshProUGUI saveUsernameBtnText;
    private TextMeshProUGUI profileFeedbackText;

    // Skor Tablosu Penceresi (Modal)
    private GameObject leaderboardModalObj;
    private RectTransform leaderboardListContent;
    private TextMeshProUGUI leaderboardStatusText;
    private Image myRankAvatarImg;
    private TextMeshProUGUI myRankRowText;
    private TextMeshProUGUI myRankScoreText;
    private readonly List<GameObject> spawnedRowObjects = new List<GameObject>();

    public static void AttachToCurrentScene()
    {
        LeaderboardAndProfileUI existing = Object.FindAnyObjectByType<LeaderboardAndProfileUI>();
        if (existing != null)
        {
            existing.RefreshAllUI();
            return;
        }

        Canvas targetCanvas = FindPrimaryCanvas();
        if (targetCanvas == null) return;

        GameObject host = new GameObject("LeaderboardAndProfileUI", typeof(RectTransform));
        host.transform.SetParent(targetCanvas.transform, false);
        host.transform.localScale = Vector3.one;
        RectTransform rt = host.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        LeaderboardAndProfileUI ui = host.AddComponent<LeaderboardAndProfileUI>();
        ui.Initialize(targetCanvas);
    }

    private static Canvas FindPrimaryCanvas()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas best = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null || !canvases[i].isRootCanvas) continue;
            if (canvases[i].name == "Canvas") return canvases[i];
            if (best == null) best = canvases[i];
        }
        return best;
    }

    private void Initialize(Canvas canvas)
    {
        rootCanvas = canvas;
        shopConfig = CharacterShopConfig.Load();
        arcadeFont = ResolveArcadeFont();

        btnGoldSprite = Resources.Load<Sprite>("UI/menu_btn_gold");
        btnPinkSprite = Resources.Load<Sprite>("UI/menu_btn_pink");
        btnCyanSprite = Resources.Load<Sprite>("UI/menu_btn_cyan");
        btnRedSprite = Resources.Load<Sprite>("UI/menu_btn_red");
        btnGreenSprite = Resources.Load<Sprite>("UI/menu_btn_green");
        roundedCardSprite = Resources.Load<Sprite>("UI/menu_card_pixel");
        if (roundedCardSprite == null) roundedCardSprite = CreateRoundedSprite(64, 64, 16, true);
        roundedPillSprite = btnGoldSprite != null ? btnGoldSprite : CreateRoundedSprite(64, 64, 18, false);
        trophyIconSprite = Resources.Load<Sprite>("UI/icon_trophy_pixel");
        profileIconSprite = Resources.Load<Sprite>("UI/icon_profile_pixel");
        leaderboardBtnBgSprite = Resources.Load<Sprite>("UI/gameover_btn_leaderboard");

        BuildLeaderboardModal();
        BuildProfileModal();

        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == "MenuScene")
        {
            BuildMainMenuTopBar();
        }
        else
        {
            AttachGameOverLeaderboardButton();
        }

        LeaderboardService svc = LeaderboardService.EnsureInstance();
        svc.OnProfileUpdated += RefreshAllUI;
        svc.OnLeaderboardUpdated += PopulateLeaderboardRows;

        CharacterShopData.OnShopStateChanged += RefreshAllUI;
        RefreshAllUI();
    }

    private void OnDestroy()
    {
        if (LeaderboardService.Instance != null)
        {
            LeaderboardService.Instance.OnProfileUpdated -= RefreshAllUI;
            LeaderboardService.Instance.OnLeaderboardUpdated -= PopulateLeaderboardRows;
        }
        CharacterShopData.OnShopStateChanged -= RefreshAllUI;
    }

    private TMP_FontAsset ResolveArcadeFont()
    {
        string[] mainBtnNames = { "PlayButton", "Kostumler Button", "Ayarlar Button", "cikis", "RestartButton", "FinalScoreText" };
        for (int i = 0; i < mainBtnNames.Length; i++)
        {
            GameObject go = GameObject.Find(mainBtnNames[i]);
            if (go != null)
            {
                TextMeshProUGUI tmp = go.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null && tmp.font != null)
                {
                    arcadeFontMaterial = tmp.fontSharedMaterial;
                    return tmp.font;
                }
            }
        }

        TextMeshProUGUI[] texts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].font != null)
            {
                if (arcadeFontMaterial == null) arcadeFontMaterial = texts[i].fontSharedMaterial;
                return texts[i].font;
            }
        }
        if (shopConfig != null && shopConfig.uiFont != null)
            return shopConfig.uiFont;
        return TMP_Settings.defaultFontAsset;
    }

    // =========================================================================
    // 1. ANA MENÜ ÜST BAR: SOL ÜST SKOR TABLOSU & SAĞ ÜST PROFİL SEKMESİ
    // =========================================================================
    private void BuildMainMenuTopBar()
    {
        // Sol Üst: Skor Tablosu (Kupa) Butonu (Altın Piksel Buton)
        GameObject trophyBtnObj = CreateUIObject("TopLeft_LeaderboardButton", transform);
        SetAnchors(trophyBtnObj, new Vector2(0.032f, 0.908f), new Vector2(0.455f, 0.980f));

        Image trophyBg = trophyBtnObj.AddComponent<Image>();
        trophyBg.sprite = btnGoldSprite != null ? btnGoldSprite : roundedCardSprite;
        trophyBg.type = Image.Type.Sliced;
        trophyBg.color = Color.white;

        Button trophyBtn = trophyBtnObj.AddComponent<Button>();
        trophyBtn.onClick.AddListener(OpenLeaderboardModal);

        GameObject tIconObj = CreateUIObject("TrophyIcon", trophyBtnObj.transform);
        SetAnchors(tIconObj, new Vector2(0.05f, 0.16f), new Vector2(0.28f, 0.86f));
        Image tIconImg = tIconObj.AddComponent<Image>();
        tIconImg.sprite = trophyIconSprite != null ? trophyIconSprite : (shopConfig != null ? shopConfig.starIconSprite : roundedPillSprite);
        tIconImg.preserveAspect = true;

        CreateLabel(trophyBtnObj.transform, "Title", "SKOR TABLOSU",
            new Vector2(0.29f, 0.48f), new Vector2(0.95f, 0.88f), 10f, 16f, TextAlignmentOptions.Left, Color.white, false);

        topTrophySubText = CreateLabel(trophyBtnObj.transform, "SubRank", "SIRA: #-",
            new Vector2(0.29f, 0.14f), new Vector2(0.95f, 0.48f), 8.5f, 12.5f, TextAlignmentOptions.Left, Color.white, false);

        // Sağ Üst: Profil Sekmesi / Butonu (Turkuaz Piksel Buton)
        GameObject profBtnObj = CreateUIObject("TopRight_ProfileButton", transform);
        SetAnchors(profBtnObj, new Vector2(0.545f, 0.908f), new Vector2(0.968f, 0.980f));

        Image profBg = profBtnObj.AddComponent<Image>();
        profBg.sprite = btnCyanSprite != null ? btnCyanSprite : roundedCardSprite;
        profBg.type = Image.Type.Sliced;
        profBg.color = Color.white;

        Button profBtn = profBtnObj.AddComponent<Button>();
        profBtn.onClick.AddListener(OpenProfileModal);

        GameObject avFrame = CreateUIObject("AvatarFrame", profBtnObj.transform);
        SetAnchors(avFrame, new Vector2(0.05f, 0.13f), new Vector2(0.29f, 0.87f));
        Image avFrameBg = avFrame.AddComponent<Image>();
        avFrameBg.sprite = roundedPillSprite;
        avFrameBg.type = Image.Type.Sliced;
        avFrameBg.color = new Color(0.98f, 0.95f, 0.86f, 1f);

        GameObject avObj = CreateUIObject("AvatarSprite", avFrame.transform);
        SetAnchors(avObj, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f));
        topProfileAvatarImg = avObj.AddComponent<Image>();
        topProfileAvatarImg.preserveAspect = true;

        GameObject avSelfieObj = CreateUIObject("SelfieHead", avObj.transform);
        RectTransform avSelfieRt = avSelfieObj.GetComponent<RectTransform>();
        avSelfieRt.anchorMin = new Vector2(0.5f, 0.62f);
        avSelfieRt.anchorMax = new Vector2(0.5f, 0.62f);
        avSelfieRt.sizeDelta = new Vector2(20f, 20f);
        topProfileSelfieOverlayImg = avSelfieObj.AddComponent<Image>();
        topProfileSelfieOverlayImg.preserveAspect = true;
        avSelfieObj.SetActive(false);

        topProfileNameText = CreateLabel(profBtnObj.transform, "Username", "Guest",
            new Vector2(0.31f, 0.46f), new Vector2(0.95f, 0.88f), 10f, 17f, TextAlignmentOptions.Left, Color.white, true);

        topProfileSubText = CreateLabel(profBtnObj.transform, "StatusSub", "PROFİL • İSİM SEÇ",
            new Vector2(0.31f, 0.10f), new Vector2(0.95f, 0.46f), 8.5f, 12.5f, TextAlignmentOptions.Left, new Color(0.84f, 0.96f, 1f, 1f), true);
    }

    // =========================================================================
    // 2. GAME OVER EKRANINA SKOR TABLOSU BUTONU
    // =========================================================================
    public void AttachGameOverLeaderboardButton()
    {
        GameManager gm = GameManager.Instance != null ? GameManager.Instance : Object.FindFirstObjectByType<GameManager>();
        if (gm == null || gm.gameOverPanel == null) return;

        Transform restartTr = gm.gameOverPanel.transform.Find("RestartButton");
        if (restartTr != null) SetAnchors(restartTr.gameObject, new Vector2(0.102f, 0.426f), new Vector2(0.898f, 0.520f));

        Transform menuTr = gm.gameOverPanel.transform.Find("MenuButton");
        if (menuTr != null) SetAnchors(menuTr.gameObject, new Vector2(0.102f, 0.320f), new Vector2(0.898f, 0.414f));

        Transform contTr = gm.gameOverPanel.transform.Find("ContinueWithCoinsButton");
        if (contTr != null) SetAnchors(contTr.gameObject, new Vector2(0.102f, 0.214f), new Vector2(0.898f, 0.308f));

        Transform existing = gm.gameOverPanel.transform.Find("GameOverLeaderboardButton");
        if (existing != null)
        {
            SetAnchors(existing.gameObject, new Vector2(0.102f, 0.108f), new Vector2(0.898f, 0.202f));
            Button existingBtn = existing.GetComponent<Button>();
            if (existingBtn != null)
            {
                existingBtn.onClick.RemoveAllListeners();
                existingBtn.onClick.AddListener(OpenLeaderboardModal);
            }
            return;
        }

        GameObject btnObj = CreateUIObject("GameOverLeaderboardButton", gm.gameOverPanel.transform);
        SetAnchors(btnObj, new Vector2(0.102f, 0.108f), new Vector2(0.898f, 0.202f));

        Image bg = btnObj.AddComponent<Image>();
        if (leaderboardBtnBgSprite != null)
        {
            bg.sprite = leaderboardBtnBgSprite;
            bg.type = Image.Type.Simple;
            bg.color = Color.white;
        }
        else
        {
            bg.sprite = roundedCardSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.18f, 0.56f, 0.94f, 1f);
        }

        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(OpenLeaderboardModal);

        TextMeshProUGUI lbl = CreateLabel(btnObj.transform, "Label", "SKOR TABLOSU",
            new Vector2(0.23f, 0.14f), new Vector2(0.94f, 0.84f), 12f, 26f, TextAlignmentOptions.Center, Color.white, true);
        lbl.enableVertexGradient = true;
        lbl.colorGradient = new VertexGradient(
            Color.white,
            Color.white,
            new Color(0.82f, 0.84f, 0.88f, 1f),
            new Color(0.82f, 0.84f, 0.88f, 1f));
    }

    // =========================================================================
    // 3. OYUNCU PROFİLİ & BENZERSİZ KULLANICI ADI DEĞİŞTİRME PENCERESİ
    // =========================================================================
    private void BuildProfileModal()
    {
        profileModalObj = CreateUIObject("ProfileModalOverlay", transform);
        SetAnchors(profileModalObj, Vector2.zero, Vector2.one);

        Image dimBg = profileModalObj.AddComponent<Image>();
        dimBg.color = new Color(0.04f, 0.06f, 0.12f, 0.78f);

        GameObject card = CreateUIObject("ProfileCard", profileModalObj.transform);
        SetAnchors(card, new Vector2(0.065f, 0.12f), new Vector2(0.935f, 0.88f));
        Image cardImg = card.AddComponent<Image>();
        cardImg.sprite = roundedCardSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(0.99f, 0.95f, 0.86f, 1f);

        // Üst Başlık Şeridi
        GameObject header = CreateUIObject("HeaderBar", card.transform);
        SetAnchors(header, new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.965f));
        Image headerImg = header.AddComponent<Image>();
        headerImg.sprite = roundedPillSprite;
        headerImg.type = Image.Type.Sliced;
        headerImg.color = new Color(0.18f, 0.56f, 0.92f, 1f);

        CreateLabel(header.transform, "Title", "OYUNCU PROFİLİ",
            new Vector2(0.08f, 0.10f), new Vector2(0.84f, 0.90f), 14f, 24f, TextAlignmentOptions.Center, Color.white, true);

        CreateCloseButton(header.transform, () => profileModalObj.SetActive(false));

        // Karakter Önizleme & Durum Kutusu
        GameObject heroBox = CreateUIObject("HeroBox", card.transform);
        SetAnchors(heroBox, new Vector2(0.06f, 0.56f), new Vector2(0.94f, 0.83f));
        Image heroBg = heroBox.AddComponent<Image>();
        heroBg.sprite = roundedCardSprite;
        heroBg.type = Image.Type.Sliced;
        heroBg.color = new Color(0.94f, 0.89f, 0.77f, 1f);

        GameObject avBox = CreateUIObject("AvatarBox", heroBox.transform);
        SetAnchors(avBox, new Vector2(0.04f, 0.12f), new Vector2(0.32f, 0.88f));
        Image avBoxBg = avBox.AddComponent<Image>();
        avBoxBg.sprite = roundedCardSprite;
        avBoxBg.type = Image.Type.Sliced;
        avBoxBg.color = new Color(1f, 0.98f, 0.93f, 1f);

        GameObject avSpriteObj = CreateUIObject("CharacterImg", avBox.transform);
        SetAnchors(avSpriteObj, new Vector2(0.10f, 0.08f), new Vector2(0.90f, 0.92f));
        profileModalAvatarImg = avSpriteObj.AddComponent<Image>();
        profileModalAvatarImg.preserveAspect = true;

        GameObject avSelfie = CreateUIObject("SelfieHead", avSpriteObj.transform);
        RectTransform avSelfieRt = avSelfie.GetComponent<RectTransform>();
        avSelfieRt.anchorMin = new Vector2(0.5f, 0.62f);
        avSelfieRt.anchorMax = new Vector2(0.5f, 0.62f);
        avSelfieRt.sizeDelta = new Vector2(36f, 36f);
        profileModalSelfieOverlayImg = avSelfie.AddComponent<Image>();
        profileModalSelfieOverlayImg.preserveAspect = true;
        avSelfie.SetActive(false);

        profileModalCurrentNameText = CreateLabel(heroBox.transform, "CurrentName", "Guest_0000",
            new Vector2(0.36f, 0.62f), new Vector2(0.96f, 0.92f), 14f, 24f, TextAlignmentOptions.Left, new Color(0.18f, 0.14f, 0.10f, 1f), false);

        GameObject badgeObj = CreateUIObject("StatusBadge", heroBox.transform);
        SetAnchors(badgeObj, new Vector2(0.36f, 0.39f), new Vector2(0.86f, 0.59f));
        profileModalBadgeBg = badgeObj.AddComponent<Image>();
        profileModalBadgeBg.sprite = roundedPillSprite;
        profileModalBadgeBg.type = Image.Type.Sliced;
        profileModalBadgeBg.color = new Color(0.95f, 0.56f, 0.14f, 1f);

        profileModalBadgeText = CreateLabel(badgeObj.transform, "BadgeTxt", "MİSAFİR OYUNCU",
            new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f), 9f, 14f, TextAlignmentOptions.Center, Color.white, true);

        profileModalScoreText = CreateLabel(heroBox.transform, "BestScoreTxt", "REKOR: 0",
            new Vector2(0.36f, 0.08f), new Vector2(0.66f, 0.34f), 10f, 16f, TextAlignmentOptions.Left, new Color(0.20f, 0.46f, 0.82f, 1f), false);

        profileModalRankText = CreateLabel(heroBox.transform, "RankTxt", "SIRA: #-",
            new Vector2(0.67f, 0.08f), new Vector2(0.96f, 0.34f), 10f, 16f, TextAlignmentOptions.Left, new Color(0.84f, 0.48f, 0.06f, 1f), false);

        CreateLabel(card.transform, "InputLabel", "KULLANICI ADIN (BENZERSİZ):",
            new Vector2(0.07f, 0.465f), new Vector2(0.93f, 0.535f), 11f, 16f, TextAlignmentOptions.Left, new Color(0.24f, 0.18f, 0.12f, 1f), false);

        // TMP_InputField
        GameObject inputObj = CreateUIObject("UsernameInputField", card.transform);
        SetAnchors(inputObj, new Vector2(0.06f, 0.345f), new Vector2(0.94f, 0.455f));
        Image inputBg = inputObj.AddComponent<Image>();
        inputBg.sprite = roundedCardSprite;
        inputBg.type = Image.Type.Sliced;
        inputBg.color = Color.white;

        GameObject textArea = CreateUIObject("TextArea", inputObj.transform);
        SetAnchors(textArea, new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.90f));
        textArea.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholderTxt = CreateLabel(textArea.transform, "Placeholder", "Yeni kullanıcı adı yaz (3-16)...",
            Vector2.zero, Vector2.one, 12f, 18f, TextAlignmentOptions.Left, new Color(0.58f, 0.58f, 0.62f, 0.85f), false);
        placeholderTxt.enableAutoSizing = false;
        placeholderTxt.fontSize = 15f;

        TextMeshProUGUI inputTxt = CreateLabel(textArea.transform, "Text", "",
            Vector2.zero, Vector2.one, 12f, 20f, TextAlignmentOptions.Left, new Color(0.14f, 0.14f, 0.18f, 1f), false);
        inputTxt.enableAutoSizing = false;
        inputTxt.fontSize = 17f;

        inputObj.SetActive(false);
        usernameInputField = inputObj.AddComponent<TMP_InputField>();
        usernameInputField.textViewport = textArea.GetComponent<RectTransform>();
        usernameInputField.textComponent = inputTxt;
        usernameInputField.placeholder = placeholderTxt;
        usernameInputField.characterLimit = 16;
        usernameInputField.contentType = TMP_InputField.ContentType.Standard;
        usernameInputField.lineType = TMP_InputField.LineType.SingleLine;
        inputObj.SetActive(true);

        // Kaydet Butonu
        GameObject saveBtnObj = CreateUIObject("SaveUsernameButton", card.transform);
        SetAnchors(saveBtnObj, new Vector2(0.06f, 0.215f), new Vector2(0.94f, 0.322f));
        Image saveBtnImg = saveBtnObj.AddComponent<Image>();
        saveBtnImg.sprite = roundedCardSprite;
        saveBtnImg.type = Image.Type.Sliced;
        saveBtnImg.color = new Color(0.16f, 0.76f, 0.36f, 1f);

        saveUsernameBtn = saveBtnObj.AddComponent<Button>();
        saveUsernameBtn.onClick.AddListener(OnClickSaveUsername);

        saveUsernameBtnText = CreateLabel(saveBtnObj.transform, "Label", "KULLANICI ADINI KAYDET",
            new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 12f, 20f, TextAlignmentOptions.Center, Color.white, true);

        profileFeedbackText = CreateLabel(card.transform, "FeedbackText", "Aynı kullanıcı adını başka bir oyuncu alamaz.",
            new Vector2(0.06f, 0.135f), new Vector2(0.94f, 0.205f), 9.5f, 14f, TextAlignmentOptions.Center, new Color(0.32f, 0.26f, 0.20f, 1f), false);
        profileFeedbackText.textWrappingMode = TextWrappingModes.Normal;

        // Alt Skor Tablosuna Geçiş & Kapat Butonları
        GameObject openLbBtnObj = CreateUIObject("OpenLeaderboardFromProfileBtn", card.transform);
        SetAnchors(openLbBtnObj, new Vector2(0.06f, 0.032f), new Vector2(0.60f, 0.122f));
        Image openLbImg = openLbBtnObj.AddComponent<Image>();
        openLbImg.sprite = roundedCardSprite;
        openLbImg.type = Image.Type.Sliced;
        openLbImg.color = new Color(0.96f, 0.68f, 0.12f, 1f);

        Button openLbBtn = openLbBtnObj.AddComponent<Button>();
        openLbBtn.onClick.AddListener(() =>
        {
            profileModalObj.SetActive(false);
            OpenLeaderboardModal();
        });

        CreateLabel(openLbBtnObj.transform, "Label", "SKOR TABLOSU",
            new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.88f), 10f, 17f, TextAlignmentOptions.Center, Color.white, true);

        GameObject exitProfBtnObj = CreateUIObject("ExitProfileBtn", card.transform);
        SetAnchors(exitProfBtnObj, new Vector2(0.63f, 0.032f), new Vector2(0.94f, 0.122f));
        Image exitProfImg = exitProfBtnObj.AddComponent<Image>();
        exitProfImg.sprite = roundedCardSprite;
        exitProfImg.type = Image.Type.Sliced;
        exitProfImg.color = new Color(0.88f, 0.24f, 0.20f, 1f);

        Button exitProfBtn = exitProfBtnObj.AddComponent<Button>();
        exitProfBtn.onClick.AddListener(() => profileModalObj.SetActive(false));

        CreateLabel(exitProfBtnObj.transform, "Label", "KAPAT",
            new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.88f), 10f, 17f, TextAlignmentOptions.Center, Color.white, true);

        profileModalObj.SetActive(false);
    }

    // =========================================================================
    // 4. GLOBAL SKOR TABLOSU (TOP 50 + SENİN SIRAN) PENCERESİ
    // =========================================================================
    private void BuildLeaderboardModal()
    {
        leaderboardModalObj = CreateUIObject("LeaderboardModalOverlay", transform);
        SetAnchors(leaderboardModalObj, Vector2.zero, Vector2.one);

        Image dimBg = leaderboardModalObj.AddComponent<Image>();
        dimBg.color = new Color(0.04f, 0.06f, 0.12f, 0.80f);

        GameObject card = CreateUIObject("LeaderboardCard", leaderboardModalObj.transform);
        SetAnchors(card, new Vector2(0.045f, 0.07f), new Vector2(0.955f, 0.93f));
        Image cardImg = card.AddComponent<Image>();
        cardImg.sprite = roundedCardSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(0.99f, 0.95f, 0.86f, 1f);

        GameObject header = CreateUIObject("HeaderBar", card.transform);
        SetAnchors(header, new Vector2(0.035f, 0.895f), new Vector2(0.965f, 0.978f));
        Image headerImg = header.AddComponent<Image>();
        headerImg.sprite = roundedPillSprite;
        headerImg.type = Image.Type.Sliced;
        headerImg.color = new Color(0.96f, 0.66f, 0.12f, 1f);

        if (trophyIconSprite != null)
        {
            GameObject hIcon = CreateUIObject("TrophyIcon", header.transform);
            SetAnchors(hIcon, new Vector2(0.03f, 0.14f), new Vector2(0.14f, 0.86f));
            Image hIconImg = hIcon.AddComponent<Image>();
            hIconImg.sprite = trophyIconSprite;
            hIconImg.preserveAspect = true;
        }

        CreateLabel(header.transform, "Title", "SKOR TABLOSU",
            new Vector2(0.15f, 0.10f), new Vector2(0.68f, 0.90f), 14f, 24f, TextAlignmentOptions.Left, Color.white, true);

        // Yenile Butonu
        GameObject refreshObj = CreateUIObject("RefreshButton", header.transform);
        SetAnchors(refreshObj, new Vector2(0.69f, 0.14f), new Vector2(0.85f, 0.86f));
        Image refBg = refreshObj.AddComponent<Image>();
        refBg.sprite = roundedPillSprite;
        refBg.type = Image.Type.Sliced;
        refBg.color = new Color(0.20f, 0.64f, 0.95f, 1f);
        Button refBtn = refreshObj.AddComponent<Button>();
        refBtn.onClick.AddListener(RefreshLeaderboardFromServer);
        CreateLabel(refreshObj.transform, "Txt", "YENİLE",
            new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.90f), 8.5f, 13f, TextAlignmentOptions.Center, Color.white, true);

        CreateCloseButton(header.transform, () => leaderboardModalObj.SetActive(false));

        leaderboardStatusText = CreateLabel(card.transform, "StatusText", "Skorlar yükleniyor...",
            new Vector2(0.06f, 0.845f), new Vector2(0.94f, 0.888f), 10f, 14f, TextAlignmentOptions.Center, new Color(0.36f, 0.28f, 0.18f, 1f), false);

        GameObject scrollArea = CreateUIObject("ScrollArea", card.transform);
        SetAnchors(scrollArea, new Vector2(0.04f, 0.165f), new Vector2(0.96f, 0.84f));
        Image scrollBg = scrollArea.AddComponent<Image>();
        scrollBg.sprite = roundedCardSprite;
        scrollBg.type = Image.Type.Sliced;
        scrollBg.color = new Color(0.93f, 0.88f, 0.76f, 1f);

        GameObject viewport = CreateUIObject("Viewport", scrollArea.transform);
        SetAnchors(viewport, new Vector2(0.015f, 0.015f), new Vector2(0.985f, 0.985f));
        viewport.AddComponent<RectMask2D>();
        Image vpImg = viewport.AddComponent<Image>();
        vpImg.color = new Color(0f, 0f, 0f, 0.01f);

        GameObject content = CreateUIObject("Content", viewport.transform);
        leaderboardListContent = content.GetComponent<RectTransform>();
        leaderboardListContent.anchorMin = new Vector2(0f, 1f);
        leaderboardListContent.anchorMax = new Vector2(1f, 1f);
        leaderboardListContent.pivot = new Vector2(0.5f, 1f);
        leaderboardListContent.anchoredPosition = Vector2.zero;
        leaderboardListContent.sizeDelta = new Vector2(0f, 300f);

        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect sr = scrollArea.AddComponent<ScrollRect>();
        sr.viewport = viewport.GetComponent<RectTransform>();
        sr.content = leaderboardListContent;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Elastic;
        sr.elasticity = 0.08f;
        sr.scrollSensitivity = 22f;

        GameObject footer = CreateUIObject("MyRankFooter", card.transform);
        SetAnchors(footer, new Vector2(0.04f, 0.025f), new Vector2(0.96f, 0.148f));
        Image footerBg = footer.AddComponent<Image>();
        footerBg.sprite = roundedCardSprite;
        footerBg.type = Image.Type.Sliced;
        footerBg.color = new Color(0.15f, 0.68f, 0.36f, 1f);

        GameObject myAvObj = CreateUIObject("MyAvatar", footer.transform);
        SetAnchors(myAvObj, new Vector2(0.03f, 0.14f), new Vector2(0.16f, 0.86f));
        myRankAvatarImg = myAvObj.AddComponent<Image>();
        myRankAvatarImg.preserveAspect = true;

        myRankRowText = CreateLabel(footer.transform, "MyRankName", "#- • Guest",
            new Vector2(0.18f, 0.48f), new Vector2(0.66f, 0.90f), 11f, 17f, TextAlignmentOptions.Left, Color.white, true);

        myRankScoreText = CreateLabel(footer.transform, "MyRankScore", "REKOR: 0",
            new Vector2(0.18f, 0.10f), new Vector2(0.66f, 0.48f), 9.5f, 14f, TextAlignmentOptions.Left, new Color(0.90f, 1f, 0.92f, 1f), true);

        GameObject editNameBtnObj = CreateUIObject("EditNameBtn", footer.transform);
        SetAnchors(editNameBtnObj, new Vector2(0.68f, 0.16f), new Vector2(0.97f, 0.84f));
        Image editNameBg = editNameBtnObj.AddComponent<Image>();
        editNameBg.sprite = roundedPillSprite;
        editNameBg.type = Image.Type.Sliced;
        editNameBg.color = new Color(0.98f, 0.72f, 0.14f, 1f);

        Button editNameBtn = editNameBtnObj.AddComponent<Button>();
        editNameBtn.onClick.AddListener(() =>
        {
            leaderboardModalObj.SetActive(false);
            OpenProfileModal();
        });

        CreateLabel(editNameBtnObj.transform, "Label", "İSİM SEÇ",
            new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.90f), 9.5f, 15f, TextAlignmentOptions.Center, Color.white, true);

        leaderboardModalObj.SetActive(false);
    }

    // =========================================================================
    // 5. AKSİYONLAR & VERİ GÜNCELLEME
    // =========================================================================
    public void OpenProfileModal()
    {
        if (profileModalObj == null) BuildProfileModal();
        if (profileModalObj == null) return;
        if (leaderboardModalObj != null) leaderboardModalObj.SetActive(false);

        transform.SetAsLastSibling();
        profileModalObj.transform.SetAsLastSibling();
        profileModalObj.SetActive(true);

        LeaderboardService svc = LeaderboardService.EnsureInstance();
        if (usernameInputField != null)
        {
            usernameInputField.text = svc.IsGuest ? string.Empty : svc.CurrentUsername;
        }
        if (profileFeedbackText != null)
        {
            profileFeedbackText.color = new Color(0.32f, 0.26f, 0.20f, 1f);
            profileFeedbackText.text = svc.IsGuest
                ? "Şu an Misafir (Guest) görünüyorsun. İstediğin benzersiz adı seç!"
                : "Kullanıcı adını istediğin zaman başka boş bir adla değiştirebilirsin.";
        }

        RefreshAllUI();
        svc.SyncPlayerScore(PlayerPrefs.GetInt("HighScore", 0), _ => RefreshAllUI());
    }

    public void OpenLeaderboardModal()
    {
        if (leaderboardModalObj == null) BuildLeaderboardModal();
        if (leaderboardModalObj == null) return;
        if (profileModalObj != null) profileModalObj.SetActive(false);

        transform.SetAsLastSibling();
        leaderboardModalObj.transform.SetAsLastSibling();
        leaderboardModalObj.SetActive(true);

        RefreshAllUI();
        RefreshLeaderboardFromServer();
    }

    private void RefreshLeaderboardFromServer()
    {
        if (leaderboardStatusText != null)
        {
            leaderboardStatusText.text = "Skor tablosu güncelleniyor...";
        }

        LeaderboardService.EnsureInstance().FetchLeaderboard(50, (ok, resp) =>
        {
            if (leaderboardStatusText == null) return;
            if (!ok || resp == null)
            {
                leaderboardStatusText.text = "Sunucuya bağlanılamadı. Tekrar dene (YENİLE).";
                return;
            }
            int count = resp.entries != null ? resp.entries.Length : 0;
            leaderboardStatusText.text = $"TOP 50 OYUNCU ({count} KAYIT)";
            PopulateLeaderboardRows(resp);
        });
    }

    private void OnClickSaveUsername()
    {
        if (usernameInputField == null) return;
        string requested = usernameInputField.text != null ? usernameInputField.text.Trim() : string.Empty;

        if (saveUsernameBtn != null) saveUsernameBtn.interactable = false;
        if (profileFeedbackText != null)
        {
            profileFeedbackText.color = new Color(0.18f, 0.45f, 0.82f, 1f);
            profileFeedbackText.text = "Kullanıcı adı kontrol ediliyor...";
        }

        LeaderboardService.EnsureInstance().ChangeUsername(requested, (ok, msg) =>
        {
            if (saveUsernameBtn != null) saveUsernameBtn.interactable = true;
            if (profileFeedbackText != null)
            {
                profileFeedbackText.color = ok
                    ? new Color(0.12f, 0.62f, 0.26f, 1f)
                    : new Color(0.82f, 0.18f, 0.16f, 1f);
                profileFeedbackText.text = msg;
            }
            RefreshAllUI();
        });
    }

    public void RefreshAllUI()
    {
        LeaderboardService svc = LeaderboardService.EnsureInstance();
        string selectedCharId = CharacterShopData.GetSelectedCharacterId();
        Sprite charSprite = GetCharacterAvatarSprite(selectedCharId);
        Sprite selfieSprite = CharacterShopData.IsCustomFaceEnabled() ? CharacterShopData.LoadCustomFaceSprite() : null;

        string rankLabel = svc.CurrentRank > 0 ? $"#{svc.CurrentRank}" : "#-";
        int bestScore = Mathf.Max(svc.BestScore, PlayerPrefs.GetInt("HighScore", 0));

        if (topProfileAvatarImg != null && charSprite != null) topProfileAvatarImg.sprite = charSprite;
        if (topProfileSelfieOverlayImg != null)
        {
            topProfileSelfieOverlayImg.sprite = selfieSprite;
            topProfileSelfieOverlayImg.gameObject.SetActive(selfieSprite != null);
        }
        if (topProfileNameText != null) topProfileNameText.text = svc.CurrentUsername;
        if (topProfileSubText != null)
        {
            topProfileSubText.text = svc.IsGuest ? "MİSAFİR • İSİM SEÇ" : $"{rankLabel} • REKOR: {bestScore}";
        }
        if (topTrophySubText != null)
        {
            topTrophySubText.text = svc.CurrentRank > 0 ? $"SIRAN: {rankLabel}" : $"REKOR: {bestScore}";
        }

        if (profileModalAvatarImg != null && charSprite != null) profileModalAvatarImg.sprite = charSprite;
        if (profileModalSelfieOverlayImg != null)
        {
            profileModalSelfieOverlayImg.sprite = selfieSprite;
            profileModalSelfieOverlayImg.gameObject.SetActive(selfieSprite != null);
        }
        if (profileModalCurrentNameText != null) profileModalCurrentNameText.text = svc.CurrentUsername;
        if (profileModalBadgeText != null)
        {
            profileModalBadgeText.text = svc.IsGuest ? "MİSAFİR (GUEST)" : "KAYITLI OYUNCU";
        }
        if (profileModalBadgeBg != null)
        {
            profileModalBadgeBg.color = svc.IsGuest
                ? new Color(0.95f, 0.56f, 0.14f, 1f)
                : new Color(0.16f, 0.74f, 0.36f, 1f);
        }
        if (profileModalScoreText != null) profileModalScoreText.text = $"REKOR: {bestScore}";
        if (profileModalRankText != null) profileModalRankText.text = $"SIRA: {rankLabel}";

        if (myRankAvatarImg != null && charSprite != null) myRankAvatarImg.sprite = charSprite;
        if (myRankRowText != null) myRankRowText.text = $"{rankLabel} • {svc.CurrentUsername}";
        if (myRankScoreText != null) myRankScoreText.text = $"EN İYİ SKORUN: {bestScore}";

        AttachGameOverLeaderboardButton();
    }

    private void PopulateLeaderboardRows(LeaderboardService.LeaderboardResponse resp)
    {
        if (leaderboardListContent == null || resp == null) return;

        for (int i = 0; i < spawnedRowObjects.Count; i++)
        {
            if (spawnedRowObjects[i] != null) Destroy(spawnedRowObjects[i]);
        }
        spawnedRowObjects.Clear();

        LeaderboardService.LeaderboardEntry[] entries = resp.entries;
        if (entries == null || entries.Length == 0)
        {
            return;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            LeaderboardService.LeaderboardEntry entry = entries[i];
            if (entry == null) continue;

            GameObject rowObj = CreateUIObject($"Row_{entry.rank}", leaderboardListContent);
            spawnedRowObjects.Add(rowObj);

            RectTransform rowRt = rowObj.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, 44f);
            LayoutElement le = rowObj.AddComponent<LayoutElement>();
            le.preferredHeight = 44f;
            le.minHeight = 44f;

            Image rowBg = rowObj.AddComponent<Image>();
            rowBg.sprite = roundedCardSprite;
            rowBg.type = Image.Type.Sliced;

            if (entry.is_me)
            {
                rowBg.color = new Color(0.76f, 0.96f, 0.80f, 1f);
            }
            else if (entry.rank == 1)
            {
                rowBg.color = new Color(1.00f, 0.90f, 0.50f, 1f);
            }
            else if (entry.rank == 2)
            {
                rowBg.color = new Color(0.89f, 0.92f, 0.96f, 1f);
            }
            else if (entry.rank == 3)
            {
                rowBg.color = new Color(0.95f, 0.79f, 0.62f, 1f);
            }
            else
            {
                rowBg.color = (i % 2 == 0)
                    ? new Color(1.00f, 0.98f, 0.93f, 1f)
                    : new Color(0.97f, 0.94f, 0.87f, 1f);
            }

            GameObject rankBadge = CreateUIObject("RankBadge", rowObj.transform);
            SetAnchors(rankBadge, new Vector2(0.02f, 0.14f), new Vector2(0.18f, 0.86f));
            Image rankBg = rankBadge.AddComponent<Image>();
            rankBg.sprite = roundedPillSprite;
            rankBg.type = Image.Type.Sliced;
            rankBg.color = entry.rank == 1 ? new Color(0.95f, 0.64f, 0.06f, 1f)
                         : entry.rank == 2 ? new Color(0.52f, 0.58f, 0.68f, 1f)
                         : entry.rank == 3 ? new Color(0.78f, 0.44f, 0.18f, 1f)
                         : new Color(0.32f, 0.26f, 0.20f, 0.85f);

            CreateLabel(rankBadge.transform, "Txt", $"#{entry.rank}",
                Vector2.zero, Vector2.one, 10f, 16f, TextAlignmentOptions.Center, Color.white, true);

            GameObject avObj = CreateUIObject("CharAvatar", rowObj.transform);
            SetAnchors(avObj, new Vector2(0.20f, 0.10f), new Vector2(0.32f, 0.90f));
            Image avImg = avObj.AddComponent<Image>();
            avImg.sprite = GetCharacterAvatarSprite(entry.character_id);
            avImg.preserveAspect = true;

            string displayName = entry.is_me ? $"{entry.username} (SEN)" : entry.username;
            Color nameCol = entry.is_me
                ? new Color(0.08f, 0.42f, 0.18f, 1f)
                : new Color(0.18f, 0.14f, 0.10f, 1f);

            CreateLabel(rowObj.transform, "Username", displayName,
                new Vector2(0.34f, 0.12f), new Vector2(0.73f, 0.88f), 11f, 17f, TextAlignmentOptions.Left, nameCol, false);

            CreateLabel(rowObj.transform, "Score", entry.best_score.ToString(),
                new Vector2(0.74f, 0.12f), new Vector2(0.97f, 0.88f), 12f, 19f, TextAlignmentOptions.Right, new Color(0.16f, 0.46f, 0.84f, 1f), false);
        }

        RefreshAllUI();
    }

    // =========================================================================
    // 6. YARDIMCI UI OLUŞTURUCULAR (TMP OUTLINE CRASH-PROOF)
    // =========================================================================
    private Sprite GetCharacterAvatarSprite(string characterId)
    {
        if (shopConfig == null) shopConfig = CharacterShopConfig.Load();
        if (shopConfig != null)
        {
            CharacterShopConfig.CharacterVisualEntry entry = shopConfig.GetVisualEntry(characterId);
            if (entry != null && entry.idleFrames != null && entry.idleFrames.Length > 0 && entry.idleFrames[0] != null)
            {
                return entry.idleFrames[0];
            }
        }
        return profileIconSprite != null ? profileIconSprite : roundedPillSprite;
    }

    private void CreateCloseButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        GameObject closeObj = CreateUIObject("CloseButton", parent);
        SetAnchors(closeObj, new Vector2(0.87f, 0.14f), new Vector2(0.98f, 0.86f));
        Image closeBg = closeObj.AddComponent<Image>();
        closeBg.sprite = roundedPillSprite;
        closeBg.type = Image.Type.Sliced;
        closeBg.color = new Color(0.90f, 0.24f, 0.22f, 1f);

        Button closeBtn = closeObj.AddComponent<Button>();
        closeBtn.onClick.AddListener(onClick);

        CreateLabel(closeObj.transform, "X", "X",
            Vector2.zero, Vector2.one, 12f, 20f, TextAlignmentOptions.Center, Color.white, true);
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one;
        return go;
    }

    private static void SetAnchors(GameObject go, Vector2 min, Vector2 max)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private TextMeshProUGUI CreateLabel(
        Transform parent,
        string name,
        string text,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float minSize,
        float maxSize,
        TextAlignmentOptions align,
        Color color,
        bool addShadow)
    {
        GameObject go = CreateUIObject(name, parent);
        SetAnchors(go, anchorMin, anchorMax);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (arcadeFont != null) tmp.font = arcadeFont;
        if (arcadeFontMaterial != null) tmp.fontSharedMaterial = arcadeFontMaterial;
        tmp.text = text;
        tmp.alignment = align;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.fontStyle = FontStyles.Normal;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.raycastTarget = false;

        if (addShadow && arcadeFontMaterial == null)
        {
            Shadow sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0.06f, 0.04f, 0.03f, 0.85f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
        }
        return tmp;
    }

    private static Sprite CreateRoundedSprite(int w, int h, int r, bool withBorder)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color[] cols = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - y, y - (h - 1 - r)));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(r - dist + 0.5f);

                if (withBorder && dist > r - 3.0f)
                {
                    cols[y * w + x] = new Color(0.18f, 0.13f, 0.09f, alpha);
                }
                else
                {
                    cols[y * w + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }
}
