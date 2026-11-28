using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        public int[] ci;
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
    private float botShieldTimer;
    private float botSuperFeetTimer;
    private Rigidbody2D botRb;
    private BoxCollider2D botCollider;
    private Transform botFeetTransform;
    private GameObject botRocketFlameObj;
    private SpriteRenderer botRocketFlameRenderer;
    private LayerMask botGroundMask = 1 << 6;
    private float botMoveSpeed = 5.5f;
    private float botJumpForce = 5.2f;
    private float botBaseGravityScale = 1.5f;
    private float botFootRadius = 0.30f;
    private float botWrapThreshold = 3.0f;
    private float botCurrentMoveInput;
    private float botJumpCooldown;
    private float botCoyoteTimer;
    private float botCornerHopCooldown;
    private float botUngroundLockTimer;
    private float botAirTime;
    private PlatformMovement botLockedTargetPlat;
    private PlatformMovement botLastAttemptFromPlat;
    private PlatformMovement botLastAttemptTargetPlat;
    private int botSamePlatFailCount;
    private float botLastTriedHookSign;
    private float botHookSideSign;
    private float botLastTakeoffY = -99f;
    private bool botGrounded;
    private bool botWasGrounded = true;
    private bool botPlacedOnStartPlatform;
    private Vector2 botVisualStretch = Vector2.one;

    // Rakip karakter objesi ve Gerçek Zamanlı WebSocket + Sıfır-Işınlanma Senkronizasyonu
    private GameObject opponentAvatarObj;
    private SpriteRenderer opponentSpriteRenderer;
    private Transform opponentOverheadRoot;
    private TextMeshPro opponentNameTagTmp;
    private TextMeshPro opponentNameShadowTmp;
    private TextMeshPro opponentHealthNumTmp;
    private TextMeshPro opponentHealthShadowTmp;
    private GameObject opponentShieldAuraObj;
    private SpriteRenderer opponentShieldAuraRenderer;
    private GameObject opponentSuperFeetObj;
    private SpriteRenderer opponentSuperFeetRenderer;
    private static Sprite cachedPixelHeartSprite;
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

    // İki oyuncu arası ortak deterministik item senkronizasyonu (bir oyuncu alınca diğerinden anında silinir)
    private readonly HashSet<int> collectedDuelItemIds = new HashSet<int>();
    private readonly Dictionary<int, GameObject> activeDuelItems = new Dictionary<int, GameObject>();
    private readonly List<int> recentLocalCollectedItems = new List<int>();
    private readonly List<int> botItemPickupBuffer = new List<int>();

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

    // Oyun İçi Düello HUD (Gelişmiş Piksel Arena Skorboard)
    private Sprite duelScoreboardFrameSprite;
    private Sprite duelCrownSprite;
    private GameObject duelTopHudObj;
    private Image duelMyAvatarImg;
    private Image duelOppAvatarImg;
    private string duelLoadedOppCharId;
    private readonly Image[] duelMyHeartImgs = new Image[3];
    private readonly Image[] duelOppHeartImgs = new Image[3];
    private TextMeshProUGUI duelMyNameTmp;
    private TextMeshProUGUI duelOppNameTmp;
    private TextMeshProUGUI duelMyStatusText;
    private TextMeshProUGUI duelOppStatusText;
    private TextMeshProUGUI duelMyElimBadgeTmp;
    private TextMeshProUGUI duelOppElimBadgeTmp;
    private GameObject duelDeltaPillObj;
    private Image duelDeltaPillBg;
    private TextMeshProUGUI duelDeltaTmp;
    private RectTransform duelCrownRt;
    private RectTransform duelTugLeftRt;
    private RectTransform duelTugRightRt;
    private RectTransform duelTugSparkRt;
    private float displayedDuelTugRatio = 0.5f;
    private float displayedCrownAnchorX = 0.082f;
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
                collectedDuelItemIds.Clear();
                activeDuelItems.Clear();
                recentLocalCollectedItems.Clear();
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

    public bool IsDuelItemCollected(int itemId)
    {
        return itemId >= 0 && collectedDuelItemIds.Contains(itemId);
    }

    public void RegisterDuelItem(int itemId, GameObject itemObj)
    {
        if (itemId < 0 || itemObj == null) return;
        if (collectedDuelItemIds.Contains(itemId))
        {
            Destroy(itemObj);
            return;
        }
        activeDuelItems[itemId] = itemObj;
    }

    public void NotifyLocalItemCollected(int itemId)
    {
        if (!inActiveDuel || itemId < 0) return;

        collectedDuelItemIds.Add(itemId);
        activeDuelItems.Remove(itemId);

        if (!recentLocalCollectedItems.Contains(itemId))
        {
            recentLocalCollectedItems.Add(itemId);
            if (recentLocalCollectedItems.Count > 16)
            {
                recentLocalCollectedItems.RemoveAt(0);
            }
        }

        if (!IsBotMatch)
        {
            SendGameplayWsBroadcast();
        }
    }

    private void MarkDuelItemCollectedRemotely(int itemId)
    {
        if (itemId < 0 || !collectedDuelItemIds.Add(itemId)) return;

        if (activeDuelItems.TryGetValue(itemId, out GameObject itemObj))
        {
            activeDuelItems.Remove(itemId);
            if (itemObj != null)
            {
                CoinPickup coin = itemObj.GetComponent<CoinPickup>();
                if (coin != null)
                {
                    coin.ConsumeRemotely();
                    return;
                }

                PowerUpPickup pu = itemObj.GetComponent<PowerUpPickup>();
                if (pu != null)
                {
                    pu.ConsumeRemotely();
                    return;
                }

                HealthPickup hp = itemObj.GetComponent<HealthPickup>();
                if (hp != null)
                {
                    hp.ConsumeRemotely();
                    return;
                }

                Destroy(itemObj);
            }
        }
    }

    private void CheckBotSharedItemPickups(Vector3 botPos, bool localOnRocket)
    {
        if (!IsBotMatch || activeDuelItems.Count == 0) return;

        botItemPickupBuffer.Clear();
        foreach (KeyValuePair<int, GameObject> kv in activeDuelItems)
        {
            if (kv.Value == null)
            {
                botItemPickupBuffer.Add(kv.Key);
                continue;
            }

            Vector3 diff = kv.Value.transform.position - botPos;
            if ((diff.x * diff.x + diff.y * diff.y) <= 0.30f)
            {
                PowerUpPickup pu = kv.Value.GetComponent<PowerUpPickup>();
                if (pu != null)
                {
                    if (pu.powerUpType == PowerUpType.Rocket)
                    {
                        botSelfRocketTimer = Mathf.Max(botSelfRocketTimer, 3.0f);
                        if (!localOnRocket)
                        {
                            opponentRocketTimer = Mathf.Max(opponentRocketTimer, 3.0f);
                        }
                    }
                    else if (pu.powerUpType == PowerUpType.Shield)
                    {
                        OpponentShield = true;
                        botShieldTimer = 12f;
                    }
                    else if (pu.powerUpType == PowerUpType.SuperFeet)
                    {
                        OpponentSuperFeet = true;
                        botSuperFeetTimer = 12f;
                    }
                }
                else if (kv.Value.GetComponent<HealthPickup>() != null)
                {
                    OpponentHealth = Mathf.Min(5, OpponentHealth + 1);
                }

                botItemPickupBuffer.Add(kv.Key);
            }
        }

        for (int i = 0; i < botItemPickupBuffer.Count; i++)
        {
            int id = botItemPickupBuffer[i];
            if (activeDuelItems.TryGetValue(id, out GameObject go) && go == null)
            {
                activeDuelItems.Remove(id);
            }
            else
            {
                MarkDuelItemCollectedRemotely(id);
            }
        }
    }

    /// <summary>
    /// Uzay evresindeki düşen meteorların karşı oyuncuya (veya bota) çarpmasını yönetir.
    /// Kalkan varsa kalkan meteoru emer; yoksa bot 1 can kaybeder ve meteor parçalanır.
    /// </summary>
    public void CheckMeteorHitOpponent(SpaceMeteorProjectile meteor)
    {
        if (!inActiveDuel || !OpponentAlive || opponentAvatarObj == null || meteor == null) return;

        Vector3 diff = meteor.transform.position - opponentAvatarObj.transform.position;
        if (Mathf.Abs(diff.x) > 0.36f || Mathf.Abs(diff.y) > 0.38f) return;

        if (!meteor.TryConsumeHit()) return;

        if (!IsBotMatch)
        {
            // Online insan maçında hasar ve kalkan tüketimi karşı cihazda hesaplanıp WebSocket ile senkronlanır; burada görsel patlama yeterlidir.
            return;
        }

        if (botSelfRocketTimer > 0f)
        {
            return;
        }

        if (OpponentShield)
        {
            OpponentShield = false;
            botShieldTimer = 0f;
            return;
        }

        OpponentHealth = Mathf.Max(0, OpponentHealth - 1);
        botLockedTargetPlat = null;
        botHookSideSign = 0f;

        if (OpponentHealth <= 0)
        {
            OpponentAlive = false;
            UpdateBotRocketFlame(false);
            return;
        }

        // Meteor darbesi alan botu üstteki güvenli bir platformda yeniden doğur
        PlatformMovement[] platforms = FindObjectsByType<PlatformMovement>(FindObjectsSortMode.None);
        PlatformMovement respawnPlat = null;
        float highestY = -999f;
        for (int i = 0; i < platforms.Length; i++)
        {
            if (platforms[i] == null) continue;
            float py = platforms[i].transform.position.y;
            if (py > highestY && py < 3.6f)
            {
                highestY = py;
                respawnPlat = platforms[i];
            }
        }

        Vector3 respawnPos = respawnPlat != null
            ? new Vector3(respawnPlat.transform.position.x, GetPlatformTopY(respawnPlat) + 0.28f, 0f)
            : new Vector3(0f, 1.8f, 0f);

        opponentAvatarObj.transform.position = respawnPos;
        if (botRb != null)
        {
            botRb.position = respawnPos;
            botRb.linearVelocity = new Vector2(0f, -GameManager.globalPlatformSpeed);
        }
        botJumpCooldown = 0.14f;
        botVisualStretch = new Vector2(1.15f, 0.88f);
        JumpDustEffect.SpawnSuperJumpBurst(respawnPos);
    }

    public void OnLocalPlayerEliminated(int finalLocalScore)
    {
        if (!inActiveDuel || localEliminated) return;
        localEliminated = true;

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            if (gm.player != null)
            {
                PlayerController pc = gm.player.GetComponent<PlayerController>();
                if (pc != null) pc.HideAllMobileControls();
                gm.player.SetActive(false);
            }
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
        collectedDuelItemIds.Clear();
        activeDuelItems.Clear();
        recentLocalCollectedItems.Clear();

        if (!IsBotMatch && !string.IsNullOrEmpty(CurrentRoomCode))
        {
            EnsureRealtimeSocketConnected(CurrentRoomCode);
        }

        botSimTime = 0f;
        botExactScore = 0f;
        botSelfRocketTimer = 0f;
        botShieldTimer = 0f;
        botSuperFeetTimer = 0f;
        OpponentShield = false;
        OpponentSuperFeet = false;
        botMistakeTimer = 0f;
        botMistakeOffset = 0f;
        botCurrentMoveInput = 0f;
        botJumpCooldown = 0.10f;
        botCoyoteTimer = 0f;
        botCornerHopCooldown = 0f;
        botUngroundLockTimer = 0f;
        botAirTime = 0f;
        botLockedTargetPlat = null;
        botLastAttemptFromPlat = null;
        botLastAttemptTargetPlat = null;
        botSamePlatFailCount = 0;
        botLastTriedHookSign = 0f;
        botHookSideSign = 0f;
        botLastTakeoffY = -99f;
        botGrounded = false;
        botWasGrounded = true;
        botPlacedOnStartPlatform = false;
        botVisualStretch = Vector2.one;
        botNextRocketTime = UnityEngine.Random.Range(16f, 24f);
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
        opponentShieldAuraObj = null;
        opponentShieldAuraRenderer = null;
        opponentSuperFeetObj = null;
        opponentSuperFeetRenderer = null;
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
            float feetLocalY = (pc != null && pc.ayakNoktasi != null) ? pc.ayakNoktasi.localPosition.y : -0.158f;
            feetObj.transform.localPosition = new Vector3(0f, feetLocalY, 0f);
            botFeetTransform = feetObj.transform;
        }

        // Tepesindeki Piksel İsim (Sarı-Turuncu Degrade) + Altında Kırmızı Piksel Kalp ve Can Sayısı
        GameObject overheadRootObj = new GameObject("OpponentOverheadRoot");
        overheadRootObj.transform.SetParent(opponentAvatarObj.transform, false);
        overheadRootObj.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        opponentOverheadRoot = overheadRootObj.transform;

        string upperName = string.IsNullOrEmpty(OpponentName) ? "RAKİP" : OpponentName.ToUpperInvariant();

        // 1) Üst Satır: Rakip İsmi (Sarı-Krem -> Turuncu Degrade + Koyu Piksel Gölge, kompakt boyut)
        GameObject nameObj = new GameObject("OpponentNameTag");
        nameObj.transform.SetParent(opponentOverheadRoot, false);
        nameObj.transform.localPosition = new Vector3(0f, 0.34f, 0f);
        nameObj.transform.localScale = new Vector3(0.080f, 0.080f, 1f);

        opponentNameTagTmp = nameObj.AddComponent<TextMeshPro>();
        if (arcadeFont != null) opponentNameTagTmp.font = arcadeFont;
        if (arcadeFontMaterial != null) opponentNameTagTmp.fontSharedMaterial = arcadeFontMaterial;
        opponentNameTagTmp.fontSize = 26f;
        opponentNameTagTmp.fontStyle = FontStyles.Bold;
        opponentNameTagTmp.alignment = TextAlignmentOptions.Center;
        opponentNameTagTmp.textWrappingMode = TextWrappingModes.NoWrap;
        opponentNameTagTmp.color = Color.white;
        opponentNameTagTmp.enableVertexGradient = true;
        opponentNameTagTmp.colorGradient = new VertexGradient(
            new Color(1.00f, 0.96f, 0.72f, 1f),
            new Color(1.00f, 0.96f, 0.72f, 1f),
            new Color(1.00f, 0.65f, 0.30f, 1f),
            new Color(1.00f, 0.65f, 0.30f, 1f));
        opponentNameTagTmp.sortingOrder = 26;
        opponentNameTagTmp.text = upperName;

        GameObject nameShadowObj = new GameObject("OpponentNameShadow");
        nameShadowObj.transform.SetParent(nameObj.transform, false);
        nameShadowObj.transform.localPosition = new Vector3(0.14f, -0.16f, 0.02f);
        nameShadowObj.transform.localScale = Vector3.one;
        opponentNameShadowTmp = nameShadowObj.AddComponent<TextMeshPro>();
        if (arcadeFont != null) opponentNameShadowTmp.font = arcadeFont;
        if (arcadeFontMaterial != null) opponentNameShadowTmp.fontSharedMaterial = arcadeFontMaterial;
        opponentNameShadowTmp.fontSize = 26f;
        opponentNameShadowTmp.fontStyle = FontStyles.Bold;
        opponentNameShadowTmp.alignment = TextAlignmentOptions.Center;
        opponentNameShadowTmp.textWrappingMode = TextWrappingModes.NoWrap;
        opponentNameShadowTmp.color = new Color(0.22f, 0.11f, 0.17f, 0.95f);
        opponentNameShadowTmp.sortingOrder = 25;
        opponentNameShadowTmp.text = upperName;

        // 2) Alt Satır: Piksel Kırmızı Kalp İkonu + Beyaz Can Sayısı (Örn: ❤ 3)
        GameObject heartObj = new GameObject("OpponentHeartIcon");
        heartObj.transform.SetParent(opponentOverheadRoot, false);
        heartObj.transform.localPosition = new Vector3(-0.095f, 0.125f, 0f);
        heartObj.transform.localScale = new Vector3(0.56f, 0.56f, 1f);
        SpriteRenderer heartSr = heartObj.AddComponent<SpriteRenderer>();
        heartSr.sprite = GetPixelHeartSprite();
        heartSr.sortingOrder = 26;

        GameObject hpNumObj = new GameObject("OpponentHealthNum");
        hpNumObj.transform.SetParent(opponentOverheadRoot, false);
        hpNumObj.transform.localPosition = new Vector3(0.072f, 0.13f, 0f);
        hpNumObj.transform.localScale = new Vector3(0.082f, 0.082f, 1f);

        opponentHealthNumTmp = hpNumObj.AddComponent<TextMeshPro>();
        if (arcadeFont != null) opponentHealthNumTmp.font = arcadeFont;
        if (arcadeFontMaterial != null) opponentHealthNumTmp.fontSharedMaterial = arcadeFontMaterial;
        opponentHealthNumTmp.fontSize = 24f;
        opponentHealthNumTmp.fontStyle = FontStyles.Bold;
        opponentHealthNumTmp.alignment = TextAlignmentOptions.Center;
        opponentHealthNumTmp.textWrappingMode = TextWrappingModes.NoWrap;
        opponentHealthNumTmp.color = Color.white;
        opponentHealthNumTmp.sortingOrder = 26;
        opponentHealthNumTmp.text = OpponentHealth.ToString();

        GameObject hpShadowObj = new GameObject("OpponentHealthShadow");
        hpShadowObj.transform.SetParent(hpNumObj.transform, false);
        hpShadowObj.transform.localPosition = new Vector3(0.14f, -0.16f, 0.02f);
        hpShadowObj.transform.localScale = Vector3.one;
        opponentHealthShadowTmp = hpShadowObj.AddComponent<TextMeshPro>();
        if (arcadeFont != null) opponentHealthShadowTmp.font = arcadeFont;
        if (arcadeFontMaterial != null) opponentHealthShadowTmp.fontSharedMaterial = arcadeFontMaterial;
        opponentHealthShadowTmp.fontSize = 24f;
        opponentHealthShadowTmp.fontStyle = FontStyles.Bold;
        opponentHealthShadowTmp.alignment = TextAlignmentOptions.Center;
        opponentHealthShadowTmp.textWrappingMode = TextWrappingModes.NoWrap;
        opponentHealthShadowTmp.color = new Color(0.20f, 0.10f, 0.16f, 0.95f);
        opponentHealthShadowTmp.sortingOrder = 25;
        opponentHealthShadowTmp.text = OpponentHealth.ToString();
    }

    private static Sprite GetPixelHeartSprite()
    {
        if (cachedPixelHeartSprite != null) return cachedPixelHeartSprite;

        // 13x12 piksel sanat kalp (örnek görseldeki koyu çerçeveli kırmızı piksel kalp + sol üst parlama)
        string[] rows = new string[]
        {
            "..BBB...BBB..",
            ".BRRRB.BRRRB.",
            "BRWWWRBRRRRRB",
            "BRWRRRRRRRRRB",
            "BRRRRRRRRRRRB",
            "BRRRRRRRRRRSB",
            ".BRRRRRRRRSB.",
            "..BRRRRRRSB..",
            "...BRRRRSB...",
            "....BRRSB....",
            ".....BSB.....",
            "......B......"
        };

        int h = rows.Length;
        int w = rows[0].Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[w * h];

        Color cBorder = new Color(0.15f, 0.07f, 0.11f, 1f);
        Color cRed    = new Color(0.94f, 0.18f, 0.24f, 1f);
        Color cShade  = new Color(0.72f, 0.10f, 0.18f, 1f);
        Color cShine  = new Color(1.00f, 0.96f, 0.96f, 1f);

        for (int y = 0; y < h; y++)
        {
            string row = rows[h - 1 - y];
            for (int x = 0; x < w; x++)
            {
                char ch = row[x];
                Color px = Color.clear;
                if (ch == 'B') px = cBorder;
                else if (ch == 'R') px = cRed;
                else if (ch == 'S') px = cShade;
                else if (ch == 'W') px = cShine;
                cols[y * w + x] = px;
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedPixelHeartSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
        return cachedPixelHeartSprite;
    }

    private void LoadOpponentCornerSprites(string charId)
    {
        opponentCornerFrames = null;
        Texture2D tex = Resources.Load<Texture2D>("Characters/corner_hang_" + charId);
        if (tex == null) tex = Resources.Load<Texture2D>("Characters/corner_hang_pink_man");
        if (tex == null || tex.height <= 0) return;

        float cornerPpu = 114f;
        if (charId == CharacterShopData.NinjaFrogId)
        {
            cornerPpu = 88f;
        }
        else if (charId == CharacterShopData.VirtualGuyId)
        {
            cornerPpu = 98f;
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

    private static Sprite GetCharacterPortraitSprite(string charId)
    {
        CharacterShopConfig cfg = CharacterShopConfig.Load();
        if (cfg != null)
        {
            CharacterShopConfig.CharacterVisualEntry entry = cfg.GetVisualEntry(charId);
            if (entry != null && entry.idleFrames != null && entry.idleFrames.Length > 0 && entry.idleFrames[0] != null)
            {
                return entry.idleFrames[0];
            }
        }
        return null;
    }

    private static Sprite LoadCrispUISprite(string resourcePath)
    {
        Sprite sp = Resources.Load<Sprite>(resourcePath);
        if (sp != null) return sp;

        Texture2D tex = Resources.Load<Texture2D>(resourcePath);
        if (tex != null)
        {
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        return null;
    }

    private void BuildGameplayDuelHUD(Transform canvasTr)
    {
        // Solo moddaki üst CAN / SKOR yazılarını gizle (Pause ve Coin olduğu yerde temiz kalır)
        GameManager gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            if (gm.scoreText != null) gm.scoreText.gameObject.SetActive(false);
            if (gm.healthText != null) gm.healthText.gameObject.SetActive(false);
        }

        displayedDuelTugRatio = 0.5f;
        displayedCrownAnchorX = 0.082f;
        duelLoadedOppCharId = string.Empty;

        // 1. Üst Piksel Arena Düello Skorboardu (Pause ve Coin ile sıfır çakışma)
        duelTopHudObj = CreateUIObject("DuelTopVSBar", canvasTr);
        SetAnchors(duelTopHudObj, new Vector2(0.018f, 0.922f), new Vector2(0.860f, 0.990f));

        Image vsBg = duelTopHudObj.AddComponent<Image>();
        if (duelScoreboardFrameSprite == null)
        {
            duelScoreboardFrameSprite = LoadCrispUISprite("UI/duel_scoreboard_frame");
        }
        if (duelScoreboardFrameSprite != null)
        {
            vsBg.sprite = duelScoreboardFrameSprite;
            vsBg.type = Image.Type.Simple;
            vsBg.color = Color.white;
        }
        else
        {
            vsBg.sprite = pillSprite;
            vsBg.type = Image.Type.Sliced;
            vsBg.color = new Color(0.10f, 0.14f, 0.24f, 0.94f);
        }
        vsBg.raycastTarget = false;

        // Sol Karakter Portresi (Oyuncu + Varsa Selfie Kafası)
        GameObject myPortraitObj = CreateUIObject("MyAvatar", duelTopHudObj.transform);
        SetAnchors(myPortraitObj, new Vector2(0.038f, 0.275f), new Vector2(0.126f, 0.775f));
        duelMyAvatarImg = myPortraitObj.AddComponent<Image>();
        duelMyAvatarImg.sprite = GetCharacterPortraitSprite(CharacterShopData.GetSelectedCharacterId());
        duelMyAvatarImg.preserveAspect = true;
        duelMyAvatarImg.raycastTarget = false;

        if (CharacterShopData.IsCustomFaceEnabled())
        {
            Sprite faceSp = CharacterShopData.LoadCustomFaceSprite();
            if (faceSp != null)
            {
                GameObject myFaceObj = CreateUIObject("MySelfieHead", myPortraitObj.transform);
                SetAnchors(myFaceObj, new Vector2(0.14f, 0.30f), new Vector2(0.86f, 0.96f));
                Image faceImg = myFaceObj.AddComponent<Image>();
                faceImg.sprite = faceSp;
                faceImg.preserveAspect = true;
                faceImg.raycastTarget = false;
            }
        }

        // Sağ Karakter Portresi (Rakip)
        GameObject oppPortraitObj = CreateUIObject("OppAvatar", duelTopHudObj.transform);
        SetAnchors(oppPortraitObj, new Vector2(0.874f, 0.275f), new Vector2(0.962f, 0.775f));
        duelOppAvatarImg = oppPortraitObj.AddComponent<Image>();
        duelLoadedOppCharId = OpponentCharId;
        duelOppAvatarImg.sprite = GetCharacterPortraitSprite(duelLoadedOppCharId);
        duelOppAvatarImg.preserveAspect = true;
        duelOppAvatarImg.raycastTarget = false;
        oppPortraitObj.transform.localScale = new Vector3(-1f, 1f, 1f); // Rakip sola (merkeze) baksın

        // Üst Satır İsimler: Sol "SEN", Sağ Rakip İsmi
        duelMyNameTmp = CreateLabel(duelTopHudObj.transform, "MyName", "SEN",
            new Vector2(0.146f, 0.58f), new Vector2(0.422f, 0.87f), 7.5f, 11.5f,
            TextAlignmentOptions.Left, new Color(0.56f, 0.98f, 0.88f, 1f), true);
        duelMyNameTmp.fontStyle = FontStyles.Bold;

        string oppUpper = string.IsNullOrEmpty(OpponentName) ? "RAKİP" : OpponentName.ToUpperInvariant();
        duelOppNameTmp = CreateLabel(duelTopHudObj.transform, "OppName", oppUpper,
            new Vector2(0.578f, 0.58f), new Vector2(0.854f, 0.87f), 7.0f, 11.5f,
            TextAlignmentOptions.Right, new Color(1.00f, 0.70f, 0.82f, 1f), true);
        duelOppNameTmp.fontStyle = FontStyles.Bold;

        // Orta Satır Skorlar + Simetrik 3'er Piksel Kalp
        duelMyStatusText = CreateLabel(duelTopHudObj.transform, "MyScore", "0",
            new Vector2(0.146f, 0.23f), new Vector2(0.298f, 0.60f), 9.5f, 16f,
            TextAlignmentOptions.Left, Color.white, true);
        duelMyStatusText.fontStyle = FontStyles.Bold;

        duelOppStatusText = CreateLabel(duelTopHudObj.transform, "OppScore", "0",
            new Vector2(0.702f, 0.23f), new Vector2(0.854f, 0.60f), 9.5f, 16f,
            TextAlignmentOptions.Right, Color.white, true);
        duelOppStatusText.fontStyle = FontStyles.Bold;

        Sprite heartSp = GetPixelHeartSprite();
        for (int i = 0; i < 3; i++)
        {
            GameObject mh = CreateUIObject("MyHeart_" + i, duelTopHudObj.transform);
            float lx0 = 0.302f + i * 0.040f;
            SetAnchors(mh, new Vector2(lx0, 0.28f), new Vector2(lx0 + 0.037f, 0.56f));
            Image mhImg = mh.AddComponent<Image>();
            mhImg.sprite = heartSp;
            mhImg.preserveAspect = true;
            mhImg.raycastTarget = false;
            duelMyHeartImgs[i] = mhImg;

            GameObject oh = CreateUIObject("OppHeart_" + i, duelTopHudObj.transform);
            float rx0 = 0.581f + (2 - i) * 0.040f; // Merkeze yakın olan son kalp, dıştan içe azalsın
            SetAnchors(oh, new Vector2(rx0, 0.28f), new Vector2(rx0 + 0.037f, 0.56f));
            Image ohImg = oh.AddComponent<Image>();
            ohImg.sprite = heartSp;
            ohImg.preserveAspect = true;
            ohImg.raycastTarget = false;
            duelOppHeartImgs[i] = ohImg;
        }

        // Elenme Rozetleri (0 can kaldığında kalplerin yerinde yanar)
        duelMyElimBadgeTmp = CreateLabel(duelTopHudObj.transform, "MyElimBadge", "ELENDİ",
            new Vector2(0.298f, 0.26f), new Vector2(0.425f, 0.56f), 6.5f, 10f,
            TextAlignmentOptions.Right, new Color(1.00f, 0.36f, 0.46f, 1f), true);
        duelMyElimBadgeTmp.fontStyle = FontStyles.Bold;
        duelMyElimBadgeTmp.gameObject.SetActive(false);

        duelOppElimBadgeTmp = CreateLabel(duelTopHudObj.transform, "OppElimBadge", "ELENDİ",
            new Vector2(0.575f, 0.26f), new Vector2(0.702f, 0.56f), 6.5f, 10f,
            TextAlignmentOptions.Left, new Color(1.00f, 0.36f, 0.46f, 1f), true);
        duelOppElimBadgeTmp.fontStyle = FontStyles.Bold;
        duelOppElimBadgeTmp.gameObject.SetActive(false);

        // Merkez Altın Elmas İçinde "VS"
        TextMeshProUGUI vsCenterTmp = CreateLabel(duelTopHudObj.transform, "VS", "VS",
            new Vector2(0.436f, 0.35f), new Vector2(0.564f, 0.80f), 9.5f, 14.5f,
            TextAlignmentOptions.Center, new Color(1.00f, 0.98f, 0.90f, 1f), true);
        vsCenterTmp.fontStyle = FontStyles.Bold;

        // Alt Çekişme (Tug-of-War) Momentum Barı (x: 0.154..0.846, y: 0.111..0.194)
        GameObject tugLeftObj = CreateUIObject("TugLeftFill", duelTopHudObj.transform);
        duelTugLeftRt = tugLeftObj.GetComponent<RectTransform>();
        SetAnchors(tugLeftObj, new Vector2(0.154f, 0.112f), new Vector2(0.500f, 0.192f));
        Image tugLeftImg = tugLeftObj.AddComponent<Image>();
        tugLeftImg.color = new Color(0.26f, 0.95f, 0.78f, 1f);
        tugLeftImg.raycastTarget = false;

        GameObject tugRightObj = CreateUIObject("TugRightFill", duelTopHudObj.transform);
        duelTugRightRt = tugRightObj.GetComponent<RectTransform>();
        SetAnchors(tugRightObj, new Vector2(0.500f, 0.112f), new Vector2(0.846f, 0.192f));
        Image tugRightImg = tugRightObj.AddComponent<Image>();
        tugRightImg.color = new Color(1.00f, 0.38f, 0.56f, 1f);
        tugRightImg.raycastTarget = false;

        GameObject tugSparkObj = CreateUIObject("TugSpark", duelTopHudObj.transform);
        duelTugSparkRt = tugSparkObj.GetComponent<RectTransform>();
        SetAnchors(tugSparkObj, new Vector2(0.496f, 0.085f), new Vector2(0.504f, 0.220f));
        Image tugSparkImg = tugSparkObj.AddComponent<Image>();
        tugSparkImg.color = new Color(1.00f, 0.97f, 0.76f, 1f);
        tugSparkImg.raycastTarget = false;

        // VS Altında Canlı Puan Farkı Rozeti (+120 / -85)
        duelDeltaPillObj = CreateUIObject("ScoreDeltaPill", duelTopHudObj.transform);
        SetAnchors(duelDeltaPillObj, new Vector2(0.415f, -0.25f), new Vector2(0.585f, 0.08f));
        duelDeltaPillBg = duelDeltaPillObj.AddComponent<Image>();
        duelDeltaPillBg.sprite = pillSprite;
        duelDeltaPillBg.type = Image.Type.Sliced;
        duelDeltaPillBg.color = new Color(0.11f, 0.09f, 0.18f, 0.94f);
        duelDeltaPillBg.raycastTarget = false;

        duelDeltaTmp = CreateLabel(duelDeltaPillObj.transform, "DeltaTxt", "+0",
            new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.95f), 6.5f, 10f,
            TextAlignmentOptions.Center, new Color(0.46f, 1.00f, 0.68f, 1f), true);
        duelDeltaTmp.fontStyle = FontStyles.Bold;
        duelDeltaPillObj.SetActive(false);

        // Lider Oyuncunun Portresi Üstünde Süzülen Altın Piksel Taç
        if (duelCrownSprite == null)
        {
            duelCrownSprite = LoadCrispUISprite("UI/duel_crown_pixel");
        }
        if (duelCrownSprite != null)
        {
            GameObject crownObj = CreateUIObject("LeaderCrown", duelTopHudObj.transform);
            duelCrownRt = crownObj.GetComponent<RectTransform>();
            SetAnchors(crownObj, new Vector2(0.050f, 0.80f), new Vector2(0.114f, 1.15f));
            Image crownImg = crownObj.AddComponent<Image>();
            crownImg.sprite = duelCrownSprite;
            crownImg.preserveAspect = true;
            crownImg.raycastTarget = false;
            crownObj.SetActive(false);
        }

        // 2. Roket Sabotaj Uyarı Şeridi
        sabotageBannerObj = CreateUIObject("RocketSabotageBanner", canvasTr);
        SetAnchors(sabotageBannerObj, new Vector2(0.08f, 0.832f), new Vector2(0.92f, 0.885f));
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
            sd = SharedSeed,
            ci = recentLocalCollectedItems.Count > 0 ? recentLocalCollectedItems.ToArray() : null
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

            if (d.ci != null && d.ci.Length > 0)
            {
                for (int i = 0; i < d.ci.Length; i++)
                {
                    MarkDuelItemCollectedRemotely(d.ci[i]);
                }
            }

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
            PlatformMovement startPlat = FindPlatformByIndex(0);
            if (startPlat == null)
            {
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
            }

            if (startPlat != null)
            {
                Collider2D pCol = startPlat.GetComponent<Collider2D>();
                float topY = pCol != null ? pCol.bounds.max.y : (startPlat.transform.position.y + 0.15f);
                Vector3 placePos = new Vector3(startPlat.transform.position.x + 0.22f, topY + 0.255f, 0f);
                opponentAvatarObj.transform.position = placePos;
                botRb.position = placePos;
                botRb.linearVelocity = new Vector2(0f, -GameManager.globalPlatformSpeed);
                OpponentTargetPos = placePos;
                botPlacedOnStartPlatform = true;
            }
        }

        Camera cam = Camera.main;
        float camBottomY = -5.0f;
        if (cam != null)
        {
            botWrapThreshold = (cam.orthographicSize * cam.aspect) + 0.45f;
            camBottomY = cam.transform.position.y - cam.orthographicSize;
        }

        botSimTime += dt;
        if (botCornerHopCooldown > 0f) botCornerHopCooldown -= dt;
        if (botUngroundLockTimer > 0f) botUngroundLockTimer -= dt;

        if (botShieldTimer > 0f)
        {
            botShieldTimer -= dt;
            if (botShieldTimer <= 0f)
            {
                botShieldTimer = 0f;
                OpponentShield = false;
            }
        }
        if (botSuperFeetTimer > 0f)
        {
            botSuperFeetTimer -= dt;
            if (botSuperFeetTimer <= 0f)
            {
                botSuperFeetTimer = 0f;
                OpponentSuperFeet = false;
            }
        }

        GameManager gm = GameManager.Instance;
        bool localOnRocket = gm != null && gm.IsRocketBoosting;
        float scoreRate = (botSelfRocketTimer > 0f || localOnRocket) ? 16f : 10f;
        botExactScore += dt * scoreRate;
        OpponentScore = Mathf.RoundToInt(botExactScore);

        float speedRatio = (GameManager.globalPlatformSpeed > 0f) ? (GameManager.globalPlatformSpeed / 1.7f) : 1f;
        speedRatio = Mathf.Clamp(speedRatio, 1f, 1.41f);

        // Bot yalnızca sahnedeki gerçek bir Roket itemini topladığında (CheckBotSharedItemPickups) roketle uçar!

        // Uzay evresindeki düşen meteorları algıla (kaçınma ve güvenli platform seçimi için)
        SpaceMeteorProjectile[] activeMeteors = FindObjectsByType<SpaceMeteorProjectile>(FindObjectsSortMode.None);

        // Zemin & Köşe Tırmanma Kontrolü (Simetrik ve görsel corner_hang kutusuyla birebir uyumlu)
        Vector2 botCenterPos = opponentAvatarObj.transform.position;
        Vector2 feetPos = botFeetTransform != null
            ? (Vector2)botFeetTransform.position
            : botCenterPos + new Vector2(0f, -0.225f);
        Collider2D rawGroundHit = Physics2D.OverlapCircle(feetPos, botFootRadius, botGroundMask);

        bool standingOnTop = false;
        bool touchingUpperSideCorner = false;
        PlatformMovement cornerPlat = null;
        float currentBotMinY = botCollider.bounds.min.y;

        // Zıplama anından hemen sonraki 70ms boyunca (FixedUpdate konum adımı tamamlanana kadar) sahte yere temas algılamasını kilitle!
        if (botUngroundLockTimer <= 0f && rawGroundHit != null)
        {
            Bounds gb = rawGroundHit.bounds;
            if (botRb.linearVelocity.y <= 0.22f && currentBotMinY >= gb.max.y - 0.14f)
            {
                standingOnTop = true;
            }
        }

        // Üstteki bir platformun yan/köşe yüzeyiyle temas kontrolü:
        // YALNIZCA ayaklar platformun üst yüzeyine yetişemediğinde (currentBotMinY < sb.max.y - 0.03f) ve tepe noktasında (vy <= 0.20f) devreye girer!
        if (botUngroundLockTimer <= 0f && !standingOnTop && botRb.linearVelocity.y <= 0.20f && botRb.linearVelocity.y >= -3.8f)
        {
            Collider2D sideHit = Physics2D.OverlapBox(
                botCenterPos + new Vector2(0f, -0.04f),
                new Vector2(0.68f, 0.44f),
                0f,
                botGroundMask);

            if (sideHit != null && !sideHit.usedByEffector)
            {
                Bounds sb = sideHit.bounds;
                if (currentBotMinY < sb.max.y - 0.03f
                    && sb.max.y > botLastTakeoffY + 0.30f
                    && botCenterPos.y >= sb.min.y - 0.08f)
                {
                    touchingUpperSideCorner = true;
                    cornerPlat = sideHit.GetComponentInParent<PlatformMovement>();
                }
            }
        }

        botGrounded = standingOnTop;
        PlatformMovement standingPlat = standingOnTop && rawGroundHit != null
            ? rawGroundHit.GetComponentInParent<PlatformMovement>()
            : null;
        Collider2D standingCol = standingOnTop ? rawGroundHit : null;

        if (botGrounded)
        {
            botCoyoteTimer = 0.12f;
            botAirTime = 0f;
            if (!botWasGrounded && botRb.linearVelocity.y <= 0.15f)
            {
                botVisualStretch = new Vector2(1.14f, 0.88f);
                // Aynı platforma geri düştüyse başarısız deneme sayacını artır ki aynı hatayı tekrarlamasın!
                if (standingPlat != null && standingPlat == botLastAttemptFromPlat)
                {
                    botSamePlatFailCount++;
                }
                else
                {
                    botSamePlatFailCount = 0;
                    botLastTriedHookSign = 0f;
                }
                botLockedTargetPlat = null;
                botHookSideSign = 0f;
            }
        }
        else
        {
            botCoyoteTimer -= dt;
        }
        botWasGrounded = botGrounded;

        Vector3 botPos = opponentAvatarObj.transform.position;
        float botFeetY = botCollider.bounds.min.y;
        float fullWrapWidth = botWrapThreshold * 2f;
        float currentMoveSpeed = botMoveSpeed * Mathf.Sqrt(speedRatio);
        float jumpImpulse = botJumpForce * Mathf.Sqrt(speedRatio);
        float gravityAccel = Mathf.Max(6f, Mathf.Abs(Physics2D.gravity.y) * botBaseGravityScale * speedRatio);

        if (botSelfRocketTimer > 0f)
        {
            botSelfRocketTimer -= dt;
            botRb.gravityScale = 0f;
            float targetY = gm != null ? gm.rocketTargetY : 1.15f;
            float nextY = Mathf.Lerp(botPos.y, targetY, Mathf.Clamp01(dt * 6.5f));

            float rocketVx;
            if (botSelfRocketTimer < 0.95f)
            {
                // Roket bitmeden son 0.95 saniyede altındaki en güvenli platformun tam üstüne hizalan!
                PlatformMovement landingTarget = null;
                float bestRocketLandScore = -999f;
                for (int i = 0; i < platforms.Length; i++)
                {
                    PlatformMovement p = platforms[i];
                    if (p == null) continue;
                    float py = p.transform.position.y - GameManager.globalPlatformSpeed * botSelfRocketTimer;
                    if (py > nextY + 0.35f || py < -2.4f) continue;

                    float predX = p.PredictXAfter(botSelfRocketTimer + 0.15f);
                    float dx = predX - botPos.x;
                    float meteorPenalty = IsThreatenedByMeteor(predX, py, activeMeteors) ? 3.5f : 0f;
                    float sc = py * 1.4f - Mathf.Abs(dx) * 0.65f - meteorPenalty;
                    if (sc > bestRocketLandScore)
                    {
                        bestRocketLandScore = sc;
                        landingTarget = p;
                    }
                }

                if (landingTarget != null)
                {
                    float aimRocketX = landingTarget.PredictXAfter(botSelfRocketTimer + 0.12f);
                    float dx = aimRocketX - botPos.x;
                    rocketVx = Mathf.Clamp(dx * 5.2f, -currentMoveSpeed, currentMoveSpeed);
                }
                else
                {
                    rocketVx = Mathf.Clamp(-botPos.x * 2.5f, -currentMoveSpeed * 0.7f, currentMoveSpeed * 0.7f);
                }
            }
            else
            {
                rocketVx = Mathf.Sin(botSimTime * 2.8f) * currentMoveSpeed * 0.55f;
                if (Mathf.Abs(botPos.x) > 1.35f && Mathf.Sign(rocketVx) == Mathf.Sign(botPos.x))
                {
                    rocketVx = -rocketVx;
                }
            }

            opponentAvatarObj.transform.position = new Vector3(botPos.x, nextY, 0f);
            botRb.linearVelocity = new Vector2(rocketVx, 0f);
            botCurrentMoveInput = Mathf.Clamp(rocketVx / Mathf.Max(0.1f, currentMoveSpeed), -1f, 1f);
            UpdateBotRocketFlame(true);

            if (botSelfRocketTimer <= 0f)
            {
                UpdateBotRocketFlame(false);
                botRb.linearVelocity = new Vector2(rocketVx * 0.4f, -GameManager.globalPlatformSpeed);
            }
        }
        else
        {
            UpdateBotRocketFlame(false);
            botRb.gravityScale = botBaseGravityScale * speedRatio;

            float desiredMoveInput = 0f;
            bool launchedVelocityThisFrame = false;

            // 0) Yüksek platform kenarına yetişemediğinde (Corner Hang) tam platform üstüne çıkaracak çevik köşe sıçraması (fazla yükselip havada vakit kaybetmez!)
            if (!botGrounded && touchingUpperSideCorner && cornerPlat != null && botCornerHopCooldown <= 0f)
            {
                float inwardDir = Mathf.Sign(cornerPlat.transform.position.x - botPos.x);
                if (Mathf.Abs(inwardDir) < 0.01f) inwardDir = 1f;

                botCurrentMoveInput = inwardDir * 0.90f;
                desiredMoveInput = inwardDir;
                botRb.linearVelocity = new Vector2(inwardDir * currentMoveSpeed * 0.90f, jumpImpulse * 0.58f);
                launchedVelocityThisFrame = true;
                botUngroundLockTimer = 0.06f;
                botVisualStretch = new Vector2(0.84f, 1.20f);
                JumpDustEffect.SpawnJumpDust(feetPos);
                botCornerHopCooldown = 0.24f;
                botLockedTargetPlat = cornerPlat;
                botHookSideSign = 0f;
            }
            else if (botGrounded || (botCoyoteTimer > 0f && botUngroundLockTimer <= 0f && botRb.linearVelocity.y <= 0.15f && standingPlat != null))
            {
                botJumpCooldown -= dt;

                // YERDEYKEN: Platform üzerinde sağa-sola gezinme yapma; ÖNCE DÜZ ZIPLA, SONRA HAVADA YÖN VER!
                desiredMoveInput = 0f;

                float safeMinX = botPos.x - 0.45f;
                float safeMaxX = botPos.x + 0.45f;
                if (standingCol != null)
                {
                    Bounds sb = standingCol.bounds;
                    safeMinX = sb.min.x + 0.08f;
                    safeMaxX = sb.max.x - 0.08f;
                    if (safeMinX > safeMaxX)
                    {
                        float mid = sb.center.x;
                        safeMinX = mid - 0.04f;
                        safeMaxX = mid + 0.04f;
                    }
                }
                else if (standingPlat != null)
                {
                    safeMinX = standingPlat.transform.position.x - 0.52f;
                    safeMaxX = standingPlat.transform.position.x + 0.52f;
                }

                PlatformMovement bestUpper = SelectBestUpperPlatformForBot(
                    platforms,
                    standingPlat,
                    botPos,
                    botFeetY,
                    fullWrapWidth,
                    currentMoveSpeed,
                    jumpImpulse,
                    gravityAccel,
                    activeMeteors,
                    out float estFlightTime);

                bool underImmediateMeteor = IsThreatenedByMeteor(botPos.x, botPos.y, activeMeteors, out float meteorThreatX);
                bool calmWaitNearTop = botPos.y > 2.15f && !underImmediateMeteor;

                if (bestUpper != null && !calmWaitNearTop)
                {
                    Collider2D upperCol = bestUpper.GetComponent<Collider2D>();
                    float upperTopY = upperCol != null ? upperCol.bounds.max.y : (bestUpper.transform.position.y + 0.14f);
                    bool upperIsSolid = upperCol == null || !upperCol.usedByEffector;

                    float nearFutureUpperX = bestUpper.PredictXAfter(0.16f);
                    float landingUpperX = bestUpper.PredictXAfter(estFlightTime);
                    float horizDx = ComputeSmartHorizontalDelta(botPos.x, landingUpperX, fullWrapWidth);

                    PlatformMovement blockingCeil = FindOverheadSolidBlocker(
                        platforms,
                        standingPlat,
                        botPos.x,
                        botPos.y,
                        upperTopY,
                        0.14f);

                    bool blockedOverhead = (upperIsSolid && Mathf.Abs(nearFutureUpperX - botPos.x) < 0.88f && (upperTopY - botFeetY) < 1.90f)
                                        || (blockingCeil != null);

                    // Havada hangi yöne kavis çizeceğini alt platformun açık kenarına ve ekran sınırına göre seç
                    float plannedHookSign = 0f;
                    if (blockedOverhead)
                    {
                        PlatformMovement refBlock = blockingCeil != null ? blockingCeil : bestUpper;
                        float bx0 = refBlock.PredictXAfter(0.15f);
                        float sx0 = standingPlat != null ? standingPlat.PredictXAfter(0.10f) : botPos.x;

                        float rClear = bx0 + 0.92f;
                        float lClear = bx0 - 0.92f;
                        float rScore = Mathf.Abs(rClear - botPos.x)
                                     + Mathf.Max(0f, rClear - (sx0 + 0.56f)) * 1.6f
                                     + (rClear > 2.25f ? 2.5f : 0f);
                        float lScore = Mathf.Abs(lClear - botPos.x)
                                     + Mathf.Max(0f, (sx0 - 0.56f) - lClear) * 1.6f
                                     + (lClear < -2.25f ? 2.5f : 0f);

                        plannedHookSign = (rScore <= lScore) ? 1f : -1f;

                        // Yalnızca 3+ kez aynı kenardan da geçemediyse zıt kenarı dene
                        if (botSamePlatFailCount >= 3 && Mathf.Abs(botLastTriedHookSign) > 0.1f)
                        {
                            float flipped = -botLastTriedHookSign;
                            if (Mathf.Abs(bx0 + flipped * 0.92f) <= 2.30f)
                            {
                                plannedHookSign = flipped;
                            }
                        }
                    }

                    // Uzaktaki hareketli platform bize doğru yaklaşıyorsa menzile girene kadar platformda sabit bekle, sonra zıpla!
                    float maxAirSteerReach = (currentMoveSpeed * Mathf.Max(0.25f, estFlightTime - 0.035f) * 0.95f) + 0.52f;
                    bool waitForMovingPlat = !blockedOverhead
                        && Mathf.Abs(horizDx) > maxAirSteerReach
                        && bestUpper.isMovingHorizontal
                        && (Mathf.Abs(bestUpper.PredictXAfter(estFlightTime + 0.25f) - botPos.x) < Mathf.Abs(horizDx))
                        && botPos.y > -2.2f;

                    if (!waitForMovingPlat && botJumpCooldown <= 0f && botRb.linearVelocity.y <= 0.15f)
                    {
                        float retryJumpMult = (botSamePlatFailCount >= 1) ? 1.06f : 1.0f;
                        if (Mathf.Abs(plannedHookSign) > 0.1f)
                        {
                            botLastTriedHookSign = plannedHookSign;
                        }

                        // ÖNCE SAF DİKEY ZIPLA (ters yöne giden platformun yatay hızını miras alma; havada yön ver!)
                        ExecuteBotJump(
                            jumpImpulse * retryJumpMult,
                            feetPos,
                            botPos.y,
                            standingPlat,
                            bestUpper,
                            plannedHookSign,
                            gm);
                        launchedVelocityThisFrame = true;
                    }
                }
                else if (standingPlat != null && underImmediateMeteor)
                {
                    float dodgeDir = (botPos.x >= meteorThreatX) ? 1f : -1f;
                    float targetStandX = dodgeDir > 0f ? safeMaxX : safeMinX;
                    desiredMoveInput = Mathf.Clamp((targetStandX - botPos.x) * 4.0f, -1f, 1f);
                }
            }
            else
            {
                // HAVADAYKEN: Önü açıksa ilk 0.035 sn düz yükselip sonra havada yön ver; tavan altındaysa havalanır havalanmaz kenara kır!
                botAirTime += dt;
                float airSteerDelay = (Mathf.Abs(botHookSideSign) > 0.1f || botSamePlatFailCount >= 1) ? 0f : 0.035f;

                if (botAirTime < airSteerDelay)
                {
                    desiredMoveInput = 0f;
                }
                else if (botRb.linearVelocity.y > 0.08f)
                {
                    PlatformMovement airTarget = botLockedTargetPlat;
                    if (airTarget == null || (airTarget.transform.position.y - botPos.y) < -0.25f || (airTarget.transform.position.y - botPos.y) > 2.15f)
                    {
                        airTarget = SelectBestUpperPlatformForBot(
                            platforms,
                            null,
                            botPos,
                            botFeetY,
                            fullWrapWidth,
                            currentMoveSpeed,
                            botRb.linearVelocity.y,
                            gravityAccel,
                            activeMeteors,
                            out _);
                        botLockedTargetPlat = airTarget;
                    }

                    float remRiseTime = Mathf.Clamp(botRb.linearVelocity.y / gravityAccel, 0.06f, 0.45f);
                    float airTargetTopY = airTarget != null ? GetPlatformTopY(airTarget) : (botPos.y + 1.1f);
                    PlatformMovement airBlocker = FindOverheadSolidBlocker(platforms, null, botPos.x, botPos.y, airTargetTopY, 0.06f);

                    if (airTarget != null)
                    {
                        Collider2D tCol = airTarget.GetComponent<Collider2D>();
                        float platTopY = tCol != null ? tCol.bounds.max.y : (airTarget.transform.position.y + 0.14f);
                        float platBottomY = tCol != null ? tCol.bounds.min.y : (airTarget.transform.position.y - 0.14f);
                        float botTopY = botCollider.bounds.max.y;
                        bool targetIsSolid = tCol == null || !tCol.usedByEffector;

                        float aimX = airTarget.PredictXAfter(remRiseTime + 0.08f);

                        // Katı platform gövdesi kuralı: ayaklar üst yüzeyi aşana kadar platformun ANLIK [nowX-0.91, nowX+0.91] alt gövdesine girme!
                        if (airBlocker != null && airBlocker != airTarget && botFeetY < GetPlatformTopY(airBlocker) - 0.02f)
                        {
                            float bxNow = airBlocker.PredictXAfter(0.05f);
                            float s = Mathf.Abs(botHookSideSign) > 0.1f
                                ? botHookSideSign
                                : ((botPos.x >= bxNow) ? 1f : -1f);
                            botHookSideSign = s;
                            aimX = bxNow + s * 0.92f;
                        }
                        else if (targetIsSolid
                                 && botFeetY < platTopY - 0.02f
                                 && (botRb.linearVelocity.y > 1.35f || botTopY < platBottomY + 0.08f))
                        {
                            float nowPlatX = airTarget.PredictXAfter(0.05f);
                            float side = Mathf.Abs(botHookSideSign) > 0.1f
                                ? botHookSideSign
                                : ((botPos.x >= nowPlatX) ? 1f : -1f);

                            bool crossesUnderneath =
                                (side > 0f && aimX < nowPlatX + 0.91f && botPos.x < nowPlatX + 0.95f) ||
                                (side < 0f && aimX > nowPlatX - 0.91f && botPos.x > nowPlatX - 0.95f);

                            if (crossesUnderneath && (platBottomY - botTopY) < 0.78f)
                            {
                                botHookSideSign = side;
                                aimX = nowPlatX + side * 0.91f;
                            }
                        }

                        float wDx = ComputeSmartHorizontalDelta(botPos.x, aimX, fullWrapWidth);
                        if (Mathf.Abs(wDx) > 0.02f)
                        {
                            desiredMoveInput = Mathf.Clamp(wDx * 7.5f, -1f, 1f);
                        }
                    }
                }
                else
                {
                    // DÜŞERKEN: Önce kilitli hedef platforma, olmuyorsa yetişilebilecek en yüksek güvenli platforma havada yönlen!
                    PlatformMovement landPlat = null;
                    float bestLandTime = 0.20f;

                    if (botLockedTargetPlat != null)
                    {
                        float lockTopY = GetPlatformTopY(botLockedTargetPlat);
                        if (lockTopY <= botFeetY + 0.22f && IsLandingReachable(
                                botPos.x,
                                botFeetY,
                                botRb.linearVelocity.y,
                                botLockedTargetPlat,
                                lockTopY,
                                fullWrapWidth,
                                currentMoveSpeed,
                                gravityAccel,
                                out float lockFallT))
                        {
                            landPlat = botLockedTargetPlat;
                            bestLandTime = lockFallT;
                        }
                    }

                    if (landPlat == null)
                    {
                        float bestLandScore = -999f;
                        for (int i = 0; i < platforms.Length; i++)
                        {
                            PlatformMovement p = platforms[i];
                            if (p == null) continue;
                            float topY = GetPlatformTopY(p);
                            if (topY > botFeetY + 0.22f || topY < camBottomY - 0.15f) continue;

                            if (!IsLandingReachable(
                                    botPos.x,
                                    botFeetY,
                                    botRb.linearVelocity.y,
                                    p,
                                    topY,
                                    fullWrapWidth,
                                    currentMoveSpeed,
                                    gravityAccel,
                                    out float tFall))
                            {
                                continue;
                            }

                            float predX = p.PredictXAfter(tFall);
                            float wDx = ComputeSmartHorizontalDelta(botPos.x, predX, fullWrapWidth);
                            float meteorPenalty = IsThreatenedByMeteor(predX, topY, activeMeteors) ? 2.4f : 0f;
                            float landScore = topY * 1.65f - Mathf.Abs(wDx) * 0.70f - meteorPenalty;

                            if (landScore > bestLandScore)
                            {
                                bestLandScore = landScore;
                                landPlat = p;
                                bestLandTime = tFall;
                            }
                        }
                    }

                    if (landPlat != null)
                    {
                        float landX = landPlat.PredictXAfter(bestLandTime);

                        // Alt platforma geri iniyorsa aynı kavis (hookSign) yönündeki en dış güvenli kenara in ki bir sonraki zıplamada tavanı direkt geçsin!
                        if (botLockedTargetPlat != null && landPlat != botLockedTargetPlat)
                        {
                            float failedTargetTopY = GetPlatformTopY(botLockedTargetPlat);
                            float landTopY = GetPlatformTopY(landPlat);
                            if (failedTargetTopY > landTopY + 0.25f)
                            {
                                float ceilX = botLockedTargetPlat.PredictXAfter(bestLandTime + 0.14f);
                                if (Mathf.Abs(landX - ceilX) < 0.90f)
                                {
                                    float side = Mathf.Abs(botHookSideSign) > 0.1f
                                        ? botHookSideSign
                                        : ((landX <= ceilX) ? -1f : 1f);
                                    Collider2D lCol = landPlat.GetComponent<Collider2D>();
                                    float halfW = lCol != null ? Mathf.Max(0.28f, lCol.bounds.extents.x - 0.08f) : 0.52f;
                                    landX = Mathf.Clamp(ceilX + side * 0.92f, landX - halfW, landX + halfW);
                                }
                            }
                        }

                        if (IsThreatenedByMeteor(landX, landPlat.transform.position.y, activeMeteors, out float mX))
                        {
                            float nudge = (landX >= mX) ? 0.28f : -0.28f;
                            landX += nudge;
                        }

                        float wDx = ComputeSmartHorizontalDelta(botPos.x, landX, fullWrapWidth);
                        if (Mathf.Abs(wDx) > 0.02f)
                        {
                            desiredMoveInput = Mathf.Clamp(wDx * 6.5f, -1f, 1f);
                        }
                    }
                }
            }

            if (!launchedVelocityThisFrame)
            {
                float accelRate = botGrounded ? 30f : 65f;
                botCurrentMoveInput = Mathf.MoveTowards(botCurrentMoveInput, desiredMoveInput, dt * accelRate);
                float platformCarryVx = (botGrounded && standingPlat != null && standingPlat.isMovingHorizontal)
                    ? standingPlat.CurrentHorizontalVelocity
                    : 0f;

                botRb.linearVelocity = new Vector2((botCurrentMoveInput * currentMoveSpeed) + platformCarryVx, botRb.linearVelocity.y);
            }
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

        // Gerçek düşüş kontrolü: ekran alt sınırını geçtiğinde kalkan varsa kalkan kurtarır, yoksa 1 can kaybeder
        float botDeathY = camBottomY - 0.45f;
        if (afterPos.y < botDeathY)
        {
            botLockedTargetPlat = null;
            botHookSideSign = 0f;
            if (OpponentShield)
            {
                OpponentShield = false;
                botShieldTimer = 0f;
            }
            else
            {
                OpponentHealth = Mathf.Max(0, OpponentHealth - 1);
                if (OpponentHealth <= 0)
                {
                    OpponentAlive = false;
                    UpdateBotRocketFlame(false);
                    return;
                }
            }

            // Mevcut en yüksek güvenli platformun üstünde yeniden doğ
            PlatformMovement respawnPlat = null;
            float highestY = -999f;
            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] == null) continue;
                float py = platforms[i].transform.position.y;
                if (py > highestY && py < 3.6f && !IsThreatenedByMeteor(platforms[i].transform.position.x, py, activeMeteors))
                {
                    highestY = py;
                    respawnPlat = platforms[i];
                }
            }

            Vector3 respawnPos = respawnPlat != null
                ? new Vector3(respawnPlat.transform.position.x, GetPlatformTopY(respawnPlat) + 0.28f, 0f)
                : new Vector3(0f, 1.8f, 0f);

            opponentAvatarObj.transform.position = respawnPos;
            botRb.position = respawnPos;
            botRb.linearVelocity = new Vector2(0f, -GameManager.globalPlatformSpeed);
            botJumpCooldown = 0.12f;
            botVisualStretch = new Vector2(1.15f, 0.88f);
            JumpDustEffect.SpawnSuperJumpBurst(respawnPos);
        }

        OpponentTargetPos = opponentAvatarObj.transform.position;
        OpponentVy = botRb.linearVelocity.y;
        CheckBotSharedItemPickups(afterPos, localOnRocket);
    }

    private void ExecuteBotJump(
        float jumpVelocityY,
        Vector2 feetPos,
        float currentBotY,
        PlatformMovement fromPlat,
        PlatformMovement targetPlat,
        float hookSideSign,
        GameManager gm)
    {
        // Doğrudan dikey hız ata (AddForce gecikmesi olmadan anında yükselir ve hedef kilidini korur!)
        botCurrentMoveInput = 0f;
        botAirTime = 0f;
        botUngroundLockTimer = 0.07f;
        botRb.linearVelocity = new Vector2(0f, jumpVelocityY);
        botVisualStretch = new Vector2(0.80f, 1.24f);
        JumpDustEffect.SpawnJumpDust(feetPos);
        botGrounded = false;
        botWasGrounded = false;
        botCoyoteTimer = 0f;
        botLastTakeoffY = currentBotY - 0.24f;
        botLastAttemptFromPlat = fromPlat;
        botLastAttemptTargetPlat = targetPlat;
        botLockedTargetPlat = targetPlat;
        botHookSideSign = hookSideSign;

        // Doğal insan tepki ritmi: alt bölgedeyken anında çevik zıplar, üstte ve öndeyken sakin ritimle çıkar
        int localHp = gm != null ? gm.health : 3;
        if (currentBotY < -0.65f)
        {
            botJumpCooldown = UnityEngine.Random.Range(0.03f, 0.06f);
        }
        else if (OpponentHealth > localHp)
        {
            botJumpCooldown = UnityEngine.Random.Range(0.11f, 0.20f);
        }
        else
        {
            botJumpCooldown = UnityEngine.Random.Range(0.06f, 0.13f);
        }
    }

    private static float GetPlatformTopY(PlatformMovement p)
    {
        if (p == null) return 0f;
        Collider2D col = p.GetComponent<Collider2D>();
        return col != null ? col.bounds.max.y : (p.transform.position.y + 0.14f);
    }

    private static float ComputeSmartHorizontalDelta(float fromX, float toX, float fullSpan)
    {
        float directDx = toX - fromX;
        // Düello genişliği (-1.55..+1.55) doğrudan tek sıçramada geçilebildiği için gereksiz ekran sarma riskine girme
        if (Mathf.Abs(directDx) <= 3.15f || fullSpan <= 0.5f)
        {
            return directDx;
        }
        float wrapped = ShortestWrappedDx(fromX, toX, fullSpan);
        return (Mathf.Abs(wrapped) + 0.35f < Mathf.Abs(directDx)) ? wrapped : directDx;
    }

    private PlatformMovement SelectBestUpperPlatformForBot(
        PlatformMovement[] platforms,
        PlatformMovement standingPlat,
        Vector3 botPos,
        float botFeetY,
        float fullWrapWidth,
        float currentMoveSpeed,
        float upwardSpeed,
        float gravityAccel,
        SpaceMeteorProjectile[] activeMeteors,
        out float chosenFlightTime)
    {
        chosenFlightTime = 0.48f;
        PlatformMovement best = null;
        float bestCost = 999f;

        float vRel = Mathf.Max(2.5f, upwardSpeed) + GameManager.globalPlatformSpeed;
        // Köşe tırmanışı (corner hop) payı dahil ulaşılabilir maksimum dikey kazanç
        float maxReachableDy = ((vRel * vRel) / (2f * gravityAccel)) + 0.28f;
        // Aynı platformda 2+ kez takıldıysa yandaki/hafif alttaki alternatif kaçış platformlarına da izin ver!
        float minAllowedDy = (standingPlat != null && botSamePlatFailCount >= 2) ? -0.45f : 0.28f;

        for (int i = 0; i < platforms.Length; i++)
        {
            PlatformMovement p = platforms[i];
            if (p == null || p == standingPlat) continue;

            float topY = GetPlatformTopY(p);
            float dyTop = topY - botFeetY;
            if (dyTop < minAllowedDy || dyTop > Mathf.Min(2.08f, maxReachableDy))
                continue;

            float flightT = Mathf.Clamp(0.36f + Mathf.Max(0f, dyTop) * 0.13f, 0.32f, 0.64f);
            float futureX = p.PredictXAfter(flightT);
            float horizDx = ComputeSmartHorizontalDelta(botPos.x, futureX, fullWrapWidth);

            // Çok uzakta ve uzaklaşmakta olan platformlara ağır ceza ver
            float maxHoriz = currentMoveSpeed * flightT + 0.65f;
            float outOfRangePenalty = Mathf.Abs(horizDx) > maxHoriz ? (Mathf.Abs(horizDx) - maxHoriz) * 3.2f : 0f;

            // Tavan engeli varsa ek maliyet (açık platform varsa onu tercih etsin, yoksa kavisle çıksın!)
            bool directlyOverhead = Mathf.Abs(futureX - botPos.x) < 0.86f && dyTop > 0.15f && dyTop < 1.85f;
            float ceilingPenalty = directlyOverhead ? 0.60f : 0f;

            // Parabolik sıçrama yayının ortasında başka bir katı platform yolu tıkıyor mu? (Varsa önce o ara basamağı seçtir!)
            float pathBlockPenalty = 0f;
            if (!directlyOverhead && dyTop > 0.55f)
            {
                for (int k = 0; k < platforms.Length; k++)
                {
                    PlatformMovement midP = platforms[k];
                    if (midP == null || midP == p || midP == standingPlat) continue;
                    Collider2D mCol = midP.GetComponent<Collider2D>();
                    if (mCol != null && mCol.usedByEffector) continue;

                    float midTop = mCol != null ? mCol.bounds.max.y : (midP.transform.position.y + 0.14f);
                    float midBot = mCol != null ? mCol.bounds.min.y : (midP.transform.position.y - 0.14f);
                    if (midBot < botPos.y + 0.10f || midTop > topY - 0.15f) continue;

                    float frac = Mathf.Clamp01((midBot - botFeetY) / Mathf.Max(0.25f, dyTop));
                    float arcSampleX = Mathf.Lerp(botPos.x, botPos.x + horizDx, frac);
                    float midPredX = midP.PredictXAfter(flightT * frac);
                    if (Mathf.Abs(arcSampleX - midPredX) < 0.82f)
                    {
                        pathBlockPenalty = 3.8f;
                        break;
                    }
                }
            }

            // Tekrar cezası yalnızca 2+ başarısız denemeden sonra devreye girsin (ilk denemede açık kenara indiği için 2. zıplamada rahatça çıkar)
            float repeatFailPenalty = (standingPlat != null && botSamePlatFailCount >= 2 && p == botLastAttemptTargetPlat)
                ? (botSamePlatFailCount * 1.5f)
                : 0f;

            // 1-adım ileri bakış (Lookahead): Bu platformun üstünde devam eden başka bir platform var mı?
            bool hasNextStepUp = false;
            for (int j = 0; j < platforms.Length; j++)
            {
                PlatformMovement p2 = platforms[j];
                if (p2 == null || p2 == p || p2 == standingPlat) continue;
                float dy2 = GetPlatformTopY(p2) - topY;
                if (dy2 >= 0.45f && dy2 <= 1.95f)
                {
                    hasNextStepUp = true;
                    break;
                }
            }
            float lookaheadBonus = hasNextStepUp ? -0.45f : 0.35f;

            // Platform üzerinde Can veya Roket/Güçlendirme varsa hafif cazibe bonusu
            float pickupBonus = (p.GetComponentInChildren<PowerUpPickup>() != null || p.GetComponentInChildren<HealthPickup>() != null)
                ? -0.35f
                : 0f;

            float meteorPenalty = IsThreatenedByMeteor(futureX, topY, activeMeteors) ? 2.8f : 0f;

            // İdeal tırmanma adımı (~1.35-1.55 birim) en verimli ve doğal görünen adımdır
            float cost = Mathf.Abs(dyTop - 1.38f) * 1.10f
                       + Mathf.Abs(horizDx) * 0.42f
                       + ceilingPenalty
                       + pathBlockPenalty
                       + repeatFailPenalty
                       + outOfRangePenalty
                       + lookaheadBonus
                       + pickupBonus
                       + meteorPenalty;

            if (cost < bestCost)
            {
                bestCost = cost;
                best = p;
                chosenFlightTime = flightT;
            }
        }

        return best;
    }

    private static PlatformMovement FindOverheadSolidBlocker(
        PlatformMovement[] platforms,
        PlatformMovement ignorePlat,
        float botX,
        float botY,
        float targetTopY,
        float lookAheadDt)
    {
        PlatformMovement lowestBlocker = null;
        float lowestY = 999f;

        for (int i = 0; i < platforms.Length; i++)
        {
            PlatformMovement p = platforms[i];
            if (p == null || p == ignorePlat) continue;

            Collider2D col = p.GetComponent<Collider2D>();
            if (col != null && col.usedByEffector) continue;

            float topY = col != null ? col.bounds.max.y : (p.transform.position.y + 0.14f);
            float bottomY = col != null ? col.bounds.min.y : (p.transform.position.y - 0.14f);

            if (bottomY < botY + 0.05f || bottomY > targetTopY + 0.45f)
                continue;

            float predX = p.PredictXAfter(lookAheadDt);
            if (Mathf.Abs(predX - botX) < 0.88f && bottomY < lowestY)
            {
                lowestY = bottomY;
                lowestBlocker = p;
            }
        }

        return lowestBlocker;
    }

    private static bool IsLandingReachable(
        float botX,
        float botFeetY,
        float botVy,
        PlatformMovement plat,
        float platTopY,
        float fullWrapWidth,
        float currentMoveSpeed,
        float gravityAccel,
        out float fallTime)
    {
        // Düşen platforma göre bağıl düşüş süresini çöz: 0.5*g*t^2 + (-botVy - vPlat)*t - dropDist = 0
        float dropDist = Mathf.Max(0f, botFeetY - platTopY);
        float relDownSpeed = (-botVy) - GameManager.globalPlatformSpeed;
        float disc = (relDownSpeed * relDownSpeed) + (2f * gravityAccel * dropDist);
        fallTime = disc > 0f
            ? Mathf.Clamp((-relDownSpeed + Mathf.Sqrt(disc)) / gravityAccel, 0.05f, 0.85f)
            : 0.18f;

        float predX = plat.PredictXAfter(fallTime);
        float dx = ComputeSmartHorizontalDelta(botX, predX, fullWrapWidth);
        float maxReachX = (currentMoveSpeed * fallTime * 0.95f) + 0.56f;
        return Mathf.Abs(dx) <= maxReachX;
    }

    private static bool IsThreatenedByMeteor(float x, float y, SpaceMeteorProjectile[] meteors)
    {
        return IsThreatenedByMeteor(x, y, meteors, out _);
    }

    private static bool IsThreatenedByMeteor(float x, float y, SpaceMeteorProjectile[] meteors, out float threatX)
    {
        threatX = 0f;
        if (meteors == null || meteors.Length == 0) return false;
        for (int i = 0; i < meteors.Length; i++)
        {
            if (meteors[i] == null) continue;
            Vector3 mp = meteors[i].transform.position;
            if (mp.y >= y - 0.35f && mp.y <= y + 4.8f && Mathf.Abs(mp.x - x) < 0.78f)
            {
                threatX = mp.x;
                return true;
            }
        }
        return false;
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

        if (opponentOverheadRoot != null)
        {
            // Karakter yön değiştirip esnerken üstteki piksel isim + kalp-can rozeti 1:1 sabit ve düz okunur kalsın
            opponentOverheadRoot.localScale = new Vector3((opponentFacing / 1.805f) / sx, (1f / 1.422f) / sy, 1f);

            string upperName = string.IsNullOrEmpty(OpponentName) ? "RAKİP" : OpponentName.ToUpperInvariant();
            string hpStr = OpponentHealth.ToString();

            if (opponentNameTagTmp != null && opponentNameTagTmp.text != upperName)
                opponentNameTagTmp.text = upperName;
            if (opponentNameShadowTmp != null && opponentNameShadowTmp.text != upperName)
                opponentNameShadowTmp.text = upperName;
            if (opponentHealthNumTmp != null && opponentHealthNumTmp.text != hpStr)
                opponentHealthNumTmp.text = hpStr;
            if (opponentHealthShadowTmp != null && opponentHealthShadowTmp.text != hpStr)
                opponentHealthShadowTmp.text = hpStr;
        }

        UpdateOpponentPowerUpVisuals();

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

    private void UpdateOpponentPowerUpVisuals()
    {
        if (opponentAvatarObj == null) return;

        // 1. Koruyucu Kalkan Baloncuğu (hem Online Rakip hem Bot için görünür!)
        if (OpponentShield)
        {
            if (opponentShieldAuraObj == null)
            {
                opponentShieldAuraObj = new GameObject("OpponentShieldBubbleAura");
                opponentShieldAuraObj.transform.SetParent(opponentAvatarObj.transform, false);
                opponentShieldAuraObj.transform.localPosition = Vector3.zero;
                opponentShieldAuraRenderer = opponentShieldAuraObj.AddComponent<SpriteRenderer>();
                opponentShieldAuraRenderer.sprite = PowerUpPickup.GetShieldAuraSprite();
                opponentShieldAuraRenderer.sortingOrder = 10;
            }
            if (!opponentShieldAuraObj.activeSelf) opponentShieldAuraObj.SetActive(true);
            float pulse = 1f + Mathf.Sin(Time.time * 4.5f) * 0.05f;
            opponentShieldAuraObj.transform.localScale = new Vector3(0.30f * pulse, 0.38f * pulse, 1f);

            if (opponentShieldAuraRenderer != null)
            {
                float alpha = (IsBotMatch && botShieldTimer > 0f && botShieldTimer <= 3f)
                    ? (0.25f + 0.75f * Mathf.Abs(Mathf.Sin(Time.time * 16f)))
                    : (0.85f + 0.15f * Mathf.Sin(Time.time * 6f));
                opponentShieldAuraRenderer.color = new Color(1f, 1f, 1f, alpha);
            }
        }
        else if (opponentShieldAuraObj != null && opponentShieldAuraObj.activeSelf)
        {
            opponentShieldAuraObj.SetActive(false);
        }

        // 2. Kuş Ayağı Görseli (hem Online Rakip hem Bot için görünür!)
        if (OpponentSuperFeet)
        {
            if (opponentSuperFeetObj == null)
            {
                opponentSuperFeetObj = new GameObject("OpponentSuperFeetVisual");
                opponentSuperFeetObj.transform.SetParent(opponentAvatarObj.transform, false);
                opponentSuperFeetObj.transform.localPosition = new Vector3(0f, -0.155f, 0f);
                opponentSuperFeetObj.transform.localScale = new Vector3(0.62f / 1.805f, 0.62f / 1.422f, 1f);
                opponentSuperFeetRenderer = opponentSuperFeetObj.AddComponent<SpriteRenderer>();
                opponentSuperFeetRenderer.sprite = PowerUpPickup.GetSuperFeetSprite();
                opponentSuperFeetRenderer.sortingOrder = 9;
            }
            if (!opponentSuperFeetObj.activeSelf) opponentSuperFeetObj.SetActive(true);
            float waddle = Mathf.Sin(Time.time * 12f) * 6f;
            opponentSuperFeetObj.transform.localRotation = Quaternion.Euler(0f, 0f, waddle);
            if (opponentSuperFeetRenderer != null)
            {
                float alpha = (IsBotMatch && botSuperFeetTimer > 0f && botSuperFeetTimer <= 3f)
                    ? (0.28f + 0.72f * Mathf.Abs(Mathf.Sin(Time.time * 16f)))
                    : 1f;
                opponentSuperFeetRenderer.color = new Color(1f, 1f, 1f, alpha);
            }
        }
        else if (opponentSuperFeetObj != null && opponentSuperFeetObj.activeSelf)
        {
            opponentSuperFeetObj.SetActive(false);
        }
    }

    private void UpdateDuelHudTexts()
    {
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            if (gm.scoreText != null && gm.scoreText.gameObject.activeSelf)
                gm.scoreText.gameObject.SetActive(false);
            if (gm.healthText != null && gm.healthText.gameObject.activeSelf)
                gm.healthText.gameObject.SetActive(false);
        }

        int myHealth = (gm != null && !localEliminated) ? Mathf.Clamp(gm.health, 0, 3) : 0;
        int oppHealth = OpponentAlive ? Mathf.Clamp(OpponentHealth, 0, 3) : 0;
        int myScore = Mathf.RoundToInt(GameManager.currentScore);
        int oppScore = OpponentScore;

        // Rakip ismi veya kostümü sonradan güncellendiyse tazele
        if (duelOppNameTmp != null)
        {
            string oppUpper = string.IsNullOrEmpty(OpponentName) ? "RAKİP" : OpponentName.ToUpperInvariant();
            if (duelOppNameTmp.text != oppUpper) duelOppNameTmp.text = oppUpper;
        }
        if (duelOppAvatarImg != null && duelLoadedOppCharId != OpponentCharId && !string.IsNullOrEmpty(OpponentCharId))
        {
            duelLoadedOppCharId = OpponentCharId;
            Sprite sp = GetCharacterPortraitSprite(duelLoadedOppCharId);
            if (sp != null) duelOppAvatarImg.sprite = sp;
        }
        if (duelMyAvatarImg != null)
        {
            duelMyAvatarImg.color = localEliminated ? new Color(0.45f, 0.45f, 0.52f, 0.80f) : Color.white;
        }
        if (duelOppAvatarImg != null)
        {
            duelOppAvatarImg.color = !OpponentAlive ? new Color(0.45f, 0.45f, 0.52f, 0.80f) : Color.white;
        }

        // Skor renkleri (Öndeki oyuncunun skoru altın sarısı parlar)
        bool iLead = myScore > oppScore + 2;
        bool oppLeads = oppScore > myScore + 2;
        Color leadGold = new Color(1.00f, 0.91f, 0.38f, 1f);
        Color elimTint = new Color(0.75f, 0.66f, 0.72f, 0.92f);

        if (duelMyStatusText != null)
        {
            duelMyStatusText.text = myScore.ToString();
            duelMyStatusText.color = localEliminated ? elimTint : (iLead ? leadGold : Color.white);
        }
        if (duelOppStatusText != null)
        {
            duelOppStatusText.text = oppScore.ToString();
            duelOppStatusText.color = !OpponentAlive ? elimTint : (oppLeads ? leadGold : Color.white);
        }

        // 3'er Piksel Kalp & ELENDİ Rozetleri
        Color lostHeartCol = new Color(0.20f, 0.14f, 0.26f, 0.55f);
        bool showMyElim = localEliminated || myHealth <= 0;
        if (duelMyElimBadgeTmp != null && duelMyElimBadgeTmp.gameObject.activeSelf != showMyElim)
        {
            duelMyElimBadgeTmp.gameObject.SetActive(showMyElim);
        }
        for (int i = 0; i < duelMyHeartImgs.Length; i++)
        {
            if (duelMyHeartImgs[i] == null) continue;
            if (duelMyHeartImgs[i].gameObject.activeSelf == showMyElim)
                duelMyHeartImgs[i].gameObject.SetActive(!showMyElim);
            if (!showMyElim)
                duelMyHeartImgs[i].color = (i < myHealth) ? Color.white : lostHeartCol;
        }

        bool showOppElim = !OpponentAlive || oppHealth <= 0;
        if (duelOppElimBadgeTmp != null && duelOppElimBadgeTmp.gameObject.activeSelf != showOppElim)
        {
            duelOppElimBadgeTmp.gameObject.SetActive(showOppElim);
        }
        for (int i = 0; i < duelOppHeartImgs.Length; i++)
        {
            if (duelOppHeartImgs[i] == null) continue;
            if (duelOppHeartImgs[i].gameObject.activeSelf == showOppElim)
                duelOppHeartImgs[i].gameObject.SetActive(!showOppElim);
            if (!showOppElim)
                duelOppHeartImgs[i].color = (i < oppHealth) ? Color.white : lostHeartCol;
        }

        // Canlı Puan Farkı Rozeti (+Δ / -Δ)
        int diff = myScore - oppScore;
        if (duelDeltaPillObj != null && duelDeltaTmp != null)
        {
            bool showDelta = Mathf.Abs(diff) >= 3;
            if (duelDeltaPillObj.activeSelf != showDelta)
                duelDeltaPillObj.SetActive(showDelta);
            if (showDelta)
            {
                if (diff > 0)
                {
                    duelDeltaTmp.text = $"+{diff}";
                    duelDeltaTmp.color = new Color(0.42f, 1.00f, 0.72f, 1f);
                }
                else
                {
                    duelDeltaTmp.text = diff.ToString();
                    duelDeltaTmp.color = new Color(1.00f, 0.50f, 0.62f, 1f);
                }
            }
        }

        // Alt Çekişme (Tug-of-War) Momentum Barı
        if (duelTugLeftRt != null && duelTugRightRt != null)
        {
            float targetRatio = Mathf.Clamp(0.5f + (diff / 320f), 0.10f, 0.90f);
            displayedDuelTugRatio = Mathf.Lerp(displayedDuelTugRatio, targetRatio, Mathf.Clamp01(Time.unscaledDeltaTime * 8f));
            float splitX = Mathf.Lerp(0.154f, 0.846f, displayedDuelTugRatio);

            duelTugLeftRt.anchorMin = new Vector2(0.154f, 0.112f);
            duelTugLeftRt.anchorMax = new Vector2(splitX, 0.192f);
            duelTugRightRt.anchorMin = new Vector2(splitX, 0.112f);
            duelTugRightRt.anchorMax = new Vector2(0.846f, 0.192f);

            if (duelTugSparkRt != null)
            {
                duelTugSparkRt.anchorMin = new Vector2(splitX - 0.004f, 0.085f);
                duelTugSparkRt.anchorMax = new Vector2(splitX + 0.004f, 0.220f);
            }
        }

        // Liderlik Tacı (Öndeki oyuncunun avatarının üstüne yumuşakça süzülür)
        if (duelCrownRt != null)
        {
            bool hasLeader = iLead || oppLeads;
            if (duelCrownRt.gameObject.activeSelf != hasLeader)
                duelCrownRt.gameObject.SetActive(hasLeader);

            if (hasLeader)
            {
                float targetCenterX = iLead ? 0.082f : 0.918f;
                displayedCrownAnchorX = Mathf.Lerp(displayedCrownAnchorX, targetCenterX, Mathf.Clamp01(Time.unscaledDeltaTime * 10f));
                float bob = 0.035f * Mathf.Sin(Time.unscaledTime * 4.2f);
                duelCrownRt.anchorMin = new Vector2(displayedCrownAnchorX - 0.032f, 0.80f + bob);
                duelCrownRt.anchorMax = new Vector2(displayedCrownAnchorX + 0.032f, 1.15f + bob);
            }
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
