using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// LuckiestGuy-Regular.ttf kaynağından çalışma zamanında gerçek Dynamic SDF fallback fontu üretir
/// ve tüm sahnedeki TMP font varlıklarının (LuckiestGuy, yeni, para vb.) 0. sırasına ekler.
/// Böylece Ç, Ğ, İ, Ö, Ş, Ü, ç, ğ, ı, ö, ş, ü harfleri LiberationSans'a düşmeden
/// birebir aynı LuckiestGuy yazı tipi ve dış çizgi stiliyle görünür.
/// </summary>
public static class TurkishFontBootstrap
{
    private const string TurkishChars = "ÇĞİÖŞÜçğıöşü•…—";
    private static TMP_FontAsset dynamicTurkishFont;
    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitBeforeScene()
    {
        EnsureTurkishFallback();
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitAfterScene()
    {
        EnsureTurkishFallback();
        AttachToAllLoadedFonts();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureTurkishFallback();
        AttachToAllLoadedFonts();
    }

    public static TMP_FontAsset EnsureTurkishFallback()
    {
        if (dynamicTurkishFont != null) return dynamicTurkishFont;

        Font sourceTtf = Resources.Load<Font>("Fonts/LuckiestGuy-Regular");
        if (sourceTtf == null) return null;

        dynamicTurkishFont = TMP_FontAsset.CreateFontAsset(
            sourceTtf,
            64,
            7,
            GlyphRenderMode.SDFAA,
            512,
            512,
            AtlasPopulationMode.Dynamic,
            true);

        if (dynamicTurkishFont == null) return null;

        dynamicTurkishFont.name = "LuckiestGuy_Turkish_Dynamic_SDF";
        dynamicTurkishFont.isMultiAtlasTexturesEnabled = true;
        dynamicTurkishFont.TryAddCharacters(TurkishChars, out _);
        Object.DontDestroyOnLoad(dynamicTurkishFont);

        if (TMP_Settings.fallbackFontAssets != null)
        {
            TMP_Settings.fallbackFontAssets.Remove(dynamicTurkishFont);
            TMP_Settings.fallbackFontAssets.Insert(0, dynamicTurkishFont);
        }

        return dynamicTurkishFont;
    }

    public static void AttachToAllLoadedFonts()
    {
        TMP_FontAsset fallback = EnsureTurkishFallback();
        if (fallback == null) return;

        TMP_FontAsset[] allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        for (int i = 0; i < allFonts.Length; i++)
        {
            TMP_FontAsset f = allFonts[i];
            if (f == null || f == fallback) continue;

            if (f.fallbackFontAssetTable == null)
            {
                f.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }
            if (!f.fallbackFontAssetTable.Contains(fallback))
            {
                f.fallbackFontAssetTable.Insert(0, fallback);
            }
        }

        TMP_Text[] allTexts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allTexts.Length; i++)
        {
            if (allTexts[i] != null)
            {
                allTexts[i].ForceMeshUpdate(true, true);
            }
        }
    }
}
