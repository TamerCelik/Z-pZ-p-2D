using System;
using UnityEngine;

/// <summary>
/// Karakter mağazası kalıcı verilerini (toplam coin, açık kostümler, seçili karakter)
/// PlayerPrefs üzerinde yönetir ve katalog tanımlarını tek merkezde tutar.
/// </summary>
public static class CharacterShopData
{
    public const string TotalCoinsKey = "TotalCoins";
    public const string SelectedCharacterKey = "SelectedCharacterId";
    private const string UnlockedPrefix = "UnlockedChar_";

    public const string PinkManId = "pink_man";
    public const string NinjaFrogId = "ninja_frog";
    public const string MaskDudeId = "mask_dude";
    public const string VirtualGuyId = "virtual_guy";

    public readonly struct CharacterCatalogEntry
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly int Price;
        public readonly Color AccentColor;

        public CharacterCatalogEntry(string id, string displayName, int price, Color accentColor)
        {
            Id = id;
            DisplayName = displayName;
            Price = price;
            AccentColor = accentColor;
        }
    }

    public static readonly CharacterCatalogEntry[] Catalog = new CharacterCatalogEntry[]
    {
        new CharacterCatalogEntry(PinkManId,    "PINK MAN",    0,   new Color(1.00f, 0.45f, 0.68f, 1f)),
        new CharacterCatalogEntry(NinjaFrogId,  "NINJA FROG",  75,  new Color(0.28f, 0.85f, 0.48f, 1f)),
        new CharacterCatalogEntry(MaskDudeId,   "MASK DUDE",   175, new Color(0.96f, 0.62f, 0.14f, 1f)),
        new CharacterCatalogEntry(VirtualGuyId, "VIRTUAL GUY", 350, new Color(0.24f, 0.72f, 0.98f, 1f)),
    };

    public static event Action OnShopStateChanged;

    public static bool TryGetEntry(string characterId, out CharacterCatalogEntry entry)
    {
        for (int i = 0; i < Catalog.Length; i++)
        {
            if (Catalog[i].Id == characterId)
            {
                entry = Catalog[i];
                return true;
            }
        }
        entry = Catalog[0];
        return false;
    }

    public static int GetCoins()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(TotalCoinsKey, 0));
    }

    public static void AddCoins(int amount)
    {
        if (amount <= 0) return;
        int updated = GetCoins() + amount;
        PlayerPrefs.SetInt(TotalCoinsKey, updated);
        PlayerPrefs.Save();
        OnShopStateChanged?.Invoke();
    }

    public static bool IsUnlocked(string characterId)
    {
        if (TryGetEntry(characterId, out CharacterCatalogEntry entry) && entry.Price <= 0)
            return true;

        return PlayerPrefs.GetInt(UnlockedPrefix + characterId, 0) == 1;
    }

    public static string GetSelectedCharacterId()
    {
        string saved = PlayerPrefs.GetString(SelectedCharacterKey, PinkManId);
        if (!TryGetEntry(saved, out _) || !IsUnlocked(saved))
            return PinkManId;

        return saved;
    }

    public static bool SelectCharacter(string characterId)
    {
        if (!TryGetEntry(characterId, out _) || !IsUnlocked(characterId))
            return false;

        PlayerPrefs.SetString(SelectedCharacterKey, characterId);
        PlayerPrefs.Save();
        OnShopStateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Karakter açıksa seçer; kilitliyse bakiye yeterliyse satın alır ve otomatik seçer.
    /// </summary>
    public static bool TryBuyOrSelectCharacter(
        string characterId,
        out bool newlyBought,
        out bool insufficientFunds,
        out int remainingCoins)
    {
        newlyBought = false;
        insufficientFunds = false;
        remainingCoins = GetCoins();

        if (!TryGetEntry(characterId, out CharacterCatalogEntry entry))
            return false;

        if (IsUnlocked(characterId))
        {
            SelectCharacter(characterId);
            return true;
        }

        if (remainingCoins < entry.Price)
        {
            insufficientFunds = true;
            return false;
        }

        remainingCoins -= entry.Price;
        PlayerPrefs.SetInt(TotalCoinsKey, remainingCoins);
        PlayerPrefs.SetInt(UnlockedPrefix + entry.Id, 1);
        PlayerPrefs.SetString(SelectedCharacterKey, entry.Id);
        PlayerPrefs.Save();

        newlyBought = true;
        OnShopStateChanged?.Invoke();
        return true;
    }
}

