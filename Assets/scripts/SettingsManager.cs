using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsManager : MonoBehaviour
{
    [Header("UI Objeleri")]
    public GameObject ayarlarPaneli;
    public Slider sesSlider;

    [Header("Kontrol Ayarları")]
    [Tooltip("Kontrol modunu gösteren buton metni (Örn: TUŞLAR / JİROSKOP)")]
    public TextMeshProUGUI kontrolButonText;

    void Start()
    {
        // Ses ayarını yükle
        float kayitliSes = PlayerPrefs.GetFloat("OyunSesi", 1f);
        AudioListener.volume = kayitliSes;

        if (sesSlider != null)
        {
            sesSlider.value = kayitliSes;
        }

        // Kayıtlı kontrol modunu yükle ve metni güncelle
        int kayitliMod = PlayerPrefs.GetInt("ControlMode", 0);
        GuncelleKontrolText(kayitliMod);

        if (ayarlarPaneli != null)
        {
            ayarlarPaneli.SetActive(false);
        }
    }

    public void PaneliAc()
    {
        if (ayarlarPaneli != null) ayarlarPaneli.SetActive(true);
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
    /// Ayarlar panelindeki butona tıklandığında modları sırayla değiştirir.
    /// 0 = Tuşlar, 1 = Jiroskop (Eğim)
    /// </summary>
    public void KontrolModunuDegistir()
    {
        int mevcutMod = PlayerPrefs.GetInt("ControlMode", 0);
        int yeniMod = (mevcutMod == 0) ? 1 : 0;
        PlayerPrefs.SetInt("ControlMode", yeniMod);
        PlayerPrefs.Save();
        GuncelleKontrolText(yeniMod);
    }

    public void SetControlMode(int mode)
    {
        PlayerPrefs.SetInt("ControlMode", mode);
        PlayerPrefs.Save();
        GuncelleKontrolText(mode);
    }

    private void GuncelleKontrolText(int mode)
    {
        if (kontrolButonText != null)
        {
            kontrolButonText.text = (mode == 1) ? "JIROSKOP" : "TUŞ";
        }
    }
}
