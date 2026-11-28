using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ayarlar menüsünde ("KONTROL TÜRÜ" başlığı altında) yan yana Kawaii
/// JOYSTİCK (ControlMode = 2) ve YÖN TUŞLARI (ControlMode = 0) seçim kartlarını,
/// seçili kartın sol üst köşesindeki yıldız rozetini ve alttaki "TAMAM" butonunu kurar.
/// </summary>
public class KawaiiControlSelectorUI : MonoBehaviour
{
    public const int ModeButtons = 0;  // YÖN TUŞLARI
    public const int ModeJoystick = 2; // JOYSTİCK

    private Action<int> onModeSelected;
    private RectTransform joystickCardRt;
    private RectTransform dpadCardRt;
    private Image joystickCardImg;
    private Image dpadCardImg;
    private RectTransform starBadgeRt;

    private static Sprite cachedJoystickSprite;
    private static Sprite cachedDpadSprite;
    private static Sprite cachedStarSprite;

    /// <summary>
    /// Kayıtlı kontrol modunu okur; eski jiroskop (1) veya geçersiz değerleri 0 (Yön Tuşları) olarak normalize eder.
    /// </summary>
    public static int GetNormalizedControlMode()
    {
        int raw = PlayerPrefs.GetInt("ControlMode", ModeButtons);
        if (raw != ModeButtons && raw != ModeJoystick)
        {
            raw = ModeButtons;
            PlayerPrefs.SetInt("ControlMode", raw);
            PlayerPrefs.Save();
        }
        return raw;
    }

    public static KawaiiControlSelectorUI Attach(GameObject settingsPanel, Action<int> onModeChanged)
    {
        if (settingsPanel == null) return null;

        KawaiiControlSelectorUI existing = settingsPanel.GetComponent<KawaiiControlSelectorUI>();
        if (existing == null)
        {
            existing = settingsPanel.AddComponent<KawaiiControlSelectorUI>();
            existing.BuildUI(settingsPanel.transform);
        }

        existing.onModeSelected = onModeChanged;
        existing.RefreshVisuals(GetNormalizedControlMode());
        return existing;
    }

    private void OnEnable()
    {
        HideLegacyControlButtons(transform);
        RefreshVisuals(GetNormalizedControlMode());
    }

    private void BuildUI(Transform panelTr)
    {
        HideLegacyControlButtons(panelTr);
        ConfigureTamamButton(panelTr);

        EnsureSpritesLoaded();

        // Sol Kart Yuvası: JOYSTİCK (Mockup oranlarıyla birebir hizalı)
        GameObject joySlot = CreateUIObject("KawaiiSlot_Joystick", panelTr);
        SetNormalizedRect(joySlot, new Vector2(0.084f, 0.264f), new Vector2(0.476f, 0.456f));

        GameObject joyCardObj = CreateUIObject("Card_Joystick", joySlot.transform);
        SetNormalizedRect(joyCardObj, Vector2.zero, Vector2.one);
        joystickCardRt = joyCardObj.GetComponent<RectTransform>();

        AspectRatioFitter joyArf = joyCardObj.AddComponent<AspectRatioFitter>();
        joyArf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        joyArf.aspectRatio = 176f / 173f;

        joystickCardImg = joyCardObj.AddComponent<Image>();
        joystickCardImg.sprite = cachedJoystickSprite;
        joystickCardImg.preserveAspect = true;
        joystickCardImg.color = Color.white;

        Button joyBtn = joyCardObj.AddComponent<Button>();
        ConfigureCardButtonColors(joyBtn, joystickCardImg);
        joyBtn.onClick.AddListener(() => HandleCardClicked(ModeJoystick));

        // Sağ Kart Yuvası: YÖN TUŞLARI
        GameObject dpadSlot = CreateUIObject("KawaiiSlot_Dpad", panelTr);
        SetNormalizedRect(dpadSlot, new Vector2(0.524f, 0.264f), new Vector2(0.916f, 0.456f));

        GameObject dpadCardObj = CreateUIObject("Card_Dpad", dpadSlot.transform);
        SetNormalizedRect(dpadCardObj, Vector2.zero, Vector2.one);
        dpadCardRt = dpadCardObj.GetComponent<RectTransform>();

        AspectRatioFitter dpadArf = dpadCardObj.AddComponent<AspectRatioFitter>();
        dpadArf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        dpadArf.aspectRatio = 189f / 173f;

        dpadCardImg = dpadCardObj.AddComponent<Image>();
        dpadCardImg.sprite = cachedDpadSprite;
        dpadCardImg.preserveAspect = true;
        dpadCardImg.color = Color.white;

        Button dpadBtn = dpadCardObj.AddComponent<Button>();
        ConfigureCardButtonColors(dpadBtn, dpadCardImg);
        dpadBtn.onClick.AddListener(() => HandleCardClicked(ModeButtons));

        // Seçili Kartın Sol Üst Köşesindeki Altın Yıldız Rozeti
        GameObject starObj = CreateUIObject("SelectedStarBadge", joyCardObj.transform);
        starBadgeRt = starObj.GetComponent<RectTransform>();
        starBadgeRt.anchorMin = new Vector2(0f, 1f);
        starBadgeRt.anchorMax = new Vector2(0f, 1f);
        starBadgeRt.pivot = new Vector2(0.5f, 0.5f);
        starBadgeRt.sizeDelta = new Vector2(38f, 38f);
        starBadgeRt.anchoredPosition = new Vector2(8f, -6f);

        Image starImg = starObj.AddComponent<Image>();
        starImg.sprite = cachedStarSprite;
        starImg.preserveAspect = true;
        starImg.raycastTarget = false;
    }

