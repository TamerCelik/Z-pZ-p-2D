using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Mobil dokunmatik kontroller veya fare tıklamaları için buton bileşeni.
/// Sol, Sağ ve Zıplama butonlarına atanarak PlayerController'ı tetikler.
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

    private PlayerController player;

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

        FindPlayer();
        if (player == null) return;

        if (buttonType == ButtonType.Left || buttonType == ButtonType.Right)
        {
            player.SetMobileMoveInput(0f);
        }
    }
}
