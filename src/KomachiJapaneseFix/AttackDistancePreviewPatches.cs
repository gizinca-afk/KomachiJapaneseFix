using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Attack;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Patches;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Distance;

namespace KomachiJapaneseFix;

public readonly record struct KomachiAttackDistanceDamage(
    int CurrentLevel,
    IReadOnlyDictionary<int, decimal> DamageByLevel);

public static class KomachiAttackDistancePreview
{
    public static Color GetPreviewColor(bool isCurrentDistance) =>
        Color.FromHtml(isCurrentDistance ? "ff3b9d" : "ffa4d6");

    public static bool IsSupportedTargetType(TargetType targetType) =>
        targetType is TargetType.AnyEnemy or TargetType.AllEnemies;

    public static int GetDamageDistanceLevel(int initialLevel, int displacement) =>
        Math.Clamp(initialLevel + displacement, DistancePower.MinLevel, DistancePower.MaxLevel);

    private static readonly FieldInfo PowerOwnerField =
        AccessTools.Field(typeof(PowerModel), "_owner")
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_owner");

    private static readonly FieldInfo PowerAmountField =
        AccessTools.Field(typeof(PowerModel), "_amount")
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_amount");

    private static readonly FieldInfo CreaturePowersField =
        AccessTools.Field(typeof(Creature), "_powers")
        ?? throw new MissingFieldException(typeof(Creature).FullName, "_powers");

    public static bool IsSupportedCard(CardModel? card) =>
        card is STS_Komachi_OnozukaCard
        && card.Type == CardType.Attack
        && IsSupportedTargetType(card.TargetType)
        && card.Pile?.Type == PileType.Hand
        && card.CombatState is not null
        && GetDamageVar(card) is not null;

    public static KomachiAttackDistanceDamage? Preview(CardModel card, Creature target)
    {
        if (!IsSupportedCard(card)
            || (card.TargetType == TargetType.AnyEnemy
                ? !card.IsValidTarget(target)
                : !card.CombatState!.HittableEnemies.Contains(target)))
        {
            return null;
        }

        DynamicVarSet vars = card.DynamicVars;
        DynamicVar damageVar = GetDamageVar(card)!;
        int currentLevel = Math.Clamp(
            DistancePower.GetLevel(target), DistancePower.MinLevel, DistancePower.MaxLevel);
        // Sweep moves every enemy before its attack. The labels still describe
        // the enemy's starting distance, while the damage uses the new one.
        int preAttackDisplacement = card is Sweep sweep ? sweep.Value1 : 0;
        DistancePower? distance = target.GetPower<DistancePower>();
        List<PowerModel>? powers = null;
        decimal actualDefaultDamage = 0m;

        if (distance is null)
        {
            // A target without DistancePower uses the implicit level 3. Keep
            // its real preview at level 3, and attach a silent temporary power
            // only while calculating the other four hypothetical levels.
            RefreshCardPreview(card, target, vars);
            actualDefaultDamage = damageVar.PreviewValue;
            distance = (DistancePower)ModelDb.Power<DistancePower>().ToMutable();
            PowerOwnerField.SetValue(distance, target);
            PowerAmountField.SetValue(distance, DistancePower.DefaultLevel);
            powers = CreaturePowersField.GetValue(target) as List<PowerModel>
                ?? throw new InvalidOperationException("Creature power storage is unavailable.");
            powers.Add(distance);
        }

        DistancePower previewDistance = distance;
        int? previousOverride = previewDistance.PreviewAmountOverride;
        Dictionary<int, decimal> byLevel = new(5);
        try
        {
            for (int level = DistancePower.MinLevel; level <= DistancePower.MaxLevel; level++)
            {
                int damageLevel = GetDamageDistanceLevel(level, preAttackDisplacement);
                if (powers is not null && damageLevel == DistancePower.DefaultLevel)
                {
                    byLevel[level] = KomachiSpiritDamagePreview.ToDisplayedDamage(actualDefaultDamage);
                    continue;
                }

                previewDistance.PreviewAmountOverride = damageLevel;
                RefreshCardPreview(card, target, vars);
                byLevel[level] = KomachiSpiritDamagePreview.ToDisplayedDamage(damageVar.PreviewValue);
            }
        }
        finally
        {
            previewDistance.PreviewAmountOverride = previousOverride;
            powers?.Remove(previewDistance);
            // Leave the card's text showing the real target and distance.
            RefreshCardPreview(card, target, vars);
        }

        return byLevel.Values.Distinct().Count() > 1
            ? new KomachiAttackDistanceDamage(currentLevel, byLevel)
            : null;
    }

