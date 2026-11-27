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
    [Tooltip("Yön tuşlarının sağda mı solda mı olduğunu gösteren buton metni")]
    public TextMeshProUGUI tusYeriButonText;

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

        int kayitliDuzen = PlayerPrefs.GetInt("ButtonLayout", 0);
        GuncelleTusYeriText(kayitliDuzen);

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
    /// 0 = Tuşlar, 1 = Jiroskop (Eğim), 2 = Analog
    /// </summary>
    public void KontrolModunuDegistir()
    {
        int mevcutMod = PlayerPrefs.GetInt("ControlMode", 0);
        int yeniMod = (mevcutMod + 1) % 3;
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
            if (mode == 1) kontrolButonText.text = "JİROSKOP";
            else if (mode == 2) kontrolButonText.text = "ANALOG";
            else kontrolButonText.text = "TUŞ";
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
