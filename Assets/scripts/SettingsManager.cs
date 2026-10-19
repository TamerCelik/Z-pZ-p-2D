using UnityEngine;
using UnityEngine.UI; // Slider kullanabilmek için bu kütüphane þart

public class SettingsManager : MonoBehaviour
{
    [Header("UI Objeleri")]
    public GameObject ayarlarPaneli;
    public Slider sesSlider;

    void Start()
    {
        // PC hafýzasýndaki ses ayarýný çek (Yoksa 1 yani %100 kabul et)
        float kayitliSes = PlayerPrefs.GetFloat("OyunSesi", 1f);

        // Oyunun genel sesini ayarla
        AudioListener.volume = kayitliSes;

        // Slider çubuðunu kayýtlý sese göre güncelle
        if (sesSlider != null)
        {
            sesSlider.value = kayitliSes;
        }

        // Oyun baþlarken ayarlar paneli kapalý dursun
        if (ayarlarPaneli != null)
        {
            ayarlarPaneli.SetActive(false);
        }
    }

    public void PaneliAc()
    {
        ayarlarPaneli.SetActive(true);
    }

    public void PaneliKapat()
    {
        ayarlarPaneli.SetActive(false);
    }

    public void SesAyarla(float yeniSes)
    {
        // Genel sesi deðiþtir
        AudioListener.volume = yeniSes;

        // Yeni ses seviyesini kaydet ki oyunu kapatýp açýnca hatýrlasýn
        PlayerPrefs.SetFloat("OyunSesi", yeniSes);
    }
}