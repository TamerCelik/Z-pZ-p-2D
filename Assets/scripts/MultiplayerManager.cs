using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 2 Kişilik Online Düello (Supabase Oda Kodu + Hızlı Eşleşme + Bot Test Modu):
/// - Ana menüde OYNA butonuna basılınca OYNA butonunun hemen üstünde TEKLİ ve ÇİFTLİ seçenekleri açılır.
/// - TEKLİ seçilirse normal tek kişilik oyun başlar; ÇİFTLİ seçilirse 2 Kişilik Düello Lobisi açılır.
/// - İki oyuncu aynı harita tohumuyla (seed) aynı anda ve 3 canla başlar; 50 Coin Devam Et kapalıdır.
/// - Karakterler birbirinin içinden geçer.
/// - Roket Sabotajı (Seçenek A): Roketi alan oyuncu 3.2 sn güvende uçar; rakip için oyun 1.38x hızlanır.
/// - 3 canı biten oyuncu yaşayan rakibini canlı izleyebilir (Spectator Mode) veya istediği an maçtan çıkabilir.
/// </summary>
public class MultiplayerManager : MonoBehaviour
{
    private const string SupabaseUrl = "https://bgnjapojaevoqhwbndye.supabase.co";
    private const string SupabasePublishableKey = "sb_publishable_UGQr2dJjLeF3Yw_OPpFD7w_xQ4Ja3B1";
    private const string SupabaseRealtimeWsUrl = "wss://bgnjapojaevoqhwbndye.supabase.co/realtime/v1/websocket?apikey=" + SupabasePublishableKey + "&vsn=1.0.0";

    public const float DuelRocketSpeedMultiplier = 1.38f;
    public const int DuelWinCoinReward = 30;

    [Serializable]
    private class WsStateData
    {
        public string s;
        public int seq;
        public float t;
        public float x;
        public float y;
        public float vx;
        public float vy;
        public int plat;
        public int hp;
        public int sc;
        public bool alv;
        public bool rkt;
        public bool trig_rkt;
        public bool sf;
        public bool sh;
        public bool jmp;
        public string nm;
        public string ch;
        public int sd;
    }

    [Serializable]
    private class WsIncomingOuter
    {
        public string type;
        public WsStateData payload;
    }

    [Serializable]
    private class WsIncomingEnvelope
    {
        public string topic;
        public WsIncomingOuter payload;
    }

    [Serializable]
    private class CreateRoomPayload
    {
        public string p_secret;
        public string p_name;
        public string p_char;
        public bool p_is_public;
    }

    [Serializable]
    private class JoinRoomPayload
    {
        public string p_secret;
        public string p_name;
        public string p_char;
        public string p_room_code;
    }

    [Serializable]
    private class SyncStatePayload
    {
        public string p_room_code;
        public string p_secret;
        public float p_x;
        public float p_y;
        public float p_vy;
        public int p_health;
        public int p_score;
        public bool p_alive;
        public bool p_trigger_rocket;
        public bool p_superfeet;
        public bool p_shield;
    }

    [Serializable]
    private class LeaveRoomPayload
    {
        public string p_room_code;
        public string p_secret;
    }

    [Serializable]
    public class RoomStateResponse
    {
        public bool success;
        public string error;
        public string message;
        public string room_code;
        public int seed;
        public string status;
        public bool is_host;
        public string opp_name;
        public string opp_char;
        public float opp_x;
        public float opp_y;
        public float opp_vy;
        public int opp_health;
        public int opp_score;
        public bool opp_alive;
        public bool opp_rocket;
        public bool opp_superfeet;
        public bool opp_shield;
        public string winner_name;
    }

    public static MultiplayerManager Instance { get; private set; }

    public static bool IsDuelActive => Instance != null && Instance.inActiveDuel;
    public static bool IsSpectating => Instance != null && Instance.inActiveDuel && Instance.localEliminated && !Instance.duelFinished;
    public static bool IsOpponentRocketSabotageActive => Instance != null && Instance.inActiveDuel && Instance.opponentRocketTimer > 0f;
    public static float DuelMatchElapsedTime { get; private set; }

    public string CurrentRoomCode { get; private set; } = string.Empty;
    public int SharedSeed { get; private set; } = 12345;
    public bool IsHost { get; private set; }
    public bool IsBotMatch { get; private set; }

    public string OpponentName { get; private set; } = "Rakip";
    public string OpponentCharId { get; private set; } = CharacterShopData.NinjaFrogId;
    public Vector2 OpponentTargetPos { get; private set; }
    public float OpponentVy { get; private set; }
    public int OpponentHealth { get; private set; } = 3;
    public int OpponentScore { get; private set; }
    public bool OpponentAlive { get; private set; } = true;
    public bool OpponentSuperFeet { get; private set; }
    public bool OpponentShield { get; private set; }

    private bool inActiveDuel;
    private bool localEliminated;
    private bool duelFinished;
    private bool rewardGranted;
    private bool pendingLocalRocketTrigger;
    private float opponentRocketTimer;
    private float syncPollingTimer;
    private bool syncInFlight;

    // Bot gerçek fizik & yapay zeka simülasyonu
    private float botSimTime;
    private float botExactScore;
    private float botNextRocketTime;
    private float botNextMistakeTime;
    private float botMistakeTimer;
    private float botMistakeOffset;
    private float botSelfRocketTimer;
    private Rigidbody2D botRb;
    private BoxCollider2D botCollider;
    private Transform botFeetTransform;
    private GameObject botRocketFlameObj;
    private SpriteRenderer botRocketFlameRenderer;
    private LayerMask botGroundMask = 1 << 6;
    private float botMoveSpeed = 5.5f;
    private float botJumpForce = 5.2f;
    private float botBaseGravityScale = 1.5f;
    private float botFootRadius = 0.28f;
    private float botWrapThreshold = 3.0f;
    private float botCurrentMoveInput;
    private float botJumpCooldown;
    private bool botGrounded;
    private bool botWasGrounded = true;
    private bool botPlacedOnStartPlatform;
    private Vector2 botVisualStretch = Vector2.one;

    // Rakip karakter objesi ve Gerçek Zamanlı WebSocket + Sıfır-Işınlanma Senkronizasyonu
    private GameObject opponentAvatarObj;
    private SpriteRenderer opponentSpriteRenderer;
    private TextMeshPro opponentNameTagTmp;
    private Sprite[] opponentIdleFrames;
    private Sprite[] opponentCornerFrames;
    private float opponentAnimTimer;
    private float opponentCornerAnimTimer;
    private int opponentAnimFrame;
    private float opponentFacing = 1f;
    private Vector2 remoteSmoothVel;
    private Vector2 remoteRenderPos;
    private bool remotePlacedOnStartPlat = false;
    private int remotePacketPlatIdx = 0;
    private float remotePacketX = 0f;
    private float remotePacketY = -1.5f;
    private float remotePacketVx = 0f;
    private float remotePacketVy = 0f;
    private float remoteWorldYOffset = 0f;
    private bool remoteHasWorldYOffset = false;
    private float lastRemotePacketTime = 0f;
    private bool remoteRocketActive = false;
    private int remoteLastSeq = 0;

    // Supabase Realtime WebSocket (25 Hz, ~50ms gecikme)
    private ClientWebSocket realtimeWs;
    private CancellationTokenSource wsCts;
    private readonly ConcurrentQueue<string> wsIncomingQueue = new ConcurrentQueue<string>();
    private volatile bool wsJoinedTopic = false;
    private volatile bool wsSendBusy = false;
    private string wsConnectedRoomCode = string.Empty;
    private int wsMsgRefCounter = 1;
    private int localBroadcastSeq = 0;
    private float wsBroadcastTimer = 0f;
    private float wsHeartbeatTimer = 15f;
    private float lastWsPacketReceivedTime = -99f;
    private bool prevLocalGroundedForNet = true;
    private bool pendingLocalJumpNet = false;
    private bool pendingWsRocketTrigger = false;

    // Menü UI
    private TMP_FontAsset arcadeFont;
    private Material arcadeFontMaterial;
    private Sprite cardSprite;
    private Sprite pillSprite;
    private Sprite btnGoldSprite;
    private Sprite btnPinkSprite;
    private Sprite btnCyanSprite;
    private Sprite btnRedSprite;
    private Sprite btnGreenSprite;
    private GameObject playModeSelectorObj;
    private GameObject lobbyModalObj;
    private TextMeshProUGUI lobbyStatusText;
    private TextMeshProUGUI lobbyRoomCodeBigText;
    private TMP_InputField joinCodeInput;
    private Coroutine lobbyWaitCoroutine;

