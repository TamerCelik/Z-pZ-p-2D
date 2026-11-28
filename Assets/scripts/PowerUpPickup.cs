using UnityEngine;

public enum PowerUpType
{
    Shield = 0,
    Rocket = 1,
    SuperFeet = 2
}

/// <summary>
/// Platform üzerinde beliren güçlendirme (Koruyucu Kalkan / Baloncuk ve Roket Fırlatması) objesi.
/// </summary>
public class PowerUpPickup : MonoBehaviour
{
    public PowerUpType powerUpType = PowerUpType.Shield;
    public float bobSpeed = 3.6f;
    public float bobAmount = 0.09f;
    public float pulseSpeed = 4.2f;

    private float startLocalY;
    private Vector3 baseScale;
    private bool isCollected = false;

    /// <summary>
    /// Düello modunda iki oyuncu arasında ortak olan deterministik item kimliği (-1: tek oyunculu).
    /// </summary>
    public int DuelItemId { get; set; } = -1;

    private static Sprite cachedShieldPickupSprite;
    private static Sprite cachedRocketPickupSprite;
    private static Sprite cachedSuperFeetSprite;
    private static Sprite cachedShieldAuraSprite;
    private static Sprite cachedRocketFlameSprite;

    public void Configure(PowerUpType type, Transform platformParent, Vector3 localOffset)
    {
        powerUpType = type;
        transform.SetParent(platformParent, false);
        transform.localPosition = localOffset;

        Vector3 parentScale = platformParent != null ? platformParent.lossyScale : Vector3.one;
        float invX = 1f / Mathf.Max(0.01f, Mathf.Abs(parentScale.x));
        float invY = 1f / Mathf.Max(0.01f, Mathf.Abs(parentScale.y));
        float iconSize = (type == PowerUpType.Shield) ? 0.56f : (type == PowerUpType.SuperFeet ? 0.72f : 0.68f);
        transform.localScale = new Vector3(iconSize * invX, iconSize * invY, 1f);

        SpriteRenderer sr = gameObject.GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        if (type == PowerUpType.Shield) sr.sprite = GetShieldPickupSprite();
        else if (type == PowerUpType.SuperFeet) sr.sprite = GetSuperFeetSprite();
        else sr.sprite = GetRocketPickupSprite();
        sr.sortingOrder = 6;

        CircleCollider2D col = gameObject.GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.42f;

        startLocalY = transform.localPosition.y;
        baseScale = transform.localScale;
    }

    void Start()
    {
        startLocalY = transform.localPosition.y;
        baseScale = transform.localScale;
    }

    void Update()
    {
        float newY = startLocalY + Mathf.Sin(Time.time * bobSpeed) * bobAmount;
        transform.localPosition = new Vector3(transform.localPosition.x, newY, transform.localPosition.z);

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.09f;
        transform.localScale = baseScale * pulse;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isCollected) return;

        if (other.GetComponent<PlayerController>() != null || other.CompareTag("Player") || other.name.ToLower().Contains("karakter"))
        {
            isCollected = true;

            if (DuelItemId >= 0 && MultiplayerManager.IsDuelActive && MultiplayerManager.Instance != null)
            {
                MultiplayerManager.Instance.NotifyLocalItemCollected(DuelItemId);
            }

            GameManager gm = GameManager.Instance != null ? GameManager.Instance : Object.FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                if (powerUpType == PowerUpType.Shield)
                {
                    gm.ActivateShield();
                }
                else if (powerUpType == PowerUpType.SuperFeet)
                {
                    gm.ActivateSuperFeet();
                }
                else
                {
                    gm.ActivateRocketBoost();
                }
            }

            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Düello modunda rakip bu güçlendirmeyi aldığında yerel oyuncuya efekt vermeden sahneden kaldırır.
    /// </summary>
    public void ConsumeRemotely()
    {
        if (isCollected) return;
        isCollected = true;
        JumpDustEffect.SpawnJumpDust(transform.position);
        Destroy(gameObject);
    }

    #region Procedural Pixel-Art Sprites
    public static Sprite GetSuperFeetSprite()
    {
        if (cachedSuperFeetSprite != null) return cachedSuperFeetSprite;

        cachedSuperFeetSprite = Resources.Load<Sprite>("PowerUps/pigeon_feet");
        if (cachedSuperFeetSprite != null) return cachedSuperFeetSprite;

        // Yedek basit ayak ikonu (Resources yüklenemezse)
        return GetShieldPickupSprite();
    }

    public static Sprite GetShieldPickupSprite()
    {
        if (cachedShieldPickupSprite != null) return cachedShieldPickupSprite;

        const int size = 28;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];
        float c = (size - 1) * 0.5f;

