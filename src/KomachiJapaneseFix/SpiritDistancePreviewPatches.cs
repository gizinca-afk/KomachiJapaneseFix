using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Patches;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Distance;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Abilities;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Spirits;

namespace KomachiJapaneseFix;

public readonly record struct KomachiSpiritDistanceDamage(
    Creature Target,
    int CurrentLevel,
    IReadOnlyDictionary<int, decimal> DamageByLevel);

public static class KomachiSpiritDistancePreview
{
    private readonly record struct DamageSource(Creature Target, Creature? Dealer, decimal BaseDamage);

    public static Color GetPreviewColor(bool isCurrentDistance) =>
        KomachiAttackDistancePreview.GetPreviewColor(isCurrentDistance);

    // Resolve private fields only when a target has no DistancePower. An API
    // change here must not break hovering unrelated power icons.
    private static class PreviewFields
    {
        internal static readonly FieldInfo PowerOwner =
            AccessTools.Field(typeof(PowerModel), "_owner")
            ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_owner");

        internal static readonly FieldInfo PowerAmount =
            AccessTools.Field(typeof(PowerModel), "_amount")
            ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_amount");

        internal static readonly FieldInfo CreaturePowers =
            AccessTools.Field(typeof(Creature), "_powers")
            ?? throw new MissingFieldException(typeof(Creature).FullName, "_powers");
    }

    public static bool IsSupportedPower(PowerModel? power) =>
        power is GuidedSpiritPower or VengefulSpiritPower;

    public static IReadOnlyDictionary<int, decimal> BuildDisplayedDamageByLevel(
        Func<int, decimal> rawDamageAtLevel)
    {
        Dictionary<int, decimal> byLevel = new(5);
        for (int level = DistancePower.MinLevel; level <= DistancePower.MaxLevel; level++)
        {
            byLevel[level] = KomachiSpiritDamagePreview.ToDisplayedDamage(
                rawDamageAtLevel(level));
        }

        return byLevel;
    }

    public static KomachiSpiritDistanceDamage? Preview(PowerModel power)
    {
        if (!TryGetDamageSource(power, out DamageSource source))
        {
            return null;
        }

        Creature target = source.Target;
        int currentLevel = Math.Clamp(
            DistancePower.GetLevel(target), DistancePower.MinLevel, DistancePower.MaxLevel);
        DistancePower? distance = target.GetPower<DistancePower>();
        List<PowerModel>? powers = null;
        decimal actualDefaultDamage = 0m;

        if (distance is null)
        {
            // With no DistancePower the real damage is calculated at the
            // implicit level 3. Keep that exact result for the highlighted
            // column; only the hypothetical levels need a temporary power.
            actualDefaultDamage = CalculateRawDamage(source);
            distance = (DistancePower)ModelDb.Power<DistancePower>().ToMutable();
            PreviewFields.PowerOwner.SetValue(distance, target);
            PreviewFields.PowerAmount.SetValue(distance, DistancePower.DefaultLevel);
            powers = PreviewFields.CreaturePowers.GetValue(target) as List<PowerModel>
                ?? throw new InvalidOperationException("Creature power storage is unavailable.");
            powers.Add(distance);
        }

        DistancePower previewDistance = distance;
        int? previousOverride = previewDistance.PreviewAmountOverride;
        IReadOnlyDictionary<int, decimal> byLevel;
        try
        {
            byLevel = BuildDisplayedDamageByLevel(level =>
            {
                if (powers is not null && level == DistancePower.DefaultLevel)
                {
                    return actualDefaultDamage;
                }

                previewDistance.PreviewAmountOverride = level;
                return CalculateRawDamage(source);
            });
        }
        finally
        {
            previewDistance.PreviewAmountOverride = previousOverride;
            powers?.Remove(previewDistance);
        }

        return new KomachiSpiritDistanceDamage(target, currentLevel, byLevel);
    }

    private static bool TryGetDamageSource(PowerModel power, out DamageSource source)
    {
        switch (power)
        {
            case GuidedSpiritPower guided
                when guided.Amount > 0 && guided.DamageTarget is { CurrentHp: > 0 } guidedTarget:
                source = new DamageSource(guidedTarget, guided.Owner, guided.BaseDamage);
                return true;

            case VengefulSpiritPower vengeful
                when vengeful.Amount > 0 && vengeful.DamageTarget is { CurrentHp: > 0 } vengefulTarget:
                source = new DamageSource(
                    vengefulTarget,
                    vengeful.Applier,
                    (vengeful.Amount + vengeful.PendingStacksPreview) * 2m);
                return true;

            default:
                source = default;
                return false;
        }
    }

    private static decimal CalculateRawDamage(DamageSource source)
    {
        decimal damage = source.BaseDamage;
        Creature? dealer = source.Dealer;
        if (dealer?.CombatState is { } combatState
            && dealer.Player?.RunState is { } runState)
        {
            // This is the same hook, ValueProp and preview mode used by the
            // original powers' KomachiHelpers.FindDamageDealt calculation.
            damage = Hook.ModifyDamage(
                runState,
                combatState,
                source.Target,
                dealer,
                source.BaseDamage,
                ValueProp.Move,
                null,
                null,
                ModifyDamageHookType.All,
                CardPreviewMode.None,
                out IEnumerable<AbstractModel> _);
        }

        return damage;
    }
}