    private static void RefreshCardPreview(CardModel card, Creature target, DynamicVarSet vars)
    {
        vars.ClearPreview();
        card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, vars);
    }

    private static DynamicVar? GetDamageVar(CardModel card)
    {
        if (card.DynamicVars.TryGetValue("Damage", out DynamicVar? damage)
            && damage is DamageVar)
        {
            return damage;
        }

        // Roaming Spirits always attacks with its calculated damage. The
        // other calculated-only card attacks only after an optional release.
        return card is ScytheOfRoamingSpirits
            && card.DynamicVars.TryGetValue("CalculatedDamage", out DynamicVar? calculated)
            && calculated is CalculatedDamageVar
                ? calculated
                : null;
    }
}

internal static class KomachiDistancePreviewOverlay
{
    internal static NEnemyIntentDistanceCluster AddAboveTarget(
        NCreature targetNode,
        IReadOnlyDictionary<int, decimal> damageByLevel,
        int currentLevel)
    {
        NEnemyIntentDistanceCluster cluster = NEnemyIntentDistanceCluster.Create(
            damageByLevel, currentLevel);
        cluster.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (Control child in cluster.GetChildren().OfType<Control>())
        {
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        targetNode.AddChild(cluster);
        Vector2 intentPosition = targetNode.IntentContainer.Position;
        Vector2 intentSize = targetNode.IntentContainer.Size;
        float top = intentPosition.Y;
        foreach (NEnemyIntentDistanceCluster existing in
                 targetNode.GetChildren().OfType<NEnemyIntentDistanceCluster>())
        {
            if (!ReferenceEquals(existing, cluster)
                && GodotObject.IsInstanceValid(existing)
                && !existing.IsQueuedForDeletion())
            {
                top = Math.Min(top, existing.Position.Y);
            }
        }

        cluster.Position = new Vector2(
            intentPosition.X + intentSize.X * 0.5f - cluster.Size.X * 0.5f,
            top - cluster.Size.Y - 12f);
        return cluster;
    }
}

internal static class KomachiAttackDistancePreviewController
{
    private sealed class PreviewState(NCard card)
    {
        internal Creature? Target { get; set; }
        internal bool IsMultiTarget { get; set; }
        internal Action<CombatState> OnStateChanged { get; } = _ => Refresh(card);
        internal List<NEnemyIntentDistanceCluster> Clusters { get; } = [];
    }

    private static readonly Dictionary<NCard, PreviewState> Active = [];
    private static bool _combatEndedSubscribed;

    internal static void OnPreviewTargetChanged(NCard card, Creature? target)
    {
        // All-enemy cards are driven by Show/HideMultiCreatureTargetingVisuals.
        // With one enemy, the game also sets a normal preview target; ignore it
        // so that it cannot create a duplicate row or clear the multi preview.
        if (card.Model?.TargetType == TargetType.AllEnemies)
        {
            return;
        }

        if (target is null || !KomachiAttackDistancePreview.IsSupportedCard(card.Model))
        {
            Hide(card);
            return;
        }

        PreviewState state = GetOrCreateState(card);
        state.IsMultiTarget = false;
        state.Target = target;
        Refresh(card);
    }

    internal static void OnMultiTargetPreviewRequested(NCard card)
    {
        if (card.Model is not { TargetType: TargetType.AllEnemies } model
            || !KomachiAttackDistancePreview.IsSupportedCard(model))
        {
            return;
        }

        PreviewState state = GetOrCreateState(card);
        state.IsMultiTarget = true;
        state.Target = null;
        Refresh(card);
    }

