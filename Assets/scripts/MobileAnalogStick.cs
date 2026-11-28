using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Analog kontrol modu (ControlMode == 2) için alt barın yerine geçen şık dairesel analog çubuk.
/// - Sağa / sola çekildiğinde orantılı ve akıcı yatay hareket verir.
/// - Yukarı (veya çapraz yukarı-sol / yukarı-sağ) itildiğinde zıplamayı tetikler.
/// </summary>
public class MobileAnalogStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Analog Ayarları")]
    public float maxRadius = 38f;
    public float horizontalDeadZone = 0.09f;
    public float fullSpeedThreshold = 0.68f;
    public float jumpUpThreshold = 0.38f;
    public float returnSmoothSpeed = 18f;

    private RectTransform touchZoneRect;
    private RectTransform ringRect;
    private RectTransform knobRect;
    private Image ringImage;
    private Image knobImage;

    private PlayerController player;
    private bool isDragging = false;
    private int activePointerId = -999;
    private Vector2 currentKnobPos = Vector2.zero;
    private Vector2 normalizedStick = Vector2.zero;
    private bool jumpTriggeredOnCurrentPush = false;

    private static Sprite cachedRingSprite;
    private static Sprite cachedKnobSprite;

    public static MobileAnalogStick CreateUnderCanvas(Transform canvasTransform, int desiredSiblingIndex)
    {
        GameObject rootObj = new GameObject("MobileAnalogStickRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rootObj.transform.SetParent(canvasTransform, false);
        if (desiredSiblingIndex >= 0 && desiredSiblingIndex < canvasTransform.childCount)
        {
            rootObj.transform.SetSiblingIndex(desiredSiblingIndex);
        }

        // Parmağın rahatça yakalaması için geniş görünmez dokunma alanı (sol altta başparmak hizasında)
        RectTransform rootRt = rootObj.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0f, 0f);
        rootRt.anchorMax = new Vector2(0f, 0f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = new Vector2(90f, 84f);
        rootRt.sizeDelta = new Vector2(180f, 164f);

        Image hitImg = rootObj.GetComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0f);
        hitImg.raycastTarget = true;

        // Dış yarı saydam ince halka (referans görseldeki dairesel çerçeve)
        GameObject ringObj = new GameObject("AnalogOuterRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ringObj.transform.SetParent(rootObj.transform, false);
        RectTransform ringRt = ringObj.GetComponent<RectTransform>();
        ringRt.anchorMin = new Vector2(0.5f, 0.5f);
        ringRt.anchorMax = new Vector2(0.5f, 0.5f);
        ringRt.pivot = new Vector2(0.5f, 0.5f);
        ringRt.anchoredPosition = Vector2.zero;
        ringRt.sizeDelta = new Vector2(126f, 126f);

        Image rImg = ringObj.GetComponent<Image>();
        rImg.sprite = GetOrCreateRingSprite();
        rImg.raycastTarget = false;
        rImg.color = new Color(1f, 1f, 1f, 0.88f);

        // İç parlak 3B içbükey beyaz analog topuzu
        GameObject knobObj = new GameObject("AnalogThumbKnob", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        knobObj.transform.SetParent(ringObj.transform, false);
        RectTransform knobRt = knobObj.GetComponent<RectTransform>();
        knobRt.anchorMin = new Vector2(0.5f, 0.5f);
        knobRt.anchorMax = new Vector2(0.5f, 0.5f);
        knobRt.pivot = new Vector2(0.5f, 0.5f);
        knobRt.anchoredPosition = Vector2.zero;
        knobRt.sizeDelta = new Vector2(60f, 60f);

        Image kImg = knobObj.GetComponent<Image>();
        kImg.sprite = GetOrCreateKnobSprite();
        kImg.raycastTarget = false;
        kImg.color = Color.white;

        MobileAnalogStick stick = rootObj.AddComponent<MobileAnalogStick>();
        stick.touchZoneRect = rootRt;
        stick.ringRect = ringRt;
        stick.knobRect = knobRt;
        stick.ringImage = rImg;
        stick.knobImage = kImg;
        return stick;
    }

    private void EnsurePlayer()
    {
        if (player == null)
        {
            player = Object.FindFirstObjectByType<PlayerController>();
        }
    }

    private void OnDisable()
    {
        ResetStickImmediate();
    }

    public void ResetStickImmediate()
    {
        isDragging = false;
        activePointerId = -999;
        currentKnobPos = Vector2.zero;
        normalizedStick = Vector2.zero;
        jumpTriggeredOnCurrentPush = false;

        if (knobRect != null) knobRect.anchoredPosition = Vector2.zero;
        if (ringImage != null) ringImage.color = new Color(1f, 1f, 1f, 0.88f);

        if (player != null)
        {
            player.SetMobileMoveInput(0f);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        EnsurePlayer();
        isDragging = true;
        activePointerId = eventData.pointerId;
        if (ringImage != null) ringImage.color = Color.white;
        UpdateStickFromEvent(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData.pointerId != activePointerId) return;
        UpdateStickFromEvent(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId) return;
        isDragging = false;
        activePointerId = -999;
        normalizedStick = Vector2.zero;
        jumpTriggeredOnCurrentPush = false;
        if (ringImage != null) ringImage.color = new Color(1f, 1f, 1f, 0.88f);

        EnsurePlayer();
        if (player != null)
        {
            player.SetMobileMoveInput(0f);
        }
    }

    private void UpdateStickFromEvent(PointerEventData eventData)
    {
        if (ringRect == null) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                ringRect,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        Vector2 clamped = Vector2.ClampMagnitude(localPoint, maxRadius);
        currentKnobPos = clamped;
        if (knobRect != null)
        {
            knobRect.anchoredPosition = currentKnobPos;
        }

        normalizedStick = clamped / Mathf.Max(1f, maxRadius);
        ApplyStickToPlayer();
    }

    private void ApplyStickToPlayer()
    {
        EnsurePlayer();
        if (player == null) return;

        // 1. Orantılı yatay hareket (ölü bölge + tam hıza yumuşak geçiş)
        float absX = Mathf.Abs(normalizedStick.x);
        float moveX = 0f;
        if (absX > horizontalDeadZone)
        {
            float t = Mathf.InverseLerp(horizontalDeadZone, fullSpeedThreshold, absX);
            moveX = Mathf.Sign(normalizedStick.x) * t;
        }
        player.SetMobileMoveInput(moveX);

        // 2. Yukarı / çapraz yukarı itişte zıplama
        if (normalizedStick.y >= jumpUpThreshold)
        {
            if (!jumpTriggeredOnCurrentPush)
            {
                jumpTriggeredOnCurrentPush = true;
                player.TriggerMobileJump();
            }
        }
        else if (normalizedStick.y < jumpUpThreshold * 0.65f)
        {
            jumpTriggeredOnCurrentPush = false;
        }
    }

    private void Update()
    {
        if (!isDragging)
        {
            if (currentKnobPos.sqrMagnitude > 0.05f)
            {
                currentKnobPos = Vector2.Lerp(currentKnobPos, Vector2.zero, Mathf.Clamp01(Time.unscaledDeltaTime * returnSmoothSpeed));
                if (knobRect != null) knobRect.anchoredPosition = currentKnobPos;
            }
            else if (currentKnobPos != Vector2.zero)
            {
                currentKnobPos = Vector2.zero;
                if (knobRect != null) knobRect.anchoredPosition = Vector2.zero;
            }
            return;
        }

        // Parmağı yukarı/çapraz-yukarı basılı tutarken yeni bir platforma inildiğinde akıcı şekilde tekrar zıplat
        if (normalizedStick.y >= 0.46f && player != null && player.IsGrounded)
        {
            player.TriggerMobileJump();
        }
    }

    private static Sprite GetOrCreateRingSprite()
    {
        if (cachedRingSprite != null) return cachedRingSprite;

        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] cols = new Color[size * size];
        float half = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float ny = (y - half) / half;
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;
                float r = Mathf.Sqrt(nx * nx + ny * ny);

                if (r > 0.98f)
                {
                    cols[y * size + x] = Color.clear;
                    continue;
                }

                // İnce yarı saydam dış çember (r = 0.895 .. 0.955) + çok hafif iç dolgu
                float outerEdge = Mathf.Clamp01((0.96f - r) / 0.022f);
                float ringBand = Mathf.Clamp01(1f - Mathf.Abs(r - 0.922f) / 0.032f);
                float innerFill = r < 0.90f ? 0.075f : 0f;

                float alpha = Mathf.Clamp01(innerFill + ringBand * 0.30f) * outerEdge;
                cols[y * size + x] = new Color(0.66f, 0.77f, 0.94f, alpha);
            }
        }

        tex.SetPixels(cols);
        tex.Apply(false);
        cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cachedRingSprite;
    }

    private static Sprite GetOrCreateKnobSprite()
    {
        if (cachedKnobSprite != null) return cachedKnobSprite;

        const int size = 192;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] cols = new Color[size * size];
        float half = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float ny = (y - half) / half;
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;

                // Hafif alt gölge
                float shadowDist = Mathf.Sqrt(nx * nx + (ny + 0.055f) * (ny + 0.055f));
                float shadowAlpha = Mathf.Clamp01((0.95f - shadowDist) / 0.09f) * 0.28f;

                float r = Mathf.Sqrt(nx * nx + ny * ny);
                if (r > 0.91f)
                {
                    cols[y * size + x] = shadowAlpha > 0.002f
                        ? new Color(0.04f, 0.07f, 0.16f, shadowAlpha)
                        : Color.clear;
                    continue;
                }

                float edgeAA = Mathf.Clamp01((0.91f - r) / 0.025f);

                Color knobCol;
                if (r >= 0.72f)
                {
                    // Dış 3B yuvarlatılmış çerçeve (üstte parlak beyaz, altta gümüş-mavi gölge)
                    float bevelLight = Mathf.Clamp01(0.58f + ny * 0.38f - nx * 0.14f);
                    knobCol = Color.Lerp(
                        new Color(0.73f, 0.78f, 0.87f, 1f),
                        new Color(1.00f, 1.00f, 1.00f, 1f),
                        bevelLight);
                }
                else
                {
                    // İçbükey (concave) başparmak yuvası: sol-üst hafif gümüş gölge, sağ-alt parlak beyaz
                    float dishLight = Mathf.Clamp01(0.52f - ny * 0.34f + nx * 0.18f + (r / 0.72f) * 0.14f);
                    knobCol = Color.Lerp(
                        new Color(0.84f, 0.88f, 0.94f, 1f),
                        new Color(0.99f, 0.995f, 1.00f, 1f),
                        dishLight);
                }

                // Kenar yumuşatmasıyla alt gölgeyi birleştir
                Color finalCol = Color.Lerp(new Color(0.04f, 0.07f, 0.16f, shadowAlpha), knobCol, edgeAA);
                finalCol.a = Mathf.Max(shadowAlpha, edgeAA);
                cols[y * size + x] = finalCol;
            }
        }

        tex.SetPixels(cols);
        tex.Apply(false);
        cachedKnobSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cachedKnobSprite;
    }
}
