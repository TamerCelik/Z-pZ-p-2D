using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Supabase REST/RPC üzerinden benzersiz kullanıcı adı, misafir (Guest) başlangıcı,
/// skor senkronizasyonu ve global liderlik tablosunu yöneten servis.
/// Ekstra SDK gerektirmez; saf UnityWebRequest ile hem PC hem Android'de çalışır.
/// </summary>
public class LeaderboardService : MonoBehaviour
{
    private const string SupabaseUrl = "https://bgnjapojaevoqhwbndye.supabase.co";
    private const string SupabasePublishableKey = "sb_publishable_UGQr2dJjLeF3Yw_OPpFD7w_xQ4Ja3B1";

    private const string DeviceSecretKey = "LB_DeviceSecret_v1";
    private const string UsernameKey = "LB_Username_v1";
    private const string IsGuestKey = "LB_IsGuest_v1";
    private const string BestScoreKey = "HighScore";
    private const string RankKey = "LB_Rank_v1";
    private const string TotalPlayersKey = "LB_TotalPlayers_v1";

    [Serializable]
    private class SyncRequestPayload
    {
        public string p_device_secret;
        public string p_default_username;
        public int p_score;
        public string p_character_id;
    }

    [Serializable]
    public class SyncPlayerResponse
    {
        public string username;
        public bool is_guest;
        public int best_score;
        public string character_id;
        public int rank;
        public int total_players;
    }

    [Serializable]
    private class ChangeUsernamePayload
    {
        public string p_device_secret;
        public string p_new_username;
    }

    [Serializable]
    public class ChangeUsernameResponse
    {
        public bool success;
        public string error;
        public string message;
        public string username;
        public bool is_guest;
    }

    [Serializable]
    private class GetLeaderboardPayload
    {
        public string p_device_secret;
        public int p_limit;
    }

    [Serializable]
    public class LeaderboardEntry
    {
        public int rank;
        public string username;
        public bool is_guest;
        public int best_score;
        public string character_id;
        public bool is_me;
    }

    [Serializable]
    public class LeaderboardResponse
    {
        public LeaderboardEntry[] entries;
        public LeaderboardEntry me;
    }

    public static LeaderboardService Instance { get; private set; }

    public event Action OnProfileUpdated;
    public event Action<LeaderboardResponse> OnLeaderboardUpdated;

    public string CurrentUsername { get; private set; }
    public bool IsGuest { get; private set; } = true;
    public int BestScore { get; private set; }
    public int CurrentRank { get; private set; }
    public int TotalPlayers { get; private set; }
    public bool IsSyncing { get; private set; }
    public LeaderboardResponse CachedLeaderboard { get; private set; }

