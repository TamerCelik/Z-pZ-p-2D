using UnityEngine;

public class PlatformMovement : MonoBehaviour
{
    // Artýk burada "public float speed" tanýmlamýyoruz.
    // Çünkü hýzý GameManager'dan alacaðýz.

    void Update()
    {
        // GameManager'daki static hýzý kullan:
        transform.Translate(Vector3.down * GameManager.globalPlatformSpeed * Time.deltaTime);

        if (transform.position.y < -10f)
        {
            Destroy(gameObject);
        }
    }
}