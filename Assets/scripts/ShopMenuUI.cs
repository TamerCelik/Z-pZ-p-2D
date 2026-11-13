using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ana menüdeki sahne-içi Kostüm Mağazası (ShopPanel) ve MenuCoinBadge UI elemanlarını
/// Inspector referansları üzerinden yöneten kontrolcü (SettingsManager deseninde).
/// </summary>
public class ShopMenuUI : MonoBehaviour
{
    private const float PreviewFrameRate = 12f;

    [Serializable]
    public class ShopCardSlot
    {
        public string characterId;
        public Image cardBackground;
        public Image selectionBorder;
        public Image previewImage;
        public Button actionButton;
        public Image actionButtonImage;
        public TextMeshProUGUI actionButtonLabel;
    }

    [Header("Panel ve Genel Referanslar")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private TextMeshProUGUI menuBalanceText;
    [SerializeField] private TextMeshProUGUI shopBalanceText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Karakter Kartları")]
    [SerializeField] private ShopCardSlot[] cardSlots;

    [Header("Görünüm Ayarları")]
    [SerializeField] private bool autoTintCards = true;

    private CharacterShopConfig config;
    private AudioSource uiAudioSource;
    private float previewTimer;
    private int previewFrameIndex;
    private Coroutine statusFeedbackCoroutine;

    private static readonly Color SelectedBtnColor = new Color(0.13f, 0.77f, 0.42f, 1f);
    private static readonly Color UnlockedBtnColor = new Color(0.20f, 0.65f, 0.96f, 1f);
    private static readonly Color BuyableBtnColor = new Color(0.96f, 0.62f, 0.08f, 1f);
    private static readonly Color LockedBtnColor = new Color(0.55f, 0.58f, 0.66f, 1f);

    private static readonly Color SelectedCardBg = new Color(0.89f, 1.00f, 0.92f, 1f);
    private static readonly Color UnlockedCardBg = new Color(1.00f, 0.98f, 0.95f, 1f);
    private static readonly Color LockedCardBg = new Color(0.92f, 0.92f, 0.95f, 1f);

    private void Awake()
    {
        config = CharacterShopConfig.Load();

        uiAudioSource = GetComponent<AudioSource>();
        if (uiAudioSource == null) uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;

        if (shopPanel != null)
            shopPanel.SetActive(false);

        BindSlotButtons();
        RefreshUI();
    }

    private void OnEnable()
    {
        CharacterShopData.OnShopStateChanged += RefreshUI;
        RefreshUI();
    }

    private void OnDisable()
    {
        CharacterShopData.OnShopStateChanged -= RefreshUI;
    }

    private void Update()
    {
        if (shopPanel == null || !shopPanel.activeInHierarchy)
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
        RefreshUI();
        SetStatusMessage("KOSTUMUNU SEC", new Color(0.35f, 0.24f, 0.30f, 1f));
        if (shopPanel != null)
        {
            shopPanel.SetActive(true);
        }
    }

    public void CloseShop()
    {
        if (shopPanel != null)
        {
            shopPanel.SetActive(false);
        }
        PlayUiSound(0.95f, 0.30f);
    }

    public void OnSlotClicked(int slotIndex)
    {
        if (cardSlots == null || slotIndex < 0 || slotIndex >= cardSlots.Length)
            return;

        ShopCardSlot slot = cardSlots[slotIndex];
        if (slot != null && !string.IsNullOrEmpty(slot.characterId))
        {
            HandleCharacterClick(slot.characterId);
        }
    }

    private void BindSlotButtons()
    {
        if (cardSlots == null) return;

        for (int i = 0; i < cardSlots.Length; i++)
        {
            ShopCardSlot slot = cardSlots[i];
            if (slot == null || slot.actionButton == null) continue;

            int indexCopy = i;
            slot.actionButton.onClick.RemoveAllListeners();
            slot.actionButton.onClick.AddListener(() => OnSlotClicked(indexCopy));
        }
    }

    private void HandleCharacterClick(string characterId)
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

        if (cardSlots == null) return;

        for (int i = 0; i < cardSlots.Length; i++)
        {
            ShopCardSlot slot = cardSlots[i];
            if (slot == null || !CharacterShopData.TryGetEntry(slot.characterId, out var entry))
                continue;

            bool unlocked = CharacterShopData.IsUnlocked(slot.characterId);
            bool selected = selectedId == slot.characterId;
            bool affordable = coins >= entry.Price;

            if (slot.selectionBorder != null)
                slot.selectionBorder.gameObject.SetActive(selected);

            if (autoTintCards && slot.cardBackground != null)
                slot.cardBackground.color = selected ? SelectedCardBg : (unlocked ? UnlockedCardBg : LockedCardBg);

            if (slot.previewImage != null)
                slot.previewImage.color = unlocked ? Color.white : new Color(0.70f, 0.70f, 0.76f, 0.92f);

            if (slot.actionButtonImage != null)
            {
                slot.actionButtonImage.color = selected
                    ? SelectedBtnColor
                    : (unlocked ? UnlockedBtnColor : (affordable ? BuyableBtnColor : LockedBtnColor));
            }

            if (slot.actionButtonLabel != null)
            {
                slot.actionButtonLabel.text = selected
                    ? "SECILI"
                    : (unlocked ? "SEC" : (affordable ? entry.Price + " AL" : entry.Price + " COIN"));
            }
        }
    }

    private void UpdateCharacterPreviews()
    {
        if (config == null) config = CharacterShopConfig.Load();
        if (config == null || cardSlots == null) return;

        previewTimer += Time.unscaledDeltaTime * PreviewFrameRate;
        if (previewTimer < 1f) return;

        int steps = Mathf.FloorToInt(previewTimer);
        previewTimer -= steps;
        previewFrameIndex += steps;

        for (int i = 0; i < cardSlots.Length; i++)
        {
            ShopCardSlot slot = cardSlots[i];
            if (slot == null || slot.previewImage == null) continue;

            var visualEntry = config.GetVisualEntry(slot.characterId);
            Sprite[] frames = visualEntry != null ? visualEntry.idleFrames : null;
            if (frames != null && frames.Length > 0)
            {
                int frame = (previewFrameIndex + i * 2) % frames.Length;
                slot.previewImage.sprite = frames[frame];
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
        if (config == null) config = CharacterShopConfig.Load();
        if (config == null || config.coinCollectSound == null || uiAudioSource == null) return;
        uiAudioSource.pitch = pitch;
        uiAudioSource.PlayOneShot(config.coinCollectSound, volume);
    }
}
