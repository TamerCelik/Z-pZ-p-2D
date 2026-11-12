using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ziplama ve yere inis anlarinda karakterin ayaklarinin altinda toz parcaciklari (Ziplama Tozu) olusturur.
/// </summary>
public class JumpDustEffect : MonoBehaviour
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

    private const int DustPoolSize = 24;

    private static JumpDustEffect instance;

    private readonly List<DustPuff> dustPool = new List<DustPuff>();
    private Sprite dustSprite;
    private Material unlitMaterial;
    private float dustBaseWorldScale = 1f;

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
        for (int i = 0; i < DustPoolSize; i++)
        {
            GameObject go = new GameObject($"Dust_{i}");
            go.transform.SetParent(transform);
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

    private void EmitTakeoffDust(Vector3 feetPos)
    {
        Color tint = new Color(1f, 0.98f, 0.93f);
        const int count = 6;
        for (int i = 0; i < count; i++)
        {
            float spreadT = ((float)i / (count - 1)) * 2f - 1f;
            Vector3 spawnPos = feetPos + new Vector3(spreadT * 0.16f + Random.Range(-0.04f, 0.04f), Random.Range(-0.03f, 0.05f), 0f);
            Vector2 vel = new Vector2(spreadT * Random.Range(0.95f, 1.55f) + Random.Range(-0.25f, 0.25f), Random.Range(0.15f, 0.72f));
            float startS = dustBaseWorldScale * Random.Range(0.75f, 1.15f);
            float endS = startS * Random.Range(1.35f, 1.65f);
            SpawnPuff(spawnPos, vel, startS, endS, Random.Range(0.26f, 0.38f), tint, 0.85f);
        }
    }

    private void EmitLandingDust(Vector3 feetPos)
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
