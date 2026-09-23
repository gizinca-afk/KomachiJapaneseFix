using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Character;

namespace KomachiJapaneseFix;

// The upstream scene names front/back VFX slots, but BaseLib converts both empty
// Node2D placeholders into NParticlesContainer instances containing zero GPU
// particles. NEnergyCounter therefore receives the increase event but Restart()
// has nothing to play. Supply one visible red-magenta pulse matching Komachi's
// energy orb without duplicating the standard energy-gain sound.
public static class KomachiEnergyGainFeedback
{
    public const string FlashNodeName = "KomachiJapaneseFixEnergyGainFlash";

    public static Color FlashOuterColor => Color.FromHtml("ff3f72");
    public static Color FlashHighlightColor => Color.FromHtml("ffb1c4");
    public static Color FlashCoreColor => Color.FromHtml("ff5c85");

    private const string FlashSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"160\" height=\"160\" viewBox=\"0 0 160 160\">" +
        "<circle cx=\"80\" cy=\"80\" r=\"69\" fill=\"none\" stroke=\"#FF3F72\" stroke-width=\"7\" opacity=\"0.48\"/>" +
        "<circle cx=\"80\" cy=\"80\" r=\"61\" fill=\"none\" stroke=\"#FFB1C4\" stroke-width=\"8\" opacity=\"0.95\"/>" +
        "<circle cx=\"80\" cy=\"80\" r=\"52\" fill=\"#FF5C85\" opacity=\"0.24\"/>" +
        "<g fill=\"#FFB1C4\" opacity=\"0.88\">" +
        "<circle cx=\"80\" cy=\"5\" r=\"4\"/><circle cx=\"80\" cy=\"155\" r=\"4\"/>" +
        "<circle cx=\"5\" cy=\"80\" r=\"4\"/><circle cx=\"155\" cy=\"80\" r=\"4\"/>" +
        "<circle cx=\"27\" cy=\"27\" r=\"3\"/><circle cx=\"133\" cy=\"133\" r=\"3\"/>" +
        "<circle cx=\"133\" cy=\"27\" r=\"3\"/><circle cx=\"27\" cy=\"133\" r=\"3\"/>" +
        "</g></svg>";

    private static readonly FieldInfo PlayerField =
        AccessTools.Field(typeof(NEnergyCounter), "_player")
        ?? throw new MissingFieldException(typeof(NEnergyCounter).FullName, "_player");

    private static readonly Dictionary<ulong, Tween> ActiveTweens = [];
    private static Texture2D? _flashTexture;

    public static bool ShouldFlash(Type? characterType, int oldEnergy, int newEnergy) =>
        characterType is not null
        && typeof(Komachi_Character).IsAssignableFrom(characterType)
        && newEnergy > oldEnergy;

    internal static void Install(NEnergyCounter counter)
    {
        if (!GodotObject.IsInstanceValid(counter)
            || PlayerField.GetValue(counter) is not Player player
            || player.Character is not Komachi_Character
            || counter.GetNodeOrNull<TextureRect>(FlashNodeName) is not null)
        {
            return;
        }

        _flashTexture ??= CreateFlashTexture();
        if (_flashTexture is null)
        {
            return;
        }

        TextureRect flash = new()
        {
            Name = FlashNodeName,
            Texture = _flashTexture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 100,
            Visible = false,
        };
        counter.AddChild(flash);
        flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        ulong id = counter.GetInstanceId();
        counter.TreeExited += () =>
        {
            if (ActiveTweens.Remove(id, out Tween? tween)
                && GodotObject.IsInstanceValid(tween))
            {
                tween.Kill();
            }
        };
    }

    internal static void Flash(NEnergyCounter counter, int oldEnergy, int newEnergy)
    {
        Player? player = PlayerField.GetValue(counter) as Player;
        if (!ShouldFlash(player?.Character.GetType(), oldEnergy, newEnergy)
            || !GodotObject.IsInstanceValid(counter)
            || !counter.IsInsideTree())
        {
            return;
        }

        Install(counter);
        if (counter.GetNodeOrNull<TextureRect>(FlashNodeName) is not TextureRect flash
            || !GodotObject.IsInstanceValid(flash))
        {
            return;
        }

        ulong id = counter.GetInstanceId();
        if (ActiveTweens.Remove(id, out Tween? previous)
            && GodotObject.IsInstanceValid(previous))
        {
            previous.Kill();
        }

        flash.PivotOffset = flash.Size * 0.5f;
        flash.Scale = Vector2.One * 0.72f;
        flash.Modulate = Colors.White;
        flash.Visible = true;

        Tween tween = flash.CreateTween().SetParallel();
        ActiveTweens[id] = tween;
        tween.TweenProperty(flash, "scale", Vector2.One * 1.32f, 0.38)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "modulate:a", 0f, 0.46)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(flash))
            {
                flash.Visible = false;
            }
            ActiveTweens.Remove(id);
        }));
    }

    private static Texture2D? CreateFlashTexture()
    {
        using Image image = new();
        if (image.LoadSvgFromString(FlashSvg, 1f) != Error.Ok)
        {
            return null;
        }
        return ImageTexture.CreateFromImage(image);
    }
}

[HarmonyPatch(typeof(NEnergyCounter), nameof(NEnergyCounter._Ready))]
internal static class KomachiEnergyCounterReadyPatch
{
    [HarmonyPostfix]
    private static void AddEnergyGainFlash(NEnergyCounter __instance) =>
        KomachiEnergyGainFeedback.Install(__instance);
}

[HarmonyPatch(typeof(NEnergyCounter), "OnEnergyChanged")]
[HarmonyAfter("STS_Komachi_Onozuka")]
internal static class KomachiEnergyCounterChangedPatch
{
    [HarmonyPostfix]
    private static void PlayEnergyGainFlash(
        NEnergyCounter __instance,
        int oldEnergy,
        int newEnergy) =>
        KomachiEnergyGainFeedback.Flash(__instance, oldEnergy, newEnergy);
}
