using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Platformlar üzerinde doğan toplanabilir coin meyveleri (Cherries +1 / nadir Pineapple +3).
/// Sprite frame animasyonu, süzülme efekti, toplama parçacığı ve uçan "+1 / +3" yazısını yönetir.
/// </summary>
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Değer ve Türü")]
    [SerializeField] private int coinValue = 1;
    [SerializeField] private bool isRareFruit = false;

    [Header("Hareket ve Animasyon")]
    [SerializeField] private float bobSpeed = 3.6f;
    [SerializeField] private float bobAmount = 0.09f;
    [SerializeField] private float pulseSpeed = 4.2f;
    [SerializeField] private float frameRate = 18f;

    [Header("Boyut Ayarları")]
    [SerializeField] private float normalWorldScale = 2.15f;
    [SerializeField] private float rareWorldScale = 2.45f;

    private SpriteRenderer spriteRenderer;
    private Collider2D triggerCollider;
    private Sprite[] idleFrames;
    private Sprite[] collectedFrames;
    private CharacterShopConfig config;

    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale = Vector3.one;
    private float localBobAmount = 0.09f;
    private float phaseOffset;
    private float frameTimer;
    private int currentFrameIndex;
    private bool isCollected;

    private static readonly Color NormalCherryTint = Color.white;
    private static readonly Color RareGoldA = new Color(1f, 0.95f, 0.48f, 1f);
    private static readonly Color RareGoldB = new Color(1f, 0.78f, 0.18f, 1f);

    /// <summary>
    /// PlatformSpawner tarafından yeni oluşturulan coin nesnesini değer ve ölçekle yapılandırır.
    /// </summary>
    public void Configure(int value, bool rare, Transform platformParent, Vector3 localOffset)
    {
        coinValue = Mathf.Max(1, value);
        isRareFruit = rare;
        config = CharacterShopConfig.Load();

        EnsureComponents();

        float desiredWorldScale = rare ? rareWorldScale : normalWorldScale;

        if (platformParent != null)
        {
            transform.SetParent(platformParent, false);
            transform.localPosition = localOffset;

            Vector3 parentScale = platformParent.lossyScale;
            float invX = 1f / Mathf.Max(0.01f, Mathf.Abs(parentScale.x));
            float invY = 1f / Mathf.Max(0.01f, Mathf.Abs(parentScale.y));
            baseLocalScale = new Vector3(desiredWorldScale * invX, desiredWorldScale * invY, 1f);
            localBobAmount = bobAmount * invY;
        }
        else
        {
            baseLocalScale = Vector3.one * desiredWorldScale;
            localBobAmount = bobAmount;
        }

        transform.localScale = baseLocalScale;
        baseLocalPosition = transform.localPosition;
        ApplyConfigFrames();
    }

    private void Awake()
    {
        EnsureComponents();
        config = CharacterShopConfig.Load();
        baseLocalPosition = transform.localPosition;
        baseLocalScale = transform.localScale;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);
        ApplyConfigFrames();
    }

    private void EnsureComponents()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        spriteRenderer.sortingOrder = 5;

        if (triggerCollider == null)
        {
            CircleCollider2D circle = GetComponent<CircleCollider2D>();
            if (circle == null)
            {
                circle = gameObject.AddComponent<CircleCollider2D>();
                circle.radius = 0.26f;
            }
            circle.isTrigger = true;
            triggerCollider = circle;
        }
    }

    private void ApplyConfigFrames()
    {
        if (config == null) config = CharacterShopConfig.Load();
        if (config == null) return;

        idleFrames = (isRareFruit && config.pineappleFrames != null && config.pineappleFrames.Length > 0)
            ? config.pineappleFrames
            : config.cherryFrames;

        collectedFrames = config.collectedFrames;

        if (idleFrames != null && idleFrames.Length > 0 && spriteRenderer != null)
        {
            spriteRenderer.sprite = idleFrames[0];
        }
    }

    private void Update()
    {
        if (isCollected) return;

        float t = Time.time + phaseOffset;

        // Süzülme ve hafif nefes alma ölçeği
        float yOffset = Mathf.Sin(t * bobSpeed) * localBobAmount;
        transform.localPosition = baseLocalPosition + new Vector3(0f, yOffset, 0f);

        float pulse = 1f + Mathf.Sin(t * pulseSpeed) * 0.06f;
        transform.localScale = baseLocalScale * pulse;

        if (spriteRenderer == null) return;

        // Nadir meyvede altın parıltı
        if (isRareFruit)
        {
            float shimmer = 0.5f + 0.5f * Mathf.Sin(Time.time * 5.2f);
            spriteRenderer.color = Color.Lerp(RareGoldA, RareGoldB, shimmer);
        }
        else
        {
            spriteRenderer.color = NormalCherryTint;
        }

        // 17 karelik meyve animasyonunu oynat
        if (idleFrames != null && idleFrames.Length > 1)
        {
            frameTimer += Time.deltaTime * frameRate;
            if (frameTimer >= 1f)
            {
                int steps = Mathf.FloorToInt(frameTimer);
                frameTimer -= steps;
                currentFrameIndex = (currentFrameIndex + steps) % idleFrames.Length;
                spriteRenderer.sprite = idleFrames[currentFrameIndex];
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider2D other)
    {
        if (isCollected || other == null) return;

        bool isPlayer = other.CompareTag("Player")
            || other.GetComponentInParent<PlayerController>() != null
            || other.name.ToLowerInvariant().Contains("karakter");

        if (!isPlayer) return;

        isCollected = true;
        if (triggerCollider != null) triggerCollider.enabled = false;

        CharacterShopData.AddCoins(coinValue);

        AudioClip sfx = config != null ? config.coinCollectSound : null;
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyCoinCollected(coinValue, sfx, isRareFruit);
        }
        else if (sfx != null)
        {
            AudioSource.PlayClipAtPoint(sfx, transform.position, 0.55f);
        }

        SpawnFloatingBonusText();
        Vector3 worldScale = transform.lossyScale;
        transform.SetParent(null, true);
        transform.localScale = worldScale * 1.15f;
        StartCoroutine(PlayCollectedAndDestroy());
    }

    private IEnumerator PlayCollectedAndDestroy()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = isRareFruit ? new Color(1f, 0.92f, 0.45f, 1f) : Color.white;
        }

        if (collectedFrames != null && collectedFrames.Length > 0 && spriteRenderer != null)
        {
            const float burstFrameDuration = 1f / 24f;
            for (int i = 0; i < collectedFrames.Length; i++)
            {
                spriteRenderer.sprite = collectedFrames[i];
                transform.position += Vector3.up * (0.35f * burstFrameDuration);
                yield return new WaitForSeconds(burstFrameDuration);
            }
        }

        Destroy(gameObject);
    }

    private void SpawnFloatingBonusText()
    {
        GameObject popupGo = new GameObject("CoinBonusText");
        popupGo.transform.position = transform.position + new Vector3(0f, 0.25f, 0f);

        TextMeshPro tmp = popupGo.AddComponent<TextMeshPro>();
        if (config != null && config.uiFont != null)
        {
            tmp.font = config.uiFont;
        }

        tmp.text = isRareFruit ? ("+" + coinValue + "!") : ("+" + coinValue);
        tmp.fontSize = isRareFruit ? 4.4f : 3.6f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = isRareFruit
            ? new Color(1f, 0.85f, 0.18f, 1f)
            : new Color(1f, 0.96f, 0.55f, 1f);
        tmp.sortingOrder = 25;

        RectTransform rt = tmp.rectTransform;
        rt.sizeDelta = new Vector2(2.5f, 1f);

        FloatingCoinPopup.Attach(popupGo, tmp);
    }
}

/// <summary>
/// Coin toplandığında çıkan "+1 / +3!" yazısını yukarı doğru süzüp soluklaştırır ve yok eder.
/// </summary>
internal class FloatingCoinPopup : MonoBehaviour
{
    private TextMeshPro tmp;
    private Color baseColor;
    private float elapsed;
    private const float Duration = 0.65f;

    public static void Attach(GameObject target, TextMeshPro textMesh)
    {
        FloatingCoinPopup comp = target.AddComponent<FloatingCoinPopup>();
        comp.tmp = textMesh;
        comp.baseColor = textMesh.color;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Duration);

        transform.position += Vector3.up * (1.25f * Time.deltaTime);

        if (tmp != null)
        {
            Color c = baseColor;
            c.a = 1f - (t * t);
            tmp.color = c;
        }

        if (elapsed >= Duration)
        {
            Destroy(gameObject);
        }
    }
}