    // Oyun İçi Düello HUD
    private GameObject duelTopHudObj;
    private TextMeshProUGUI duelMyStatusText;
    private TextMeshProUGUI duelOppStatusText;
    private GameObject sabotageBannerObj;
    private TextMeshProUGUI sabotageBannerText;
    private GameObject spectatorOverlayObj;
    private TextMeshProUGUI spectatorInfoText;
    private GameObject duelResultModalObj;
    private TextMeshProUGUI duelResultTitleText;
    private TextMeshProUGUI duelResultSummaryText;
    private TextMeshProUGUI duelResultRewardText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        EnsureInstance();
    }

    public static MultiplayerManager EnsureInstance()
    {
        if (Instance != null) return Instance;
        GameObject go = new GameObject("MultiplayerManager");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<MultiplayerManager>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        SetupSceneUI(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        CloseRealtimeSocket();
        if (Instance == this) Instance = null;
    }

    private void OnApplicationQuit()
    {
        CloseRealtimeSocket();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        playModeSelectorObj = null;
        lobbyModalObj = null;

        if (inActiveDuel && scene.name == "SampleScene")
        {
            DuelMatchElapsedTime = 0f;
            UnityEngine.Random.InitState(SharedSeed);
            SetupGameplayDuelScene();
        }
        else
        {
            if (scene.name == "MenuScene")
            {
                inActiveDuel = false;
                localEliminated = false;
                duelFinished = false;
            }
            SetupSceneUI(scene.name);
        }
    }

    private void Update()
    {
        // Lobi dahil her sahnede gelen canlı WebSocket mesajlarını işle ve heartbeat gönder
        PumpRealtimeWebSocket(Time.unscaledDeltaTime);

        if (!inActiveDuel || SceneManager.GetActiveScene().name != "SampleScene")
            return;

        float dt = Time.deltaTime;
        DuelMatchElapsedTime += dt;

        if (opponentRocketTimer > 0f)
        {
            opponentRocketTimer -= dt;
            if (sabotageBannerObj != null)
            {
                sabotageBannerObj.SetActive(true);
                float pulse = 0.92f + 0.08f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 10f));
                sabotageBannerObj.transform.localScale = new Vector3(pulse, pulse, 1f);
                if (sabotageBannerText != null)
                {
                    sabotageBannerText.text = $"RAKIP ROKET ALDI! HIZLANDI ({Mathf.CeilToInt(opponentRocketTimer)}sn)";
                }
            }
        }
        else if (sabotageBannerObj != null && sabotageBannerObj.activeSelf)
        {
            sabotageBannerObj.SetActive(false);
        }

        if (IsBotMatch)
        {
            UpdateBotOpponentSimulation(dt);
        }
        else
        {
            EnsureRealtimeSocketConnected(CurrentRoomCode);

            // Yerel zıplama anını yakala ki 40ms paketler arasında bile zıplama kaçmasın
            GameManager gmLocal = GameManager.Instance;
            PlayerController pcLocal = (gmLocal != null && gmLocal.player != null) ? gmLocal.player.GetComponent<PlayerController>() : null;
            Rigidbody2D rbLocal = (gmLocal != null && gmLocal.player != null) ? gmLocal.player.GetComponent<Rigidbody2D>() : null;
            if (pcLocal != null)
            {
                bool nowGrounded = pcLocal.IsGrounded;
                if (prevLocalGroundedForNet && !nowGrounded && rbLocal != null && rbLocal.linearVelocity.y > 1.2f)
                {
                    pendingLocalJumpNet = true;
                }
                prevLocalGroundedForNet = nowGrounded;
            }

            // 1) Birincil Kanal: 25 Hz (her 0.04 sn) düşük gecikmeli (~50ms) Supabase Realtime WebSocket Broadcast!
            wsBroadcastTimer -= Time.unscaledDeltaTime;
            if (wsBroadcastTimer <= 0f)
            {
                wsBroadcastTimer = 0.04f;
                SendGameplayWsBroadcast();
            }

            // 2) İkincil Kanal: Oda skor/durum kaydı (ve WebSocket kapalıysa yedek konum senkronu)
            bool wsLive = wsJoinedTopic && (Time.unscaledTime - lastWsPacketReceivedTime < 1.2f);
            syncPollingTimer -= Time.unscaledDeltaTime;
            if (syncPollingTimer <= 0f && !syncInFlight)
            {
                syncPollingTimer = wsLive ? 0.75f : 0.08f;
                StartCoroutine(SyncMatchStateRoutine());
            }
        }

        UpdateOpponentAvatarVisuals(dt);
        UpdateDuelHudTexts();
        CheckDuelCompletion();
    }

    // =========================================================================
    // 1. OYUN İÇİ OLAY BİLDİRİMLERİ (GAMEMANAGER ENTEGRASYONU)
    // =========================================================================
    public void NotifyLocalRocketPickedUp()
    {
        if (!inActiveDuel) return;
        pendingLocalRocketTrigger = true;
        pendingWsRocketTrigger = true;
        if (!IsBotMatch) SendGameplayWsBroadcast();
    }

    public void OnLocalPlayerEliminated(int finalLocalScore)
    {
        if (!inActiveDuel || localEliminated) return;
        localEliminated = true;

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            if (gm.player != null) gm.player.SetActive(false);
            if (gm.pauseButton != null) gm.pauseButton.SetActive(false);

            MobileButton[] mobileButtons = FindObjectsByType<MobileButton>(FindObjectsSortMode.None);
            for (int i = 0; i < mobileButtons.Length; i++)
            {
                if (mobileButtons[i] != null) mobileButtons[i].gameObject.SetActive(false);
            }
        }

        LeaderboardService.EnsureInstance().SubmitScore(finalLocalScore);

        if (OpponentAlive)
        {
            Time.timeScale = 1f;
            if (spectatorOverlayObj != null)
            {
                spectatorOverlayObj.transform.SetAsLastSibling();
                spectatorOverlayObj.SetActive(true);
            }
        }
        else
        {
            ShowDuelResultScreen();
        }
    }

    public void LeaveDuelAndReturnToMenu()
    {
        CloseRealtimeSocket();
        if (!string.IsNullOrEmpty(CurrentRoomCode) && !IsBotMatch)
        {
            StartCoroutine(LeaveRoomRoutine(CurrentRoomCode));
        }

        inActiveDuel = false;
        localEliminated = false;
        duelFinished = false;
        CurrentRoomCode = string.Empty;
        Time.timeScale = 1f;
        SceneManager.LoadScene("MenuScene");
    }

    // =========================================================================
    // 2. ANA MENÜ: "OYNA" ÜSTÜNDE TEKLİ / ÇİFTLİ SEÇİMİ & LOBİ PENCERESİ
    // =========================================================================
    private void SetupSceneUI(string sceneName)
    {
        EnsureStyleAssets();
        if (sceneName != "MenuScene") return;

        Canvas canvas = FindPrimaryCanvas();
        if (canvas == null) return;

        // Eski ana menü düello butonu varsa temizle
        Transform oldBtn = canvas.transform.Find("MainMenuDuelButton");
        if (oldBtn != null) Destroy(oldBtn.gameObject);

        if (playModeSelectorObj == null)
        {
            BuildPlayModeSelector(canvas.transform);
        }
        if (lobbyModalObj == null)
        {
            BuildLobbyModal(canvas.transform);
        }
    }

    /// <summary>
    /// Ana menüde OYNA butonuna basıldığında hemen üstünde TEKLİ ve ÇİFTLİ seçeneklerini açar/kapatır.
    /// </summary>
    public void TogglePlayModeSelector()
    {
        Canvas canvas = FindPrimaryCanvas();
        if (canvas == null) return;

        if (playModeSelectorObj == null)
        {
            BuildPlayModeSelector(canvas.transform);
        }
        if (playModeSelectorObj == null) return;

        bool nextState = !playModeSelectorObj.activeSelf;
        if (nextState)
        {
            playModeSelectorObj.transform.SetAsLastSibling();
        }
        playModeSelectorObj.SetActive(nextState);
    }

    private void BuildPlayModeSelector(Transform canvasTr)
    {
        // PlayButton (x = 0.2135 .. 0.7865, y = 0.4844 .. 0.5361) hemen üstünde
        // Ana menü butonlarıyla birebir aynı piksel-art stilde TEKLI ve CIFTLI butonları
        playModeSelectorObj = CreateUIObject("PlayModeSelectorBar", canvasTr);
        SetAnchors(playModeSelectorObj, new Vector2(0.18f, 0.546f), new Vector2(0.82f, 0.602f));

        // Sol: TEKLİ (Ana menü OYNA sarı-altın piksel buton stili)
        CreatePixelMenuButton(
            playModeSelectorObj.transform,
            "SinglePlayerBtn",
            "TEKLİ",
            new Vector2(0.00f, 0.00f),
            new Vector2(0.48f, 1.00f),
            btnGoldSprite != null ? btnGoldSprite : btnGreenSprite,
            () =>
            {
                inActiveDuel = false;
                localEliminated = false;
                duelFinished = false;
                Time.timeScale = 1f;
                SceneManager.LoadScene(1);
            });

        // Sağ: ÇİFTLİ (Ana menü AYARLAR turkuaz piksel buton stili)
        CreatePixelMenuButton(
            playModeSelectorObj.transform,
            "DualPlayerBtn",
            "ÇİFTLİ",
            new Vector2(0.52f, 0.00f),
            new Vector2(1.00f, 1.00f),
            btnCyanSprite,
            () =>
            {
                if (playModeSelectorObj != null) playModeSelectorObj.SetActive(false);
                OpenLobbyModal();
            });

        playModeSelectorObj.SetActive(false);
    }

    private void BuildLobbyModal(Transform canvasTr)
    {
        lobbyModalObj = CreateUIObject("MultiplayerLobbyModal", canvasTr);
        SetAnchors(lobbyModalObj, Vector2.zero, Vector2.one);

        Image dim = lobbyModalObj.AddComponent<Image>();
        dim.color = new Color(0.04f, 0.06f, 0.14f, 0.82f);

        GameObject card = CreateUIObject("LobbyCard", lobbyModalObj.transform);
        SetAnchors(card, new Vector2(0.055f, 0.14f), new Vector2(0.945f, 0.86f));
        Image cardBg = card.AddComponent<Image>();
        cardBg.sprite = cardSprite;
        cardBg.type = Image.Type.Sliced;
        cardBg.color = Color.white;

        // Üst Başlık Şeridi (Ana menü altın buton stili)
        GameObject header = CreateUIObject("Header", card.transform);
        SetAnchors(header, new Vector2(0.04f, 0.855f), new Vector2(0.96f, 0.962f));
        Image hBg = header.AddComponent<Image>();
        hBg.sprite = btnGoldSprite != null ? btnGoldSprite : pillSprite;
        hBg.type = Image.Type.Sliced;
        hBg.color = Color.white;

        CreateLabel(header.transform, "Title", "2 KİŞİLİK DÜELLO",
            new Vector2(0.05f, 0.12f), new Vector2(0.83f, 0.92f), 13f, 22f, TextAlignmentOptions.Center, Color.white, false);

        // Sağ Üst Kapat (X) Butonu (Ana menü kırmızı buton stili)
        CreatePixelMenuButton(header.transform, "CloseXBtn", "X",
            new Vector2(0.85f, 0.10f), new Vector2(0.98f, 0.90f), btnRedSprite, CloseLobbyModal);

        // Oda Kodu Gösterge Kutusu (Ana menü pembe buton stili)
        GameObject codeBox = CreateUIObject("RoomCodeBox", card.transform);
        SetAnchors(codeBox, new Vector2(0.06f, 0.65f), new Vector2(0.94f, 0.825f));
        Image codeBg = codeBox.AddComponent<Image>();
        codeBg.sprite = btnPinkSprite != null ? btnPinkSprite : cardSprite;
        codeBg.type = Image.Type.Sliced;
        codeBg.color = Color.white;

        lobbyRoomCodeBigText = CreateLabel(codeBox.transform, "RoomCodeBig", "ODA KODU: ----",
            new Vector2(0.04f, 0.46f), new Vector2(0.96f, 0.92f), 14f, 22f, TextAlignmentOptions.Center, Color.white, false);

        lobbyStatusText = CreateLabel(codeBox.transform, "StatusTxt", "Oda kur, koda katıl veya hızlı rakip ara!",
            new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.46f), 9.5f, 13.5f, TextAlignmentOptions.Center, Color.white, false);

        // 1. ODA KUR (Yeşil Piksel Buton) & 2. HIZLI EŞLEŞME (Altın Piksel Buton)
        CreatePixelMenuButton(card.transform, "CreateRoomBtn", "ODA KUR",
            new Vector2(0.06f, 0.50f), new Vector2(0.485f, 0.62f), btnGreenSprite, OnClickCreateRoom);

        CreatePixelMenuButton(card.transform, "QuickMatchBtn", "RAKİP ARA",
            new Vector2(0.515f, 0.50f), new Vector2(0.94f, 0.62f), btnGoldSprite, OnClickQuickMatch);

        // 3. ODAYA KATIL (4 Haneli Kod Girişi + Turkuaz Piksel Katıl Butonu)
        GameObject inputObj = CreateUIObject("JoinCodeInput", card.transform);
        SetAnchors(inputObj, new Vector2(0.06f, 0.345f), new Vector2(0.54f, 0.468f));
        Image inBg = inputObj.AddComponent<Image>();
        inBg.sprite = cardSprite;
        inBg.type = Image.Type.Sliced;
        inBg.color = Color.white;

        GameObject textArea = CreateUIObject("TextArea", inputObj.transform);
        SetAnchors(textArea, new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.88f));
        textArea.AddComponent<RectMask2D>();

        TextMeshProUGUI ph = CreateLabel(textArea.transform, "Placeholder", "KOD GİR",
            Vector2.zero, Vector2.one, 11f, 17f, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.92f), false);
        ph.enableAutoSizing = false;
        ph.fontSize = 16f;

        TextMeshProUGUI inTxt = CreateLabel(textArea.transform, "Text", "",
            Vector2.zero, Vector2.one, 12f, 20f, TextAlignmentOptions.Center, Color.white, false);
        inTxt.enableAutoSizing = false;
        inTxt.fontSize = 18f;

        inputObj.SetActive(false);
        joinCodeInput = inputObj.AddComponent<TMP_InputField>();
        joinCodeInput.textViewport = textArea.GetComponent<RectTransform>();
        joinCodeInput.textComponent = inTxt;
        joinCodeInput.placeholder = ph;
        joinCodeInput.characterLimit = 6;
        joinCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        inputObj.SetActive(true);

        CreatePixelMenuButton(card.transform, "JoinRoomBtn", "ODAYA KATIL",
            new Vector2(0.565f, 0.345f), new Vector2(0.94f, 0.468f), btnCyanSprite, OnClickJoinByCode);

        // 4. BOT RAKİPLE HEMEN DENE (Turkuaz Piksel Buton)
        CreatePixelMenuButton(card.transform, "BotDuelBtn", "BOT RAKİPLE OYNA",
            new Vector2(0.06f, 0.195f), new Vector2(0.94f, 0.315f), btnCyanSprite, StartBotPracticeDuel);

        // 5. ALT ÇIKIŞ / GERİ DÖN BUTONU (Ana menü kırmızı piksel buton stili)
        CreatePixelMenuButton(card.transform, "ExitLobbyBtn", "ÇIKIŞ / GERİ",
            new Vector2(0.16f, 0.05f), new Vector2(0.84f, 0.165f), btnRedSprite, CloseLobbyModal);

        lobbyModalObj.SetActive(false);
    }

    public void OpenLobbyModal()
    {
        if (lobbyModalObj == null)
        {
            Canvas c = FindPrimaryCanvas();
            if (c != null) BuildLobbyModal(c.transform);
        }
        if (lobbyModalObj == null) return;

        if (playModeSelectorObj != null) playModeSelectorObj.SetActive(false);

        lobbyModalObj.transform.SetAsLastSibling();
        lobbyModalObj.SetActive(true);
        if (lobbyRoomCodeBigText != null) lobbyRoomCodeBigText.text = "ODA KODU: ----";
        if (lobbyStatusText != null) lobbyStatusText.text = "Oda kur, kod girerek katıl veya hızlı rakip ara!";
    }

    private void CloseLobbyModal()
    {
        if (lobbyWaitCoroutine != null)
        {
            StopCoroutine(lobbyWaitCoroutine);
            lobbyWaitCoroutine = null;
        }
        if (!inActiveDuel)
        {
            CloseRealtimeSocket();
        }
        if (!string.IsNullOrEmpty(CurrentRoomCode) && !inActiveDuel)
        {
            StartCoroutine(LeaveRoomRoutine(CurrentRoomCode));
            CurrentRoomCode = string.Empty;
        }
        if (lobbyModalObj != null) lobbyModalObj.SetActive(false);
    }

    private void OnClickCreateRoom()
    {
        if (lobbyWaitCoroutine != null) StopCoroutine(lobbyWaitCoroutine);
        lobbyWaitCoroutine = StartCoroutine(CreateAndWaitRoomRoutine(isPublic: false));
    }

    private void OnClickQuickMatch()
    {
        if (lobbyWaitCoroutine != null) StopCoroutine(lobbyWaitCoroutine);
        lobbyWaitCoroutine = StartCoroutine(JoinOrWaitQuickMatchRoutine());
    }

    private void OnClickJoinByCode()
    {
        string code = (joinCodeInput != null && joinCodeInput.text != null) ? joinCodeInput.text.Trim().ToUpperInvariant() : string.Empty;
        if (code.Length < 4)
        {
            if (lobbyStatusText != null) lobbyStatusText.text = "Lütfen 4 haneli oda kodunu gir!";
            return;
        }
        if (lobbyWaitCoroutine != null) StopCoroutine(lobbyWaitCoroutine);
        lobbyWaitCoroutine = StartCoroutine(JoinSpecificRoomRoutine(code));
    }

    public void StartBotPracticeDuel()
    {
        if (lobbyWaitCoroutine != null) StopCoroutine(lobbyWaitCoroutine);
        IsBotMatch = true;
        IsHost = true;
        CurrentRoomCode = "BOT";
        SharedSeed = UnityEngine.Random.Range(10000, 99999);

        string[] botNames = { "TurboKurbaga", "UzayNinjasi", "ZipZipKrali", "PixelAvcisi" };
        string[] botChars = { CharacterShopData.NinjaFrogId, CharacterShopData.MaskDudeId, CharacterShopData.VirtualGuyId };
        OpponentName = botNames[UnityEngine.Random.Range(0, botNames.Length)];
        OpponentCharId = botChars[UnityEngine.Random.Range(0, botChars.Length)];

        BeginDuelMatch();
    }

    // =========================================================================
    // 3. SUPABASE ODA KURMA / KATILMA / EŞLEŞME AKIŞI
    // =========================================================================
    private IEnumerator CreateAndWaitRoomRoutine(bool isPublic)
    {
        if (lobbyStatusText != null) lobbyStatusText.text = "Oda oluşturuluyor...";

        LeaderboardService lb = LeaderboardService.EnsureInstance();
        CreateRoomPayload payload = new CreateRoomPayload
        {
            p_secret = GetDeviceSecret(),
            p_name = lb.CurrentUsername,
            p_char = CharacterShopData.GetSelectedCharacterId(),
            p_is_public = isPublic
        };

        using (UnityWebRequest req = CreateRpcRequest("rpc_mp_create_room", JsonUtility.ToJson(payload)))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (lobbyStatusText != null) lobbyStatusText.text = "Supabase SQL henüz çalıştırılmamış! (Bot ile deneyebilirsin)";
                yield break;
            }

            RoomStateResponse resp = JsonUtility.FromJson<RoomStateResponse>(req.downloadHandler.text);
            if (resp == null || !resp.success)
            {
                if (lobbyStatusText != null) lobbyStatusText.text = resp != null ? resp.message : "Oda açılamadı.";
                yield break;
            }

            CurrentRoomCode = resp.room_code;
            SharedSeed = resp.seed;
            IsHost = true;
            IsBotMatch = false;
            EnsureRealtimeSocketConnected(CurrentRoomCode);

            if (lobbyRoomCodeBigText != null) lobbyRoomCodeBigText.text = $"ODA KODU: {CurrentRoomCode}";
            if (lobbyStatusText != null) lobbyStatusText.text = "Arkadaşının bu kodu girip katılması bekleniyor...";
        }

        while (!string.IsNullOrEmpty(CurrentRoomCode) && !inActiveDuel)
        {
            yield return new WaitForSecondsRealtime(0.22f);
            if (inActiveDuel) yield break;

            SyncStatePayload poll = new SyncStatePayload
            {
                p_room_code = CurrentRoomCode,
                p_secret = GetDeviceSecret(),
                p_x = 0f,
                p_y = 0f,
                p_vy = 0f,
                p_health = 3,
                p_score = 0,
                p_alive = true,
                p_trigger_rocket = false,
                p_superfeet = false,
                p_shield = false
            };

            using (UnityWebRequest req = CreateRpcRequest("rpc_mp_sync_state", JsonUtility.ToJson(poll)))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    RoomStateResponse resp = JsonUtility.FromJson<RoomStateResponse>(req.downloadHandler.text);
                    if (resp != null && resp.status == "playing" && !string.IsNullOrEmpty(resp.opp_name))
                    {
                        OpponentName = resp.opp_name;
                        OpponentCharId = string.IsNullOrEmpty(resp.opp_char) ? CharacterShopData.NinjaFrogId : resp.opp_char;
                        SharedSeed = resp.seed;
                        BeginDuelMatch();
                        yield break;
                    }
                }
            }
        }
    }

    private IEnumerator JoinOrWaitQuickMatchRoutine()
    {
        if (lobbyStatusText != null) lobbyStatusText.text = "Açık oda aranıyor...";

        LeaderboardService lb = LeaderboardService.EnsureInstance();
        JoinRoomPayload payload = new JoinRoomPayload
        {
            p_secret = GetDeviceSecret(),
            p_name = lb.CurrentUsername,
            p_char = CharacterShopData.GetSelectedCharacterId(),
            p_room_code = "AUTO"
        };

        using (UnityWebRequest req = CreateRpcRequest("rpc_mp_join_room", JsonUtility.ToJson(payload)))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (lobbyStatusText != null) lobbyStatusText.text = "Supabase multiplayer SQL henüz çalıştırılmamış!";
                yield break;
            }

            RoomStateResponse resp = JsonUtility.FromJson<RoomStateResponse>(req.downloadHandler.text);
            if (resp != null && resp.success)
            {
                CurrentRoomCode = resp.room_code;
                SharedSeed = resp.seed;
                IsHost = resp.is_host;
                IsBotMatch = false;
                EnsureRealtimeSocketConnected(CurrentRoomCode);

                if (resp.status == "playing" && !string.IsNullOrEmpty(resp.opp_name))
                {
                    OpponentName = resp.opp_name;
                    OpponentCharId = string.IsNullOrEmpty(resp.opp_char) ? CharacterShopData.NinjaFrogId : resp.opp_char;
                    BeginDuelMatch();
                    yield break;
                }

                if (lobbyRoomCodeBigText != null) lobbyRoomCodeBigText.text = $"ODA KODU: {CurrentRoomCode}";
                if (lobbyStatusText != null) lobbyStatusText.text = "Rakip bekleniyor... (Arkadaşın bu kodu da girebilir)";
            }
        }

        while (!string.IsNullOrEmpty(CurrentRoomCode) && !inActiveDuel)
        {
            yield return new WaitForSecondsRealtime(0.22f);
            if (inActiveDuel) yield break;
            SyncStatePayload poll = new SyncStatePayload
            {
                p_room_code = CurrentRoomCode,
                p_secret = GetDeviceSecret(),
                p_x = 0f,
                p_y = 0f,
                p_vy = 0f,
                p_health = 3,
                p_score = 0,
                p_alive = true,
                p_trigger_rocket = false,
                p_superfeet = false,
                p_shield = false
            };

            using (UnityWebRequest req = CreateRpcRequest("rpc_mp_sync_state", JsonUtility.ToJson(poll)))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    RoomStateResponse resp = JsonUtility.FromJson<RoomStateResponse>(req.downloadHandler.text);
                    if (resp != null && resp.status == "playing" && !string.IsNullOrEmpty(resp.opp_name))
                    {
                        OpponentName = resp.opp_name;
                        OpponentCharId = string.IsNullOrEmpty(resp.opp_char) ? CharacterShopData.NinjaFrogId : resp.opp_char;
                        SharedSeed = resp.seed;
                        BeginDuelMatch();
                        yield break;
                    }
                }
            }
        }
    }

    private IEnumerator JoinSpecificRoomRoutine(string code)
    {
        if (lobbyStatusText != null) lobbyStatusText.text = $"'{code}' odasına bağlanılıyor...";

        LeaderboardService lb = LeaderboardService.EnsureInstance();
        JoinRoomPayload payload = new JoinRoomPayload
        {
            p_secret = GetDeviceSecret(),
            p_name = lb.CurrentUsername,
            p_char = CharacterShopData.GetSelectedCharacterId(),
            p_room_code = code
        };

        using (UnityWebRequest req = CreateRpcRequest("rpc_mp_join_room", JsonUtility.ToJson(payload)))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (lobbyStatusText != null) lobbyStatusText.text = "Bağlantı hatası veya SQL tablosu eksik!";
                yield break;
            }

            RoomStateResponse resp = JsonUtility.FromJson<RoomStateResponse>(req.downloadHandler.text);
            if (resp == null || !resp.success)
            {
                if (lobbyStatusText != null) lobbyStatusText.text = (resp != null && !string.IsNullOrEmpty(resp.message)) ? resp.message : "Oda bulunamadı veya dolu!";
                yield break;
            }

            CurrentRoomCode = resp.room_code;
            SharedSeed = resp.seed;
            IsHost = false;
            IsBotMatch = false;
            OpponentName = string.IsNullOrEmpty(resp.opp_name) ? "Rakip" : resp.opp_name;
            OpponentCharId = string.IsNullOrEmpty(resp.opp_char) ? CharacterShopData.PinkManId : resp.opp_char;
            EnsureRealtimeSocketConnected(CurrentRoomCode);

            BeginDuelMatch();
        }
    }

    private void BeginDuelMatch()
    {
        if (lobbyWaitCoroutine != null)
        {
            StopCoroutine(lobbyWaitCoroutine);
            lobbyWaitCoroutine = null;
        }

        inActiveDuel = true;
        localEliminated = false;
        duelFinished = false;
        rewardGranted = false;
        pendingLocalRocketTrigger = false;
        pendingWsRocketTrigger = false;
        pendingLocalJumpNet = false;
        prevLocalGroundedForNet = true;
        opponentRocketTimer = 0f;

        OpponentHealth = 3;
        OpponentScore = 0;
        OpponentAlive = true;
        OpponentSuperFeet = false;
        OpponentShield = false;
        OpponentTargetPos = new Vector2(0.0f, -1.5f);
        DuelMatchElapsedTime = 0f;

        remoteSmoothVel = Vector2.zero;
        remoteRenderPos = new Vector2(0f, -1.5f);
        remotePlacedOnStartPlat = false;
        remotePacketPlatIdx = 0;
        remotePacketX = 0f;
        remotePacketY = -1.5f;
        remotePacketVx = 0f;
        remotePacketVy = 0f;
        remoteWorldYOffset = 0f;
        remoteHasWorldYOffset = false;
        lastRemotePacketTime = Time.unscaledTime;
        lastWsPacketReceivedTime = -99f;
        remoteRocketActive = false;
        remoteLastSeq = 0;
        localBroadcastSeq = 0;
        wsBroadcastTimer = 0f;

        if (!IsBotMatch && !string.IsNullOrEmpty(CurrentRoomCode))
        {
            EnsureRealtimeSocketConnected(CurrentRoomCode);
        }

        botSimTime = 0f;
        botExactScore = 0f;
        botSelfRocketTimer = 0f;
        botMistakeTimer = 0f;
        botMistakeOffset = 0f;
        botCurrentMoveInput = 0f;
        botJumpCooldown = 0.12f;
        botGrounded = false;
        botWasGrounded = true;
        botPlacedOnStartPlatform = false;
        botVisualStretch = Vector2.one;
        botNextRocketTime = UnityEngine.Random.Range(15f, 23f);
        botNextMistakeTime = UnityEngine.Random.Range(20f, 30f);

        Time.timeScale = 1f;
        SceneManager.LoadScene("SampleScene");
    }

    // =========================================================================
    // 4. OYUN İÇİ DÜELLO SAHNESİ KURULUMU (AVATAR + VS HUD + İZLEYİCİ + SONUÇ)
    // =========================================================================
    private void SetupGameplayDuelScene()
    {
        EnsureStyleAssets();
        SpawnOpponentAvatar();

        Canvas canvas = FindPrimaryCanvas();
        if (canvas != null)
        {
            BuildGameplayDuelHUD(canvas.transform);
        }
    }

    private void SpawnOpponentAvatar()
    {
        if (opponentAvatarObj != null) Destroy(opponentAvatarObj);
        botRb = null;
        botCollider = null;
        botFeetTransform = null;
        botRocketFlameObj = null;
        botRocketFlameRenderer = null;
        botPlacedOnStartPlatform = false;

        GameManager gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
        PlayerController pc = gm != null && gm.player != null
            ? gm.player.GetComponent<PlayerController>()
            : FindFirstObjectByType<PlayerController>();

        Vector3 baseScale = (gm != null && gm.player != null)
            ? new Vector3(Mathf.Abs(gm.player.transform.localScale.x), Mathf.Abs(gm.player.transform.localScale.y), 1f)
            : new Vector3(1.805f, 1.422f, 1f);

        opponentAvatarObj = new GameObject("RemoteOpponentAvatar");
        opponentAvatarObj.transform.localScale = baseScale;

        Vector3 startPos = (gm != null && gm.player != null)
            ? gm.player.transform.position + new Vector3(0.24f, 0.05f, 0f)
            : new Vector3(0.24f, -1.5f, 0f);
        opponentAvatarObj.transform.position = startPos;
        OpponentTargetPos = startPos;

        opponentSpriteRenderer = opponentAvatarObj.AddComponent<SpriteRenderer>();
        opponentSpriteRenderer.sortingOrder = 8;
        opponentSpriteRenderer.color = new Color(1f, 1f, 1f, 0.88f);

        CharacterShopConfig cfg = CharacterShopConfig.Load();
        if (cfg != null)
        {
            CharacterShopConfig.CharacterVisualEntry entry = cfg.GetVisualEntry(OpponentCharId);
            if (entry != null && entry.idleFrames != null && entry.idleFrames.Length > 0)
            {
                opponentIdleFrames = entry.idleFrames;
                opponentSpriteRenderer.sprite = opponentIdleFrames[0];
            }
        }
        LoadOpponentCornerSprites(OpponentCharId);

        if (pc != null)
        {
            botMoveSpeed = pc.moveSpeed;
            botJumpForce = pc.jumpForce;
            botFootRadius = pc.ayakCapi;
            if (pc.zeminKatmani.value != 0) botGroundMask = pc.zeminKatmani;
        }

        // Bot maçındaysa yerel oyuncuyla birebir aynı gerçek Rigidbody2D + BoxCollider2D fiziğini kur
        if (IsBotMatch)
        {
            BoxCollider2D playerBox = (gm != null && gm.player != null) ? gm.player.GetComponent<BoxCollider2D>() : null;
            Rigidbody2D playerRb = (gm != null && gm.player != null) ? gm.player.GetComponent<Rigidbody2D>() : null;

            botCollider = opponentAvatarObj.AddComponent<BoxCollider2D>();
            if (playerBox != null)
            {
                botCollider.size = playerBox.size;
                botCollider.offset = playerBox.offset;
                botCollider.sharedMaterial = playerBox.sharedMaterial;
            }
            else
            {
                botCollider.size = new Vector2(0.24167f, 0.28308f);
                botCollider.offset = new Vector2(0.00705f, -0.03323f);
            }

            // Sadece zemin (platform) katmanıyla çarpış; yerel oyuncu, coin, power-up ve meteorların içinden geç!
            botCollider.includeLayers = botGroundMask;
            botCollider.excludeLayers = ~botGroundMask.value;
            botCollider.callbackLayers = 0;
            if (playerBox != null)
            {
                Physics2D.IgnoreCollision(botCollider, playerBox, true);
            }

            botBaseGravityScale = playerRb != null ? playerRb.gravityScale : 1.5f;
            botRb = opponentAvatarObj.AddComponent<Rigidbody2D>();
            botRb.bodyType = RigidbodyType2D.Dynamic;
            botRb.mass = playerRb != null ? playerRb.mass : 1f;
            botRb.linearDamping = 0f;
            botRb.angularDamping = 0.05f;
            botRb.gravityScale = botBaseGravityScale;
            botRb.constraints = RigidbodyConstraints2D.FreezeRotation;
            botRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            botRb.interpolation = RigidbodyInterpolation2D.Interpolate;

            GameObject feetObj = new GameObject("BotFeetPoint");
            feetObj.transform.SetParent(opponentAvatarObj.transform, false);
            feetObj.transform.localPosition = (pc != null && pc.ayakNoktasi != null)
                ? pc.ayakNoktasi.localPosition
                : new Vector3(-0.054f, -0.158f, 0f);
            botFeetTransform = feetObj.transform;
        }

        // Tepesindeki "Rakip Adı + CAN: 3" Etiketi
        GameObject tagObj = new GameObject("OpponentNameTag");
        tagObj.transform.SetParent(opponentAvatarObj.transform, false);
        tagObj.transform.localPosition = new Vector3(0f, 0.28f, 0f);
        tagObj.transform.localScale = new Vector3(0.08f, 0.10f, 1f);

        opponentNameTagTmp = tagObj.AddComponent<TextMeshPro>();
        if (arcadeFont != null) opponentNameTagTmp.font = arcadeFont;
        opponentNameTagTmp.fontSize = 24f;
        opponentNameTagTmp.alignment = TextAlignmentOptions.Center;
        opponentNameTagTmp.color = new Color(1f, 0.94f, 0.42f, 1f);
        opponentNameTagTmp.sortingOrder = 25;
        opponentNameTagTmp.text = $"{OpponentName}\nCAN: 3";
    }

    private void LoadOpponentCornerSprites(string charId)
    {
        opponentCornerFrames = null;
        Texture2D tex = Resources.Load<Texture2D>("Characters/corner_hang_" + charId);
        if (tex == null) tex = Resources.Load<Texture2D>("Characters/corner_hang_pink_man");
        if (tex == null || tex.height <= 0) return;

        float cornerPpu = 114f;
        if (charId == CharacterShopData.NinjaFrogId || charId == CharacterShopData.VirtualGuyId)
        {
            cornerPpu = 88f;
        }
        else if (charId == CharacterShopData.MaskDudeId)
        {
            cornerPpu = 90f;
        }

        int frameSize = tex.height;
        int count = Mathf.Max(1, tex.width / frameSize);
        opponentCornerFrames = new Sprite[count];
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < count; i++)
        {
            opponentCornerFrames[i] = Sprite.Create(
                tex,
                new Rect(i * frameSize, 0f, frameSize, frameSize),
                pivot,
                cornerPpu);
        }
    }

    private void BuildGameplayDuelHUD(Transform canvasTr)
    {
        // 1. Üst Orta Karşılaştırma (VS) Barı
        duelTopHudObj = CreateUIObject("DuelTopVSBar", canvasTr);
        SetAnchors(duelTopHudObj, new Vector2(0.16f, 0.885f), new Vector2(0.84f, 0.940f));
        Image vsBg = duelTopHudObj.AddComponent<Image>();
        vsBg.sprite = pillSprite;
        vsBg.type = Image.Type.Sliced;
        vsBg.color = new Color(0.10f, 0.14f, 0.24f, 0.90f);

        duelMyStatusText = CreateLabel(duelTopHudObj.transform, "MyStat", "SEN (3 CAN): 0",
            new Vector2(0.03f, 0.08f), new Vector2(0.46f, 0.92f), 8.5f, 13f, TextAlignmentOptions.Left, new Color(0.48f, 0.98f, 0.62f, 1f), true);

        CreateLabel(duelTopHudObj.transform, "VS", "VS",
            new Vector2(0.46f, 0.10f), new Vector2(0.54f, 0.90f), 9f, 13f, TextAlignmentOptions.Center, new Color(1f, 0.82f, 0.24f, 1f), true);

        duelOppStatusText = CreateLabel(duelTopHudObj.transform, "OppStat", $"{OpponentName} (3 CAN): 0",
            new Vector2(0.54f, 0.08f), new Vector2(0.97f, 0.92f), 8.5f, 13f, TextAlignmentOptions.Right, new Color(0.58f, 0.88f, 1f, 1f), true);

        // 2. Roket Sabotaj Uyarı Şeridi
        sabotageBannerObj = CreateUIObject("RocketSabotageBanner", canvasTr);
        SetAnchors(sabotageBannerObj, new Vector2(0.08f, 0.820f), new Vector2(0.92f, 0.875f));
        Image sabBg = sabotageBannerObj.AddComponent<Image>();
        sabBg.sprite = pillSprite;
        sabBg.type = Image.Type.Sliced;
        sabBg.color = new Color(0.92f, 0.22f, 0.16f, 0.94f);
        sabotageBannerText = CreateLabel(sabotageBannerObj.transform, "Txt", "RAKIP ROKET ALDI! OYUN HIZLANDI!",
            new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.90f), 10f, 15f, TextAlignmentOptions.Center, Color.white, true);
        sabotageBannerObj.SetActive(false);

        // 3. Ölünce Yaşayan Rakibi İzleme (Spectator Mode) Ekranı
        spectatorOverlayObj = CreateUIObject("SpectatorModeOverlay", canvasTr);
        SetAnchors(spectatorOverlayObj, Vector2.zero, Vector2.one);

        GameObject specTop = CreateUIObject("SpectatorTopBar", spectatorOverlayObj.transform);
        SetAnchors(specTop, new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.87f));
        Image specTopBg = specTop.AddComponent<Image>();
        specTopBg.sprite = cardSprite;
        specTopBg.type = Image.Type.Sliced;
        specTopBg.color = new Color(0.14f, 0.18f, 0.30f, 0.94f);

        spectatorInfoText = CreateLabel(specTop.transform, "Info", $"ELENDİN! • {OpponentName} CANLI İZLENİYOR",
            new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.88f), 10f, 16f, TextAlignmentOptions.Center, new Color(1f, 0.90f, 0.38f, 1f), true);

        CreateStyledActionButton(spectatorOverlayObj.transform, "ExitSpectatorBtn", "MAÇTAN ÇIK (ANA MENÜ)",
            new Vector2(0.18f, 0.05f), new Vector2(0.82f, 0.14f), new Color(0.90f, 0.26f, 0.22f, 1f), LeaveDuelAndReturnToMenu);

        spectatorOverlayObj.SetActive(false);

        // 4. Maç Sonu Düello Sonuç Kartı
        duelResultModalObj = CreateUIObject("DuelResultModal", canvasTr);
        SetAnchors(duelResultModalObj, Vector2.zero, Vector2.one);

        Image resDim = duelResultModalObj.AddComponent<Image>();
        resDim.color = new Color(0.04f, 0.06f, 0.12f, 0.84f);

        GameObject resCard = CreateUIObject("ResultCard", duelResultModalObj.transform);
        SetAnchors(resCard, new Vector2(0.07f, 0.18f), new Vector2(0.93f, 0.82f));
        Image resCardBg = resCard.AddComponent<Image>();
        resCardBg.sprite = cardSprite;
        resCardBg.type = Image.Type.Sliced;
        resCardBg.color = new Color(0.99f, 0.95f, 0.86f, 1f);

        GameObject resHeader = CreateUIObject("Header", resCard.transform);
        SetAnchors(resHeader, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.96f));
        Image resHBg = resHeader.AddComponent<Image>();
        resHBg.sprite = pillSprite;
        resHBg.type = Image.Type.Sliced;
        resHBg.color = new Color(0.96f, 0.66f, 0.10f, 1f);

        duelResultTitleText = CreateLabel(resHeader.transform, "Title", "DÜELLO BİTTİ!",
            new Vector2(0.06f, 0.10f), new Vector2(0.94f, 0.90f), 15f, 24f, TextAlignmentOptions.Center, Color.white, true);

        duelResultSummaryText = CreateLabel(resCard.transform, "Summary", "",
            new Vector2(0.07f, 0.42f), new Vector2(0.93f, 0.78f), 12f, 20f, TextAlignmentOptions.Center, new Color(0.20f, 0.16f, 0.12f, 1f), false);
        duelResultSummaryText.textWrappingMode = TextWrappingModes.Normal;

        duelResultRewardText = CreateLabel(resCard.transform, "Reward", "",
            new Vector2(0.07f, 0.28f), new Vector2(0.93f, 0.40f), 11f, 18f, TextAlignmentOptions.Center, new Color(0.14f, 0.68f, 0.30f, 1f), false);

        CreateStyledActionButton(resCard.transform, "MenuBtn", "ANA MENÜYE DÖN",
            new Vector2(0.14f, 0.07f), new Vector2(0.86f, 0.22f), new Color(0.18f, 0.58f, 0.94f, 1f), LeaveDuelAndReturnToMenu);

        duelResultModalObj.SetActive(false);
    }

    // =========================================================================
    // 5. GERÇEK ZAMANLI WEBSOCKET (25 HZ) + YEDEK HTTP SENKRON & BOT SİMÜLASYONU
    // =========================================================================
    private void EnsureRealtimeSocketConnected(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode) || roomCode == "BOT" || IsBotMatch)
            return;

        if (realtimeWs != null &&
            wsConnectedRoomCode == roomCode &&
            (realtimeWs.State == WebSocketState.Open || realtimeWs.State == WebSocketState.Connecting))
        {
            return;
        }

        CloseRealtimeSocket();
        wsConnectedRoomCode = roomCode;
        wsJoinedTopic = false;
        wsSendBusy = false;
        wsCts = new CancellationTokenSource();
        realtimeWs = new ClientWebSocket();
        _ = ConnectAndReceiveWsAsync(realtimeWs, roomCode, wsCts.Token);
    }

    private void CloseRealtimeSocket()
    {
        wsJoinedTopic = false;
        wsSendBusy = false;
        wsConnectedRoomCode = string.Empty;

        if (wsCts != null)
        {
            try { wsCts.Cancel(); } catch { }
            try { wsCts.Dispose(); } catch { }
            wsCts = null;
        }

        if (realtimeWs != null)
        {
            try { realtimeWs.Dispose(); } catch { }
            realtimeWs = null;
        }

        while (wsIncomingQueue.TryDequeue(out _)) { }
    }

    private async Task ConnectAndReceiveWsAsync(ClientWebSocket ws, string roomCode, CancellationToken ct)
    {
        try
        {
            await ws.ConnectAsync(new Uri(SupabaseRealtimeWsUrl), ct).ConfigureAwait(false);
            if (ws.State != WebSocketState.Open || ct.IsCancellationRequested)
                return;

            string topic = "realtime:duel_" + roomCode;
            string joinMsg = "{\"topic\":\"" + topic + "\",\"event\":\"phx_join\",\"payload\":{\"config\":{\"broadcast\":{\"self\":false,\"ack\":false}}},\"ref\":\"1\"}";
            byte[] joinBytes = Encoding.UTF8.GetBytes(joinMsg);
            await ws.SendAsync(new ArraySegment<byte>(joinBytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            wsJoinedTopic = true;

            byte[] buffer = new byte[4096];
            StringBuilder sb = new StringBuilder(1024);

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                if (sb.Length > 0)
                {
                    wsIncomingQueue.Enqueue(sb.ToString());
                }
            }
        }
        catch
        {
            wsJoinedTopic = false;
        }
    }

    private void SendWsRawFireAndForget(string json)
    {
        ClientWebSocket ws = realtimeWs;
        CancellationTokenSource cts = wsCts;
        if (ws == null || cts == null || ws.State != WebSocketState.Open || wsSendBusy)
            return;

        wsSendBusy = true;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        _ = SendBytesAsync(ws, bytes, cts.Token);
    }

    private async Task SendBytesAsync(ClientWebSocket ws, byte[] bytes, CancellationToken ct)
    {
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch
        {
            wsJoinedTopic = false;
        }
        finally
        {
            wsSendBusy = false;
        }
    }

    private void SendGameplayWsBroadcast()
    {
        if (!wsJoinedTopic || realtimeWs == null || realtimeWs.State != WebSocketState.Open || string.IsNullOrEmpty(CurrentRoomCode))
            return;

        GameManager gm = GameManager.Instance;
        Vector3 myPos = (gm != null && gm.player != null) ? gm.player.transform.position : Vector3.zero;
        Rigidbody2D rb = (gm != null && gm.player != null) ? gm.player.GetComponent<Rigidbody2D>() : null;
        PlayerController pc = (gm != null && gm.player != null) ? gm.player.GetComponent<PlayerController>() : null;

        float vx = rb != null ? rb.linearVelocity.x : 0f;
        float vy = rb != null ? rb.linearVelocity.y : 0f;
        bool localOnRocket = gm != null && gm.IsRocketBoosting;
        int myHealth = gm != null ? Mathf.Max(0, gm.health) : 3;
        int myScore = Mathf.RoundToInt(GameManager.currentScore);

        int platIdx = -1;
        float sendX = myPos.x;
        float sendVx = vx;

        if (pc != null && pc.IsGrounded && !localOnRocket && pc.CurrentStandingPlatform != null && pc.CurrentStandingPlatform.PlatformIndex >= 0)
        {
            PlatformMovement sp = pc.CurrentStandingPlatform;
            platIdx = sp.PlatformIndex;
            sendX = Mathf.Clamp(myPos.x - sp.transform.position.x, -0.88f, 0.88f);
            sendVx = vx - (sp.isMovingHorizontal ? sp.CurrentHorizontalVelocity : 0f);
            vy = 0f;
        }

        bool jumpedNow = pendingLocalJumpNet;
        pendingLocalJumpNet = false;

        bool trigRocket = pendingWsRocketTrigger;
        pendingWsRocketTrigger = false;

        string secret = GetDeviceSecret();
        string shortId = secret.Length > 8 ? secret.Substring(0, 8) : secret;

        WsStateData data = new WsStateData
        {
            s = shortId,
            seq = ++localBroadcastSeq,
            t = DuelMatchElapsedTime,
            x = sendX,
            y = myPos.y,
            vx = Mathf.Clamp(sendVx, -12f, 12f),
            vy = Mathf.Clamp(vy, -30f, 30f),
            plat = platIdx,
            hp = myHealth,
            sc = myScore,
            alv = !localEliminated && myHealth > 0,
            rkt = localOnRocket,
            trig_rkt = trigRocket,
            sf = gm != null && gm.IsSuperFeetActive,
            sh = gm != null && gm.HasShield,
            jmp = jumpedNow,
            nm = LeaderboardService.EnsureInstance().CurrentUsername,
            ch = CharacterShopData.GetSelectedCharacterId(),
            sd = SharedSeed
        };

        string topic = "realtime:duel_" + CurrentRoomCode;
        int msgRef = ++wsMsgRefCounter;
        string envelope = "{\"topic\":\"" + topic + "\",\"event\":\"broadcast\",\"payload\":{\"type\":\"broadcast\",\"event\":\"st\",\"payload\":" + JsonUtility.ToJson(data) + "},\"ref\":\"" + msgRef + "\"}";
        SendWsRawFireAndForget(envelope);
    }

    private void PumpRealtimeWebSocket(float unscaledDt)
    {
        if (realtimeWs != null && realtimeWs.State == WebSocketState.Open)
        {
            wsHeartbeatTimer -= unscaledDt;
            if (wsHeartbeatTimer <= 0f)
            {
                wsHeartbeatTimer = 18f;
                int hbRef = ++wsMsgRefCounter;
                SendWsRawFireAndForget("{\"topic\":\"phoenix\",\"event\":\"heartbeat\",\"payload\":{},\"ref\":\"" + hbRef + "\"}");
            }
        }

        string mySecret = GetDeviceSecret();
        string myShortId = mySecret.Length > 8 ? mySecret.Substring(0, 8) : mySecret;

        while (wsIncomingQueue.TryDequeue(out string raw))
        {
            if (string.IsNullOrEmpty(raw) || !raw.Contains("\"broadcast\"") || !raw.Contains("\"seq\":"))
                continue;

            WsIncomingEnvelope env = null;
            try
            {
                env = JsonUtility.FromJson<WsIncomingEnvelope>(raw);
            }
            catch
            {
                continue;
            }

            WsStateData d = env != null && env.payload != null ? env.payload.payload : null;
            if (d == null || d.s == myShortId)
                continue;

            // Lobide bekleyen Host, karşı oyuncu odaya girip ilk paketi attığı anda 50ms içinde maça başlasın!
            if (!inActiveDuel && !string.IsNullOrEmpty(CurrentRoomCode) && !IsBotMatch)
            {
                if (!string.IsNullOrEmpty(d.nm)) OpponentName = d.nm;
                if (!string.IsNullOrEmpty(d.ch)) OpponentCharId = d.ch;
                if (d.sd > 0) SharedSeed = d.sd;
                BeginDuelMatch();
            }

            if (!inActiveDuel) continue;
            if (d.seq > 0 && d.seq <= remoteLastSeq) continue;
            remoteLastSeq = d.seq;
            lastWsPacketReceivedTime = Time.unscaledTime;

            ApplyIncomingRemoteState(
                d.nm,
                d.ch,
                d.t,
                d.x,
                d.y,
                d.vx,
                d.vy,
                d.plat,
                d.hp,
                d.sc,
                d.alv,
                d.rkt,
                d.trig_rkt || d.rkt,
                d.sf,
                d.sh,
                d.jmp);
        }
    }

    private void ApplyIncomingRemoteState(
        string oppName,
        string oppChar,
        float oppMatchTime,
        float x,
        float y,
        float vx,
        float vy,
        int platIdx,
        int hp,
        int sc,
        bool alv,
        bool rkt,
        bool trigRocket,
        bool sf,
        bool sh,
        bool jumped)
    {
        if (!string.IsNullOrEmpty(oppName)) OpponentName = oppName;
        if (!string.IsNullOrEmpty(oppChar) && oppChar != OpponentCharId)
        {
            OpponentCharId = oppChar;
            CharacterShopConfig cfg = CharacterShopConfig.Load();
            if (cfg != null && opponentSpriteRenderer != null)
            {
                var entry = cfg.GetVisualEntry(OpponentCharId);
                if (entry != null && entry.idleFrames != null && entry.idleFrames.Length > 0)
                {
                    opponentIdleFrames = entry.idleFrames;
                }
            }
            LoadOpponentCornerSprites(OpponentCharId);
        }

        // Guest cihazda maç saatini Host saatine yumuşakça hizala ki hareketli platformlar %100 aynı fazda kalsın
        if (!IsHost && oppMatchTime > 0.1f && Mathf.Abs(DuelMatchElapsedTime - oppMatchTime) < 4.0f)
        {
            DuelMatchElapsedTime = Mathf.Lerp(DuelMatchElapsedTime, oppMatchTime + 0.045f, 0.08f);
        }

        OpponentHealth = Mathf.Clamp(hp, 0, 5);
        OpponentScore = Mathf.Max(OpponentScore, sc);
        OpponentAlive = alv && OpponentHealth > 0;
        OpponentSuperFeet = sf;
        OpponentShield = sh;
        remoteRocketActive = rkt;

        int prevPlat = remotePacketPlatIdx;
        if (jumped || (prevPlat >= 0 && platIdx < 0 && vy > 1.0f))
        {
            botVisualStretch = new Vector2(0.80f, 1.24f);
            if (opponentAvatarObj != null)
            {
                JumpDustEffect.SpawnJumpDust(opponentAvatarObj.transform.position + new Vector3(0f, -0.22f, 0f));
            }
        }
        else if (prevPlat < 0 && platIdx >= 0 && !rkt)
        {
            botVisualStretch = new Vector2(1.14f, 0.88f);
        }

        remotePacketPlatIdx = platIdx;
        remotePacketX = x;
        remotePacketY = y;
        remotePacketVx = vx;
        remotePacketVy = vy;
        OpponentVy = vy;
        lastRemotePacketTime = Time.unscaledTime;

        GameManager gm = GameManager.Instance;
        bool localOnRocket = gm != null && gm.IsRocketBoosting;
        if (trigRocket && !localOnRocket && opponentRocketTimer <= 0.1f)
        {
            opponentRocketTimer = 3.0f;
        }
    }

    private IEnumerator SyncMatchStateRoutine()
    {
        if (string.IsNullOrEmpty(CurrentRoomCode) || IsBotMatch) yield break;
        syncInFlight = true;

        GameManager gm = GameManager.Instance;
        Vector3 myPos = (gm != null && gm.player != null) ? gm.player.transform.position : Vector3.zero;
        Rigidbody2D rb = (gm != null && gm.player != null) ? gm.player.GetComponent<Rigidbody2D>() : null;
        PlayerController pc = (gm != null && gm.player != null) ? gm.player.GetComponent<PlayerController>() : null;
        float myVy = rb != null ? rb.linearVelocity.y : 0f;
        int myHealth = gm != null ? Mathf.Max(0, gm.health) : 0;
        int myScore = Mathf.RoundToInt(GameManager.currentScore);
        bool triggerRocket = pendingLocalRocketTrigger;
        pendingLocalRocketTrigger = false;
        bool localOnRocket = gm != null && gm.IsRocketBoosting;

        float sendX = myPos.x;
        float sendY = myPos.y;
        float sendVy = Mathf.Clamp(myVy, -50f, 50f);

        if (pc != null && pc.IsGrounded && !localOnRocket && pc.CurrentStandingPlatform != null && pc.CurrentStandingPlatform.PlatformIndex >= 0)
        {
            PlatformMovement sp = pc.CurrentStandingPlatform;
            sendX = Mathf.Clamp(myPos.x - sp.transform.position.x, -0.88f, 0.88f);
            sendY = myPos.y;
            sendVy = 1000f + sp.PlatformIndex;
        }

        SyncStatePayload payload = new SyncStatePayload
        {
            p_room_code = CurrentRoomCode,
            p_secret = GetDeviceSecret(),
            p_x = sendX,
            p_y = sendY,
            p_vy = sendVy,
            p_health = myHealth,
            p_score = myScore,
            p_alive = !localEliminated && myHealth > 0,
            p_trigger_rocket = triggerRocket,
            p_superfeet = gm != null && gm.IsSuperFeetActive,
            p_shield = gm != null && gm.HasShield
        };

        using (UnityWebRequest req = CreateRpcRequest("rpc_mp_sync_state", JsonUtility.ToJson(payload)))
        {
            yield return req.SendWebRequest();
            syncInFlight = false;

            if (req.result == UnityWebRequest.Result.Success)
            {
                RoomStateResponse resp = JsonUtility.FromJson<RoomStateResponse>(req.downloadHandler.text);
                if (resp != null && resp.success)
                {
                    // WebSocket canlı akıyorsa eski/gecikmeli HTTP pozisyonunun karakteri geri sıçratmasını kesin olarak önle!
                    bool wsFresh = wsJoinedTopic && (Time.unscaledTime - lastWsPacketReceivedTime < 1.2f);
                    if (wsFresh)
                    {
                        if (!string.IsNullOrEmpty(resp.opp_name)) OpponentName = resp.opp_name;
                        OpponentScore = Mathf.Max(OpponentScore, resp.opp_score);
                        if (!resp.opp_alive || resp.opp_health <= 0)
                        {
                            OpponentHealth = 0;
                            OpponentAlive = false;
                        }
                        if (resp.opp_rocket && !localOnRocket && opponentRocketTimer <= 0.1f)
                        {
                            opponentRocketTimer = 3.0f;
                        }
                    }
                    else
                    {
                        int platIdx = (resp.opp_vy >= 999.5f && !resp.opp_rocket)
                            ? Mathf.RoundToInt(resp.opp_vy - 1000f)
                            : -1;
                        float vyClean = platIdx >= 0 ? 0f : resp.opp_vy;
                        ApplyIncomingRemoteState(
                            resp.opp_name,
                            resp.opp_char,
                            0f,
                            resp.opp_x,
                            resp.opp_y,
                            0f,
                            vyClean,
                            platIdx,
                            resp.opp_health,
                            resp.opp_score,
                            resp.opp_alive,
                            resp.opp_rocket,
                            resp.opp_rocket,
                            resp.opp_superfeet,
                            resp.opp_shield,
                            false);
                    }
                }
            }
        }
    }

    private void UpdateBotOpponentSimulation(float dt)
    {
        if (!OpponentAlive || opponentAvatarObj == null || botRb == null || botCollider == null)
            return;
        if (dt <= 0.0001f || Time.timeScale <= 0.01f)
            return;

        PlatformMovement[] platforms = FindObjectsByType<PlatformMovement>(FindObjectsSortMode.None);

        // İlk karede GameManager.Start() ilk platformu oluşturduktan hemen sonra botu da aynı başlangıç platformuna oturt
        if (!botPlacedOnStartPlatform && platforms.Length > 0)
        {
            PlatformMovement startPlat = null;
            GameManager gmInit = GameManager.Instance;
            float refY = (gmInit != null && gmInit.player != null) ? gmInit.player.transform.position.y : -1.5f;
            float bestStartDiff = 999f;
            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] == null) continue;
                float d = Mathf.Abs(platforms[i].transform.position.y - refY);
                if (d < bestStartDiff)
                {
                    bestStartDiff = d;
                    startPlat = platforms[i];
                }
            }

            if (startPlat != null)
            {
                Collider2D pCol = startPlat.GetComponent<Collider2D>();
                float topY = pCol != null ? pCol.bounds.max.y : (startPlat.transform.position.y + 0.16f);
                Vector3 placePos = new Vector3(startPlat.transform.position.x + 0.22f, topY + 0.28f, 0f);
                opponentAvatarObj.transform.position = placePos;
                botRb.position = placePos;
                botRb.linearVelocity = new Vector2(0f, -GameManager.globalPlatformSpeed);
                OpponentTargetPos = placePos;
                botPlacedOnStartPlatform = true;
            }
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            botWrapThreshold = (cam.orthographicSize * cam.aspect) + 0.45f;
        }

        botSimTime += dt;

        GameManager gm = GameManager.Instance;
        bool localOnRocket = gm != null && gm.IsRocketBoosting;
        float scoreRate = (botSelfRocketTimer > 0f || localOnRocket) ? 16f : 10f;
        botExactScore += dt * scoreRate;
        OpponentScore = Mathf.RoundToInt(botExactScore);

        float speedRatio = (GameManager.globalPlatformSpeed > 0f) ? (GameManager.globalPlatformSpeed / 1.7f) : 1f;
        speedRatio = Mathf.Clamp(speedRatio, 1f, 1.41f);

        // Roket zamanı geldiyse bot gerçekten roketle uçar ve rakibe sabotaj uygular
        if (botSimTime >= botNextRocketTime)
        {
            botNextRocketTime = botSimTime + UnityEngine.Random.Range(18f, 27f);
            botSelfRocketTimer = 3.0f;
            if (!localOnRocket)
            {
                opponentRocketTimer = 3.0f;
            }
        }

        // Arada bir gerçek insan gibi ufak hedef sapması / geç tepki (maçın rekabetçi ve kazanılabilir kalması için)
        if (botSimTime >= botNextMistakeTime)
        {
            botNextMistakeTime = botSimTime + UnityEngine.Random.Range(18f, 28f);
            botMistakeTimer = UnityEngine.Random.Range(0.55f, 0.95f);
            botMistakeOffset = (UnityEngine.Random.value < 0.5f ? -1f : 1f) * UnityEngine.Random.Range(0.45f, 0.72f);
        }
        if (botMistakeTimer > 0f)
        {
            botMistakeTimer -= dt;
        }

        // Zemin kontrolü (PlayerController ile birebir aynı mantık)
        Vector2 feetPos = botFeetTransform != null ? (Vector2)botFeetTransform.position : (Vector2)opponentAvatarObj.transform.position + new Vector2(0f, -0.22f);
        Collider2D groundHit = Physics2D.OverlapCircle(feetPos, botFootRadius, botGroundMask);
        if (groundHit != null)
        {
            if (botRb.linearVelocity.y > 0.20f || botCollider.bounds.min.y < groundHit.bounds.center.y)
            {
                groundHit = null;
            }
        }
        botGrounded = groundHit != null;
        PlatformMovement standingPlat = (botGrounded && groundHit != null) ? groundHit.GetComponentInParent<PlatformMovement>() : null;

        if (botGrounded && !botWasGrounded && botRb.linearVelocity.y <= 0.15f)
        {
            botVisualStretch = new Vector2(1.14f, 0.88f);
        }
        botWasGrounded = botGrounded;

        Vector3 botPos = opponentAvatarObj.transform.position;
        float fullWrapWidth = botWrapThreshold * 2f;

        if (botSelfRocketTimer > 0f)
        {
            botSelfRocketTimer -= dt;
            botRb.gravityScale = 0f;
            float targetY = gm != null ? gm.rocketTargetY : 1.15f;
            float nextY = Mathf.Lerp(botPos.y, targetY, Mathf.Clamp01(dt * 6.5f));
            float weaveVx = Mathf.Sin(botSimTime * 3.2f) * botMoveSpeed * 0.65f;
            opponentAvatarObj.transform.position = new Vector3(botPos.x, nextY, 0f);
            botRb.linearVelocity = new Vector2(weaveVx, 0f);
            UpdateBotRocketFlame(true);

            // Roket biterken en yakın güvenli platforma hizalan
            if (botSelfRocketTimer <= 0f)
            {
                UpdateBotRocketFlame(false);
            }
        }
        else
        {
            UpdateBotRocketFlame(false);
            botRb.gravityScale = botBaseGravityScale * speedRatio;

            float desiredMoveInput = 0f;
            float currentMoveSpeed = botMoveSpeed * Mathf.Sqrt(speedRatio);

            if (botGrounded)
            {
                botJumpCooldown -= dt;

                // Üstteki en uygun ulaşılabilir platformu seç
                PlatformMovement bestUpper = null;
                float bestScore = 999f;
                for (int i = 0; i < platforms.Length; i++)
                {
                    PlatformMovement p = platforms[i];
                    if (p == null || p == standingPlat) continue;
                    float dy = p.transform.position.y - botPos.y;
                    if (dy < 0.38f || dy > 2.15f) continue;

                    float wDx = ShortestWrappedDx(botPos.x, p.transform.position.x, fullWrapWidth);
                    float score = Mathf.Abs(dy - 1.35f) * 1.35f + Mathf.Abs(wDx) * 0.48f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestUpper = p;
                    }
                }

                if (bestUpper != null)
                {
                    Collider2D upperCol = bestUpper.GetComponent<Collider2D>();
                    bool upperIsSolid = upperCol != null && !upperCol.usedByEffector;
                    float aimX = bestUpper.transform.position.x
                        + (bestUpper.isMovingHorizontal ? bestUpper.CurrentHorizontalVelocity * 0.22f : 0f)
                        + (botMistakeTimer > 0f ? botMistakeOffset : 0f);

                    float rawDxToUpper = ShortestWrappedDx(botPos.x, aimX, fullWrapWidth);
                    bool blockedDirectlyOverhead = upperIsSolid && Mathf.Abs(rawDxToUpper) < 0.78f && (bestUpper.transform.position.y - botPos.y) < 1.85f;

                    float walkTargetX = aimX;
                    if (blockedDirectlyOverhead)
                    {
                        // Tıpkı gerçek oyuncu gibi üstteki katı platformun altından sıyrılıp yandan zıplamak için kenara açıl
                        float sideSign = 1f;
                        if (standingPlat != null)
                        {
                            sideSign = (standingPlat.transform.position.x >= bestUpper.transform.position.x) ? 1f : -1f;
                        }
                        walkTargetX = bestUpper.transform.position.x + sideSign * 0.88f;
                    }

                    float walkDx = ShortestWrappedDx(botPos.x, walkTargetX, fullWrapWidth);
                    if (Mathf.Abs(walkDx) > 0.08f)
                    {
                        desiredMoveInput = Mathf.Clamp(walkDx * 3.2f, -1f, 1f);
                    }

                    bool onStandingEdge = false;
                    if (standingPlat != null && groundHit != null)
                    {
                        Bounds sb = groundHit.bounds;
                        onStandingEdge = (botPos.x <= sb.min.x + 0.14f && desiredMoveInput < 0f)
                                      || (botPos.x >= sb.max.x - 0.14f && desiredMoveInput > 0f);
                    }

                    bool urgentLow = botPos.y < -1.55f;
                    bool clearToJump = !blockedDirectlyOverhead || onStandingEdge || urgentLow;
                    bool withinHorizontalReach = Mathf.Abs(rawDxToUpper) <= 1.75f || urgentLow || onStandingEdge;

                    if (botJumpCooldown <= 0f && clearToJump && withinHorizontalReach && botRb.linearVelocity.y <= 0.15f)
                    {
                        botRb.linearVelocity = new Vector2(botRb.linearVelocity.x, 0f);
                        float jumpImpulse = botJumpForce * Mathf.Sqrt(speedRatio);
                        botRb.AddForce(Vector2.up * jumpImpulse, ForceMode2D.Impulse);
                        botVisualStretch = new Vector2(0.80f, 1.24f);
                        JumpDustEffect.SpawnJumpDust(feetPos);
                        botGrounded = false;
                        botJumpCooldown = botPos.y < -0.8f
                            ? UnityEngine.Random.Range(0.03f, 0.08f)
                            : UnityEngine.Random.Range(0.07f, 0.17f);
                    }
                }
                else if (standingPlat != null)
                {
                    // Ekranın çok üstündeyse mevcut platformun ortasında dengede kal
                    float centerDx = standingPlat.transform.position.x - botPos.x;
                    if (Mathf.Abs(centerDx) > 0.12f)
                    {
                        desiredMoveInput = Mathf.Clamp(centerDx * 2.5f, -1f, 1f);
                    }
                }
            }
            else
            {
                // Havadayken gerçek oyuncu gibi hedef platforma yönlen
                if (botRb.linearVelocity.y > 0.20f)
                {
                    // Yükselirken: hedef üst platformu bul, katı platformun altındaysa kenarından dolaşıp üstüne kıvrıl
                    PlatformMovement airTarget = null;
                    float bestAirScore = 999f;
                    for (int i = 0; i < platforms.Length; i++)
                    {
                        PlatformMovement p = platforms[i];
                        if (p == null) continue;
                        float dy = p.transform.position.y - botPos.y;
                        if (dy < -0.18f || dy > 1.85f) continue;
                        float wDx = ShortestWrappedDx(botPos.x, p.transform.position.x, fullWrapWidth);
                        float sc = Mathf.Abs(wDx) + Mathf.Abs(dy - 0.65f) * 0.7f;
                        if (sc < bestAirScore)
                        {
                            bestAirScore = sc;
                            airTarget = p;
                        }
                    }

                    if (airTarget != null)
                    {
                        Collider2D tCol = airTarget.GetComponent<Collider2D>();
                        float platTopY = tCol != null ? tCol.bounds.max.y : (airTarget.transform.position.y + 0.15f);
                        bool solidBelowTop = tCol != null && !tCol.usedByEffector && (botCollider.bounds.min.y < platTopY - 0.05f);
                        float targetX = airTarget.transform.position.x + (airTarget.isMovingHorizontal ? airTarget.CurrentHorizontalVelocity * 0.15f : 0f);

                        float wDx = ShortestWrappedDx(botPos.x, targetX, fullWrapWidth);
                        if (solidBelowTop && Mathf.Abs(wDx) < 0.76f)
                        {
                            // Ayaklar platform üst hizasını geçene kadar kenardan yüksel
                            float aroundSign = wDx <= 0f ? 1f : -1f;
                            targetX = airTarget.transform.position.x + aroundSign * 0.84f;
                            wDx = ShortestWrappedDx(botPos.x, targetX, fullWrapWidth);
                        }

                        if (Mathf.Abs(wDx) > 0.05f)
                        {
                            desiredMoveInput = Mathf.Clamp(wDx * 3.8f, -1f, 1f);
                        }
                    }
                }
                else
                {
                    // Düşerken: ayakların altındaki en yüksek uygun platformun tam üstüne in!
                    PlatformMovement landPlat = null;
                    float bestLandScore = -999f;
                    float botFeetY = botCollider.bounds.min.y;

                    for (int i = 0; i < platforms.Length; i++)
                    {
                        PlatformMovement p = platforms[i];
                        if (p == null) continue;
                        Collider2D pCol = p.GetComponent<Collider2D>();
                        float topY = pCol != null ? pCol.bounds.max.y : (p.transform.position.y + 0.15f);
                        if (topY > botFeetY + 0.18f || topY < botFeetY - 3.2f) continue;

                        float wDx = ShortestWrappedDx(botPos.x, p.transform.position.x, fullWrapWidth);
                        if (Mathf.Abs(wDx) > 2.25f) continue;

                        // Yüksek olan ve yatayda kolay yetişilen platformu tercih et
                        float landScore = topY * 1.5f - Mathf.Abs(wDx) * 0.85f;
                        if (landScore > bestLandScore)
                        {
                            bestLandScore = landScore;
                            landPlat = p;
                        }
                    }

                    if (landPlat != null)
                    {
                        float landX = landPlat.transform.position.x
                            + (landPlat.isMovingHorizontal ? landPlat.CurrentHorizontalVelocity * 0.12f : 0f)
                            + (botMistakeTimer > 0f ? botMistakeOffset : 0f);
                        float wDx = ShortestWrappedDx(botPos.x, landX, fullWrapWidth);
                        if (Mathf.Abs(wDx) > 0.05f)
                        {
                            desiredMoveInput = Mathf.Clamp(wDx * 4.2f, -1f, 1f);
                        }
                    }
                }
            }

            botCurrentMoveInput = Mathf.MoveTowards(botCurrentMoveInput, desiredMoveInput, dt * 15f);
            float platformCarryVx = (botGrounded && standingPlat != null && standingPlat.isMovingHorizontal)
                ? standingPlat.CurrentHorizontalVelocity
                : 0f;

            botRb.linearVelocity = new Vector2((botCurrentMoveInput * currentMoveSpeed) + platformCarryVx, botRb.linearVelocity.y);
        }

        // Ekran Sarma (Screen Wrap - yerel oyuncuyla birebir aynı!)
        Vector3 afterPos = opponentAvatarObj.transform.position;
        if (afterPos.x > botWrapThreshold)
        {
            afterPos.x = -botWrapThreshold + 0.1f;
            opponentAvatarObj.transform.position = afterPos;
            botRb.position = afterPos;
        }
        else if (afterPos.x < -botWrapThreshold)
        {
            afterPos.x = botWrapThreshold - 0.1f;
            opponentAvatarObj.transform.position = afterPos;
            botRb.position = afterPos;
        }

        if (Mathf.Abs(botCurrentMoveInput) > 0.05f)
        {
            opponentFacing = botCurrentMoveInput > 0f ? 1f : -1f;
        }

        // Gerçek düşüş kontrolü: DeadZone (-5.15) altına düşerse 1 can kaybeder ve üstteki bir platformda doğar
        if (afterPos.y < -5.15f)
        {
            OpponentHealth = Mathf.Max(0, OpponentHealth - 1);
            if (OpponentHealth <= 0)
            {
                OpponentAlive = false;
                UpdateBotRocketFlame(false);
                return;
            }

            // Mevcut en yüksek güvenli platformun üstünde yeniden doğ
            PlatformMovement respawnPlat = null;
            float highestY = -999f;
            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] == null) continue;
                float py = platforms[i].transform.position.y;
                if (py > highestY && py < 3.8f)
                {
                    highestY = py;
                    respawnPlat = platforms[i];
                }
            }

            Vector3 respawnPos = respawnPlat != null
                ? new Vector3(respawnPlat.transform.position.x, respawnPlat.transform.position.y + 0.48f, 0f)
                : new Vector3(0f, 1.8f, 0f);

            opponentAvatarObj.transform.position = respawnPos;
            botRb.position = respawnPos;
            botRb.linearVelocity = new Vector2(0f, -GameManager.globalPlatformSpeed);
            botMistakeTimer = 0f;
            botVisualStretch = new Vector2(1.15f, 0.88f);
            JumpDustEffect.SpawnSuperJumpBurst(respawnPos);
        }

        OpponentTargetPos = opponentAvatarObj.transform.position;
        OpponentVy = botRb.linearVelocity.y;
    }

    private static float ShortestWrappedDx(float fromX, float toX, float fullSpan)
    {
        float dx = toX - fromX;
        if (fullSpan <= 0.1f) return dx;
        if (dx > fullSpan * 0.5f) dx -= fullSpan;
        else if (dx < -fullSpan * 0.5f) dx += fullSpan;
        return dx;
    }

    private void UpdateBotRocketFlame(bool active)
    {
        if (!active)
        {
            if (botRocketFlameObj != null && botRocketFlameObj.activeSelf)
            {
                botRocketFlameObj.SetActive(false);
            }
            return;
        }

        if (opponentAvatarObj == null) return;
        if (botRocketFlameObj == null)
        {
            botRocketFlameObj = new GameObject("BotRocketFlame");
            botRocketFlameObj.transform.SetParent(opponentAvatarObj.transform, false);
            botRocketFlameObj.transform.localPosition = new Vector3(0f, -0.22f, 0f);
            botRocketFlameRenderer = botRocketFlameObj.AddComponent<SpriteRenderer>();
            botRocketFlameRenderer.sprite = CreateRoundedSprite(20, 32, 8, false);
            botRocketFlameRenderer.sortingOrder = 7;
        }

        if (!botRocketFlameObj.activeSelf) botRocketFlameObj.SetActive(true);
        float flicker = 0.85f + 0.30f * Mathf.Abs(Mathf.Sin(Time.time * 28f));
        botRocketFlameObj.transform.localScale = new Vector3(0.42f, 0.65f * flicker, 1f);
        botRocketFlameRenderer.color = new Color(1f, UnityEngine.Random.Range(0.45f, 0.78f), 0.12f, 0.90f);
    }

    private static PlatformMovement FindPlatformByIndex(int index)
    {
        if (index < 0) return null;
        PlatformMovement[] all = FindObjectsByType<PlatformMovement>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].PlatformIndex == index)
            {
                return all[i];
            }
        }
        return null;
    }

    private static float GetPlatformStandingY(PlatformMovement plat)
    {
        if (plat == null) return -1.5f;
        Collider2D col = plat.GetComponent<Collider2D>();
        float topY = col != null ? col.bounds.max.y : (plat.transform.position.y + 0.155f);
        return topY + 0.2485f;
    }

    private static float WrapCoordX(float x, float wrapT)
    {
        if (wrapT <= 0.5f) return x;
        if (x > wrapT) return -wrapT + 0.1f + ((x - wrapT) % (wrapT * 2f));
        if (x < -wrapT) return wrapT - 0.1f - ((-wrapT - x) % (wrapT * 2f));
        return x;
    }

    private void UpdateRemoteHumanAvatarMotion(float dt)
    {
        if (opponentAvatarObj == null || dt <= 0.0001f) return;

        // İlk karede başlangıç platformu (#0) oluştuğu anda karakteri tam üstüne yerleştir
        if (!remotePlacedOnStartPlat)
        {
            PlatformMovement startPlat = FindPlatformByIndex(0);
            if (startPlat != null)
            {
                Vector2 initPos = new Vector2(startPlat.transform.position.x + 0.16f, GetPlatformStandingY(startPlat));
                remoteRenderPos = initPos;
                opponentAvatarObj.transform.position = new Vector3(initPos.x, initPos.y, 0f);
                remotePlacedOnStartPlat = true;
            }
        }

        UpdateBotRocketFlame(remoteRocketActive);

        Camera cam = Camera.main;
        float wrapT = (cam != null ? (cam.orthographicSize * cam.aspect) : 2.55f) + 0.45f;
        float age = Mathf.Clamp(Time.unscaledTime - lastRemotePacketTime, 0f, 0.22f);

        Vector2 desiredPos;
        Vector2 desiredVel;

        if (remotePacketPlatIdx >= 0 && !remoteRocketActive)
        {
            PlatformMovement plat = FindPlatformByIndex(remotePacketPlatIdx);
            if (plat != null)
            {
                float standY = GetPlatformStandingY(plat);
                float rawOffset = standY - remotePacketY;
                remoteWorldYOffset = remoteHasWorldYOffset
                    ? Mathf.Lerp(remoteWorldYOffset, rawOffset, 0.25f)
                    : rawOffset;
                remoteHasWorldYOffset = true;

                float dampedWalkVx = remotePacketVx * Mathf.Exp(-6.5f * age);
                float predOffsetX = Mathf.Clamp(remotePacketX + dampedWalkVx * age, -0.82f, 0.82f);

                desiredPos = new Vector2(plat.transform.position.x + predOffsetX, standY);
                desiredVel = new Vector2(
                    (plat.isMovingHorizontal ? plat.CurrentHorizontalVelocity : 0f) + dampedWalkVx,
                    -GameManager.globalPlatformSpeed);

                if (Mathf.Abs(remotePacketVx) > 0.12f)
                {
                    opponentFacing = remotePacketVx > 0f ? 1f : -1f;
                }
            }
            else
            {
                float mappedY = remotePacketY + (remoteHasWorldYOffset ? remoteWorldYOffset : 0f) - (GameManager.globalPlatformSpeed * age);
                desiredPos = new Vector2(WrapCoordX(remotePacketX + remotePacketVx * age, wrapT), mappedY);
                desiredVel = new Vector2(remotePacketVx, -GameManager.globalPlatformSpeed);
            }
        }
        else if (remoteRocketActive)
        {
            float mappedY = remotePacketY + (remoteHasWorldYOffset ? remoteWorldYOffset : 0f);
            desiredPos = new Vector2(WrapCoordX(remotePacketX + remotePacketVx * age * 0.6f, wrapT), mappedY);
            desiredVel = new Vector2(remotePacketVx * 0.6f, 0f);
            if (Mathf.Abs(remotePacketVx) > 0.12f)
            {
                opponentFacing = remotePacketVx > 0f ? 1f : -1f;
            }
        }
        else
        {
            // Havadayken: Son ağ paketinden itibaren age süresi kadar parabolik yerçekimi ilerlet
            float speedRatio = (GameManager.globalPlatformSpeed > 0f) ? (GameManager.globalPlatformSpeed / 1.7f) : 1f;
            speedRatio = Mathf.Clamp(speedRatio, 1f, 1.41f);
            float g = 14.715f * speedRatio;

            float mappedStartY = remotePacketY + (remoteHasWorldYOffset ? remoteWorldYOffset : 0f);
            float predX = WrapCoordX(remotePacketX + remotePacketVx * age, wrapT);
            float predY = mappedStartY + (remotePacketVy * age) - (0.5f * g * age * age);
            float predVy = remotePacketVy - (g * age);

            // Aşağı inerken platformun içine gömülmeyi önle
            if (predVy <= 0.05f)
            {
                PlatformMovement[] allPlats = FindObjectsByType<PlatformMovement>(FindObjectsSortMode.None);
                for (int i = 0; i < allPlats.Length; i++)
                {
                    PlatformMovement pm = allPlats[i];
                    if (pm == null) continue;
                    if (Mathf.Abs(predX - pm.transform.position.x) <= 0.68f)
                    {
                        float standY = GetPlatformStandingY(pm);
                        if (mappedStartY >= standY - 0.12f && predY <= standY)
                        {
                            predY = standY;
                            predVy = -GameManager.globalPlatformSpeed;
                            break;
                        }
                    }
                }
            }

            desiredPos = new Vector2(predX, predY);
            desiredVel = new Vector2(remotePacketVx, predVy);

            if (Mathf.Abs(remotePacketVx) > 0.15f)
            {
                opponentFacing = remotePacketVx > 0f ? 1f : -1f;
            }
        }

        // Gerçek Ekran Sarma (sol kenardan çıkıp sağdan girme) haricinde ASLA ışınlanma yapma!
        float rawXGap = desiredPos.x - remoteRenderPos.x;
        if (Mathf.Abs(rawXGap) > wrapT * 1.30f)
        {
            remoteRenderPos.x += Mathf.Sign(rawXGap) * (wrapT * 2f - 0.2f);
            remoteSmoothVel.x = 0f;
        }

        // 1) Feed-Forward hız ilerletmesi: Platform üstündeyken platformla milimetrik aynı hızda iner ve kayar (%0 gecikme!)
        remoteRenderPos += desiredVel * dt;
        remoteRenderPos.x = WrapCoordX(remoteRenderPos.x, wrapT);

        // 2) Kalan hata farkını yumuşakça sıfırla (maxSpeed sınırı sayesinde ani paket farkında bile ışınlanmaz, akarak yetişir!)
        float smoothTime = (remotePacketPlatIdx >= 0 && !remoteRocketActive) ? 0.036f : 0.046f;
        remoteRenderPos = Vector2.SmoothDamp(remoteRenderPos, desiredPos, ref remoteSmoothVel, smoothTime, 16.5f, dt);

        OpponentTargetPos = desiredPos;
        opponentAvatarObj.transform.position = new Vector3(remoteRenderPos.x, remoteRenderPos.y, 0f);
    }

    private void UpdateOpponentAvatarVisuals(float dt)
    {
        if (opponentAvatarObj == null) return;

        if (!OpponentAlive)
        {
            if (opponentAvatarObj.activeSelf) opponentAvatarObj.SetActive(false);
            return;
        }
        if (!opponentAvatarObj.activeSelf) opponentAvatarObj.SetActive(true);

        if (IsBotMatch)
        {
            // Bot maçında pozisyon zaten doğrudan gerçek Rigidbody2D fiziği tarafından sürülüyor (sıfır kayma!)
            botVisualStretch = Vector2.Lerp(botVisualStretch, Vector2.one, Mathf.Clamp01(dt * 12f));
        }
        else
        {
            // Online insan rakip: Platform üstündeyken %100 platform yüzeyine kilitle, havadayken gerçek yerçekimiyle (Dead-Reckoning) akıt!
            UpdateRemoteHumanAvatarMotion(dt);
            botVisualStretch = Vector2.Lerp(botVisualStretch, Vector2.one, Mathf.Clamp01(dt * 12f));
        }

        float sx = Mathf.Max(0.5f, botVisualStretch.x);
        float sy = Mathf.Max(0.5f, botVisualStretch.y);
        opponentAvatarObj.transform.localScale = new Vector3(opponentFacing * 1.805f * sx, 1.422f * sy, 1f);

        if (opponentNameTagTmp != null)
        {
            // Karakter esnerken isim etiketi sabit boyutta ve düz okunur kalsın
            opponentNameTagTmp.transform.localScale = new Vector3((opponentFacing * 0.08f) / sx, 0.10f / sy, 1f);
            opponentNameTagTmp.text = $"{OpponentName}\nCAN: {OpponentHealth}";
        }

        // Platformun sağ veya sol yanındaysa rakibin kendi karakterine özel tırmanma/tutunma görüntüsünü göster
        bool inCornerPose = false;
        bool onRightSide = false;
        if (opponentCornerFrames != null && opponentCornerFrames.Length > 0 && opponentSpriteRenderer != null && botSelfRocketTimer <= 0f)
        {
            Vector3 opPos = opponentAvatarObj.transform.position;
            Collider2D nearPlat = Physics2D.OverlapBox(
                opPos + new Vector3(0f, -0.04f, 0f),
                new Vector2(0.66f, 0.44f),
                0f,
                botGroundMask);

            if (nearPlat != null)
            {
                Bounds gb = nearPlat.bounds;
                float botBottomY = opPos.y - 0.24f;
                bool notOnTop = botBottomY < (gb.max.y - 0.05f);
                bool sideVertical = notOnTop && (opPos.y >= gb.min.y - 0.16f) && (opPos.y <= gb.max.y + 0.12f);
                if (sideVertical)
                {
                    if (opPos.x >= gb.max.x + 0.04f)
                    {
                        inCornerPose = true;
                        onRightSide = true;
                    }
                    else if (opPos.x <= gb.min.x - 0.04f)
                    {
                        inCornerPose = true;
                        onRightSide = false;
                    }
                }
            }
        }

        if (inCornerPose && opponentSpriteRenderer != null)
        {
            opponentCornerAnimTimer += dt * 7f;
            int cIdx = Mathf.FloorToInt(opponentCornerAnimTimer) % opponentCornerFrames.Length;
            opponentSpriteRenderer.sprite = opponentCornerFrames[cIdx];
            bool scaleNeg = opponentAvatarObj.transform.localScale.x < 0f;
            opponentSpriteRenderer.flipX = onRightSide ? scaleNeg : !scaleNeg;
        }
        else if (opponentSpriteRenderer != null)
        {
            opponentSpriteRenderer.flipX = false;
            if (opponentIdleFrames != null && opponentIdleFrames.Length > 0)
            {
                opponentAnimTimer += dt * 12f;
                opponentAnimFrame = Mathf.FloorToInt(opponentAnimTimer) % opponentIdleFrames.Length;
                opponentSpriteRenderer.sprite = opponentIdleFrames[opponentAnimFrame];
            }
        }
    }

    private void UpdateDuelHudTexts()
    {
        GameManager gm = GameManager.Instance;
        int myHealth = gm != null ? Mathf.Max(0, gm.health) : 0;
        int myScore = Mathf.RoundToInt(GameManager.currentScore);

        if (duelMyStatusText != null)
        {
            duelMyStatusText.text = $"SEN ({myHealth} CAN): {myScore}";
        }
        if (duelOppStatusText != null)
        {
            string oppState = OpponentAlive ? $"{OpponentHealth} CAN" : "ELENDİ";
            duelOppStatusText.text = $"{OpponentName} ({oppState}): {OpponentScore}";
        }
        if (spectatorOverlayObj != null && spectatorOverlayObj.activeSelf && spectatorInfoText != null)
        {
            spectatorInfoText.text = $"ELENDİN! • {OpponentName} İZLENİYOR ({OpponentHealth} CAN • SKOR: {OpponentScore})";
        }
    }

    private void CheckDuelCompletion()
    {
        if (duelFinished) return;

        if (localEliminated && !OpponentAlive)
        {
            ShowDuelResultScreen();
        }
    }

    private void ShowDuelResultScreen()
    {
        if (duelFinished) return;
        duelFinished = true;

        if (spectatorOverlayObj != null) spectatorOverlayObj.SetActive(false);
        if (sabotageBannerObj != null) sabotageBannerObj.SetActive(false);

        int myScore = Mathf.RoundToInt(GameManager.currentScore);
        int oppScore = OpponentScore;
        bool iWon = myScore >= oppScore;

        if (iWon && !rewardGranted)
        {
            rewardGranted = true;
            CharacterShopData.AddCoins(DuelWinCoinReward);
            if (GameManager.Instance != null) GameManager.Instance.UpdateCoinUI();
        }

        LeaderboardService.EnsureInstance().SubmitScore(myScore);

        if (duelResultModalObj != null)
        {
            duelResultModalObj.transform.SetAsLastSibling();
            duelResultModalObj.SetActive(true);

            string myName = LeaderboardService.EnsureInstance().CurrentUsername;
            if (duelResultTitleText != null)
            {
                duelResultTitleText.text = iWon ? "KAZANDIN!" : "DÜELLO BİTTİ!";
            }
            if (duelResultSummaryText != null)
            {
                duelResultSummaryText.text =
                    $"1. {(iWon ? myName : OpponentName)}  —  {Mathf.Max(myScore, oppScore)} PUAN\n\n" +
                    $"2. {(iWon ? OpponentName : myName)}  —  {Mathf.Min(myScore, oppScore)} PUAN";
            }
            if (duelResultRewardText != null)
            {
                duelResultRewardText.text = iWon
                    ? $"+{DuelWinCoinReward} COIN ZAFER ÖDÜLÜ KAZANDIN!"
                    : "Bir sonraki düelloda rövanşı al!";
            }
        }

        Time.timeScale = 0f;
    }

    private IEnumerator LeaveRoomRoutine(string code)
    {
        LeaveRoomPayload payload = new LeaveRoomPayload
        {
            p_room_code = code,
            p_secret = GetDeviceSecret()
        };
        using (UnityWebRequest req = CreateRpcRequest("rpc_mp_leave_room", JsonUtility.ToJson(payload)))
        {
            yield return req.SendWebRequest();
        }
    }

    // =========================================================================
    // 6. YARDIMCI METODLAR (TMP OUTLINE CRASH-PROOF)
    // =========================================================================
    private static string GetDeviceSecret()
    {
        string s = PlayerPrefs.GetString("LB_DeviceSecret_v1", string.Empty);
        if (string.IsNullOrEmpty(s))
        {
            s = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("LB_DeviceSecret_v1", s);
            PlayerPrefs.Save();
        }
        return s;
    }

    private void EnsureStyleAssets()
    {
        // Ana menüdeki OYNA / CIKIS butonlarının tam font ve kalın dış çizgili materyalini yakala
        string[] mainBtnNames = { "PlayButton", "Kostumler Button", "Ayarlar Button", "cikis" };
        for (int i = 0; i < mainBtnNames.Length; i++)
        {
            GameObject btnGo = GameObject.Find(mainBtnNames[i]);
            if (btnGo != null)
            {
                TextMeshProUGUI btnTmp = btnGo.GetComponentInChildren<TextMeshProUGUI>(true);
                if (btnTmp != null && btnTmp.font != null)
                {
                    arcadeFont = btnTmp.font;
                    arcadeFontMaterial = btnTmp.fontSharedMaterial;
                    break;
                }
            }
        }

        if (arcadeFont == null)
        {
            TextMeshProUGUI[] allTmps = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < allTmps.Length; i++)
            {
                if (allTmps[i] != null && allTmps[i].font != null)
                {
                    arcadeFont = allTmps[i].font;
                    if (arcadeFontMaterial == null) arcadeFontMaterial = allTmps[i].fontSharedMaterial;
                    break;
                }
            }
        }

        if (btnGoldSprite == null) btnGoldSprite = Resources.Load<Sprite>("UI/menu_btn_gold");
        if (btnPinkSprite == null) btnPinkSprite = Resources.Load<Sprite>("UI/menu_btn_pink");
        if (btnCyanSprite == null) btnCyanSprite = Resources.Load<Sprite>("UI/menu_btn_cyan");
        if (btnRedSprite == null) btnRedSprite = Resources.Load<Sprite>("UI/menu_btn_red");
        if (btnGreenSprite == null) btnGreenSprite = Resources.Load<Sprite>("UI/menu_btn_green");
        if (cardSprite == null) cardSprite = Resources.Load<Sprite>("UI/menu_card_pixel");
        if (cardSprite == null) cardSprite = CreateRoundedSprite(64, 64, 16, true);
        if (pillSprite == null) pillSprite = btnGoldSprite != null ? btnGoldSprite : CreateRoundedSprite(64, 64, 18, false);
    }

    private void CreatePixelMenuButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Sprite sprite, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = CreateUIObject(name, parent);
        SetAnchors(btnObj, min, max);
        Image img = btnObj.AddComponent<Image>();
        img.sprite = sprite != null ? sprite : cardSprite;
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 0.96f, 0.78f, 1f);
        cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(onClick);

        CreateLabel(btnObj.transform, "Label", label,
            new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.90f), 11f, 21f, TextAlignmentOptions.Center, Color.white, false);
    }

    private static Canvas FindPrimaryCanvas()
    {
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas best = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null || !canvases[i].isRootCanvas) continue;
            if (canvases[i].name == "Canvas") return canvases[i];
            if (best == null) best = canvases[i];
        }
        return best;
    }

    private void CreateStyledActionButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color bgCol, UnityEngine.Events.UnityAction onClick)
    {
        Sprite sp = btnCyanSprite;
        if (bgCol.r > 0.7f && bgCol.g < 0.4f) sp = btnRedSprite;
        else if (bgCol.g > 0.65f && bgCol.r < 0.4f) sp = btnGreenSprite;
        else if (bgCol.r > 0.75f && bgCol.g > 0.5f) sp = btnGoldSprite;

        CreatePixelMenuButton(parent, name, label, min, max, sp, onClick);
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
        Vector2 min,
        Vector2 max,
        float minSize,
        float maxSize,
        TextAlignmentOptions align,
        Color col,
        bool addShadow)
    {
        GameObject go = CreateUIObject(name, parent);
        SetAnchors(go, min, max);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        if (arcadeFont != null) tmp.font = arcadeFont;
        if (arcadeFontMaterial != null) tmp.fontSharedMaterial = arcadeFontMaterial;
        tmp.text = text;
        tmp.alignment = align;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.fontStyle = FontStyles.Normal;
        tmp.color = col;
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
                cols[y * w + x] = (withBorder && dist > r - 3.0f)
                    ? new Color(0.18f, 0.13f, 0.09f, alpha)
                    : new Color(1f, 1f, 1f, alpha);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    private static UnityWebRequest CreateRpcRequest(string rpcName, string jsonBody)
    {
        string url = $"{SupabaseUrl}/rest/v1/rpc/{rpcName}";
        UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("apikey", SupabasePublishableKey);
        req.SetRequestHeader("Authorization", "Bearer " + SupabasePublishableKey);
        req.timeout = 8;
        return req;
    }
}
