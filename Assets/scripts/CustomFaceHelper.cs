using UnityEngine;

/// <summary>
/// Selfie fotoğrafını oval portreye kırpar, ince çerçeve ekler ve karakterin kafasına
/// "Koca Kafa" (Big Head) stiliyle orantılı şekilde uygular.
/// </summary>
public static class CustomFaceHelper
{
    private const string OverlayObjectName = "CustomSelfieFaceOverlay";

    public static Texture2D CaptureOvalSelfie(WebCamTexture camTex, bool isFrontFacing)
    {
        if (camTex == null || camTex.width <= 16 || camTex.height <= 16)
            return null;

        int srcW = camTex.width;
        int srcH = camTex.height;
        Color32[] srcPixels = camTex.GetPixels32();

        int rotAngle = ((camTex.videoRotationAngle % 360) + 360) % 360;
        bool vertMirror = camTex.videoVerticallyMirrored;

        bool swapWH = (rotAngle == 90 || rotAngle == 270);
        int effW = swapWH ? srcH : srcW;
        int effH = swapWH ? srcW : srcH;

        float cropSize = Mathf.Min(effW, effH) * 0.86f;
        float cropOriginX = (effW - cropSize) * 0.5f;
        float cropOriginY = (effH - cropSize) * 0.5f;

        const int outSize = 256;
        Texture2D result = new Texture2D(outSize, outSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] outPixels = new Color32[outSize * outSize];
        Color32 borderDark = new Color32(48, 26, 38, 255);
        Color32 borderLight = new Color32(255, 248, 240, 255);

        float half = (outSize - 1) * 0.5f;
        const float rx = 0.84f;
        const float ry = 0.94f;

        for (int y = 0; y < outSize; y++)
        {
            float ny = (y - half) / half;
            float v = (float)y / (outSize - 1);

            for (int x = 0; x < outSize; x++)
            {
                float nx = (x - half) / half;
                float ovalDist = Mathf.Sqrt((nx * nx) / (rx * rx) + (ny * ny) / (ry * ry));

                if (ovalDist > 1.0f)
                {
                    outPixels[y * outSize + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                if (ovalDist > 0.92f)
                {
                    byte alpha = (ovalDist > 0.975f)
                        ? (byte)Mathf.RoundToInt(Mathf.Clamp01((1.0f - ovalDist) / 0.025f) * 255f)
                        : (byte)255;
                    outPixels[y * outSize + x] = new Color32(borderDark.r, borderDark.g, borderDark.b, alpha);
                    continue;
                }

                if (ovalDist > 0.885f)
                {
                    outPixels[y * outSize + x] = borderLight;
                    continue;
                }

                float u = (float)x / (outSize - 1);
                float sampleU = isFrontFacing ? (1f - u) : u;
                float sampleV = vertMirror ? (1f - v) : v;

                float ex = cropOriginX + sampleU * cropSize;
                float ey = cropOriginY + sampleV * cropSize;

                float sx, sy;
                switch (rotAngle)
                {
                    case 90:
                        sx = effH - 1f - ey;
                        sy = ex;
                        break;
                    case 180:
                        sx = effW - 1f - ex;
                        sy = effH - 1f - ey;
                        break;
                    case 270:
                        sx = ey;
                        sy = effW - 1f - ex;
                        break;
                    default:
                        sx = ex;
                        sy = ey;
                        break;
                }

                int ix = Mathf.Clamp(Mathf.RoundToInt(sx), 0, srcW - 1);
                int iy = Mathf.Clamp(Mathf.RoundToInt(sy), 0, srcH - 1);

                Color32 px = srcPixels[iy * srcW + ix];
                px.a = 255;
                outPixels[y * outSize + x] = px;
            }
        }

        result.SetPixels32(outPixels);
        result.Apply(false);
        return result;
    }

    public static void ApplyBigHeadToPlayer(Transform playerRoot, string characterId)
    {
        if (playerRoot == null) return;

        Transform existing = playerRoot.Find(OverlayObjectName);
        bool enabled = CharacterShopData.IsCustomFaceEnabled();
        Sprite faceSprite = enabled ? CharacterShopData.LoadCustomFaceSprite() : null;

        if (!enabled || faceSprite == null)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }

        GameObject overlayObj;
        if (existing == null)
        {
            overlayObj = new GameObject(OverlayObjectName);
            overlayObj.transform.SetParent(playerRoot, false);
        }
        else
        {
            overlayObj = existing.gameObject;
            overlayObj.SetActive(true);
        }

        Vector3 headOffset = GetCharacterHeadLocalOffset(characterId);
        overlayObj.transform.localPosition = headOffset;
        overlayObj.transform.localRotation = Quaternion.identity;

        Vector3 pScale = playerRoot.localScale;
        float absX = Mathf.Max(0.01f, Mathf.Abs(pScale.x));
        float absY = Mathf.Max(0.01f, Mathf.Abs(pScale.y));
        const float targetWorldHeadSize = 0.46f;
        overlayObj.transform.localScale = new Vector3(
            targetWorldHeadSize / absX,
            targetWorldHeadSize / absY,
            1f);

        SpriteRenderer sr = overlayObj.GetComponent<SpriteRenderer>();
        if (sr == null) sr = overlayObj.AddComponent<SpriteRenderer>();
        sr.sprite = faceSprite;
        sr.color = Color.white;

        SpriteRenderer parentSr = playerRoot.GetComponent<SpriteRenderer>();
        if (parentSr != null)
        {
            sr.sortingLayerID = parentSr.sortingLayerID;
            sr.sortingOrder = parentSr.sortingOrder + 1;
        }
        else
        {
            sr.sortingOrder = 10;
        }

        SelfieHeadWobble wobble = overlayObj.GetComponent<SelfieHeadWobble>();
        if (wobble == null) wobble = overlayObj.AddComponent<SelfieHeadWobble>();
        wobble.SetBaseLocalPosition(headOffset);
    }

    public static Vector3 GetCharacterHeadLocalOffset(string characterId)
    {
        switch (characterId)
        {
            case CharacterShopData.NinjaFrogId:
                return new Vector3(0f, 0.088f, 0f);
            case CharacterShopData.MaskDudeId:
                return new Vector3(0f, 0.092f, 0f);
            case CharacterShopData.VirtualGuyId:
                return new Vector3(0f, 0.092f, 0f);
            case CharacterShopData.PinkManId:
            default:
                return new Vector3(0f, 0.090f, 0f);
        }
    }

    public static Sprite CreateOvalGuideSprite()
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color32[] cols = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        const float rx = 0.84f;
        const float ry = 0.94f;

        for (int y = 0; y < size; y++)
        {
            float ny = (y - half) / half;
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;
                float d = Mathf.Sqrt((nx * nx) / (rx * rx) + (ny * ny) / (ry * ry));
                if (d > 1.0f)
                {
                    cols[y * size + x] = new Color32(18, 12, 28, 145);
                }
                else if (d > 0.955f)
                {
                    cols[y * size + x] = new Color32(255, 246, 175, 240);
                }
                else
                {
                    cols[y * size + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        tex.SetPixels32(cols);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    public static Sprite CreateCameraBadgeSprite()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        Color[] cols = new Color[size * size];

        Color dark = new Color(0.18f, 0.11f, 0.18f, 1f);
        Color body = new Color(1.0f, 0.45f, 0.72f, 1f);
        Color bodyLight = new Color(1.0f, 0.68f, 0.85f, 1f);
        Color lensRim = new Color(0.96f, 0.96f, 1.0f, 1f);
        Color lensBlue = new Color(0.26f, 0.76f, 0.98f, 1f);
        Color flashYellow = new Color(1.0f, 0.92f, 0.35f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Color px = Color.clear;
                bool inBody = (x >= 4 && x <= 27 && y >= 7 && y <= 22);
                bool inTop = (x >= 10 && x <= 21 && y >= 22 && y <= 25);

                if (inBody || inTop)
                {
                    bool isBorder = (inBody && (x == 4 || x == 27 || y == 7 || (y == 22 && (x < 10 || x > 21))))
                                 || (inTop && (x == 10 || x == 21 || y == 25));
                    px = isBorder ? dark : ((y >= 19 || inTop) ? bodyLight : body);
                }

                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 14.5f));
                if (dist <= 5.8f)
                {
                    if (dist > 4.7f) px = dark;
                    else if (dist > 3.6f) px = lensRim;
                    else px = lensBlue;

                    if (x >= 13 && x <= 14 && y >= 16 && y <= 17) px = Color.white;
                }

                if (x >= 22 && x <= 24 && y >= 18 && y <= 20) px = flashYellow;

                cols[y * size + x] = px;
            }
        }

        tex.SetPixels(cols);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
    }
}

public class SelfieHeadWobble : MonoBehaviour
{
    private Vector3 baseLocalPos;
    private Rigidbody2D parentRb;

    public void SetBaseLocalPosition(Vector3 pos)
    {
        baseLocalPos = pos;
    }

    private void Start()
    {
        parentRb = GetComponentInParent<Rigidbody2D>();
    }

    private void LateUpdate()
    {
        float bob = Mathf.Sin(Time.time * 6.5f) * 0.008f;
        transform.localPosition = new Vector3(baseLocalPos.x, baseLocalPos.y + bob, baseLocalPos.z);

        float tilt = 0f;
        if (parentRb != null)
        {
            tilt = Mathf.Clamp(-parentRb.linearVelocity.x * 1.4f, -7f, 7f);
            if (transform.parent != null && transform.parent.localScale.x < 0f)
            {
                tilt = -tilt;
            }
        }
        transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
    }
}