    private static PreviewState GetOrCreateState(NCard card)
    {
        if (Active.TryGetValue(card, out PreviewState? state))
        {
            return state;
        }

        state = new PreviewState(card);
        Active.Add(card, state);
        CombatManager.Instance.StateTracker.CombatStateChanged += state.OnStateChanged;
        if (!_combatEndedSubscribed)
        {
            CombatManager.Instance.CombatEnded += OnCombatEnded;
            _combatEndedSubscribed = true;
        }
        return state;
    }

    internal static void Hide(NCard card)
    {
        if (!Active.Remove(card, out PreviewState? state))
        {
            return;
        }

        CombatManager.Instance.StateTracker.CombatStateChanged -= state.OnStateChanged;
        ClearClusters(state);
        if (Active.Count == 0 && _combatEndedSubscribed)
        {
            CombatManager.Instance.CombatEnded -= OnCombatEnded;
            _combatEndedSubscribed = false;
        }
    }

    internal static void HideMultiTarget(NCard card)
    {
        if (Active.TryGetValue(card, out PreviewState? state) && state.IsMultiTarget)
        {
            Hide(card);
        }
    }

    private static void OnCombatEnded(CombatRoom _)
    {
        foreach (NCard card in Active.Keys.ToArray())
        {
            Hide(card);
        }
    }

    private static void Refresh(NCard card)
    {
        if (!Active.TryGetValue(card, out PreviewState? state))
        {
            return;
        }

        ClearClusters(state);
        if (!GodotObject.IsInstanceValid(card)
            || !card.IsInsideTree()
            || NCombatRoom.Instance is not { } room)
        {
            return;
        }

        try
        {
            if (card.Model is not { } model || !KomachiAttackDistancePreview.IsSupportedCard(model))
            {
                return;
            }

            IReadOnlyList<Creature> targets = state.IsMultiTarget
                ? model.CombatState!.HittableEnemies
                : state.Target is { } singleTarget ? [singleTarget] : [];
            try
            {
                foreach (Creature target in targets)
                {
                    if (KomachiAttackDistancePreview.Preview(model, target) is not { } preview
                        || room.GetCreatureNode(target) is not { } targetNode)
                    {
                        continue;
                    }

                    NEnemyIntentDistanceCluster cluster = KomachiDistancePreviewOverlay.AddAboveTarget(
                        targetNode, preview.DamageByLevel, preview.CurrentLevel);
                    state.Clusters.Add(cluster);
                    cluster.Name = "KomachiAttackDistancePreview";
                    foreach (Label label in cluster.GetChildren().OfType<Label>())
                    {
                        label.Modulate = KomachiAttackDistancePreview.GetPreviewColor(
                            label.Modulate == StsColors.red);
                    }
                }
            }
            finally
            {
                if (state.IsMultiTarget)
                {
                    // Previewing the enemies one at a time must not leave the
                    // card's dynamic values pointing at the last enemy.
                    model.DynamicVars.ClearPreview();
                    Creature? originalTarget = targets.Count == 1
                        ? targets[0]
                        : model.CurrentTarget;
                    model.UpdateDynamicVarPreview(
                        CardPreviewMode.MultiCreatureTargeting, originalTarget, model.DynamicVars);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"[KomachiJapaneseFix] Attack distance preview failed: {ex}");
            Hide(card);
        }
    }

    private static void ClearClusters(PreviewState state)
    {
        foreach (NEnemyIntentDistanceCluster cluster in state.Clusters)
        {
            if (GodotObject.IsInstanceValid(cluster))
            {
                cluster.QueueFree();
            }
        }
        state.Clusters.Clear();
    }

}

[HarmonyPatch(typeof(NCard), "_ExitTree")]
internal static class KomachiAttackDistanceCardExitPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCard __instance) =>
        KomachiAttackDistancePreviewController.Hide(__instance);
}
