using System.Collections;
using UnityEngine;

/// <summary>
/// Platform uzerine yerlestirilen Trambolin / Yay bileseni.
/// Karakter uzerine bastiginda otomatik olarak super yuksege firlatir ve yay animasyonu oynatir.
/// </summary>
public class SpringTrampoline : MonoBehaviour
{
    [Header("Ziplama Carpani")]
    [Tooltip("Normal ziplamaya gore guc katsayisi")]
    public float bounceMultiplier = 1.95f;

    [Header("Gorseller")]
    public Sprite idleSprite;
    public Sprite jumpSprite;

    private SpriteRenderer sr;
    private Vector3 baseScale;
    private bool isBouncing = false;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseScale = transform.localScale;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isBouncing) return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null)
        {
            player = other.GetComponentInParent<PlayerController>();
        }

        if (player != null)
        {
            Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();
            // Karakter yukaridan inerken veya tramboline temas ettiginde tetikle
            if (playerRb != null && playerRb.linearVelocity.y <= 3.0f)
            {
                StartCoroutine(BounceSequence(player));
            }
        }
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (isBouncing) return;
        OnTriggerEnter2D(other);
    }

    private IEnumerator BounceSequence(PlayerController player)
    {
        isBouncing = true;

        // Karakteri super ziplat
        player.SuperBounce(bounceMultiplier);

        // Yay Ziplama Sprite'i
        if (jumpSprite != null && sr != null)
        {
            sr.sprite = jumpSprite;
        }

        // Squash & Stretch Animasyonu (Tatli bir esneme hissi)
        // 1. Basilma (Squash)
        transform.localScale = new Vector3(baseScale.x * 1.35f, baseScale.y * 0.45f, baseScale.z);
        yield return new WaitForSeconds(0.06f);

        // 2. Yukari Firlama (Stretch)
        transform.localScale = new Vector3(baseScale.x * 0.85f, baseScale.y * 1.45f, baseScale.z);
        yield return new WaitForSeconds(0.12f);

        // 3. Orijinal Boyutuna Donus
        transform.localScale = baseScale;
        if (idleSprite != null && sr != null)
        {
            sr.sprite = idleSprite;
        }

        yield return new WaitForSeconds(0.12f);
        isBouncing = false;
    }
}
