# ZıpZıp 2D

![Unity](https://img.shields.io/badge/Unity-6000.6.4f1-000000?style=for-the-badge&logo=unity&logoColor=white)
![Language](https://img.shields.io/badge/C%23-.NET-512BD4?style=for-the-badge&logo=csharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Android%20%7C%20PC-3DDC84?style=for-the-badge&logo=android&logoColor=white)
![Multiplayer](https://img.shields.io/badge/Multiplayer-Supabase%20Realtime-3ECF8E?style=for-the-badge&logo=supabase&logoColor=white)

**ZıpZıp 2D**, piksel-art ve kawaii görsel tarzıyla geliştirilmiş, hem **Tek Oyunculu (Solo)** sonsuz tırmanış hem de **Gerçek Zamanlı 1v1 Online Düello** modlarına sahip hızlı tempolu bir 2D dikey platform oyunudur.

---

## Öne Çıkan Özellikler

### 1. Gerçek Zamanlı 1v1 Düello Modu (Online PvP & Akıllı Bot)
- **Hızlı Eşleşme & Özel Oda Kodu:** Rastgele rakiplerle anında eşleşme veya oda kodu paylaşarak arkadaşınla birebir düello.
- **Düşük Gecikmeli Senkronizasyon:** Supabase Realtime WebSocket (25 Hz broadcast) + RPC altyapısı ile akıcı rakip hareketleri, kalkan/kuş ayağı efektleri ve karakter üstü canlı isim/can göstergesi.
- **Deterministik Ortak Harita & İtemler:** Her iki oyuncu için birebir aynı platform dizilimi, coinler, güçlendirmeler ve uzay meteorları üretilir. Bir oyuncunun topladığı item anında diğer oyuncunun ekranından da kalkar.
- **Roket Sabotajı & Seyirci Modu:** Rakip roket aldığında ekranında uyarı belirir ve platform akışı hızlanır. Elenen oyuncu maç tamamlanana kadar rakibini canlı izleyebilir.
- **Piksel Arena PvP Skorboard:** Canlı karakter portreleri, piksel kalpler, çekişme (*tug-of-war*) momentum barı, anlık puan farkı rozeti (`+Δ / -Δ`) ve öndeki oyuncunun üzerinde süzülen liderlik tacı.
- **Fizik Tabanlı Akıllı Yapay Zeka (Bot):** Çevrimiçi rakip bulunamadığında devreye giren; gerçek `Rigidbody2D` fiziğiyle zıplayan, platform kenarlarına tutunup tırmanan, meteorlardan kaçan ve yerdeki güçlendirmeleri toplayan rekabetçi bot.

### 2. Dinamik Gökyüzü Evreleri & Uzay Tehlikeleri
- Skor ilerledikçe **Gündüz → Gün Batımı → Gece → Derin Uzay** atmosfer geçişleri.
- **Uzay Evresi:** Neon uzay platformları ve uyarı işaretleriyle yukarıdan düşen **Uzay Meteorları**.

### 3. Güçlendirmeler ve Ekonomi

| İtem | Etki |
| :--- | :--- |
| **Roket** | Karakteri yüksek hızla yukarı taşır; 1v1 düelloda rakibin oyun hızını artırarak sabotaj uygular. |
| **Koruyucu Kalkan** | Aşağı düşmeye veya uzay meteoru çarpmasına karşı tek seferlik tam koruma sağlar. |
| **Kuş Ayağı (SuperFeet)** | Zıplama gücünü `1.82x` artırır ve yükselirken platformların altından geçerek tek sıçramada 2 platform birden çıkmayı sağlar. |
| **Can Paketi** | Eksik canı yeniler. |
| **Altın Coin** | Mağazadan karakter açmak veya tek oyunculu modda kaldığın yerden devam etmek (*Revive*) için kullanılır. |

### 4. Karakter Mağazası & Selfie Yüz Stüdyosu
- **Açılabilir Karakterler:** *Pink Man*, *Ninja Frog*, *Mask Dude* ve *Virtual Guy* kostümleri + her karaktere özel köşe tutunma (`corner_hang`) animasyonları.
- **Selfie Yüz Stüdyosu:** Oyuncunun kendi fotoğrafını çekip/yükleyip seçtiği karakterin yüzüne entegre edebildiği kişiselleştirme stüdyosu.

### 5. Global Liderlik Tablosu & Esnek Kontroller
- **Çevrimiçi Skor Tablosu:** Oyuncu profili oluşturma, benzersiz takma ad belirleme ve dünya sıralamasında en yüksek skorları görüntüleme.
- **Çift Mobil Kontrol Seçeneği:** Ayarlar menüsündeki Kawaii kontrol kartlarından **Yön Tuşları** (sağ/sol el yerleşimi değiştirilebilir) veya **Sanal Joystick** seçimi.
- **Hassas Platform Fiziği:** *Coyote Time*, *Jump Buffering*, *Squash & Stretch* sıçrama animasyonları, köşe tutunma ve ekran kenarlarından geçiş (*Screen Wrap*).

---

## Kontroller

| Platform | Hareket | Zıplama | Duraklatma |
| :--- | :--- | :--- | :--- |
| **Mobil (Yön Tuşları)** | Sol / Sağ Butonları | Zıpla Butonu | Sağ Üst Duraklat Butonu / Geri Tuşu |
| **Mobil (Joystick)** | Sol Alt Analog Joystick | Otomatik / Yönlü Sıçrama & Zıplama | Sağ Üst Duraklat Butonu / Geri Tuşu |
| **PC / Editör** | `A` / `D` veya `Sol` / `Sağ` Ok | `Space` / `W` / `Yukarı` Ok | `ESC` |

---

## Proje Yapısı

```text
Assets/
├── Scenes/
│   ├── MenuScene.unity          # Ana menü, mağaza, liderlik tablosu ve 1v1 lobi ekranı
│   └── SampleScene.unity        # Ana oynanış ve 1v1 düello sahnesi
├── Resources/
│   ├── Characters/              # Karakter sprite dizileri ve köşe tutunma (corner_hang) görselleri
│   ├── PowerUps/                # Güçlendirme ikonları ve efekt görselleri
│   ├── Sky/                     # Gökyüzü katmanları, yıldızlar ve bulutlar
│   └── UI/                      # Kawaii & Piksel-Art arayüz çerçeveleri, butonlar ve PvP skorboard
└── scripts/
    ├── GameManager.cs           # Oyun döngüsü, hız ölçekleme, skor, can ve güçlendirme yönetimi
    ├── PlayerController.cs      # Karakter fiziği, ekran sarma, köşe tutunma ve girdi yönetimi
    ├── MultiplayerManager.cs    # 1v1 WebSocket/RPC senkronizasyonu, PvP HUD ve Bot yapay zekası
    ├── PlatformSpawner.cs       # Deterministik/dinamik platform, coin ve item üretimi
    ├── PlatformMovement.cs      # Yatay/dikey platform hareketi ve tek yönlü geçiş fiziği
    ├── SkyCycleManager.cs       # Gündüz-gece-uzay gökyüzü döngüsü
    ├── SpaceMeteorHazard.cs     # Uzay evresi meteor spawner ve çarpışma sistemi
    ├── ShopMenuUI.cs            # Karakter mağazası arayüzü
    ├── SelfieFaceStudio.cs      # Özel yüz kırpma ve karaktere uygulama stüdyosu
    ├── KawaiiControlSelectorUI.cs # Yön tuşları / Joystick seçim kartları
    └── LeaderboardService.cs    # Global skor tablosu servis bağlantısı
```

---

## Kurulum ve Çalıştırma

1. **Gereksinimler:** **Unity 6 (`6000.6.4f1` veya uyumlu 6000.x sürümü)** ve Universal Render Pipeline (URP 2D).
2. Depoyu klonlayın:
   ```bash
   git clone https://github.com/TamerCelik/Z-pZ-p-2D.git
   ```
3. **Unity Hub** üzerinden projeyi ekleyin ve açın.
4. `Assets/Scenes/MenuScene.unity` sahnesini açıp **Play** tuşuna basarak oyunu tüm menü, mağaza, solo ve 1v1 düello özellikleriyle başlatabilirsiniz.

---

## Geliştirici

- **Tamer Çelik** — [GitHub (@TamerCelik)](https://github.com/TamerCelik)