        Color darkOutline = new Color(0.08f, 0.22f, 0.36f, 1f);
        Color rimCyan = new Color(0.28f, 0.88f, 1.0f, 0.95f);
        Color bodyAqua = new Color(0.55f, 0.96f, 1.0f, 0.55f);
        Color coreGlow = new Color(0.88f, 1.0f, 1.0f, 0.85f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float dy = y - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > 12.6f)
                {
                    cols[y * size + x] = Color.clear;
                }
                else if (dist > 11.1f)
                {
                    cols[y * size + x] = darkOutline;
                }
                else if (dist > 9.3f)
                {
                    cols[y * size + x] = rimCyan;
                }
                else
                {
                    Color px = bodyAqua;
                    // Inner diamond/star emblem
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) < 4.5f)
                    {
                        px = coreGlow;
                    }
                    // Top-left glossy bubble shine
                    if (dx < -2.2f && dy > 2.2f && dist > 5.2f && dist < 8.6f)
                    {
                        px = Color.white;
                    }
                    cols[y * size + x] = px;
                }
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedShieldPickupSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedShieldPickupSprite;
    }

    public static Sprite GetShieldAuraSprite()
    {
        if (cachedShieldAuraSprite != null) return cachedShieldAuraSprite;

        const int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];
        float c = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float dy = y - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > 22.5f)
                {
                    cols[y * size + x] = Color.clear;
                }
                else if (dist > 20.6f)
                {
                    cols[y * size + x] = new Color(0.32f, 0.92f, 1.0f, 0.88f);
                }
                else if (dist > 18.8f)
                {
                    cols[y * size + x] = new Color(0.68f, 0.98f, 1.0f, 0.52f);
                }
                else
                {
                    Color fill = new Color(0.45f, 0.94f, 1.0f, 0.20f);
                    if (dx < -4f && dy > 4f && dist > 12.5f && dist < 17.5f)
                    {
                        fill = new Color(1f, 1f, 1f, 0.78f);
                    }
                    cols[y * size + x] = fill;
                }
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedShieldAuraSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedShieldAuraSprite;
    }

    public static Sprite GetRocketPickupSprite()
    {
        if (cachedRocketPickupSprite != null) return cachedRocketPickupSprite;

        const int w = 24;
        const int h = 30;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[w * h];

        Color dark = new Color(0.14f, 0.10f, 0.16f, 1f);
        Color red = new Color(0.95f, 0.26f, 0.24f, 1f);
        Color bodyCream = new Color(0.98f, 0.95f, 0.88f, 1f);
        Color bodyShade = new Color(0.84f, 0.80f, 0.74f, 1f);
        Color windowBlue = new Color(0.38f, 0.84f, 1.0f, 1f);
        Color flameOrange = new Color(1.0f, 0.56f, 0.12f, 1f);
        Color flameYellow = new Color(1.0f, 0.92f, 0.28f, 1f);

        float cx = (w - 1) * 0.5f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float adx = Mathf.Abs(x - cx);
                Color px = Color.clear;

                // Bottom flame (y: 1..6)
                if (y >= 1 && y <= 6)
                {
                    float flameHalf = Mathf.Lerp(1.2f, 3.6f, (y - 1f) / 5f);
                    if (adx <= flameHalf)
                    {
                        px = (adx <= flameHalf * 0.5f) ? flameYellow : flameOrange;
                    }
                }
                // Nozzle (y: 7..8)
                else if (y >= 7 && y <= 8)
                {
                    if (adx <= 3.6f) px = dark;
                }
                // Rocket hull + side fins (y: 9..27)
                else if (y >= 9 && y <= 27)
                {
                    float t = (y - 9f) / 18f; // 0 at bottom of hull, 1 at nose tip
                    float hullHalf = (t < 0.55f)
                        ? 5.2f
                        : Mathf.Lerp(5.2f, 0.8f, (t - 0.55f) / 0.45f);

                    // Side fins (y: 9..15)
                    bool inFin = (y >= 9 && y <= 15) && adx > hullHalf && adx <= hullHalf + Mathf.Lerp(3.8f, 0.8f, (y - 9f) / 6f);

                    if (inFin)
                    {
                        px = (adx > hullHalf + 2.8f || y == 9) ? dark : red;
                    }
                    else if (adx <= hullHalf + 1.1f)
                    {
                        if (adx > hullHalf || y == 9 || y == 27)
                        {
                            px = dark;
                        }
                        else if (y >= 21) // Red nose cone
                        {
                            px = red;
                        }
                        else // Cream body with porthole window
                        {
                            float wdx = x - cx;
                            float wdy = y - 16.5f;
                            float wDist = Mathf.Sqrt(wdx * wdx + wdy * wdy);
                            if (wDist <= 2.4f)
                            {
                                px = windowBlue;
                            }
                            else if (wDist <= 3.4f)
                            {
                                px = dark;
                            }
                            else
                            {
                                px = (x > cx + 1.5f) ? bodyShade : bodyCream;
                            }
                        }
                    }
                }

                cols[y * w + x] = px;
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedRocketPickupSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 32f);
        return cachedRocketPickupSprite;
    }

    public static Sprite GetRocketFlameSprite()
    {
        if (cachedRocketFlameSprite != null) return cachedRocketFlameSprite;

        const int w = 18;
        const int h = 24;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[w * h];
        float cx = (w - 1) * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float t = (float)y / (h - 1); // 0 at bottom tip, 1 at top base under player feet
            float halfW = Mathf.Lerp(1.0f, 6.8f, Mathf.Sin(t * Mathf.PI * 0.75f));
            for (int x = 0; x < w; x++)
            {
                float adx = Mathf.Abs(x - cx);
                if (adx > halfW)
                {
                    cols[y * w + x] = Color.clear;
                }
                else if (adx < halfW * 0.38f && t > 0.35f)
                {
                    cols[y * w + x] = new Color(1f, 0.98f, 0.75f, 0.96f);
                }
                else if (adx < halfW * 0.70f)
                {
                    cols[y * w + x] = new Color(1f, 0.84f, 0.22f, 0.92f);
                }
                else
                {
                    cols[y * w + x] = new Color(1f, 0.42f, 0.10f, 0.85f);
                }
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedRocketFlameSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.9f), 32f);
        return cachedRocketFlameSprite;
    }
    #endregion
}