    private void HandleCardClicked(int selectedMode)
    {
        int normalized = (selectedMode == ModeJoystick) ? ModeJoystick : ModeButtons;
        PlayerPrefs.SetInt("ControlMode", normalized);
        PlayerPrefs.Save();

        RefreshVisuals(normalized);
        onModeSelected?.Invoke(normalized);
    }

    public void RefreshVisuals(int currentMode)
    {
        bool isJoystick = (currentMode == ModeJoystick);

        if (joystickCardImg != null)
        {
            joystickCardImg.color = isJoystick
                ? Color.white
                : new Color(0.93f, 0.90f, 0.95f, 0.90f);
        }
        if (dpadCardImg != null)
        {
            dpadCardImg.color = !isJoystick
                ? Color.white
                : new Color(0.93f, 0.90f, 0.95f, 0.90f);
        }

        if (joystickCardRt != null)
        {
            joystickCardRt.localScale = isJoystick
                ? new Vector3(1.02f, 1.02f, 1f)
                : new Vector3(0.97f, 0.97f, 1f);
        }
        if (dpadCardRt != null)
        {
            dpadCardRt.localScale = !isJoystick
                ? new Vector3(1.02f, 1.02f, 1f)
                : new Vector3(0.97f, 0.97f, 1f);
        }

        RectTransform activeCardRt = isJoystick ? joystickCardRt : dpadCardRt;
        if (starBadgeRt != null && activeCardRt != null)
        {
            starBadgeRt.SetParent(activeCardRt, false);
            starBadgeRt.anchorMin = new Vector2(0f, 1f);
            starBadgeRt.anchorMax = new Vector2(0f, 1f);
            starBadgeRt.pivot = new Vector2(0.5f, 0.5f);
            starBadgeRt.sizeDelta = new Vector2(38f, 38f);
            starBadgeRt.anchoredPosition = new Vector2(8f, -6f);
            starBadgeRt.localScale = Vector3.one;
            starBadgeRt.SetAsLastSibling();
            starBadgeRt.gameObject.SetActive(true);
        }
    }

    private static void HideLegacyControlButtons(Transform panelTr)
    {
        if (panelTr == null) return;
        string[] legacyNames = { "kontrolButton", "KontrolButton", "TusYeriButton", "KontrolLabel" };
        for (int i = 0; i < legacyNames.Length; i++)
        {
            Transform child = panelTr.Find(legacyNames[i]);
            if (child != null && child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    private static void ConfigureTamamButton(Transform panelTr)
    {
        if (panelTr == null) return;

        Transform closeBtnTr = panelTr.Find("Btn_Kapat");
        if (closeBtnTr == null) closeBtnTr = panelTr.Find("Btn_AyarlarKapat");
        if (closeBtnTr == null) return;

        RectTransform rt = closeBtnTr.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.195f, 0.162f);
            rt.anchorMax = new Vector2(0.805f, 0.246f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        TextMeshProUGUI tmp = closeBtnTr.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = "TAMAM";
        }
    }

    private static void ConfigureCardButtonColors(Button btn, Graphic targetGraphic)
    {
        btn.targetGraphic = targetGraphic;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.03f, 1.03f, 1.03f, 1f);
        cb.pressedColor = new Color(0.90f, 0.88f, 0.92f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
    }

    private static void EnsureSpritesLoaded()
    {
        if (cachedJoystickSprite == null)
            cachedJoystickSprite = LoadResourceSprite("UI/settings_card_joystick");
        if (cachedDpadSprite == null)
            cachedDpadSprite = LoadResourceSprite("UI/settings_card_dpad");
        if (cachedStarSprite == null)
            cachedStarSprite = LoadResourceSprite("UI/settings_kawaii_star");
    }

    private static Sprite LoadResourceSprite(string resourcePath)
    {
        Sprite s = Resources.Load<Sprite>(resourcePath);
        if (s != null) return s;

        Texture2D tex = Resources.Load<Texture2D>(resourcePath);
        if (tex != null)
        {
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        return null;
    }

    private static GameObject CreateUIObject(string objName, Transform parent)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one;
        return go;
    }

    private static void SetNormalizedRect(GameObject go, Vector2 min, Vector2 max)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }
}
