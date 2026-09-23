using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace KomachiJapaneseFix;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string Version = "0.2.16";

    public static void Initialize()
    {
        new Harmony("local.KomachiJapaneseFix").PatchAll(typeof(MainFile).Assembly);
        Log.Info($"[KomachiJapaneseFix] {Version} initialized; original Workshop files unchanged.");
    }
}
