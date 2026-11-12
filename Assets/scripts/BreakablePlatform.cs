using System.Collections;
using UnityEngine;

/// <summary>
/// Kirilan / Catlak Platform:
/// Karakter uzerine bastiginda hizlica titreyerek uyari verir,
/// 0.42 saniye sonra kirilarak asagi duser ve yok olur.
/// Oyuncunun tek bir kez basip hemen bir sonrakine atlamasi gerekir.
/// </summary>
public class BreakablePlatform : MonoBehaviour
{
    [Header("Kirilma Zamanlamasi")]
    [Tooltip("Karakter bastiktan kac saniye sonra kirilacagi")]
    public float breakDelay = 0.42f;

    [Tooltip("Kirildiktan sonra asagi dusme hizi")]
    public float fallSpeed = 7.5f;

    [Header("Kirilma Sprite'i (Opsiyonel)")]
    public Sprite brokenSprite;

    private bool isBreaking = false;
    private SpriteRenderer sr;
    private Collider2D col;
    private Color originalColor;
    private Vector3 originalPos;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        if (sr != null) originalColor = sr.color;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isBreaking) return;

        PlayerController player = collision.gameObject.GetComponent<PlayerController>();
        if (player == null)
        {
            player = collision.gameObject.GetComponentInParent<PlayerController>();
        }

        if (player != null)
        {
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            // Karakter yukaridan platforma bastiginda kirilmayi baslat
            if (rb != null && rb.linearVelocity.y <= 0.3f)
            {
                StartCoroutine(BreakSequence());
            }
        }
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        if (isBreaking) return;
        OnCollisionEnter2D(collision);
    }

    private IEnumerator BreakSequence()
    {
        isBreaking = true;
        originalPos = transform.position;

        float elapsed = 0f;
        Color warningColor = new Color(1f, 0.52f, 0.42f, 1f); // Catlak / kirilma uyari tonu

        // 1. Titreme / Sallanma Efekti (Oyuncuya "Hemen atla!" uyarisi verir)
        while (elapsed < breakDelay)
        {
            elapsed += Time.deltaTime;
            float shakeX = Mathf.Sin(elapsed * 60f) * 0.07f;
            transform.position = new Vector3(originalPos.x + shakeX, transform.position.y, transform.position.z);

            if (sr != null)
            {
                sr.color = Color.Lerp(originalColor, warningColor, elapsed / breakDelay);
            }

            yield return null;
        }

        // 2. Kirilma Asamasi: Carpisimyi kapat
        if (col != null)
        {
            col.enabled = false;
        }

        // Platform hareketliyse salinimi durdur
        PlatformMovement pm = GetComponent<PlatformMovement>();
        if (pm != null)
        {
            pm.enabled = false;
        }

        if (brokenSprite != null && sr != null)
        {
            sr.sprite = brokenSprite;
        }

        // 3. Hizlica Asagi Dusme ve Seffaflasarak Yok Olma
        float fallDuration = 0.45f;
        float fallElapsed = 0f;

        while (fallElapsed < fallDuration)
        {
            fallElapsed += Time.deltaTime;
            transform.position += Vector3.down * fallSpeed * Time.deltaTime;

            if (sr != null)
            {
                float alpha = Mathf.Lerp(1f, 0f, fallElapsed / fallDuration);
                sr.color = new Color(warningColor.r, warningColor.g, warningColor.b, alpha);
            }

            yield return null;
        }

        Destroy(gameObject);
    }
}
