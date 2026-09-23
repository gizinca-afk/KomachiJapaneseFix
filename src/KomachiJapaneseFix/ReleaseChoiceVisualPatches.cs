using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Tokens;

namespace KomachiJapaneseFix;

public static class KomachiReleaseChoiceVisuals
{
    public const string NonReleaseTitle = "解放しない";
    public const string NonReleaseAssetName = "release_none.png";

    private static Texture2D? _nonReleaseTexture;

    public static bool IsNonReleaseChoice(int altDescription) => altDescription == 0;

    public static string? GetChoiceTitle(int altDescription, string? language) =>
        language == "jpn" && IsNonReleaseChoice(altDescription)
            ? NonReleaseTitle
            : null;

    public static string? GetChoiceAssetName(int altDescription) =>
        IsNonReleaseChoice(altDescription) ? NonReleaseAssetName : null;

    internal static bool TryGetPortrait(CardModel card, out Texture2D? texture)
    {
        if (card is not ReleaseToken token || !IsNonReleaseChoice(token.AltDescription))
        {
            texture = null;
            return false;
        }

        if (_nonReleaseTexture is null || !GodotObject.IsInstanceValid(_nonReleaseTexture))
        {
            _nonReleaseTexture = LoadTexture(NonReleaseAssetName);
        }

        texture = _nonReleaseTexture;
        return texture is not null;
    }

    private static Texture2D? LoadTexture(string assetName)
    {
        Assembly assembly = typeof(KomachiReleaseChoiceVisuals).Assembly;
        string resourceName = $"KomachiJapaneseFix.Assets.{assetName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        using Image image = new();
        return image.LoadPngFromBuffer(bytes) == Error.Ok
            ? ImageTexture.CreateFromImage(image)
            : null;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Title), MethodType.Getter)]
internal static class KomachiReleaseChoiceTitlePatch
{
    [HarmonyPostfix]
    private static void ShowNonReleaseName(CardModel __instance, ref string __result)
    {
        if (__instance is ReleaseToken token
            && KomachiReleaseChoiceVisuals.GetChoiceTitle(
                token.AltDescription,
                LocManager.Instance?.Language) is { } title)
        {
            __result = title;
        }
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Portrait), MethodType.Getter)]
internal static class KomachiReleaseChoicePortraitPatch
{
    [HarmonyPostfix]
    private static void ShowNonReleaseArt(CardModel __instance, ref Texture2D __result)
    {
        if (KomachiReleaseChoiceVisuals.TryGetPortrait(__instance, out Texture2D? texture)
            && texture is not null)
        {
            __result = texture;
        }
    }
}
