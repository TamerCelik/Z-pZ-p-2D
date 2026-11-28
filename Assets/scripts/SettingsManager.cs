using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    [Header("UI Objeleri")]
    public GameObject ayarlarPaneli;
    public Slider sesSlider;

    [Header("Kontrol Ayarları")]
    [Tooltip("Kontrol modunu gösteren buton metni (Örn: YÖN TUŞLARI / JOYSTİCK)")]
    public TextMeshProUGUI kontrolButonText;
    [Tooltip("Yön tuşlarının sağda mı solda mı olduğunu gösteren buton metni")]
    public TextMeshProUGUI tusYeriButonText;

    private KawaiiControlSelectorUI kawaiiControlSelector;

    void Start()
    {
        // Ses ayarını yükle
        float kayitliSes = PlayerPrefs.GetFloat("OyunSesi", 1f);
        AudioListener.volume = kayitliSes;

        if (sesSlider != null)
        {
            sesSlider.value = kayitliSes;
        }

        // Kayıtlı kontrol modunu yükle (0 = Yön Tuşları, 2 = Joystick; eski Jiroskop=1 otomatik temizlenir)
        int kayitliMod = KawaiiControlSelectorUI.GetNormalizedControlMode();
        GuncelleKontrolText(kayitliMod);

        int kayitliDuzen = PlayerPrefs.GetInt("ButtonLayout", 0);
        GuncelleTusYeriText(kayitliDuzen);

        if (ayarlarPaneli != null)
        {
            kawaiiControlSelector = KawaiiControlSelectorUI.Attach(ayarlarPaneli, SetControlMode);
            ayarlarPaneli.SetActive(false);
        }
    }

    public void PaneliAc()
    {
        if (ayarlarPaneli != null)
        {
            if (kawaiiControlSelector == null)
            {
                kawaiiControlSelector = KawaiiControlSelectorUI.Attach(ayarlarPaneli, SetControlMode);
            }
            ayarlarPaneli.SetActive(true);
            kawaiiControlSelector.RefreshVisuals(KawaiiControlSelectorUI.GetNormalizedControlMode());
        }
    }

    public void PaneliKapat()
    {
        if (ayarlarPaneli != null) ayarlarPaneli.SetActive(false);
    }

    public void SesAyarla(float yeniSes)
    {
        AudioListener.volume = yeniSes;
        PlayerPrefs.SetFloat("OyunSesi", yeniSes);
    }

    /// <summary>
    /// Kontrol modunu Yön Tuşları (0) ve Joystick (2) arasında değiştirir.
    /// </summary>
    public void KontrolModunuDegistir()
    {
        int mevcutMod = KawaiiControlSelectorUI.GetNormalizedControlMode();
        int yeniMod = (mevcutMod == KawaiiControlSelectorUI.ModeJoystick)
            ? KawaiiControlSelectorUI.ModeButtons
            : KawaiiControlSelectorUI.ModeJoystick;
        SetControlMode(yeniMod);
    }

    public void SetControlMode(int mode)
    {
        int normalized = (mode == KawaiiControlSelectorUI.ModeJoystick)
            ? KawaiiControlSelectorUI.ModeJoystick
            : KawaiiControlSelectorUI.ModeButtons;
        PlayerPrefs.SetInt("ControlMode", normalized);
        PlayerPrefs.Save();
        GuncelleKontrolText(normalized);
        if (kawaiiControlSelector != null)
        {
            kawaiiControlSelector.RefreshVisuals(normalized);
        }
    }

    private void GuncelleKontrolText(int mode)
    {
        if (kontrolButonText != null)
        {
            kontrolButonText.text = (mode == KawaiiControlSelectorUI.ModeJoystick) ? "JOYSTİCK" : "YÖN TUŞLARI";
        }
    }

    public void TusYeriniDegistir()
    {
        int mevcutDuzen = PlayerPrefs.GetInt("ButtonLayout", 0);
        int yeniDuzen = (mevcutDuzen == 0) ? 1 : 0;
        PlayerPrefs.SetInt("ButtonLayout", yeniDuzen);
        PlayerPrefs.Save();
        GuncelleTusYeriText(yeniDuzen);
    }

    private void GuncelleTusYeriText(int layout)
    {
        if (tusYeriButonText != null)
        {
            tusYeriButonText.text = (layout == 1) ? "YON TUSLARI: SAG" : "YON TUSLARI: SOL";
        }
    }
}