    private string deviceSecret;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        EnsureInstance();
    }

    public static LeaderboardService EnsureInstance()
    {
        if (Instance != null) return Instance;

        GameObject go = new GameObject("LeaderboardService");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LeaderboardService>();
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

        LoadLocalIdentity();
        CharacterShopData.OnShopStateChanged += HandleShopStateChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        SyncPlayerScore(BestScore, null);
        LeaderboardAndProfileUI.AttachToCurrentScene();
    }

    private void OnDestroy()
    {
        CharacterShopData.OnShopStateChanged -= HandleShopStateChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        LeaderboardAndProfileUI.AttachToCurrentScene();
        SyncPlayerScore(PlayerPrefs.GetInt(BestScoreKey, 0), null);
    }

    private void HandleShopStateChanged()
    {
        SyncPlayerScore(PlayerPrefs.GetInt(BestScoreKey, 0), null);
    }

    private void LoadLocalIdentity()
    {
        deviceSecret = PlayerPrefs.GetString(DeviceSecretKey, string.Empty);
        if (string.IsNullOrEmpty(deviceSecret))
        {
            deviceSecret = Guid.NewGuid().ToString("N") + "_" + UnityEngine.Random.Range(100000, 999999);
            PlayerPrefs.SetString(DeviceSecretKey, deviceSecret);
        }

        CurrentUsername = PlayerPrefs.GetString(UsernameKey, string.Empty);
        if (string.IsNullOrEmpty(CurrentUsername))
        {
            CurrentUsername = "Guest_" + UnityEngine.Random.Range(1000, 9999);
            PlayerPrefs.SetString(UsernameKey, CurrentUsername);
            PlayerPrefs.SetInt(IsGuestKey, 1);
        }

        IsGuest = PlayerPrefs.GetInt(IsGuestKey, 1) == 1;
        BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
        CurrentRank = PlayerPrefs.GetInt(RankKey, 0);
        TotalPlayers = PlayerPrefs.GetInt(TotalPlayersKey, 0);
        PlayerPrefs.Save();
    }

    public void SubmitScore(int score, Action<SyncPlayerResponse> onComplete = null)
    {
        int best = Mathf.Max(score, PlayerPrefs.GetInt(BestScoreKey, 0));
        if (best > BestScore)
        {
            BestScore = best;
            PlayerPrefs.SetInt(BestScoreKey, BestScore);
            PlayerPrefs.Save();
        }
        SyncPlayerScore(best, onComplete);
    }

    public void SyncPlayerScore(int score, Action<SyncPlayerResponse> onComplete)
    {
        int highest = Mathf.Max(score, PlayerPrefs.GetInt(BestScoreKey, 0));
        StartCoroutine(SyncPlayerRoutine(highest, onComplete));
    }

    public void ChangeUsername(string newUsername, Action<bool, string> onComplete)
    {
        StartCoroutine(ChangeUsernameRoutine(newUsername, onComplete));
    }

    public void FetchLeaderboard(int limit = 50, Action<bool, LeaderboardResponse> onComplete = null)
    {
        StartCoroutine(FetchLeaderboardRoutine(limit, onComplete));
    }

    private IEnumerator SyncPlayerRoutine(int score, Action<SyncPlayerResponse> onComplete)
    {
        IsSyncing = true;
        SyncRequestPayload payload = new SyncRequestPayload
        {
            p_device_secret = deviceSecret,
            p_default_username = CurrentUsername,
            p_score = Mathf.Max(0, score),
            p_character_id = CharacterShopData.GetSelectedCharacterId()
        };

        string json = JsonUtility.ToJson(payload);
        using (UnityWebRequest req = CreateRpcRequest("rpc_sync_player", json))
        {
            yield return req.SendWebRequest();
            IsSyncing = false;

            if (req.result == UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler.text;
                SyncPlayerResponse resp = JsonUtility.FromJson<SyncPlayerResponse>(body);
                if (resp != null && !string.IsNullOrEmpty(resp.username))
                {
                    CurrentUsername = resp.username;
                    IsGuest = resp.is_guest;
                    BestScore = Mathf.Max(BestScore, resp.best_score);
                    CurrentRank = resp.rank;
                    TotalPlayers = resp.total_players;

                    PlayerPrefs.SetString(UsernameKey, CurrentUsername);
                    PlayerPrefs.SetInt(IsGuestKey, IsGuest ? 1 : 0);
                    PlayerPrefs.SetInt(BestScoreKey, BestScore);
                    PlayerPrefs.SetInt(RankKey, CurrentRank);
                    PlayerPrefs.SetInt(TotalPlayersKey, TotalPlayers);
                    PlayerPrefs.Save();

                    OnProfileUpdated?.Invoke();
                    onComplete?.Invoke(resp);
                    yield break;
                }
            }
            onComplete?.Invoke(null);
        }
    }

    private IEnumerator ChangeUsernameRoutine(string newUsername, Action<bool, string> onComplete)
    {
        string trimmed = newUsername != null ? newUsername.Trim() : string.Empty;
        if (trimmed.Length < 3 || trimmed.Length > 16)
        {
            onComplete?.Invoke(false, "Kullanıcı adı 3 ile 16 karakter arasında olmalı!");
            yield break;
        }

        // İlk olarak oyuncunun sunucuda kaydı olduğundan emin ol
        bool synced = false;
        yield return SyncPlayerRoutine(BestScore, _ => synced = true);

        ChangeUsernamePayload payload = new ChangeUsernamePayload
        {
            p_device_secret = deviceSecret,
            p_new_username = trimmed
        };

        string json = JsonUtility.ToJson(payload);
        using (UnityWebRequest req = CreateRpcRequest("rpc_change_username", json))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(false, "Bağlantı hatası! İnternetini kontrol et.");
                yield break;
            }

            ChangeUsernameResponse resp = JsonUtility.FromJson<ChangeUsernameResponse>(req.downloadHandler.text);
            if (resp != null && resp.success)
            {
                CurrentUsername = resp.username;
                IsGuest = resp.is_guest;
                PlayerPrefs.SetString(UsernameKey, CurrentUsername);
                PlayerPrefs.SetInt(IsGuestKey, IsGuest ? 1 : 0);
                PlayerPrefs.Save();

                OnProfileUpdated?.Invoke();
                FetchLeaderboard(50, null);
                onComplete?.Invoke(true, $"Kullanıcı adın '{CurrentUsername}' olarak kaydedildi!");
            }
            else
            {
                string msg = (resp != null && !string.IsNullOrEmpty(resp.message))
                    ? resp.message
                    : "Bu kullanıcı adı zaten alınmış!";
                onComplete?.Invoke(false, msg);
            }
        }
    }

    private IEnumerator FetchLeaderboardRoutine(int limit, Action<bool, LeaderboardResponse> onComplete)
    {
        // Tabloyu çekmeden önce oyuncunun güncel skorunu eşitle
        yield return SyncPlayerRoutine(PlayerPrefs.GetInt(BestScoreKey, 0), null);

        GetLeaderboardPayload payload = new GetLeaderboardPayload
        {
            p_device_secret = deviceSecret,
            p_limit = Mathf.Clamp(limit, 1, 100)
        };

        string json = JsonUtility.ToJson(payload);
        using (UnityWebRequest req = CreateRpcRequest("rpc_get_leaderboard", json))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(false, null);
                yield break;
            }

            string raw = req.downloadHandler.text;
            LeaderboardResponse resp = JsonUtility.FromJson<LeaderboardResponse>(raw);
            if (resp != null)
            {
                if (resp.entries == null) resp.entries = Array.Empty<LeaderboardEntry>();
                if (resp.me != null && !string.IsNullOrEmpty(resp.me.username))
                {
                    CurrentUsername = resp.me.username;
                    IsGuest = resp.me.is_guest;
                    CurrentRank = resp.me.rank;
                    BestScore = Mathf.Max(BestScore, resp.me.best_score);

                    PlayerPrefs.SetString(UsernameKey, CurrentUsername);
                    PlayerPrefs.SetInt(IsGuestKey, IsGuest ? 1 : 0);
                    PlayerPrefs.SetInt(RankKey, CurrentRank);
                    PlayerPrefs.SetInt(BestScoreKey, BestScore);
                    PlayerPrefs.Save();
                    OnProfileUpdated?.Invoke();
                }

                CachedLeaderboard = resp;
                OnLeaderboardUpdated?.Invoke(resp);
                onComplete?.Invoke(true, resp);
            }
            else
            {
                onComplete?.Invoke(false, null);
            }
        }
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
        req.timeout = 10;
        return req;
    }
}
