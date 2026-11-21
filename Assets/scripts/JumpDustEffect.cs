using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ziplama, inis ve Kus Ayagi super ziplama anlarinda karakterin ayaklarinin altinda
/// toz bulutu, genisleyen sok dalgasi halkasi ve pariltili yildiz partikulleri olusturur.
/// </summary>
public class JumpDustEffect : MonoBehaviour
{
    private class DustPuff
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public Vector2 startScale;
        public Vector2 endScale;
        public float rotSpeed;
        public float age;
        public float lifetime;
        public Color baseColor;
        public bool active;
    }

    private const int DustPoolSize = 52;

    private static JumpDustEffect instance;

    private readonly List<DustPuff> dustPool = new List<DustPuff>();
    private Sprite dustSprite;
    private Sprite starSprite;
    private Sprite ringSprite;
    private Material unlitMaterial;
    private float dustBaseWorldScale = 1f;
    private float starBaseWorldScale = 1f;

    private bool wasGrounded;
    private bool hasLeftGroundOnce;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    private static JumpDustEffect EnsureInstance()
    {
        if (instance == null)
        {
            GameObject go = new GameObject("JumpDustEffect");
            instance = go.AddComponent<JumpDustEffect>();
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

        float drag = Mathf.Exp(-4.8f * dt);
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

            float sx = Mathf.Lerp(puff.startScale.x, puff.endScale.x, t);
            float sy = Mathf.Lerp(puff.startScale.y, puff.endScale.y, t);
            puff.transform.localScale = new Vector3(sx, sy, 1f);

            Color c = puff.baseColor;
            c.a *= (1f - t * t);
            puff.renderer.color = c;
        }
    }

    public static void NotifyGroundState(Vector3 feetPos, bool isGrounded, float verticalVelocity)
    {
        JumpDustEffect inst = EnsureInstance();
        bool groundedNow = isGrounded && verticalVelocity <= 0.1f;

        if (groundedNow && !inst.wasGrounded && inst.hasLeftGroundOnce)
        {
            inst.EmitLandingDust(feetPos);
        }

        if (!groundedNow)
        {
            inst.hasLeftGroundOnce = true;
        }
        inst.wasGrounded = groundedNow;
    }

    public static void SpawnJumpDust(Vector3 feetPos)
    {
        EnsureInstance().EmitTakeoffDust(feetPos);
    }

    public static void SpawnSuperJumpBurst(Vector3 feetPos)
    {
        EnsureInstance().EmitSuperJumpBurst(feetPos);
    }

    public static void SpawnSuperJumpTrail(Vector3 feetPos)
    {
        EnsureInstance().EmitSuperJumpTrail(feetPos);
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
        if (dustSprite == null) dustSprite = CreateFallbackDustSprite();

        starSprite = Resources.Load<Sprite>("Sky/sky_star_sparkle");
        if (starSprite == null) starSprite = CreateFallbackStarSprite();

        ringSprite = CreateRingSprite();

        float spriteWidth = dustSprite.bounds.size.x;
        dustBaseWorldScale = (spriteWidth > 0.01f) ? (0.18f / spriteWidth) : 1f;

        float starWidth = starSprite.bounds.size.x;
        starBaseWorldScale = (starWidth > 0.01f) ? (0.20f / starWidth) : 1f;
    }

    private void BuildDustPool()
    {
        for (int i = 0; i < DustPoolSize; i++)
        {
            GameObject go = new GameObject($"Dust_{i}");
            go.transform.SetParent(transform);
            go.SetActive(false);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dustSprite;
            sr.sortingOrder = 12;
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

    private void EmitTakeoffDust(Vector3 feetPos)
    {
        // Normal zıplamada sadece klasik yumuşak toz bulutu
        Color tint = new Color(1f, 0.98f, 0.93f);
        const int count = 6;
        for (int i = 0; i < count; i++)
        {
            float spreadT = ((float)i / (count - 1)) * 2f - 1f;
            Vector3 spawnPos = feetPos + new Vector3(spreadT * 0.16f + Random.Range(-0.04f, 0.04f), Random.Range(-0.03f, 0.04f), 0f);
            Vector2 vel = new Vector2(spreadT * Random.Range(0.95f, 1.55f) + Random.Range(-0.22f, 0.22f), Random.Range(0.15f, 0.65f));
            float startS = dustBaseWorldScale * Random.Range(0.75f, 1.10f);
            float endS = startS * Random.Range(1.35f, 1.65f);
            SpawnParticle(dustSprite, spawnPos, vel, new Vector2(startS, startS), new Vector2(endS, endS),
                Random.Range(-140f, 140f), Random.Range(0.24f, 0.34f), tint, 0.82f);
        }
    }

    private void EmitSuperJumpBurst(Vector3 feetPos)
    {
        // Çift şok dalgası halkası (altın + parlak beyaz)
        SpawnParticle(ringSprite, feetPos + new Vector3(0f, -0.04f, 0f), Vector2.zero,
            new Vector2(0.32f, 0.12f), new Vector2(1.45f, 0.42f), 0f, 0.34f,
            new Color(1f, 0.82f, 0.22f), 0.92f);

        SpawnParticle(ringSprite, feetPos + new Vector3(0f, 0.02f, 0f), new Vector2(0f, 0.4f),
            new Vector2(0.22f, 0.08f), new Vector2(1.05f, 0.28f), 0f, 0.26f,
            new Color(1f, 0.98f, 0.78f), 0.88f);

        // Güçlü altın-turuncu-beyaz patlama partikülleri
        Color[] palette =
        {
            new Color(1f, 0.86f, 0.28f),
            new Color(1f, 0.58f, 0.20f),
            new Color(0.55f, 0.94f, 1f),
            new Color(1f, 0.98f, 0.88f)
        };

        const int dustCount = 10;
        for (int i = 0; i < dustCount; i++)
        {
            float spreadT = ((float)i / (dustCount - 1)) * 2f - 1f;
            Vector3 spawnPos = feetPos + new Vector3(spreadT * 0.24f, Random.Range(-0.05f, 0.05f), 0f);
            Vector2 vel = new Vector2(spreadT * Random.Range(1.8f, 3.1f), Random.Range(-0.4f, 1.3f));
            float startS = dustBaseWorldScale * Random.Range(1.05f, 1.55f);
            float endS = startS * Random.Range(1.5f, 2.0f);
            SpawnParticle(dustSprite, spawnPos, vel, new Vector2(startS, startS), new Vector2(endS, endS),
                Random.Range(-220f, 220f), Random.Range(0.32f, 0.48f), palette[i % palette.Length], 0.95f);
        }

        // 8 dönen parlak yıldız kıvılcımı
        for (int i = 0; i < 8; i++)
        {
            float angle = (i / 8f) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            float spd = Random.Range(1.8f, 3.4f);
            Vector2 vel = new Vector2(Mathf.Cos(angle) * spd, Mathf.Sin(angle) * spd * 0.65f + 0.6f);
            float s0 = starBaseWorldScale * Random.Range(1.05f, 1.55f);
            float s1 = s0 * 0.20f;
            SpawnParticle(starSprite, feetPos, vel, new Vector2(s0, s0), new Vector2(s1, s1),
                Random.Range(-320f, 320f), Random.Range(0.36f, 0.52f), palette[i % palette.Length], 1f);
        }
    }

    private void EmitSuperJumpTrail(Vector3 feetPos)
    {
        Color col = (Random.value < 0.55f) ? new Color(1f, 0.88f, 0.35f) : new Color(0.62f, 0.95f, 1f);
        Vector3 pos = feetPos + new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(-0.06f, 0.06f), 0f);
        Vector2 vel = new Vector2(Random.Range(-0.45f, 0.45f), Random.Range(-1.4f, -0.5f));
        float s0 = starBaseWorldScale * Random.Range(0.80f, 1.25f);
        float s1 = s0 * 0.18f;
        SpawnParticle(starSprite, pos, vel, new Vector2(s0, s0), new Vector2(s1, s1),
            Random.Range(-240f, 240f), Random.Range(0.24f, 0.36f), col, 0.92f);
    }

    private void EmitLandingDust(Vector3 feetPos)
    {
        Color tint = new Color(0.96f, 0.96f, 0.94f);
        for (int i = 0; i < 5; i++)
        {
            float side = (i < 2) ? -1f : (i < 4 ? 1f : 0f);
            Vector3 spawnPos = feetPos + new Vector3(side * Random.Range(0.06f, 0.18f), Random.Range(-0.02f, 0.04f), 0f);
            Vector2 vel = new Vector2(side * Random.Range(0.95f, 1.95f), Random.Range(0.12f, 0.48f));
            float startS = dustBaseWorldScale * Random.Range(0.65f, 1.0f);
            float endS = startS * Random.Range(1.25f, 1.55f);
            SpawnParticle(dustSprite, spawnPos, vel, new Vector2(startS, startS), new Vector2(endS, endS),
                Random.Range(-140f, 140f), Random.Range(0.20f, 0.30f), tint, 0.78f);
        }
    }

    private void SpawnParticle(Sprite sprite, Vector3 pos, Vector2 vel, Vector2 startScale, Vector2 endScale,
        float rotSpeed, float lifetime, Color color, float alpha)
    {
        DustPuff puff = GetFreeDustPuff();
        if (puff == null) return;

        puff.renderer.sprite = sprite != null ? sprite : dustSprite;
        puff.transform.position = pos;
        puff.transform.localScale = new Vector3(startScale.x, startScale.y, 1f);
        puff.transform.rotation = (sprite == ringSprite) ? Quaternion.identity : Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        puff.velocity = vel;
        puff.startScale = startScale;
        puff.endScale = endScale;
        puff.rotSpeed = rotSpeed;
        puff.age = 0f;
        puff.lifetime = lifetime;
        puff.baseColor = new Color(color.r, color.g, color.b, alpha);
        puff.renderer.color = puff.baseColor;
        puff.active = true;
        puff.transform.gameObject.SetActive(true);
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

    private static Sprite CreateFallbackStarSprite()
    {
        const int size = 16;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - c) / c;
                float dy = Mathf.Abs(y - c) / c;
                float a = (dx + dy < 0.72f || (dx < 0.22f && dy < 0.95f) || (dy < 0.22f && dx < 0.95f)) ? 1f : 0f;
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
    }

    private static Sprite CreateRingSprite()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color[] cols = new Color[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float ring = 1f - Mathf.Abs(dist - 0.80f) / 0.18f;
                float a = Mathf.Clamp01(ring);
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
    }
}
