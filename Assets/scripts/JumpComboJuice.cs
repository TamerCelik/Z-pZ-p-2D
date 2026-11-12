using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Ziplama & inis anlarinda ayaklarin altinda toz parcaciklari (Ziplama Tozu) uretir
/// ve farkli platformlara arka arkaya hizli ziplamalarda Combo / Seri Ziplama odulu verir.
/// </summary>
public class JumpComboJuice : MonoBehaviour
{
    private class DustPuff
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float startScale;
        public float endScale;
        public float rotSpeed;
        public float age;
        public float lifetime;
        public Color baseColor;
        public bool active;
    }

    private const float ComboWindowSeconds = 2.6f;
    private const int DustPoolSize = 28;

    private static JumpComboJuice instance;

    private readonly List<DustPuff> dustPool = new List<DustPuff>();
    private Sprite dustSprite;
    private Material unlitMaterial;
    private float dustBaseWorldScale = 1f;

    // Ground & platform tracking
    private bool wasGrounded;
    private bool hasLeftGroundOnce;
    private GameObject lastTouchedPlatform;

    // Combo tracking
    private int comboCount;
    private GameObject lastComboPlatform;
    private float comboTimer;
    private GameManager activeGameManager;
    private bool isGameplayActive = true;
    private bool isPaused;

    // HUD elements
    private GameObject comboRoot;
    private RectTransform comboRect;
    private TextMeshProUGUI comboText;
    private TextMeshProUGUI bonusPopText;
    private RectTransform bonusPopRect;
    private Image comboBarFill;
    private float comboBadgePopTimer = -1f;
    private float bonusPopTimer = -1f;
    private AudioSource comboAudioSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    private static JumpComboJuice EnsureInstance()
    {
        if (instance == null)
        {
            GameObject go = new GameObject("JumpComboJuice");
            instance = go.AddComponent<JumpComboJuice>();
        }
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        InitializeResources();
        BuildDustPool();
    }

    void OnDestroy()
    {
        if (unlitMaterial != null) Destroy(unlitMaterial);
        if (instance == this) instance = null;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        UpdateDustPool(dt);
        UpdateComboState(dt);
        UpdateComboUI(dt);
    }

    public static void SyncGameState(bool gameActive, bool paused, GameManager gm)
    {
        JumpComboJuice inst = EnsureInstance();
        inst.activeGameManager = gm;
        inst.isPaused = paused;

        if (!gameActive && inst.isGameplayActive)
        {
            inst.ClearComboInternal();
        }
        inst.isGameplayActive = gameActive;
    }

    public static void NotifyGroundState(Vector3 feetPos, Collider2D groundCollider, float verticalVelocity)
    {
        JumpComboJuice inst = EnsureInstance();
        if (groundCollider != null)
        {
            inst.lastTouchedPlatform = groundCollider.gameObject;
        }

        bool groundedNow = groundCollider != null && verticalVelocity <= 0.1f;
        if (groundedNow && !inst.wasGrounded && inst.hasLeftGroundOnce)
        {
            inst.SpawnLandingDust(feetPos);
        }

        if (!groundedNow)
        {
            inst.hasLeftGroundOnce = true;
        }
        inst.wasGrounded = groundedNow;
    }

    public static float NotifyPlayerJump(Vector3 feetPos)
    {
        JumpComboJuice inst = EnsureInstance();
        inst.RegisterJumpCombo();
        inst.SpawnJumpDust(feetPos);
        return inst.GetComboPitch();
    }

    public static void PlayJumpSfx(AudioSource templateSource, AudioClip clip, float volume, float pitch)
    {
        if (templateSource == null || clip == null) return;
        if (pitch <= 1.001f)
        {
            templateSource.PlayOneShot(clip, volume);
            return;
        }

        JumpComboJuice inst = EnsureInstance();
        if (inst.comboAudioSource == null)
        {
            inst.comboAudioSource = inst.gameObject.AddComponent<AudioSource>();
            inst.comboAudioSource.playOnAwake = false;
        }
        inst.comboAudioSource.outputAudioMixerGroup = templateSource.outputAudioMixerGroup;
        inst.comboAudioSource.spatialBlend = templateSource.spatialBlend;
        inst.comboAudioSource.pitch = pitch;
        inst.comboAudioSource.PlayOneShot(clip, volume);
    }

    public static void ResetCombo()
    {
        if (instance != null)
        {
            instance.ClearComboInternal();
        }
    }

    private void InitializeResources()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        }
        if (shader != null)
        {
            unlitMaterial = new Material(shader);
        }

        dustSprite = Resources.Load<Sprite>("Sky/sky_dust_particle");
        if (dustSprite == null)
        {
            dustSprite = CreateFallbackDustSprite();
        }

        float spriteWidth = dustSprite.bounds.size.x;
        dustBaseWorldScale = (spriteWidth > 0.01f) ? (0.18f / spriteWidth) : 1f;
    }

    private void BuildDustPool()
    {
        Transform folder = new GameObject("DustPool").transform;
        folder.SetParent(transform);

        for (int i = 0; i < DustPoolSize; i++)
        {
            GameObject go = new GameObject($"Dust_{i}");
            go.transform.SetParent(folder);
            go.SetActive(false);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dustSprite;
            sr.sortingOrder = 10;
            if (unlitMaterial != null) sr.sharedMaterial = unlitMaterial;

            dustPool.Add(new DustPuff
            {
                transform = go.transform,
                renderer = sr,
                active = false
            });
        }
    }

    private DustPuff GetFreeDustPuff()
    {
        for (int i = 0; i < dustPool.Count; i++)
        {
            if (!dustPool[i].active) return dustPool[i];
        }
        return null;
    }

    private void SpawnJumpDust(Vector3 feetPos)
    {
        bool highCombo = comboCount >= 2;
        Color tint = highCombo
            ? Color.Lerp(new Color(1f, 0.98f, 0.92f), GetComboColor(comboCount), 0.35f)
            : new Color(1f, 0.98f, 0.93f);

        int count = highCombo ? 7 : 6;
        for (int i = 0; i < count; i++)
        {
            float spreadT = (count > 1) ? ((float)i / (count - 1)) * 2f - 1f : 0f;
            Vector3 spawnPos = feetPos + new Vector3(spreadT * 0.16f + Random.Range(-0.04f, 0.04f), Random.Range(-0.03f, 0.05f), 0f);
            Vector2 vel = new Vector2(spreadT * Random.Range(0.95f, 1.55f) + Random.Range(-0.25f, 0.25f), Random.Range(0.15f, 0.72f));
            float startS = dustBaseWorldScale * Random.Range(0.75f, 1.15f);
            float endS = startS * Random.Range(1.35f, 1.65f);
            SpawnPuff(spawnPos, vel, startS, endS, Random.Range(0.26f, 0.38f), tint, 0.85f);
        }
    }

    private void SpawnLandingDust(Vector3 feetPos)
    {
        Color tint = new Color(0.96f, 0.96f, 0.94f);
        for (int i = 0; i < 4; i++)
        {
            float side = (i < 2) ? -1f : 1f;
            Vector3 spawnPos = feetPos + new Vector3(side * Random.Range(0.06f, 0.16f), Random.Range(-0.02f, 0.04f), 0f);
            Vector2 vel = new Vector2(side * Random.Range(0.9f, 1.85f), Random.Range(0.10f, 0.42f));
            float startS = dustBaseWorldScale * Random.Range(0.60f, 0.95f);
            float endS = startS * Random.Range(1.25f, 1.55f);
            SpawnPuff(spawnPos, vel, startS, endS, Random.Range(0.20f, 0.30f), tint, 0.72f);
        }
    }

    private void SpawnPuff(Vector3 pos, Vector2 vel, float startScale, float endScale, float lifetime, Color color, float alpha)
    {
        DustPuff puff = GetFreeDustPuff();
        if (puff == null) return;

        puff.transform.position = pos;
        puff.transform.localScale = new Vector3(startScale, startScale, 1f);
        puff.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        puff.velocity = vel;
        puff.startScale = startScale;
        puff.endScale = endScale;
        puff.rotSpeed = Random.Range(-140f, 140f);
        puff.age = 0f;
        puff.lifetime = lifetime;
        puff.baseColor = new Color(color.r, color.g, color.b, alpha);
        puff.renderer.color = puff.baseColor;
        puff.active = true;
        puff.transform.gameObject.SetActive(true);
    }

    private void UpdateDustPool(float dt)
    {
        float drag = Mathf.Exp(-5.5f * dt);
        for (int i = 0; i < dustPool.Count; i++)
        {
            DustPuff puff = dustPool[i];
            if (!puff.active) continue;

            puff.age += dt;
            float t = puff.age / puff.lifetime;
            if (t >= 1f)
            {
                puff.active = false;
                puff.transform.gameObject.SetActive(false);
                continue;
            }

            puff.velocity *= drag;
            puff.transform.position += (Vector3)(puff.velocity * dt);
            puff.transform.Rotate(0f, 0f, puff.rotSpeed * dt);

            float scale = Mathf.Lerp(puff.startScale, puff.endScale, t);
            puff.transform.localScale = new Vector3(scale, scale, 1f);

            Color c = puff.baseColor;
            c.a *= (1f - t * t);
            puff.renderer.color = c;
        }
    }

    private void RegisterJumpCombo()
    {
        if (activeGameManager == null)
        {
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        }

        if (lastTouchedPlatform != null &&
            lastTouchedPlatform != lastComboPlatform &&
            comboTimer > 0f &&
            comboCount >= 1)
        {
            comboCount++;
            lastComboPlatform = lastTouchedPlatform;
            comboTimer = ComboWindowSeconds;

            int bonus = Mathf.Min(50, (comboCount - 1) * 5);
            if (activeGameManager != null)
            {
                activeGameManager.AddBonusScore(bonus);
            }

            ShowComboFeedback(bonus);
        }
        else
        {
            comboCount = 1;
            lastComboPlatform = lastTouchedPlatform;
            comboTimer = ComboWindowSeconds;
            HideComboBadge();
        }
    }

    private float GetComboPitch()
    {
        if (comboCount < 2) return 1f;
        return Mathf.Min(1.28f, 1f + (comboCount - 1) * 0.035f);
    }

    private void UpdateComboState(float dt)
    {
        if (!isGameplayActive || isPaused || comboCount <= 0) return;

        comboTimer -= dt;
        if (comboTimer <= 0f)
        {
            ClearComboInternal();
        }
    }

    private void ClearComboInternal()
    {
        comboCount = 0;
        lastComboPlatform = null;
        comboTimer = 0f;
        HideComboBadge();
    }

    private void EnsureComboUI()
    {
        if (comboRoot != null) return;

        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        if (activeGameManager == null)
        {
            activeGameManager = Object.FindAnyObjectByType<GameManager>();
        }

        comboRoot = new GameObject("ComboBadgeHUD");
        comboRoot.transform.SetParent(canvas.transform, false);

        comboRect = comboRoot.AddComponent<RectTransform>();
        comboRect.anchorMin = new Vector2(0.5f, 1f);
        comboRect.anchorMax = new Vector2(0.5f, 1f);
        comboRect.pivot = new Vector2(0.5f, 0.5f);
        comboRect.anchoredPosition = new Vector2(0f, -66f);
        comboRect.sizeDelta = new Vector2(220f, 44f);

        GameObject textObj = new GameObject("ComboText");
        textObj.transform.SetParent(comboRoot.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.anchoredPosition = new Vector2(0f, 4f);
        textRect.sizeDelta = Vector2.zero;

        comboText = textObj.AddComponent<TextMeshProUGUI>();
        ApplyGameFont(comboText);
        comboText.alignment = TextAlignmentOptions.Center;
        comboText.fontSize = 22f;
        comboText.raycastTarget = false;

        // Countdown bar background
        GameObject barBgObj = new GameObject("ComboBarBg");
        barBgObj.transform.SetParent(comboRoot.transform, false);
        RectTransform barBgRect = barBgObj.AddComponent<RectTransform>();
        barBgRect.anchorMin = new Vector2(0.5f, 0f);
        barBgRect.anchorMax = new Vector2(0.5f, 0f);
        barBgRect.pivot = new Vector2(0.5f, 0.5f);
        barBgRect.anchoredPosition = new Vector2(0f, 4f);
        barBgRect.sizeDelta = new Vector2(110f, 5f);

        Image barBg = barBgObj.AddComponent<Image>();
        barBg.color = new Color(0f, 0f, 0f, 0.35f);
        barBg.raycastTarget = false;

        // Countdown bar fill
        GameObject barFillObj = new GameObject("ComboBarFill");
        barFillObj.transform.SetParent(barBgObj.transform, false);
        RectTransform fillRect = barFillObj.AddComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(1f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = Vector2.zero;

        comboBarFill = barFillObj.AddComponent<Image>();
        comboBarFill.color = GetComboColor(2);
        comboBarFill.raycastTarget = false;

        // Floating bonus pop (+5, +10, ...)
        GameObject popObj = new GameObject("ComboBonusPop");
        popObj.transform.SetParent(canvas.transform, false);
        bonusPopRect = popObj.AddComponent<RectTransform>();
        bonusPopRect.anchorMin = new Vector2(0.5f, 1f);
        bonusPopRect.anchorMax = new Vector2(0.5f, 1f);
        bonusPopRect.pivot = new Vector2(0.5f, 0.5f);
        bonusPopRect.anchoredPosition = new Vector2(0f, -94f);
        bonusPopRect.sizeDelta = new Vector2(120f, 28f);

        bonusPopText = popObj.AddComponent<TextMeshProUGUI>();
        ApplyGameFont(bonusPopText);
        bonusPopText.alignment = TextAlignmentOptions.Center;
        bonusPopText.fontSize = 18f;
        bonusPopText.color = new Color(1f, 0.96f, 0.55f, 0f);
        bonusPopText.raycastTarget = false;
        popObj.SetActive(false);

        comboRoot.SetActive(false);
    }

    private void ApplyGameFont(TextMeshProUGUI tmp)
    {
        if (activeGameManager != null && activeGameManager.scoreText != null && activeGameManager.scoreText.font != null)
        {
            tmp.font = activeGameManager.scoreText.font;
            tmp.fontSharedMaterial = activeGameManager.scoreText.fontSharedMaterial;
        }
    }

    private void ShowComboFeedback(int bonus)
    {
        EnsureComboUI();
        if (comboRoot == null) return;

        Color badgeColor = GetComboColor(comboCount);
        comboText.text = $"COMBO x{comboCount}!";
        comboText.color = badgeColor;
        if (comboBarFill != null) comboBarFill.color = badgeColor;

        comboRoot.SetActive(true);
        comboBadgePopTimer = 0f;

        if (bonusPopText != null)
        {
            bonusPopText.text = $"+{bonus}";
            bonusPopText.color = new Color(badgeColor.r, badgeColor.g, badgeColor.b, 1f);
            bonusPopRect.anchoredPosition = new Vector2(0f, -92f);
            bonusPopText.gameObject.SetActive(true);
            bonusPopTimer = 0f;
        }
    }

    private void HideComboBadge()
    {
        if (comboRoot != null) comboRoot.SetActive(false);
    }

    private void UpdateComboUI(float dt)
    {
        if (comboRoot != null && comboRoot.activeSelf)
        {
            if (comboBarFill != null)
            {
                float ratio = Mathf.Clamp01(comboTimer / ComboWindowSeconds);
                comboBarFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
            }

            if (comboBadgePopTimer >= 0f)
            {
                comboBadgePopTimer += dt;
                float t = Mathf.Clamp01(comboBadgePopTimer / 0.22f);
                float scale = t < 0.45f
                    ? Mathf.Lerp(1.32f, 1.0f, t / 0.45f)
                    : 1f;
                comboRect.localScale = new Vector3(scale, scale, 1f);
                if (t >= 1f) comboBadgePopTimer = -1f;
            }
        }

        if (bonusPopText != null && bonusPopTimer >= 0f)
        {
            bonusPopTimer += dt;
            float t = bonusPopTimer / 0.65f;
            if (t >= 1f)
            {
                bonusPopTimer = -1f;
                bonusPopText.gameObject.SetActive(false);
            }
            else
            {
                bonusPopRect.anchoredPosition = new Vector2(0f, -92f - (t * 14f));
                Color c = bonusPopText.color;
                c.a = 1f - t * t;
                bonusPopText.color = c;
            }
        }
    }

    private static Color GetComboColor(int count)
    {
        if (count >= 7) return new Color(1f, 0.42f, 0.78f); // Parlak pembe
        if (count >= 4) return new Color(1f, 0.64f, 0.26f); // Sicak turuncu
        return new Color(1f, 0.90f, 0.34f);                 // Altin sari
    }

    private static Sprite CreateFallbackDustSprite()
    {
        const int size = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color[] cols = new Color[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                float a = Mathf.Clamp01(1f - dist * dist);
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
    }
}
