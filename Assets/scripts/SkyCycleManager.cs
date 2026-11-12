using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Skor yukseldikce gokyuzunu gunduzden gun batimina, geceye, uzaya ve tekrar safaga
/// surekli bir dongu halinde yumusakca donusturen paralel katmanli atmosfer yoneticisi.
/// </summary>
public class SkyCycleManager : MonoBehaviour
{
    [System.Serializable]
    private struct SkyKeyframe
    {
        public float score;
        public Color topColor;
        public Color midColor;
        public Color bottomColor;
        public Color cloudColor;
        public float cloudAlpha;
        public float starVisibility;
        public float moonVisibility;
        public float spaceVisibility;
        public float lightIntensity;
        public Color lightColor;
    }

    private struct DriftingElement
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector3 baseScale;
        public Color baseTint;
        public float parallaxFactor;
        public float horizontalSpeed;
        public float twinkleSpeed;
        public float twinklePhase;
        public float maxAlpha;
    }

    private struct ShootingStarInstance
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float lifetime;
        public float age;
        public bool active;
    }

    [Header("Dongu Ayarlari")]
    [Tooltip("Bir tam gokyuzu dongusunun (Gunduz -> Gun Batimi -> Gece -> Uzay -> Safak -> Gunduz) uzunlugu")]
    public float cycleLength = 2000f;

    [Tooltip("Ilk oyunda orman videosunun yumusakca gokyuzune karismaya baslayacagi skor")]
    public float forestFadeStartScore = 110f;

    [Tooltip("Orman videosunun tamamen arka planda kaybolup saf gokyuzune gecilecegi skor")]
    public float forestFadeEndScore = 260f;

    [Header("Sahne Referanslari (Bos birakilirsa otomatik bulunur)")]
    public Camera mainCamera;
    public VideoPlayer backgroundVideo;
    public Light2D globalLight2D;

    [Header("Gokyuzu Sprite'lari (Assets/UI/Sky)")]
    public Sprite cloudLargeSprite;
    public Sprite cloudSmallSprite;
    public Sprite moonSprite;
    public Sprite starSparkleSprite;
    public Sprite starDotSprite;
    public Sprite planetSprite;
    public Sprite shootingStarSprite;

    [Header("Katman Yogunlugu")]
    [Range(2, 14)] public int cloudCount = 7;
    [Range(10, 60)] public int starCount = 34;
    public float minShootingStarInterval = 3.5f;
    public float maxShootingStarInterval = 6.5f;

    [Header("Bolge Gecis Bildirimi")]
    public bool showZoneBanner = true;

    [Header("Test & Onizleme (Play Modunda Slider ile Test Edebilirsiniz)")]
    public bool usePreviewScore = false;
    [Range(0f, 2000f)]
    public float previewScore = 0f;

    private SkyKeyframe[] keyframes;

    // Gradient sky background
    private SpriteRenderer gradientRenderer;
    private Texture2D gradientTexture;
    private Sprite gradientSprite;
    private Color[] gradientPixels;
    private Material unlitMaterial;

    private Color lastAppliedTop = Color.clear;
    private Color lastAppliedMid = Color.clear;
    private Color lastAppliedBottom = Color.clear;
    private float lastAppliedOverlayAlpha = -1f;

    // Video fade tracking
    private float initialVideoAlpha = 0.607f;
    private Color initialCameraColor = new Color(0.192f, 0.302f, 0.475f, 0f);

    // Parallax elements
    private readonly List<DriftingElement> clouds = new List<DriftingElement>();
    private readonly List<DriftingElement> stars = new List<DriftingElement>();
    private SpriteRenderer moonRenderer;
    private Transform moonTransform;
    private Vector3 moonBasePos;

    private SpriteRenderer planetRenderer;
    private Transform planetTransform;
    private Vector3 planetBasePos;

    private ShootingStarInstance shootingStar;
    private float nextShootingStarTimer;

    // Camera view bounds
    private float camHalfHeight = 5f;
    private float camHalfWidth = 3f;

    // Zone banner UI
    private int lastZoneIndex = 0;
    private TextMeshProUGUI bannerText;
    private RectTransform bannerRect;
    private float bannerTimer = -1f;
    private const float BannerDuration = 2.4f;

    void Start()
    {
        ResolveReferences();
        InitializeKeyframes();
        CreateUnlitMaterial();
        CreateGradientBackdrop();
        CreateClouds();
        CreateStars();
        CreateMoon();
        CreatePlanet();
        CreateShootingStar();
        CreateBannerUI();

        nextShootingStarTimer = Random.Range(minShootingStarInterval, maxShootingStarInterval);
        ApplyAtmosphere(GetEffectiveScore(), immediate: true);
    }

    void Update()
    {
        float score = GetEffectiveScore();
        ApplyAtmosphere(score, immediate: false);

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        UpdateClouds(dt);
        UpdateStars(dt);
        UpdateCelestialDrift(dt);
        UpdateShootingStar(dt);
        UpdateBanner(dt);
    }

    void OnDestroy()
    {
        if (gradientTexture != null) Destroy(gradientTexture);
        if (gradientSprite != null) Destroy(gradientSprite);
        if (unlitMaterial != null) Destroy(unlitMaterial);
    }

    private float GetEffectiveScore()
    {
        return usePreviewScore ? Mathf.Max(0f, previewScore) : Mathf.Max(0f, GameManager.currentScore);
    }

    private void ResolveReferences()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera != null)
        {
            camHalfHeight = mainCamera.orthographicSize;
            camHalfWidth = camHalfHeight * Mathf.Max(0.45f, mainCamera.aspect);
            initialCameraColor = mainCamera.backgroundColor;

            if (backgroundVideo == null)
            {
                backgroundVideo = mainCamera.GetComponent<VideoPlayer>();
            }
        }

        if (backgroundVideo != null)
        {
            initialVideoAlpha = backgroundVideo.targetCameraAlpha;
        }

        if (globalLight2D == null)
        {
            globalLight2D = Object.FindAnyObjectByType<Light2D>();
        }
    }

    private void CreateUnlitMaterial()
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
    }

    private static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;
    }

    private void InitializeKeyframes()
    {
        // Not: Ilk ve son keyframe birebir ayni olmalidir ki 2000 skorda dongu kesintisiz devam etsin.
        keyframes = new SkyKeyframe[]
        {
            new SkyKeyframe // 0: Gunduz
            {
                score = 0f,
                topColor = Hex("#3D9BE9"),
                midColor = Hex("#7BC8F6"),
                bottomColor = Hex("#D6F3FF"),
                cloudColor = Color.white,
                cloudAlpha = 0.90f,
                starVisibility = 0f,
                moonVisibility = 0f,
                spaceVisibility = 0f,
                lightIntensity = 1.00f,
                lightColor = Color.white
            },
            new SkyKeyframe // 300: Altin Saat / Gun Batimi Baslangici
            {
                score = 300f,
                topColor = Hex("#5F84D6"),
                midColor = Hex("#F6A6B2"),
                bottomColor = Hex("#FFE09E"),
                cloudColor = Hex("#FFF2E8"),
                cloudAlpha = 0.90f,
                starVisibility = 0f,
                moonVisibility = 0f,
                spaceVisibility = 0f,
                lightIntensity = 0.97f,
                lightColor = Hex("#FFF7EE")
            },
            new SkyKeyframe // 520: Gun Batimi Zirvesi
            {
                score = 520f,
                topColor = Hex("#5B3E8C"),
                midColor = Hex("#E06D94"),
                bottomColor = Hex("#FF9E64"),
                cloudColor = Hex("#FFB7A8"),
                cloudAlpha = 0.78f,
                starVisibility = 0.30f,
                moonVisibility = 0.20f,
                spaceVisibility = 0f,
                lightIntensity = 0.91f,
                lightColor = Hex("#FFE1D6")
            },
            new SkyKeyframe // 700: Alacakaranlik / Geceye Giris
            {
                score = 700f,
                topColor = Hex("#131842"),
                midColor = Hex("#2B2A63"),
                bottomColor = Hex("#69497D"),
                cloudColor = Hex("#9F8CB8"),
                cloudAlpha = 0.18f,
                starVisibility = 0.85f,
                moonVisibility = 0.90f,
                spaceVisibility = 0f,
                lightIntensity = 0.84f,
                lightColor = Hex("#DCD8FF")
            },
            new SkyKeyframe // 960: Derin Gece
            {
                score = 960f,
                topColor = Hex("#0B0F2E"),
                midColor = Hex("#162052"),
                bottomColor = Hex("#28336B"),
                cloudColor = Color.white,
                cloudAlpha = 0f,
                starVisibility = 1.00f,
                moonVisibility = 1.00f,
                spaceVisibility = 0.15f,
                lightIntensity = 0.82f,
                lightColor = Hex("#D8DDFF")
            },
            new SkyKeyframe // 1200: Uzay & Galaksi Baslangici
            {
                score = 1200f,
                topColor = Hex("#050410"),
                midColor = Hex("#160D38"),
                bottomColor = Hex("#2E155A"),
                cloudColor = Color.white,
                cloudAlpha = 0f,
                starVisibility = 1.00f,
                moonVisibility = 0.35f,
                spaceVisibility = 1.00f,
                lightIntensity = 0.85f,
                lightColor = Hex("#E5DBFF")
            },
            new SkyKeyframe // 1500: Kozmik Nebula
            {
                score = 1500f,
                topColor = Hex("#070614"),
                midColor = Hex("#1F1047"),
                bottomColor = Hex("#471E6B"),
                cloudColor = Color.white,
                cloudAlpha = 0f,
                starVisibility = 1.00f,
                moonVisibility = 0f,
                spaceVisibility = 1.00f,
                lightIntensity = 0.85f,
                lightColor = Hex("#EBDDFF")
            },
            new SkyKeyframe // 1760: Safak / Gun Dogumu
            {
                score = 1760f,
                topColor = Hex("#4D67BF"),
                midColor = Hex("#C88EC9"),
                bottomColor = Hex("#FFD1B8"),
                cloudColor = Hex("#FFEBE3"),
                cloudAlpha = 0.55f,
                starVisibility = 0.15f,
                moonVisibility = 0f,
                spaceVisibility = 0f,
                lightIntensity = 0.94f,
                lightColor = Hex("#FFF2EC")
            },
            new SkyKeyframe // 2000: Gunduz (Dongu Tamamlandi)
            {
                score = 2000f,
                topColor = Hex("#3D9BE9"),
                midColor = Hex("#7BC8F6"),
                bottomColor = Hex("#D6F3FF"),
                cloudColor = Color.white,
                cloudAlpha = 0.90f,
                starVisibility = 0f,
                moonVisibility = 0f,
                spaceVisibility = 0f,
                lightIntensity = 1.00f,
                lightColor = Color.white
            }
        };
    }

    private void CreateGradientBackdrop()
    {
        const int heightPx = 64;
        gradientTexture = new Texture2D(1, heightPx, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        gradientPixels = new Color[heightPx];

        gradientSprite = Sprite.Create(gradientTexture, new Rect(0, 0, 1, heightPx), new Vector2(0.5f, 0.5f), 1f);

        GameObject go = new GameObject("SkyGradientBackdrop");
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(0f, 0f, 5f);
        go.transform.localScale = new Vector3((camHalfWidth * 2f) + 8f, ((camHalfHeight * 2f) + 4f) / heightPx, 1f);

        gradientRenderer = go.AddComponent<SpriteRenderer>();
        gradientRenderer.sprite = gradientSprite;
        gradientRenderer.sortingOrder = -100;
        if (unlitMaterial != null) gradientRenderer.sharedMaterial = unlitMaterial;
    }

    private void CreateClouds()
    {
        Transform folder = new GameObject("SkyClouds").transform;
        folder.SetParent(transform);

        Sprite fallbackCloud = CreateFallbackCloudSprite();
        Sprite large = cloudLargeSprite != null ? cloudLargeSprite : fallbackCloud;
        Sprite small = cloudSmallSprite != null ? cloudSmallSprite : fallbackCloud;

        for (int i = 0; i < cloudCount; i++)
        {
            bool isSmall = (i % 2 == 1);
            GameObject obj = new GameObject($"Cloud_{i}");
            obj.transform.SetParent(folder);

            float x = Random.Range(-camHalfWidth - 0.5f, camHalfWidth + 0.5f);
            float y = Mathf.Lerp(-camHalfHeight, camHalfHeight + 0.5f, (float)i / Mathf.Max(1, cloudCount));
            obj.transform.position = new Vector3(x, y, 4f);

            float scale = isSmall ? Random.Range(0.32f, 0.48f) : Random.Range(0.38f, 0.55f);
            obj.transform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = isSmall ? small : large;
            sr.sortingOrder = isSmall ? -75 : -70;
            if (unlitMaterial != null) sr.sharedMaterial = unlitMaterial;

            float dir = (i % 2 == 0) ? 1f : -1f;
            clouds.Add(new DriftingElement
            {
                transform = obj.transform,
                renderer = sr,
                baseScale = obj.transform.localScale,
                baseTint = Color.white,
                parallaxFactor = isSmall ? Random.Range(0.10f, 0.16f) : Random.Range(0.18f, 0.26f),
                horizontalSpeed = dir * Random.Range(0.06f, 0.18f),
                maxAlpha = isSmall ? Random.Range(0.55f, 0.80f) : Random.Range(0.75f, 0.95f)
            });
        }
    }

    private void CreateStars()
    {
        Transform folder = new GameObject("SkyStars").transform;
        folder.SetParent(transform);

        Sprite fallbackSparkle = CreateFallbackStarSprite(true);
        Sprite fallbackDot = CreateFallbackStarSprite(false);
        Sprite sparkle = starSparkleSprite != null ? starSparkleSprite : fallbackSparkle;
        Sprite dot = starDotSprite != null ? starDotSprite : fallbackDot;

        Color[] starPalette =
        {
            Color.white,
            Hex("#FFF7D6"), // Yumusak krem
            Hex("#D9F6FF"), // Buz mavisi
            Hex("#FFE0F2")  // Pastel pembe
        };

        for (int i = 0; i < starCount; i++)
        {
            bool isSparkle = (i % 4 == 0);
            GameObject obj = new GameObject($"Star_{i}");
            obj.transform.SetParent(folder);

            float x = Random.Range(-camHalfWidth + 0.15f, camHalfWidth - 0.15f);
            float y = Random.Range(-camHalfHeight + 0.3f, camHalfHeight - 0.2f);
            obj.transform.position = new Vector3(x, y, 4.5f);

            float scale = isSparkle ? Random.Range(0.22f, 0.36f) : Random.Range(0.25f, 0.45f);
            obj.transform.localScale = new Vector3(scale, scale, 1f);

            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = isSparkle ? sparkle : dot;
            sr.sortingOrder = -90;
            if (unlitMaterial != null) sr.sharedMaterial = unlitMaterial;

            stars.Add(new DriftingElement
            {
                transform = obj.transform,
                renderer = sr,
                baseScale = obj.transform.localScale,
                baseTint = starPalette[i % starPalette.Length],
                parallaxFactor = Random.Range(0.03f, 0.08f),
                twinkleSpeed = Random.Range(1.6f, 4.2f),
                twinklePhase = Random.Range(0f, Mathf.PI * 2f),
                maxAlpha = isSparkle ? Random.Range(0.85f, 1.0f) : Random.Range(0.50f, 0.92f)
            });
        }
    }

    private void CreateMoon()
    {
        GameObject obj = new GameObject("SkyMoon");
        obj.transform.SetParent(transform);
        moonBasePos = new Vector3(camHalfWidth * 0.50f, camHalfHeight * 0.42f, 4.2f);
        obj.transform.position = moonBasePos;
        obj.transform.localScale = new Vector3(0.50f, 0.50f, 1f);

        moonTransform = obj.transform;
        moonRenderer = obj.AddComponent<SpriteRenderer>();
        moonRenderer.sprite = moonSprite != null ? moonSprite : CreateFallbackStarSprite(true);
        moonRenderer.sortingOrder = -80;
        if (unlitMaterial != null) moonRenderer.sharedMaterial = unlitMaterial;
    }

    private void CreatePlanet()
    {
        GameObject obj = new GameObject("SkyPlanet");
        obj.transform.SetParent(transform);
        planetBasePos = new Vector3(-camHalfWidth * 0.46f, camHalfHeight * 0.34f, 4.3f);
        obj.transform.position = planetBasePos;
        obj.transform.localScale = new Vector3(0.42f, 0.42f, 1f);

        planetTransform = obj.transform;
        planetRenderer = obj.AddComponent<SpriteRenderer>();
        planetRenderer.sprite = planetSprite != null ? planetSprite : CreateFallbackCloudSprite();
        planetRenderer.sortingOrder = -82;
        if (unlitMaterial != null) planetRenderer.sharedMaterial = unlitMaterial;
    }

    private void CreateShootingStar()
    {
        GameObject obj = new GameObject("SkyShootingStar");
        obj.transform.SetParent(transform);
        obj.SetActive(false);

        shootingStar.transform = obj.transform;
        shootingStar.renderer = obj.AddComponent<SpriteRenderer>();
        shootingStar.renderer.sprite = shootingStarSprite != null ? shootingStarSprite : CreateFallbackStarSprite(true);
        shootingStar.renderer.sortingOrder = -85;
        if (unlitMaterial != null) shootingStar.renderer.sharedMaterial = unlitMaterial;
        shootingStar.active = false;
    }

    private void CreateBannerUI()
    {
        if (!showZoneBanner) return;

        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (canvas == null) return;

        GameObject bannerObj = new GameObject("SkyZoneBanner");
        bannerObj.transform.SetParent(canvas.transform, false);

        bannerRect = bannerObj.AddComponent<RectTransform>();
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.anchoredPosition = new Vector2(0f, -72f);
        bannerRect.sizeDelta = new Vector2(320f, 44f);

        bannerText = bannerObj.AddComponent<TextMeshProUGUI>();
        if (gm != null && gm.scoreText != null && gm.scoreText.font != null)
        {
            bannerText.font = gm.scoreText.font;
            bannerText.fontSharedMaterial = gm.scoreText.fontSharedMaterial;
        }
        bannerText.alignment = TextAlignmentOptions.Center;
        bannerText.fontSize = 24f;
        bannerText.color = new Color(1f, 1f, 1f, 0f);
        bannerText.raycastTarget = false;
        bannerObj.SetActive(false);
    }

    private float currentCloudAlpha;
    private Color currentCloudColor;
    private float currentStarVisibility;
    private float currentSpaceVisibility;

    private void ApplyAtmosphere(float totalScore, bool immediate)
    {
        float safeCycleLen = Mathf.Max(200f, cycleLength);
        float cycleRatio = safeCycleLen / 2000f;
        float mappedScore = (totalScore % safeCycleLen) / cycleRatio;

        SkyKeyframe state = EvaluateKeyframes(mappedScore);

        // Ilk oyunda orman videosunun yumusakca kaybolup gokyuzu gradyaninin gorunur olmasi
        float overlayAlpha = 1f;
        if (totalScore < forestFadeEndScore)
        {
            overlayAlpha = Mathf.SmoothStep(0f, 1f, (totalScore - forestFadeStartScore) / Mathf.Max(1f, forestFadeEndScore - forestFadeStartScore));
        }

        // Video player saydamligi ve performans icin tamamen kapandiginda duraklatma
        if (backgroundVideo != null)
        {
            float targetVideoAlpha = Mathf.Lerp(initialVideoAlpha, 0f, overlayAlpha);
            backgroundVideo.targetCameraAlpha = targetVideoAlpha;
            if (overlayAlpha >= 0.999f && backgroundVideo.isPlaying)
            {
                backgroundVideo.Pause();
            }
            else if (overlayAlpha < 0.999f && !backgroundVideo.isPlaying && backgroundVideo.enabled)
            {
                backgroundVideo.Play();
            }
        }

        if (mainCamera != null)
        {
            // Video gorunurken orijinal kamera arka plan rengini koru, sonrasinda alt gradyana uydur
            mainCamera.backgroundColor = Color.Lerp(initialCameraColor, state.bottomColor, overlayAlpha);
        }

        UpdateGradientTexture(state.topColor, state.midColor, state.bottomColor, overlayAlpha, immediate);

        // Ilk basta orman gorunurken bulutlarin gorunurlugunu yavasca ac
        float firstCycleReveal = (totalScore < forestFadeEndScore) ? overlayAlpha : 1f;
        currentCloudAlpha = state.cloudAlpha * firstCycleReveal;
        currentCloudColor = state.cloudColor;
        currentStarVisibility = state.starVisibility * firstCycleReveal;
        currentSpaceVisibility = state.spaceVisibility * firstCycleReveal;

        if (moonRenderer != null)
        {
            Color c = Color.white;
            c.a = state.moonVisibility * firstCycleReveal;
            moonRenderer.color = c;
        }

        if (planetRenderer != null)
        {
            Color c = Color.white;
            c.a = state.spaceVisibility * firstCycleReveal;
            planetRenderer.color = c;
        }

        if (globalLight2D != null)
        {
            globalLight2D.intensity = state.lightIntensity;
            globalLight2D.color = state.lightColor;
        }

        CheckZoneBanner(totalScore, mappedScore);
    }

    private SkyKeyframe EvaluateKeyframes(float mappedScore)
    {
        for (int i = 0; i < keyframes.Length - 1; i++)
        {
            SkyKeyframe a = keyframes[i];
            SkyKeyframe b = keyframes[i + 1];

            if (mappedScore <= b.score)
            {
                float span = Mathf.Max(0.001f, b.score - a.score);
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((mappedScore - a.score) / span));

                return new SkyKeyframe
                {
                    topColor = Color.Lerp(a.topColor, b.topColor, t),
                    midColor = Color.Lerp(a.midColor, b.midColor, t),
                    bottomColor = Color.Lerp(a.bottomColor, b.bottomColor, t),
                    cloudColor = Color.Lerp(a.cloudColor, b.cloudColor, t),
                    cloudAlpha = Mathf.Lerp(a.cloudAlpha, b.cloudAlpha, t),
                    starVisibility = Mathf.Lerp(a.starVisibility, b.starVisibility, t),
                    moonVisibility = Mathf.Lerp(a.moonVisibility, b.moonVisibility, t),
                    spaceVisibility = Mathf.Lerp(a.spaceVisibility, b.spaceVisibility, t),
                    lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t),
                    lightColor = Color.Lerp(a.lightColor, b.lightColor, t)
                };
            }
        }

        return keyframes[keyframes.Length - 1];
    }

    private void UpdateGradientTexture(Color top, Color mid, Color bottom, float overlayAlpha, bool immediate)
    {
        if (!immediate &&
            ColorsNearlyEqual(top, lastAppliedTop) &&
            ColorsNearlyEqual(mid, lastAppliedMid) &&
            ColorsNearlyEqual(bottom, lastAppliedBottom) &&
            Mathf.Abs(overlayAlpha - lastAppliedOverlayAlpha) < 0.002f)
        {
            return;
        }

        lastAppliedTop = top;
        lastAppliedMid = mid;
        lastAppliedBottom = bottom;
        lastAppliedOverlayAlpha = overlayAlpha;

        int h = gradientPixels.Length;
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            Color c = (v < 0.5f)
                ? Color.Lerp(bottom, mid, v * 2f)
                : Color.Lerp(mid, top, (v - 0.5f) * 2f);
            c.a = overlayAlpha;
            gradientPixels[y] = c;
        }

        gradientTexture.SetPixels(gradientPixels);
        gradientTexture.Apply(false);
    }

    private static bool ColorsNearlyEqual(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.002f &&
               Mathf.Abs(a.g - b.g) < 0.002f &&
               Mathf.Abs(a.b - b.b) < 0.002f;
    }

    private void UpdateClouds(float dt)
    {
        float fallBase = Mathf.Max(0f, GameManager.globalPlatformSpeed);
        float wrapMarginX = camHalfWidth + 2.2f;
        float wrapMarginY = camHalfHeight + 1.4f;

        for (int i = 0; i < clouds.Count; i++)
        {
            DriftingElement el = clouds[i];
            Vector3 pos = el.transform.position;
            pos.x += el.horizontalSpeed * dt;
            pos.y -= fallBase * el.parallaxFactor * dt;

            if (pos.x > wrapMarginX) pos.x = -wrapMarginX;
            else if (pos.x < -wrapMarginX) pos.x = wrapMarginX;

            if (pos.y < -wrapMarginY)
            {
                pos.y = wrapMarginY;
                pos.x = Random.Range(-camHalfWidth, camHalfWidth);
            }

            el.transform.position = pos;

            Color c = currentCloudColor;
            c.a = currentCloudAlpha * el.maxAlpha;
            el.renderer.color = c;
        }
    }

    private void UpdateStars(float dt)
    {
        float fallBase = Mathf.Max(0f, GameManager.globalPlatformSpeed);
        float wrapMarginY = camHalfHeight + 0.8f;

        for (int i = 0; i < stars.Count; i++)
        {
            DriftingElement el = stars[i];
            Vector3 pos = el.transform.position;
            pos.y -= fallBase * el.parallaxFactor * dt;

            if (pos.y < -wrapMarginY)
            {
                pos.y = wrapMarginY;
                pos.x = Random.Range(-camHalfWidth + 0.15f, camHalfWidth - 0.15f);
            }
            el.transform.position = pos;

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * el.twinkleSpeed + el.twinklePhase);
            Color c = el.baseTint;
            c.a = currentStarVisibility * el.maxAlpha * pulse;
            el.renderer.color = c;

            float scalePulse = 0.90f + 0.16f * pulse;
            el.transform.localScale = el.baseScale * scalePulse;
        }
    }

    private void UpdateCelestialDrift(float dt)
    {
        float bob = Mathf.Sin(Time.time * 0.45f) * 0.14f;

        if (moonTransform != null)
        {
            moonTransform.position = new Vector3(moonBasePos.x, moonBasePos.y + bob, moonBasePos.z);
        }

        if (planetTransform != null)
        {
            float planetBob = Mathf.Cos(Time.time * 0.35f) * 0.16f;
            planetTransform.position = new Vector3(planetBasePos.x, planetBasePos.y + planetBob, planetBasePos.z);
            planetTransform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.4f) * 5f);
        }
    }

    private void UpdateShootingStar(float dt)
    {
        if (shootingStar.active)
        {
            shootingStar.age += dt;
            float progress = shootingStar.age / shootingStar.lifetime;

            if (progress >= 1f)
            {
                shootingStar.active = false;
                shootingStar.transform.gameObject.SetActive(false);
                nextShootingStarTimer = Random.Range(minShootingStarInterval, maxShootingStarInterval);
                return;
            }

            Vector3 pos = shootingStar.transform.position;
            pos += (Vector3)(shootingStar.velocity * dt);
            shootingStar.transform.position = pos;

            // Baslangic ve bitiste yumusak alfa gecisi
            float alphaCurve = Mathf.Sin(progress * Mathf.PI);
            Color c = Color.white;
            c.a = alphaCurve * Mathf.Max(currentStarVisibility, currentSpaceVisibility);
            shootingStar.renderer.color = c;
            return;
        }

        if (currentStarVisibility < 0.65f) return;

        nextShootingStarTimer -= dt;
        if (nextShootingStarTimer <= 0f)
        {
            SpawnShootingStar();
        }
    }

    private void SpawnShootingStar()
    {
        bool fromLeft = Random.value > 0.5f;
        float startX = fromLeft ? -camHalfWidth - 0.6f : camHalfWidth + 0.6f;
        float startY = Random.Range(camHalfHeight * 0.25f, camHalfHeight * 0.85f);

        Vector2 dir = fromLeft ? new Vector2(6.5f, -2.6f) : new Vector2(-6.5f, -2.6f);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        shootingStar.transform.position = new Vector3(startX, startY, 4.4f);
        shootingStar.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        shootingStar.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
        shootingStar.velocity = dir;
        shootingStar.lifetime = 0.85f;
        shootingStar.age = 0f;
        shootingStar.active = true;
        shootingStar.transform.gameObject.SetActive(true);
    }

    private void CheckZoneBanner(float totalScore, float mappedScore)
    {
        if (!showZoneBanner || bannerText == null) return;

        int zoneIndex;
        string label;
        Color tint;

        if (mappedScore < 300f)
        {
            zoneIndex = 0;
            label = "GUNDUZ";
            tint = Hex("#D6F3FF");
        }
        else if (mappedScore < 700f)
        {
            zoneIndex = 1;
            label = "GUN BATIMI";
            tint = Hex("#FFC6A5");
        }
        else if (mappedScore < 1200f)
        {
            zoneIndex = 2;
            label = "GECE VAKTI";
            tint = Hex("#C6D2FF");
        }
        else if (mappedScore < 1700f)
        {
            zoneIndex = 3;
            label = "UZAY BOLGESI";
            tint = Hex("#E5B8FF");
        }
        else
        {
            zoneIndex = 4;
            label = "GUN DOGUMU";
            tint = Hex("#FFE0D8");
        }

        if (zoneIndex != lastZoneIndex)
        {
            lastZoneIndex = zoneIndex;

            // Ilk baslangic aninda (0 skor) banner cikmasin, ikinci turda "GUNDUZ" cikabilir
            if (zoneIndex == 0 && totalScore < 500f) return;

            bannerText.text = label;
            bannerText.color = new Color(tint.r, tint.g, tint.b, 0f);
            bannerText.gameObject.SetActive(true);
            bannerRect.anchoredPosition = new Vector2(0f, -74f);
            bannerTimer = 0f;
        }
    }

    private void UpdateBanner(float dt)
    {
        if (bannerText == null || bannerTimer < 0f) return;

        bannerTimer += dt;
        float t = bannerTimer / BannerDuration;

        if (t >= 1f)
        {
            bannerTimer = -1f;
            bannerText.gameObject.SetActive(false);
            return;
        }

        float alpha;
        if (t < 0.2f) alpha = t / 0.2f;
        else if (t > 0.72f) alpha = (1f - t) / 0.28f;
        else alpha = 1f;

        Color c = bannerText.color;
        c.a = alpha * 0.92f;
        bannerText.color = c;

        bannerRect.anchoredPosition = new Vector2(0f, -74f + (t * 14f));
    }

    #region Fallback Sprite Generators (Inspector bos kalirsa kullanilir)
    private static Sprite CreateFallbackCloudSprite()
    {
        Texture2D tex = new Texture2D(16, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[16 * 8];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                bool fill = (y >= 1 && y <= 4 && x >= 2 && x <= 13) || (y >= 4 && y <= 6 && x >= 5 && x <= 11);
                cols[y * 16 + x] = fill ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 16, 8), new Vector2(0.5f, 0.5f), 8f);
    }

    private static Sprite CreateFallbackStarSprite(bool cross)
    {
        int size = cross ? 5 : 2;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool fill = !cross || (x == 2 || y == 2);
                cols[y * size + x] = fill ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 8f);
    }
    #endregion
}
