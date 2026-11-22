using System.Collections;
using UnityEngine;

/// <summary>
/// Uzay evresine (1150+ skor) girildiğinde ekranın üstünden önce uyarı işareti (!),
/// ardından alevli piksel meteorlar düşürerek kaçınma aksiyonu ekler.
/// </summary>
public class SpaceMeteorSpawner : MonoBehaviour
{
    [Header("Uzay Meteor Ayarları")]
    public float spaceStartScore = 1150f;
    public float warningDuration = 0.85f;
    public float minSpawnInterval = 2.2f;
    public float maxSpawnInterval = 4.6f;
    public float baseFallSpeed = 4.8f;
    public float maxFallSpeed = 6.4f;

    private SkyCycleManager skyCycle;
    private float nextMeteorTimer = 2.0f;

    private static Sprite cachedMeteorSprite;
    private static Sprite cachedWarningSprite;
    private static Sprite cachedTrailSprite;

    private void Start()
    {
        skyCycle = Object.FindAnyObjectByType<SkyCycleManager>();
        nextMeteorTimer = Random.Range(1.8f, 3.0f);
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || Time.timeScale <= 0f) return;

        float effectiveScore = skyCycle != null ? skyCycle.GetCurrentMappedScore() : GameManager.currentScore;
        if (effectiveScore < spaceStartScore)
        {
            nextMeteorTimer = Mathf.Min(nextMeteorTimer, 2.0f);
            return;
        }

