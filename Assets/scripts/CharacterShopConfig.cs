using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Mağaza, coin meyveleri ve oyun içi kostüm değişimi için gerekli sprite, animasyon klibi,
/// font ve ses varlıklarını Resources üzerinden paylaşan ScriptableObject yapılandırması.
/// </summary>
[CreateAssetMenu(fileName = "CharacterShopConfig", menuName = "ZipZip/Character Shop Config")]
public class CharacterShopConfig : ScriptableObject
{
    [Serializable]
    public class CharacterVisualEntry
    {
        public string id;
        public Sprite[] idleFrames;
        public AnimationClip idleClip;
    }

    private const string ResourcePath = "CharacterShopConfig";
    private static CharacterShopConfig cachedInstance;

    [Header("Karakter Görselleri")]
    public CharacterVisualEntry[] characters;

    [Header("Coin Meyve Animasyonları")]
    public Sprite[] cherryFrames;
    public Sprite[] pineappleFrames;
    public Sprite[] collectedFrames;

    [Header("Ses ve UI Varlıkları")]
    public AudioClip coinCollectSound;
    public TMP_FontAsset uiFont;
    public Sprite panelBgSprite;
    public Sprite cardBgSprite;
    public Sprite buttonPillSprite;
    public Sprite badgePillSprite;
    public Sprite backButtonSprite;
    public Sprite starIconSprite;

    public static CharacterShopConfig Load()
    {
        if (cachedInstance == null)
        {
            cachedInstance = Resources.Load<CharacterShopConfig>(ResourcePath);
        }
        return cachedInstance;
    }

    public CharacterVisualEntry GetVisualEntry(string characterId)
    {
        if (characters == null || characters.Length == 0)
            return null;

        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] != null && characters[i].id == characterId)
                return characters[i];
        }

        return characters[0];
    }
}