internal static class KomachiSpiritDistanceHoverController
{
    private sealed class HoverState(NPower node)
    {
        internal Action<CombatState> OnStateChanged { get; } = _ => Refresh(node);
        internal NEnemyIntentDistanceCluster? Cluster { get; set; }
    }

    private static readonly Dictionary<NPower, HoverState> Active = [];
    private static bool _combatEndedSubscribed;

    internal static void Show(NPower node)
    {
        if (!KomachiSpiritDistancePreview.IsSupportedPower(node.Model)
            || NCombatRoom.Instance is null)
        {
            return;
        }

        if (!Active.TryGetValue(node, out HoverState? state))
        {
            state = new HoverState(node);
            Active.Add(node, state);
            CombatManager.Instance.StateTracker.CombatStateChanged += state.OnStateChanged;
            if (!_combatEndedSubscribed)
            {
                CombatManager.Instance.CombatEnded += OnCombatEnded;
                _combatEndedSubscribed = true;
            }
        }

        Refresh(node);
    }

    internal static void Hide(NPower node)
    {
        if (!Active.Remove(node, out HoverState? state))
        {
            return;
        }

        CombatManager.Instance.StateTracker.CombatStateChanged -= state.OnStateChanged;
        ClearCluster(state);
        if (Active.Count == 0 && _combatEndedSubscribed)
        {
            CombatManager.Instance.CombatEnded -= OnCombatEnded;
            _combatEndedSubscribed = false;
        }
    }

    private static void OnCombatEnded(CombatRoom _)
    {
        foreach (NPower node in Active.Keys.ToArray())
        {
            Hide(node);
        }
    }

    private static void Refresh(NPower node)
    {
        if (!Active.TryGetValue(node, out HoverState? state))
        {
            return;
        }

        ClearCluster(state);
        if (!GodotObject.IsInstanceValid(node)
            || !node.IsInsideTree()
            || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        try
        {
            if (KomachiSpiritDistancePreview.Preview(node.Model) is not { } preview
                || room.GetCreatureNode(preview.Target) is not { } targetNode)
            {
                return;
            }

            state.Cluster = KomachiDistancePreviewOverlay.AddAboveTarget(
                targetNode, preview.DamageByLevel, preview.CurrentLevel);
            foreach (Label label in state.Cluster.GetChildren().OfType<Label>())
            {
                label.Modulate = KomachiSpiritDistancePreview.GetPreviewColor(
                    label.Modulate == StsColors.red);
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"[KomachiJapaneseFix] Spirit distance preview failed: {ex}");
            Hide(node);
        }
    }

    private static void ClearCluster(HoverState state)
    {
        if (state.Cluster is { } cluster && GodotObject.IsInstanceValid(cluster))
        {
            cluster.QueueFree();
        }

        state.Cluster = null;
    }

}

// The original mod draws its third damage amount in red on the power icon.
// Its floating label may be reparented while raised, so use the controller's
// model-keyed label rather than searching only under the NPower node.
[HarmonyPatch]
internal static class KomachiSpiritDamageIconColorPatch
{
    private static readonly Type ControllerType =
        typeof(GuidedSpiritPower).Assembly.GetType(
            "STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Patches.PowerPatches.ThirdAmountFloatingLabelController",
            throwOnError: true)!;

    private static readonly FieldInfo LabelsField =
        AccessTools.Field(ControllerType, "_labels")
        ?? throw new MissingFieldException(ControllerType.FullName, "_labels");

    [HarmonyTargetMethod]
    private static MethodBase TargetMethod() =>
        AccessTools.Method(ControllerType, "Refresh")
        ?? throw new MissingMethodException(ControllerType.FullName, "Refresh");

    [HarmonyPostfix]
    private static void Postfix(NPower powerNode)
    {
        if (powerNode.Model is not (GuidedSpiritPower or VengefulSpiritPower or LonelyBoundSpiritPower)
            || LabelsField.GetValue(null) is not IDictionary labels
            || labels[powerNode.Model] is not MegaLabel label
            || !GodotObject.IsInstanceValid(label))
        {
            return;
        }

        label.AddThemeColorOverride(ThemeConstants.Label.FontColor,
            KomachiSpiritDistancePreview.GetPreviewColor(isCurrentDistance: true));
    }
}

[HarmonyPatch(typeof(NPower), "OnHovered")]
internal static class KomachiSpiritDistanceHoverEnterPatch
{
    [HarmonyPostfix]
    private static void Postfix(NPower __instance) =>
        KomachiSpiritDistanceHoverController.Show(__instance);
}

[HarmonyPatch(typeof(NPower), "OnUnhovered")]
internal static class KomachiSpiritDistanceHoverExitPatch
{
    [HarmonyPostfix]
    private static void Postfix(NPower __instance) =>
        KomachiSpiritDistanceHoverController.Hide(__instance);
}

[HarmonyPatch(typeof(NPower), "OnPowerRemoved")]
internal static class KomachiSpiritDistancePowerRemovedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NPower __instance) =>
        KomachiSpiritDistanceHoverController.Hide(__instance);
}

[HarmonyPatch(typeof(NPower), "_ExitTree")]
internal static class KomachiSpiritDistanceNodeExitPatch
{
    [HarmonyPostfix]
    private static void Postfix(NPower __instance) =>
        KomachiSpiritDistanceHoverController.Hide(__instance);
}
