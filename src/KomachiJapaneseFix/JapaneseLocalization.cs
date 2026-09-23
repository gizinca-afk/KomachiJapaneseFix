using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace KomachiJapaneseFix;

public static class JapaneseLocalization
{
    public static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Tables = Load();

    private static IReadOnlyDictionary<string, Dictionary<string, string>> Load()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var assembly = typeof(JapaneseLocalization).Assembly;
        const string prefix = "KomachiJapaneseFix.Localization.jpn.";
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)
                                 && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            result.Add(name[prefix.Length..^5],
                JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
        }
        return result;
    }

    public static bool TryGet(string table, string key, out string text)
    {
        text = string.Empty;
        return LocManager.Instance?.Language == "jpn"
               && Tables.TryGetValue(table, out var values)
               && values.TryGetValue(key, out text!);
    }
}

[HarmonyPatch(typeof(LocString), nameof(LocString.GetRawText))]
internal static class JapaneseRawTextPatch
{
    [HarmonyPrefix]
    private static bool Prefix(LocString __instance, ref string __result)
    {
        if (!JapaneseLocalization.TryGet(__instance.LocTable, __instance.LocEntryKey, out var text))
            return true;
        __result = text;
        return false;
    }
}

[HarmonyPatch]
internal static class JapaneseExistsPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(LocString)).Where(m => m.Name == nameof(LocString.Exists));

    [HarmonyPrefix]
    private static bool Prefix(LocString? __instance, object[] __args, ref bool __result)
    {
        var table = __instance?.LocTable ?? (__args.Length > 0 ? __args[0] as string : null);
        var key = __instance?.LocEntryKey ?? (__args.Length > 1 ? __args[1] as string : null);
        if (table is null || key is null || !JapaneseLocalization.TryGet(table, key, out _))
            return true;
        __result = true;
        return false;
    }
}
