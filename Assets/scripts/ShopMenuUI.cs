using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ana menüdeki Kostüm Mağazası (ShopOverlay) ve kalıcı Coin Bakiye Rozetini (MenuCoinBadge)
/// yöneten kendi kendine yeten UI kontrolcüsü.
/// </summary>
public class ShopMenuUI : MonoBehaviour
{
    private const float PreviewFrameRate = 12f;

    private struct CardView
    {
        public string CharacterId;
        public Image CardBg;
        public Image HighlightFrame;
        public Image PreviewImage;
        public Image ActionButtonImage;
        public Button ActionButton;
        public TextMeshProUGUI ActionButtonLabel;
        public Sprite[] IdleFrames;
    }

    private CharacterShopConfig config;
    private AudioSource uiAudioSource;

    private GameObject shopOverlay;
    private RectTransform cardContainer;
    private TextMeshProUGUI menuBalanceText;
    private TextMeshProUGUI shopBalanceText;
    private TextMeshProUGUI statusText;

    private CardView[] cards;
    private float previewTimer;
    private int previewFrameIndex;
    private Coroutine statusFeedbackCoroutine;
    private bool isBuilt;

    private static readonly Color SelectedBtnColor = new Color(0.13f, 0.77f, 0.42f, 1f);
    private static readonly Color UnlockedBtnColor = new Color(0.20f, 0.65f, 0.96f, 1f);
    private static readonly Color BuyableBtnColor = new Color(0.96f, 0.62f, 0.08f, 1f);
    private static readonly Color LockedBtnColor = new Color(0.55f, 0.58f, 0.66f, 1f);

    private static readonly Color SelectedCardBg = new Color(0.89f, 1.00f, 0.92f, 1f);
    private static readonly Color UnlockedCardBg = new Color(1.00f, 0.98f, 0.95f, 1f);
    private static readonly Color LockedCardBg = new Color(0.92f, 0.92f, 0.95f, 1f);

    private void Awake()
    {
        EnsureBuilt();
        RefreshUI();
    }

    private void OnEnable()
    {
        CharacterShopData.OnShopStateChanged += RefreshUI;
        if (isBuilt) RefreshUI();
    }

    private void OnDisable()
    {
        CharacterShopData.OnShopStateChanged -= RefreshUI;
    }

    private void Update()
    {
        if (shopOverlay == null || !shopOverlay.activeSelf)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseShop();
            return;
        }

