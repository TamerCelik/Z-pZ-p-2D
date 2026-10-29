using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Mobil dokunmatik kontroller veya fare tıklamaları için buton bileşeni.
/// Sol, Sağ ve Zıplama butonlarına atanarak PlayerController'ı tetikler.
/// Tıklama / basma anında dokunsal ve akıcı küçülüp büyüme animasyonu sunar.
/// </summary>
public class MobileButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum ButtonType
    {
        Left,
        Right,
        Jump
    }

    [Header("Buton Görevi")]
    public ButtonType buttonType;

    [Header("Ayarlar")]
    [Tooltip("Parmağı butonun dışına kaydırınca hareketi durdur")]
    public bool stopOnPointerExit = true;

    [Header("Tıklama Animasyonu")]
    [Tooltip("Basıldığında küçülme oranı (0.88 = %12 küçülür)")]
    public float pressScale = 0.88f;
    [Tooltip("Animasyon hızı")]
    public float animationSpeed = 16f;

    private PlayerController player;
    private Vector3 initialScale;
    private Coroutine animateCoroutine;
    private bool isPressed = false;

    void Awake()
    {
        initialScale = transform.localScale;
    }

    void Start()
    {
        FindPlayer();
    }

    private void FindPlayer()
    {
        if (player == null)
        {
            player = Object.FindFirstObjectByType<PlayerController>();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        StartScaleAnimation(initialScale * pressScale);

        FindPlayer();
        if (player == null) return;

        switch (buttonType)
        {
            case ButtonType.Left:
                player.SetMobileMoveInput(-1f);
                break;
            case ButtonType.Right:
                player.SetMobileMoveInput(1f);
                break;
            case ButtonType.Jump:
                player.TriggerMobileJump();
                break;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        StartScaleAnimation(initialScale);

        FindPlayer();
        if (player == null) return;

        if (buttonType == ButtonType.Left || buttonType == ButtonType.Right)
        {
            player.SetMobileMoveInput(0f);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!stopOnPointerExit) return;

        if (isPressed)
        {
            isPressed = false;
            StartScaleAnimation(initialScale);
        }

        FindPlayer();
        if (player == null) return;

        if (buttonType == ButtonType.Left || buttonType == ButtonType.Right)
        {
            player.SetMobileMoveInput(0f);
        }
    }

    private void StartScaleAnimation(Vector3 targetScale)
    {
        if (animateCoroutine != null)
        {
            StopCoroutine(animateCoroutine);
        }
        animateCoroutine = StartCoroutine(AnimateScale(targetScale));
    }

    private IEnumerator AnimateScale(Vector3 targetScale)
    {
        while (Vector3.Distance(transform.localScale, targetScale) > 0.005f)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * animationSpeed);
            yield return null;
        }
        transform.localScale = targetScale;
        animateCoroutine = null;
    }

    void OnDisable()
    {
        isPressed = false;
        transform.localScale = initialScale;
    }
}
