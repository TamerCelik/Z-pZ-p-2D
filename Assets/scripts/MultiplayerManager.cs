using System;
using System.Collections;
using System.Text;
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

    public const float DuelRocketSpeedMultiplier = 1.38f;
    public const int DuelWinCoinReward = 30;

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

    // Bot simülasyonu
    private float botSimTime;
    private float botNextRocketTime;
    private float botNextHitTime;

    // Rakip karakter objesi
    private GameObject opponentAvatarObj;
    private SpriteRenderer opponentSpriteRenderer;
    private TextMeshPro opponentNameTagTmp;
    private Sprite[] opponentIdleFrames;
    private float opponentAnimTimer;
    private int opponentAnimFrame;
    private float opponentFacing = 1f;

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
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        playModeSelectorObj = null;
        lobbyModalObj = null;

        if (inActiveDuel && scene.name == "SampleScene")
        {
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
        if (!inActiveDuel || SceneManager.GetActiveScene().name != "SampleScene")
            return;

        float dt = Time.deltaTime;

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
            syncPollingTimer -= Time.unscaledDeltaTime;
            if (syncPollingTimer <= 0f && !syncInFlight)
            {
                syncPollingTimer = 0.14f;
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

            if (lobbyRoomCodeBigText != null) lobbyRoomCodeBigText.text = $"ODA KODU: {CurrentRoomCode}";
            if (lobbyStatusText != null) lobbyStatusText.text = "Arkadaşının bu kodu girip katılması bekleniyor...";
        }

        while (!string.IsNullOrEmpty(CurrentRoomCode))
        {
            yield return new WaitForSecondsRealtime(1.0f);

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

        while (!string.IsNullOrEmpty(CurrentRoomCode))
        {
            yield return new WaitForSecondsRealtime(1.0f);
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
        opponentRocketTimer = 0f;

        OpponentHealth = 3;
        OpponentScore = 0;
        OpponentAlive = true;
        OpponentSuperFeet = false;
        OpponentShield = false;
        OpponentTargetPos = new Vector2(0.45f, -1.5f);

        botSimTime = 0f;
        botNextRocketTime = UnityEngine.Random.Range(12f, 18f);
        botNextHitTime = UnityEngine.Random.Range(16f, 24f);

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

        opponentAvatarObj = new GameObject("RemoteOpponentAvatar");
        opponentAvatarObj.transform.localScale = new Vector3(1.80f, 1.42f, 1f);

        GameManager gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
        Vector3 startPos = (gm != null && gm.player != null)
            ? gm.player.transform.position + new Vector3(0.35f, 0f, 0f)
            : new Vector3(0.35f, -1.5f, 0f);
        opponentAvatarObj.transform.position = startPos;
        OpponentTargetPos = startPos;

        opponentSpriteRenderer = opponentAvatarObj.AddComponent<SpriteRenderer>();
        opponentSpriteRenderer.sortingOrder = 8;
        opponentSpriteRenderer.color = new Color(0.88f, 0.96f, 1.00f, 0.86f);

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
    // 5. CANLI SENKRONİZASYON, BOT SİMÜLASYONU VE GÖRSEL GÜNCELLEME
    // =========================================================================
    private IEnumerator SyncMatchStateRoutine()
    {
        if (string.IsNullOrEmpty(CurrentRoomCode) || IsBotMatch) yield break;
        syncInFlight = true;

        GameManager gm = GameManager.Instance;
        Vector3 myPos = (gm != null && gm.player != null) ? gm.player.transform.position : Vector3.zero;
        Rigidbody2D rb = (gm != null && gm.player != null) ? gm.player.GetComponent<Rigidbody2D>() : null;
        float myVy = rb != null ? rb.linearVelocity.y : 0f;
        int myHealth = gm != null ? Mathf.Max(0, gm.health) : 0;
        int myScore = Mathf.RoundToInt(GameManager.currentScore);
        bool triggerRocket = pendingLocalRocketTrigger;
        pendingLocalRocketTrigger = false;

        SyncStatePayload payload = new SyncStatePayload
        {
            p_room_code = CurrentRoomCode,
            p_secret = GetDeviceSecret(),
            p_x = myPos.x,
            p_y = myPos.y,
            p_vy = myVy,
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
                    if (!string.IsNullOrEmpty(resp.opp_name)) OpponentName = resp.opp_name;
                    OpponentTargetPos = new Vector2(resp.opp_x, resp.opp_y);
                    OpponentVy = resp.opp_vy;
                    OpponentHealth = Mathf.Clamp(resp.opp_health, 0, 5);
                    OpponentScore = Mathf.Max(OpponentScore, resp.opp_score);
                    OpponentAlive = resp.opp_alive && OpponentHealth > 0;
                    OpponentSuperFeet = resp.opp_superfeet;
                    OpponentShield = resp.opp_shield;

                    bool localOnRocket = gm != null && gm.IsRocketBoosting;
                    if (resp.opp_rocket && !localOnRocket && opponentRocketTimer <= 0.1f)
                    {
                        opponentRocketTimer = 3.0f;
                    }
                }
            }
        }
    }

    private void UpdateBotOpponentSimulation(float dt)
    {
        if (!OpponentAlive) return;

        botSimTime += dt;
        OpponentScore = Mathf.RoundToInt(botSimTime * 10.5f);

        PlatformMovement[] platforms = FindObjectsByType<PlatformMovement>(FindObjectsSortMode.None);
        PlatformMovement targetPlat = null;
        float bestDist = 999f;
        for (int i = 0; i < platforms.Length; i++)
        {
            PlatformMovement p = platforms[i];
            if (p == null) continue;
            float dy = p.transform.position.y - OpponentTargetPos.y;
            if (dy > -0.4f && dy < 2.2f)
            {
                float d = Mathf.Abs(dy - 0.8f);
                if (d < bestDist)
                {
                    bestDist = d;
                    targetPlat = p;
                }
            }
        }

        float targetX = targetPlat != null ? targetPlat.transform.position.x : Mathf.Sin(botSimTime * 1.4f) * 1.6f;
        float bounceY = Mathf.Sin(botSimTime * 5.2f) * 0.95f - 0.4f;
        OpponentVy = Mathf.Cos(botSimTime * 5.2f) * 4.5f;
        OpponentTargetPos = new Vector2(
            Mathf.Lerp(OpponentTargetPos.x, targetX, dt * 4.2f),
            Mathf.Lerp(OpponentTargetPos.y, bounceY, dt * 6f));

        if (botSimTime >= botNextRocketTime)
        {
            botNextRocketTime = botSimTime + UnityEngine.Random.Range(16f, 24f);
            GameManager gm = GameManager.Instance;
            if (gm == null || !gm.IsRocketBoosting)
            {
                opponentRocketTimer = 3.0f;
            }
        }

        if (botSimTime >= botNextHitTime)
        {
            botNextHitTime = botSimTime + UnityEngine.Random.Range(14f, 22f);
            OpponentHealth = Mathf.Max(0, OpponentHealth - 1);
            if (OpponentHealth <= 0)
            {
                OpponentAlive = false;
            }
        }
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

        Vector3 curr = opponentAvatarObj.transform.position;
        Vector3 next = Vector3.Lerp(curr, new Vector3(OpponentTargetPos.x, OpponentTargetPos.y, 0f), Mathf.Clamp01(dt * 12f));
        float dx = next.x - curr.x;
        if (Mathf.Abs(dx) > 0.005f) opponentFacing = dx > 0f ? 1f : -1f;

        opponentAvatarObj.transform.position = next;

        float stretchY = OpponentVy > 1.0f ? 1.16f : 1.0f;
        float stretchX = OpponentVy > 1.0f ? 0.88f : 1.0f;
        opponentAvatarObj.transform.localScale = new Vector3(opponentFacing * 1.80f * stretchX, 1.42f * stretchY, 1f);

        if (opponentNameTagTmp != null)
        {
            opponentNameTagTmp.transform.localScale = new Vector3(opponentFacing * 0.08f, 0.10f, 1f);
            opponentNameTagTmp.text = $"{OpponentName}\nCAN: {OpponentHealth}";
        }

        if (opponentIdleFrames != null && opponentIdleFrames.Length > 0 && opponentSpriteRenderer != null)
        {
            opponentAnimTimer += dt * 12f;
            opponentAnimFrame = Mathf.FloorToInt(opponentAnimTimer) % opponentIdleFrames.Length;
            opponentSpriteRenderer.sprite = opponentIdleFrames[opponentAnimFrame];
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