        UpdateCharacterPreviews();
    }

    public void OpenShop()
    {
        EnsureBuilt();
        RefreshUI();
        SetStatusMessage("KOSTUMUNU SEC", new Color(0.35f, 0.24f, 0.30f, 1f));
        if (shopOverlay != null)
        {
            shopOverlay.SetActive(true);
            shopOverlay.transform.SetAsLastSibling();
        }
    }

    public void CloseShop()
    {
        if (shopOverlay != null)
        {
            shopOverlay.SetActive(false);
        }
        PlayUiSound(0.95f, 0.30f);
    }

    private void EnsureBuilt()
    {
        if (isBuilt) return;
        isBuilt = true;

        config = CharacterShopConfig.Load();

        uiAudioSource = GetComponent<AudioSource>();
        if (uiAudioSource == null) uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;

        BuildMenuCoinBadge();
        BuildShopModal();
    }

    private void BuildMenuCoinBadge()
    {
        GameObject badgeGo = CreateUiObject("MenuCoinBadge", transform);
        RectTransform rt = badgeGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-14f, -16f);
        rt.sizeDelta = new Vector2(118f, 36f);

        Image bg = badgeGo.AddComponent<Image>();
        bg.sprite = config != null ? config.badgePillSprite : null;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 0.94f, 0.72f, 0.96f);

        AddCoinIcon(badgeGo.transform, new Vector2(20f, 0f), new Vector2(24f, 24f));

        menuBalanceText = CreateLabel(
            "MenuCoinText",
            badgeGo.transform,
            "0",
            17f,
            new Color(0.35f, 0.20f, 0.05f, 1f),
            TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            new Vector2(36f, 0f),
            new Vector2(-42f, -4f));
    }

    private void BuildShopModal()
    {
        shopOverlay = CreateUiObject("ShopOverlay", transform);
        StretchFull(shopOverlay.GetComponent<RectTransform>());
        Image dim = shopOverlay.AddComponent<Image>();
        dim.color = new Color(0.05f, 0.03f, 0.12f, 0.76f);
        dim.raycastTarget = true;

        GameObject panelGo = CreateUiObject("ShopCardPanel", shopOverlay.transform);
        cardContainer = panelGo.GetComponent<RectTransform>();
        cardContainer.anchorMin = new Vector2(0.5f, 0.5f);
        cardContainer.anchorMax = new Vector2(0.5f, 0.5f);
        cardContainer.sizeDelta = new Vector2(342f, 540f);

        Image panelBg = panelGo.AddComponent<Image>();
        panelBg.sprite = config != null && config.panelBgSprite != null
            ? config.panelBgSprite
            : (config != null ? config.cardBgSprite : null);
        panelBg.type = Image.Type.Sliced;
        panelBg.color = new Color(1f, 0.98f, 0.96f, 1f);

        CreateLabel(
            "ShopTitle",
            panelGo.transform,
            "KOSTUM DUKKANI",
            23f,
            new Color(0.36f, 0.17f, 0.28f, 1f),
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -34f),
            new Vector2(300f, 38f));

        // Mağaza üst bakiye rozeti
        GameObject balGo = CreateUiObject("ShopBalancePill", panelGo.transform);
        RectTransform balRt = balGo.GetComponent<RectTransform>();
        balRt.anchorMin = new Vector2(0.5f, 1f);
        balRt.anchorMax = new Vector2(0.5f, 1f);
        balRt.anchoredPosition = new Vector2(0f, -72f);
        balRt.sizeDelta = new Vector2(180f, 32f);

        Image balImg = balGo.AddComponent<Image>();
        balImg.sprite = config != null ? config.badgePillSprite : null;
        balImg.type = Image.Type.Sliced;
        balImg.color = new Color(1f, 0.91f, 0.62f, 1f);

        AddCoinIcon(balGo.transform, new Vector2(22f, 0f), new Vector2(22f, 22f));

        shopBalanceText = CreateLabel(
            "ShopBalanceText",
            balGo.transform,
            "BAKIYE: 0",
            15f,
            new Color(0.35f, 0.19f, 0.04f, 1f),
            TextAlignmentOptions.MidlineLeft,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            new Vector2(36f, 0f),
            new Vector2(-42f, 0f));

        // 2x2 Karakter Kartları Grid
        var catalog = CharacterShopData.Catalog;
        cards = new CardView[catalog.Length];

        Vector2[] offsets = new Vector2[]
        {
            new Vector2(-78f,  68f),
            new Vector2( 78f,  68f),
            new Vector2(-78f, -92f),
            new Vector2( 78f, -92f),
        };

        for (int i = 0; i < catalog.Length; i++)
        {
            cards[i] = BuildCharacterCard(panelGo.transform, catalog[i], offsets[i]);
        }

        // Geri bildirim metni
        statusText = CreateLabel(
            "ShopStatusText",
            panelGo.transform,
            "KOSTUMUNU SEC",
            14f,
            new Color(0.35f, 0.24f, 0.30f, 1f),
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 84f),
            new Vector2(290f, 26f));

        // Kapat butonu
        GameObject closeGo = CreateUiObject("Btn_CloseShop", panelGo.transform);
        RectTransform closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 40f);
        closeRt.sizeDelta = new Vector2(180f, 46f);

        Image closeImg = closeGo.AddComponent<Image>();
        closeImg.sprite = config != null && config.backButtonSprite != null
            ? config.backButtonSprite
            : (config != null ? config.buttonPillSprite : null);
        closeImg.type = Image.Type.Sliced;
        closeImg.color = config != null && config.backButtonSprite != null
            ? Color.white
            : new Color(0.92f, 0.36f, 0.56f, 1f);

        Button closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(CloseShop);

        CreateLabel(
            "CloseLabel",
            closeGo.transform,
            "KAPAT",
            18f,
            Color.white,
            TextAlignmentOptions.Center,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        shopOverlay.SetActive(false);
    }

    private CardView BuildCharacterCard(
        Transform parent,
        CharacterShopData.CharacterCatalogEntry entry,
        Vector2 anchoredPos)
    {
        GameObject cardGo = CreateUiObject("Card_" + entry.Id, parent);
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = anchoredPos;
        cardRt.sizeDelta = new Vector2(146f, 150f);

        // Seçili vurgu çerçevesi
        GameObject borderGo = CreateUiObject("SelectionBorder", cardGo.transform);
        StretchFull(borderGo.GetComponent<RectTransform>(), new Vector2(6f, 6f));
        Image borderImg = borderGo.AddComponent<Image>();
        borderImg.sprite = config != null ? config.cardBgSprite : null;
        borderImg.type = Image.Type.Sliced;
        borderImg.color = entry.AccentColor;

        // Kart ana gövdesi
        GameObject bodyGo = CreateUiObject("CardBody", cardGo.transform);
        StretchFull(bodyGo.GetComponent<RectTransform>());
        Image cardBg = bodyGo.AddComponent<Image>();
        cardBg.sprite = config != null ? config.cardBgSprite : null;
        cardBg.type = Image.Type.Sliced;
        cardBg.color = UnlockedCardBg;

        // Karakter ismi
        CreateLabel(
            "Name",
            bodyGo.transform,
            entry.DisplayName,
            14f,
            new Color(0.24f, 0.16f, 0.24f, 1f),
            TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -18f),
            new Vector2(134f, 24f));

        // Karakter Idle Önizleme Sprite'ı
        GameObject previewGo = CreateUiObject("Preview", bodyGo.transform);
        RectTransform prevRt = previewGo.GetComponent<RectTransform>();
        prevRt.anchorMin = new Vector2(0.5f, 0.5f);
        prevRt.anchorMax = new Vector2(0.5f, 0.5f);
        prevRt.anchoredPosition = new Vector2(0f, 6f);
        prevRt.sizeDelta = new Vector2(62f, 62f);

        Image previewImg = previewGo.AddComponent<Image>();
        previewImg.preserveAspect = true;

        var visualEntry = config != null ? config.GetVisualEntry(entry.Id) : null;
        Sprite[] frames = visualEntry != null ? visualEntry.idleFrames : null;
        if (frames != null && frames.Length > 0)
        {
            previewImg.sprite = frames[0];
        }

        // Satın Al / Seç butonu
        GameObject btnGo = CreateUiObject("ActionButton", bodyGo.transform);
        RectTransform btnRt = btnGo.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0f);
        btnRt.anchorMax = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0f, 22f);
        btnRt.sizeDelta = new Vector2(124f, 32f);

        Image btnImg = btnGo.AddComponent<Image>();
        btnImg.sprite = config != null ? config.buttonPillSprite : null;
        btnImg.type = Image.Type.Sliced;
        btnImg.color = BuyableBtnColor;

        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = btnImg;

        string idCopy = entry.Id;
        btn.onClick.AddListener(() => OnCardClicked(idCopy));

        TextMeshProUGUI btnLabel = CreateLabel(
            "Label",
            btnGo.transform,
            "...",
            13.5f,
            Color.white,
            TextAlignmentOptions.Center,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        return new CardView
        {
            CharacterId = entry.Id,
            CardBg = cardBg,
            HighlightFrame = borderImg,
            PreviewImage = previewImg,
            ActionButtonImage = btnImg,
            ActionButton = btn,
            ActionButtonLabel = btnLabel,
            IdleFrames = frames
        };
    }

    private void OnCardClicked(string characterId)
    {
        bool ok = CharacterShopData.TryBuyOrSelectCharacter(
            characterId,
            out bool newlyBought,
            out bool insufficientFunds,
            out _);

        if (!ok && insufficientFunds)
        {
            SetStatusMessage("YETERSIZ COIN!", new Color(0.86f, 0.22f, 0.28f, 1f));
            PlayUiSound(0.66f, 0.35f);
            return;
        }

        if (newlyBought)
        {
            SetStatusMessage("YENI KOSTUM ACILDI!", new Color(0.10f, 0.64f, 0.32f, 1f));
            PlayUiSound(1.42f, 0.55f);
        }
        else if (ok)
        {
            SetStatusMessage("KOSTUM SECILDI!", new Color(0.14f, 0.50f, 0.85f, 1f));
            PlayUiSound(1.18f, 0.40f);
        }

        RefreshUI();
    }

    public void RefreshUI()
    {
        int coins = CharacterShopData.GetCoins();
        string selectedId = CharacterShopData.GetSelectedCharacterId();

        if (menuBalanceText != null)
            menuBalanceText.text = coins.ToString();

        if (shopBalanceText != null)
            shopBalanceText.text = "BAKIYE: " + coins;

        if (cards == null) return;

        for (int i = 0; i < cards.Length; i++)
        {
            CardView card = cards[i];
            if (!CharacterShopData.TryGetEntry(card.CharacterId, out var entry))
                continue;

            bool unlocked = CharacterShopData.IsUnlocked(card.CharacterId);
            bool selected = selectedId == card.CharacterId;
            bool affordable = coins >= entry.Price;

            if (card.HighlightFrame != null)
                card.HighlightFrame.gameObject.SetActive(selected);

            if (card.CardBg != null)
                card.CardBg.color = selected ? SelectedCardBg : (unlocked ? UnlockedCardBg : LockedCardBg);

            if (card.PreviewImage != null)
                card.PreviewImage.color = unlocked ? Color.white : new Color(0.70f, 0.70f, 0.76f, 0.92f);

            if (selected)
            {
                card.ActionButtonImage.color = SelectedBtnColor;
                card.ActionButtonLabel.text = "SECILI";
            }
            else if (unlocked)
            {
                card.ActionButtonImage.color = UnlockedBtnColor;
                card.ActionButtonLabel.text = "SEC";
            }
            else if (affordable)
            {
                card.ActionButtonImage.color = BuyableBtnColor;
                card.ActionButtonLabel.text = entry.Price + " AL";
            }
            else
            {
                card.ActionButtonImage.color = LockedBtnColor;
                card.ActionButtonLabel.text = entry.Price + " COIN";
            }
        }
    }

    private void UpdateCharacterPreviews()
    {
        if (cards == null) return;

        previewTimer += Time.unscaledDeltaTime * PreviewFrameRate;
        if (previewTimer < 1f) return;

        int steps = Mathf.FloorToInt(previewTimer);
        previewTimer -= steps;
        previewFrameIndex += steps;

        for (int i = 0; i < cards.Length; i++)
        {
            Sprite[] frames = cards[i].IdleFrames;
            if (frames != null && frames.Length > 0 && cards[i].PreviewImage != null)
            {
                int frame = (previewFrameIndex + i * 2) % frames.Length;
                cards[i].PreviewImage.sprite = frames[frame];
            }
        }
    }

    private void SetStatusMessage(string message, Color color)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = color;

        if (gameObject.activeInHierarchy)
        {
            if (statusFeedbackCoroutine != null) StopCoroutine(statusFeedbackCoroutine);
            statusFeedbackCoroutine = StartCoroutine(PunchStatusRoutine());
        }
    }

    private IEnumerator PunchStatusRoutine()
    {
        if (statusText == null) yield break;
        RectTransform rt = statusText.rectTransform;
        rt.localScale = Vector3.one * 1.15f;
        float elapsed = 0f;
        const float duration = 0.22f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            rt.localScale = Vector3.Lerp(Vector3.one * 1.15f, Vector3.one, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        rt.localScale = Vector3.one;
        statusFeedbackCoroutine = null;
    }

    private void PlayUiSound(float pitch, float volume)
    {
        if (config == null || config.coinCollectSound == null || uiAudioSource == null) return;
        uiAudioSource.pitch = pitch;
        uiAudioSource.PlayOneShot(config.coinCollectSound, volume);
    }

    private void AddCoinIcon(Transform parent, Vector2 anchoredLeftPos, Vector2 size)
    {
        if (config == null || config.cherryFrames == null || config.cherryFrames.Length == 0)
            return;

        GameObject iconGo = CreateUiObject("CoinIcon", parent);
        RectTransform rt = iconGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.anchoredPosition = anchoredLeftPos;
        rt.sizeDelta = size;

        Image img = iconGo.AddComponent<Image>();
        img.sprite = config.cherryFrames[0];
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private TextMeshProUGUI CreateLabel(
        string name,
        Transform parent,
        string text,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPos,
        Vector2 sizeDelta)
    {
        GameObject go = CreateUiObject(name, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (config != null && config.uiFont != null)
            tmp.font = config.uiFont;

        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 10f;
        tmp.fontSizeMax = fontSize;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt, Vector2 padding = default)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = padding;
    }
}