        nextMeteorTimer -= Time.deltaTime;
        if (nextMeteorTimer <= 0f)
        {
            StartCoroutine(SpawnMeteorWithWarning(effectiveScore));

            // Derin uzaya indikçe meteor aralığı hafifçe sıklaşır ama adil kalır
            float difficulty = Mathf.Clamp01((effectiveScore - spaceStartScore) / 1000f);
            float minInt = Mathf.Lerp(3.2f, minSpawnInterval, difficulty);
            float maxInt = Mathf.Lerp(5.0f, maxSpawnInterval, difficulty);
            nextMeteorTimer = Random.Range(minInt, maxInt);
        }
    }

    private IEnumerator SpawnMeteorWithWarning(float effectiveScore)
    {
        Camera cam = Camera.main;
        float halfH = cam != null ? cam.orthographicSize : 5f;
        float halfW = cam != null ? halfH * cam.aspect : 2.8f;
        float safeX = Mathf.Max(1.1f, halfW - 0.28f);

        // %45 sol/sağ köşelerden, %30 oyuncunun yakınından, %25 tamamen rastgele X'ten gelsin
        float spawnX;
        float roll = Random.value;
        if (roll < 0.45f)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            spawnX = side * Random.Range(safeX * 0.60f, safeX);
        }
        else if (roll < 0.75f && GameManager.Instance != null && GameManager.Instance.player != null)
        {
            float px = GameManager.Instance.player.transform.position.x;
            spawnX = Mathf.Clamp(px + Random.Range(-0.65f, 0.65f), -safeX, safeX);
        }
        else
        {
            spawnX = Random.Range(-safeX, safeX);
        }

        // 1. Üst kenarda yanıp sönen tehlike uyarısı (!)
        GameObject warnObj = new GameObject("MeteorWarning");
        warnObj.transform.position = new Vector3(spawnX, halfH - 0.55f, 0f);
        warnObj.transform.localScale = new Vector3(0.72f, 0.72f, 1f);

        SpriteRenderer warnSr = warnObj.AddComponent<SpriteRenderer>();
        warnSr.sprite = GetWarningSprite();
        warnSr.sortingOrder = 25;

        float elapsed = 0f;
        while (elapsed < warningDuration)
        {
            if (Time.timeScale > 0f)
            {
                elapsed += Time.deltaTime;
                float blink = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(elapsed * 18f));
                float pulse = 0.92f + 0.16f * Mathf.Abs(Mathf.Sin(elapsed * 14f));
                warnObj.transform.localScale = new Vector3(0.58f * pulse, 0.58f * pulse, 1f);
                warnSr.color = new Color(1f, 0.38f, 0.25f, blink);
            }
            yield return null;
        }

        if (warnObj != null) Destroy(warnObj);

        if (Time.timeScale <= 0f) yield break;

        // 2. Alevli piksel meteoru üstten fırlat
        float diff = Mathf.Clamp01((effectiveScore - spaceStartScore) / 1000f);
        float fallSpeed = Mathf.Lerp(baseFallSpeed, maxFallSpeed, diff) + Random.Range(-0.35f, 0.45f);
        // Köşelerden gelen meteorlar ekran dışına taşmadan hafifçe içeri veya düz süzülsün
        float driftX = (Mathf.Abs(spawnX) > safeX * 0.60f)
            ? -Mathf.Sign(spawnX) * Random.Range(0.05f, 0.42f)
            : Random.Range(-0.42f, 0.42f);

        GameObject meteorObj = new GameObject("SpaceMeteorHazard");
        meteorObj.transform.position = new Vector3(spawnX, halfH + 0.75f, 0f);
        float scale = Random.Range(0.50f, 0.66f);
        meteorObj.transform.localScale = new Vector3(scale, scale, 1f);

        SpriteRenderer sr = meteorObj.AddComponent<SpriteRenderer>();
        sr.sprite = GetMeteorSprite();
        sr.sortingOrder = 14;

        CircleCollider2D col = meteorObj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.21f;

        // Arkadaki alev kuyruğu
        GameObject trailObj = new GameObject("MeteorFlameTrail");
        trailObj.transform.SetParent(meteorObj.transform, false);
        trailObj.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        trailObj.transform.localScale = new Vector3(0.85f, 1.15f, 1f);
        SpriteRenderer trailSr = trailObj.AddComponent<SpriteRenderer>();
        trailSr.sprite = GetTrailSprite();
        trailSr.sortingOrder = 13;

        SpaceMeteorProjectile proj = meteorObj.AddComponent<SpaceMeteorProjectile>();
        proj.Configure(new Vector2(driftX, -fallSpeed), -halfH - 1.2f, trailObj.transform);
    }

    #region Procedural Pixel-Art Sprites
    public static Sprite GetMeteorSprite()
    {
        if (cachedMeteorSprite != null) return cachedMeteorSprite;

        const int size = 24;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];
        float c = (size - 1) * 0.5f;

        Color outline = new Color(0.18f, 0.08f, 0.12f, 1f);
        Color hotRim = new Color(1.0f, 0.45f, 0.12f, 1f);
        Color rockLight = new Color(0.56f, 0.46f, 0.54f, 1f);
        Color rockMid = new Color(0.40f, 0.32f, 0.40f, 1f);
        Color crater = new Color(0.24f, 0.18f, 0.26f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c;
                float dy = y - c;
                float ang = Mathf.Atan2(dy, dx);
                float r = 8.4f + 1.1f * Mathf.Sin(ang * 3f + 0.5f) + 0.6f * Mathf.Cos(ang * 5f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                Color px = Color.clear;
                if (dist <= r + 1.1f)
                {
                    if (dist > r) px = outline;
                    else if (dist > r - 1.4f && dy < 1f) px = hotRim; // Alt kenar atmosfere giriş alevi
                    else
                    {
                        bool inCrater = Vector2.Distance(new Vector2(dx, dy), new Vector2(-2.2f, 1.8f)) < 2.1f
                                     || Vector2.Distance(new Vector2(dx, dy), new Vector2(2.6f, -1.2f)) < 1.7f;
                        if (inCrater) px = crater;
                        else px = (dx + dy > 1.2f) ? rockLight : rockMid;
                    }
                }
                cols[y * size + x] = px;
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedMeteorSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedMeteorSprite;
    }

    public static Sprite GetTrailSprite()
    {
        if (cachedTrailSprite != null) return cachedTrailSprite;

        const int w = 16;
        const int h = 24;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[w * h];
        float cx = (w - 1) * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float t = (float)y / (h - 1); // 0 bottom (near rock), 1 top tail tip
            float halfW = Mathf.Lerp(5.8f, 0.8f, t);
            for (int x = 0; x < w; x++)
            {
                float adx = Mathf.Abs(x - cx);
                if (adx > halfW) cols[y * w + x] = Color.clear;
                else if (adx < halfW * 0.42f && t < 0.65f) cols[y * w + x] = new Color(1f, 0.94f, 0.45f, 0.92f * (1f - t * 0.5f));
                else cols[y * w + x] = new Color(1f, 0.42f, 0.10f, 0.82f * (1f - t * 0.8f));
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedTrailSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.1f), 32f);
        return cachedTrailSprite;
    }

    public static Sprite GetWarningSprite()
    {
        if (cachedWarningSprite != null) return cachedWarningSprite;

        const int size = 24;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];
        float cx = (size - 1) * 0.5f;

        Color border = new Color(0.20f, 0.04f, 0.06f, 1f);
        Color fill = new Color(1.0f, 0.28f, 0.22f, 1f);
        Color exclam = new Color(1.0f, 0.96f, 0.72f, 1f);

        for (int y = 0; y < size; y++)
        {
            float t = (float)(y - 2) / 19f; // 0 bottom, 1 apex
            float halfW = Mathf.Lerp(9.5f, 0.5f, Mathf.Clamp01(t));
            for (int x = 0; x < size; x++)
            {
                Color px = Color.clear;
                if (y >= 2 && y <= 21)
                {
                    float adx = Mathf.Abs(x - cx);
                    if (adx <= halfW + 1.2f)
                    {
                        if (y <= 3 || adx > halfW - 0.3f) px = border;
                        else px = fill;

                        // '!' ünlem işareti
                        if (adx <= 1.2f && ((y >= 9 && y <= 15) || (y >= 5 && y <= 6)))
                        {
                            px = exclam;
                        }
                    }
                }
                cols[y * size + x] = px;
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        cachedWarningSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedWarningSprite;
    }
    #endregion
}

/// <summary>
/// Aşağı düşen tek bir uzay meteorunun hareketi ve çarpışma mantığı.
/// </summary>
public class SpaceMeteorProjectile : MonoBehaviour
{
    private Vector2 velocity;
    private float destroyY = -6.5f;
    private Transform flameTrail;
    private float spinSpeed;
    private bool hitTriggered = false;

    public void Configure(Vector2 vel, float minY, Transform trail)
    {
        velocity = vel;
        destroyY = minY;
        flameTrail = trail;
        spinSpeed = (Random.value < 0.5f ? -1f : 1f) * Random.Range(140f, 240f);
    }

    private void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);

        if (flameTrail != null)
        {
            // Kuyruk hep yukarı (hareketin tersine) baksın
            float angle = Mathf.Atan2(-velocity.y, -velocity.x) * Mathf.Rad2Deg - 90f;
            flameTrail.rotation = Quaternion.Euler(0f, 0f, angle);
            float flicker = 0.95f + Mathf.Abs(Mathf.Sin(Time.time * 24f)) * 0.35f;
            flameTrail.localScale = new Vector3(0.85f, flicker, 1f);
        }

        if (transform.position.y < destroyY)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hitTriggered) return;

        PlayerController pc = other.GetComponent<PlayerController>();
        if (pc != null || other.CompareTag("Player") || other.name.ToLower().Contains("karakter"))
        {
            hitTriggered = true;
            JumpDustEffect.SpawnJumpDust(transform.position);

            GameManager gm = GameManager.Instance != null ? GameManager.Instance : Object.FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                if (gm.IsRocketBoosting)
                {
                    // Roket uçuşundayken meteor parçalanır, hasar alınmaz!
                    Destroy(gameObject);
                    return;
                }

                if (gm.TryAbsorbHitWithShield(transform.position))
                {
                    // Kalkan meteoru yerinde karşıladı, oyuncu yerinden bile oynamadan kurtuldu!
                    Destroy(gameObject);
                    return;
                }

                if (pc != null && pc.damageSound != null)
                {
                    AudioSource a = pc.GetComponent<AudioSource>();
                    if (a != null) a.PlayOneShot(pc.damageSound, pc.hasarSesiSiddeti);
                }

                gm.TakeDamage();
            }

            Destroy(gameObject);
        }
    }
}
