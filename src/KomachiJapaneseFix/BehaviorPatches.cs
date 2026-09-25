using System.Reflection;
using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Attack;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Tokens;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Character;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Commands;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Extensions;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Minions;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Potions;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Abilities;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Distance;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Other;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Spirits;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Relics;

namespace KomachiJapaneseFix;

public static class KomachiCombatPileInspectCards
{
    public static IReadOnlyList<CardModel> CreateSnapshot(IReadOnlyList<CardModel> displayedCards) =>
        displayedCards.ToList();
}

[HarmonyPatch(typeof(NCombatPileCardSelectScreen), "UpdatePileContents")]
internal static class KomachiCombatPileInspectCardListPatch
{
    private static readonly FieldInfo CardsField =
        AccessTools.Field(typeof(NCardGridSelectionScreen), "_cards")
        ?? throw new MissingFieldException(typeof(NCardGridSelectionScreen).FullName, "_cards");

    private static readonly FieldInfo PileField =
        AccessTools.Field(typeof(NCombatPileCardSelectScreen), "_pile")
        ?? throw new MissingFieldException(typeof(NCombatPileCardSelectScreen).FullName, "_pile");

    private static readonly FieldInfo GridField =
        AccessTools.Field(typeof(NCardGridSelectionScreen), "_grid")
        ?? throw new MissingFieldException(typeof(NCardGridSelectionScreen).FullName, "_grid");

    private static readonly FieldInfo GridCardsField =
        AccessTools.Field(typeof(NCardGrid), "_cards")
        ?? throw new MissingFieldException(typeof(NCardGrid).FullName, "_cards");

    [HarmonyPostfix]
    private static void SyncInspectCards(NCombatPileCardSelectScreen __instance)
    {
        if (PileField.GetValue(__instance) is not CardPile pile
            || pile.Cards.Count == 0
            || pile.Cards[0].Owner.Character is not Komachi_Character)
            return;

        // SetCards has already applied the game's filtering and display sorting.
        // Use its complete list, not the draw pile's real order or only visible rows.
        if (GridField.GetValue(__instance) is not NCardGrid grid
            || GridCardsField.GetValue(grid) is not IReadOnlyList<CardModel> sortedGridCards)
            return;

        CardsField.SetValue(__instance,
            KomachiCombatPileInspectCards.CreateSnapshot(sortedGridCards));
    }
}

public static class KomachiSpiritDamagePreview
{
    public static decimal ToDisplayedDamage(decimal amount) =>
        (decimal)(int)Math.Clamp(amount, 0m, 999999999m);
}

[HarmonyPatch]
internal static class SpiritThirdDamagePreviewPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(GuidedSpiritPower), nameof(GuidedSpiritPower.GetThirdAmount)),
        AccessTools.Method(typeof(VengefulSpiritPower), nameof(VengefulSpiritPower.GetThirdAmount)),
        AccessTools.Method(typeof(LonelyBoundSpiritPower), nameof(LonelyBoundSpiritPower.GetThirdAmount)),
    ];

    [HarmonyPostfix]
    private static void Postfix(ref decimal? __result)
    {
        if (__result is decimal amount)
            __result = KomachiSpiritDamagePreview.ToDisplayedDamage(amount);
    }
}

[HarmonyPatch(typeof(ShinigamiFormPower), "RefreshDisplayVars")]
internal static class ShinigamiFormDisplayVarsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ShinigamiFormPower __instance)
    {
        static int Percent(int level, int stacks)
        {
            var baseMultiplier = DistancePower.GetDamageMultiplier(level);
            var final = 1m + (baseMultiplier - 1m) * (1 + stacks);
            return (int)((final - 1m) * 100m);
        }

        __instance.DynamicVars["L1Mult"].BaseValue = Percent(1, __instance.Amount);
        __instance.DynamicVars["L2Mult"].BaseValue = Percent(2, __instance.Amount);
        __instance.DynamicVars["L4Mult"].BaseValue = Percent(4, 1);
        __instance.DynamicVars["L5Mult"].BaseValue = Percent(5, 1);
        AccessTools.Method(typeof(PowerModel), "InvokeDisplayAmountChanged")?.Invoke(__instance, null);
        return false;
    }
}

[HarmonyPatch(typeof(GodOfDeathPower), nameof(GodOfDeathPower.AfterPreventingDeath))]
internal static class GodOfDeathSingleRevivePatch
{
    [HarmonyPrefix]
    private static bool Prefix(GodOfDeathPower __instance, Creature creature, ref Task __result)
    {
        __result = Run(__instance, creature);
        return false;
    }

    private static async Task Run(GodOfDeathPower power, Creature creature)
    {
        power.HasRevived = true;
        AccessTools.Method(typeof(PowerModel), "InvokeDisplayAmountChanged")?.Invoke(power, null);
        await CreatureCmd.Heal(creature, Math.Max((decimal)creature.MaxHp * power.Value1 / 100m, 1m));
        await PowerCmd.Apply<IntangiblePower>(new ThrowingPlayerChoiceContext(), creature, 1, power.Owner, null);
        if (power.applyingCard is not null && power.Owner.Player!.Deck.Cards.Contains(power.applyingCard))
            await CardPileCmd.RemoveFromDeck(power.applyingCard, false);
    }
}

[HarmonyPatch(typeof(EndlessWay), "OnPlay")]
internal static class EndlessWayReleasePatch
{
    [HarmonyPrefix]
    private static bool Prefix(EndlessWay __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
        ref Task __result)
    {
        __result = Play(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task Play(EndlessWay card, PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        var chosen = await ReleaseCmd.ChooseRelease(context, card, card.ReleaseCost, card.ReleaseCost2);
        await CreatureCmd.LoseBlock(context, play.Target, play.Target.Block, card.Owner.Creature);
        await DistanceCmd.Displace(context, play.Target, card.Value1, card.Owner.Creature, card);
        await CreatureCmd.GainBlock(card.Owner.Creature, card.DynamicVars.Block, play);
        if (!ReleaseCmd.ChoseRelease(chosen, card.ReleaseCost, card.ReleaseCost2, out var cost))
            return;
        await ReleaseCmd.Release(context, card.Owner.Creature, cost, card);
        await PowerCmd.Apply<VengefulSpiritPower>(context, play.Target,
            card.Value2 * (cost / card.ReleaseCost), card.Owner.Creature, card);
    }
}

[HarmonyPatch(typeof(LonelyBoundSpiritPower), nameof(LonelyBoundSpiritPower.OnDetonatedEarly))]
internal static class LonelyBoundSpiritTimedDetonationPatch
{
    [HarmonyPostfix]
    private static void IncludeTimedDetonation(LonelyBoundSpiritPower __instance,
        PlayerChoiceContext choiceContext, DetonationEventArgs args, ref Task __result)
    {
        if (args.DetonatedByEffect || args.Target != __instance.Owner
            || args.DetonatingPower is not VengefulSpiritPower)
            return;

        __result = DetonateAfterOriginal(__result, __instance, choiceContext, args);
    }

    private static async Task DetonateAfterOriginal(Task original, LonelyBoundSpiritPower power,
        PlayerChoiceContext context, DetonationEventArgs args)
    {
        await original;
        args.BonusAmount += power.Amount;
        await DetonateCmd.DetonateRaw(context, power, power.Amount, args.Dealer, cardSource: null);
    }
}

public static class KomachiDetonationHookFallback
{
    public static ICombatState? ResolveCombatState(DetonationEventArgs args) =>
        args.Target.CombatState ?? args.Dealer?.CombatState;

    internal static async Task Dispatch<T>(PlayerChoiceContext choiceContext,
        ICombatState combatState, Func<T, Task> invoke) where T : class
    {
        foreach (var item in combatState.IterateHookListeners().OfType<T>().ToArray())
        {
            var model = item as AbstractModel;
            choiceContext.PushModel(model!);
            try
            {
                await invoke(item);
            }
            finally
            {
                choiceContext.PopModel(model!);
            }
        }
    }
}

[HarmonyPatch(typeof(KomachiHooks), nameof(KomachiHooks.OnDetonatedEarly))]
internal static class LethalDetonationEarlyHookPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerChoiceContext choiceContext, DetonationEventArgs args,
        ref Task __result)
    {
        if (args.Target.CombatState is not null)
            return true;

        var combatState = KomachiDetonationHookFallback.ResolveCombatState(args);
        if (combatState is null)
            return true;

        __result = KomachiDetonationHookFallback.Dispatch<IOnDetonatedEarlyListener>(
            choiceContext, combatState,
            listener => listener.OnDetonatedEarly(choiceContext, args));
        return false;
    }
}

[HarmonyPatch(typeof(KomachiHooks), nameof(KomachiHooks.OnDetonated))]
internal static class LethalDetonationHookPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PlayerChoiceContext choiceContext, DetonationEventArgs args,
        ref Task __result)
    {
        if (args.Target.CombatState is not null)
            return true;

        var combatState = KomachiDetonationHookFallback.ResolveCombatState(args);
        if (combatState is null)
            return true;

        __result = KomachiDetonationHookFallback.Dispatch<IOnDetonatedListener>(
            choiceContext, combatState,
            listener => listener.OnDetonated(choiceContext, args));
        return false;
    }
}

[HarmonyPatch(typeof(DoubleHooking), "OnPlay")]
internal static class DoubleHookingEmptyChoicePatch
{
    [HarmonyPrefix]
    private static bool Prefix(DoubleHooking __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        __result = Play(__instance, choiceContext);
        return false;
    }

    private static async Task Play(DoubleHooking card, PlayerChoiceContext context)
    {
        var discard = (await CardSelectCmd.FromHandForDiscard(context, card.Owner,
            new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1), null, card)).FirstOrDefault();
        if (discard is not null)
            await CardCmd.Discard(context, discard);

        var combatState = card.Owner.PlayerCombatState;
        if (combatState is null)
            return;
        var candidates = combatState.ExhaustPile.Cards
            .Where(c => c is not DoubleHooking).ToList();
        if (candidates.Count == 0)
            return;

        List<CardModel> choice;
        if (candidates.Count == 1)
            choice = [candidates[0]];
        else
            choice = (await CardSelectCmd.FromCombatPile(context,
                combatState.ExhaustPile, card.Owner,
                new CardSelectorPrefs(new LocString("cards", card.Id.Entry + ".selectionScreenPrompt"), 0, 2),
                c => c is not DoubleHooking)).ToList();

        if (choice.Count == 0)
            return;
        var toHand = choice[0];
        await CardPileCmd.Add(toHand, PileType.Hand);
        toHand.EnergyCost.SetUntilPlayed(0);
        if (choice.Count <= 1)
            return;
        var toDiscard = choice[1];
        await CardPileCmd.Add(toDiscard, PileType.Discard);
        toDiscard.EnergyCost.SetUntilPlayed(0);
    }
}

[HarmonyPatch(typeof(ParryingScythe), nameof(ParryingScythe.BeforeDamageReceived))]
internal static class ParryingScytheAttackGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ParryingScythe __instance, Creature target, decimal amount, ValueProp props,
        Creature? dealer, ref Task __result)
    {
        __result = Run(__instance, target, amount, props, dealer);
        return false;
    }

    private static async Task Run(ParryingScythe relic, Creature target, decimal amount, ValueProp props,
        Creature? dealer)
    {
        if (relic.UsedThisCombat || dealer is null || !props.IsPoweredAttack() || amount <= 0
            || target != relic.Owner.Creature || dealer.Side != CombatSide.Enemy
            || DistancePower.GetLevel(dealer) >= 3)
            return;
        relic.Flash();
        await CreatureCmd.GainBlock(relic.Owner.Creature, amount / 2, ValueProp.Unpowered, null);
        relic.UsedThisCombat = true;
    }
}

[HarmonyPatch(typeof(ExchangeLife), "OnPlay")]
internal static class ExchangeLifeRemovalPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ExchangeLife __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
        ref Task __result)
    {
        __result = Play(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task Play(ExchangeLife card, PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        if (play.Target.CurrentHp >= card.Owner.Creature.MaxHp)
            return;

        var targetHp = play.Target.CurrentHp;
        var ownerHp = card.Owner.Creature.CurrentHp;
        var delta = targetHp - ownerHp;
        if (delta > 0)
        {
            await CreatureCmd.Damage(context, play.Target, delta,
                ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
            await CreatureCmd.Heal(card.Owner.Creature, delta);
            if (card.DeckVersion is not null)
                await CardPileCmd.RemoveFromDeck(card.DeckVersion);
        }
        else if (delta < 0)
        {
            await CreatureCmd.Heal(play.Target, -delta);
            await CreatureCmd.Damage(context, card.Owner.Creature, -delta,
                ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
        }
    }
}

[HarmonyPatch]
internal static class SpiritInvitationStackPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(SpiritInvitationPower), nameof(PowerModel.StackType));

    [HarmonyPostfix]
    private static void Postfix(ref PowerStackType __result) => __result = PowerStackType.Counter;
}

[HarmonyPatch(typeof(PossessedLily), nameof(PossessedLily.AfterPowerAmountChanged))]
internal static class PossessedLilyPositivePoisonPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PossessedLily __instance, PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, ref Task __result)
    {
        if (power is PoisonPower && power.Owner == __instance.Owner.Creature && amount > 0)
        {
            __instance.Flash();
            __result = PowerCmd.Apply<DivineSpiritPower>(choiceContext, __instance.Owner.Creature,
                amount, __instance.Owner.Creature, null);
        }
        else
            __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(FerrymansOar), nameof(FerrymansOar.AfterRoomEntered))]
internal static class FerrymansOarAllCardsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(FerrymansOar __instance, AbstractRoom room, ref Task __result)
    {
        __result = Run(__instance, room);
        return false;
    }

    private static async Task Run(FerrymansOar relic, AbstractRoom room)
    {
        if (room is not CombatRoom)
            return;
        relic.Flash();
        var cards = PileType.Draw.GetPile(relic.Owner).Cards.ToList()
            .StableShuffle(relic.Owner.RunState.Rng.CombatCardSelection)
            .Take(relic.DynamicVars.Cards.IntValue).ToList();
        foreach (var card in cards)
            CardCmd.ApplyKeyword(card, KomachiKeywords.Replenish);
        await Cmd.CustomScaledWait(0.5f, 1f);
    }
}

[HarmonyPatch(typeof(Sweep), "OnPlay")]
internal static class SweepOrderingPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Sweep __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
        ref Task __result)
    {
        __result = Play(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task Play(Sweep card, PlayerChoiceContext context, CardPlay play)
    {
        if (card.CombatState is null)
            return;
        foreach (var enemy in card.CombatState.HittableEnemies)
            await DistanceCmd.Displace(context, enemy, card.Value1, card.Owner.Creature, card);
        await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue).FromCard(card, play)
            .TargetingAllOpponents(card.CombatState).WithHitFx("vfx/vfx_giant_horizontal_slash")
            .Execute(context);
        for (var i = 0; i < card.DynamicVars.Cards.IntValue; i++)
        {
            var token = card.CombatState.CreateCard<ManipulateDistanceToken>(card.Owner);
            await CardPileCmd.AddGeneratedCardToCombat(token, PileType.Hand, card.Owner);
        }
    }
}

[HarmonyPatch(typeof(DisplacementPotion), "OnUse")]
internal static class DisplacementPotionAwaitPatch
{
    [HarmonyPrefix]
    private static bool Prefix(DisplacementPotion __instance, ref Task __result)
    {
        __result = Add(__instance);
        return false;
    }

    private static async Task Add(DisplacementPotion potion)
    {
        for (var i = 0; i < potion.DynamicVars.Cards.IntValue; i++)
        {
            var combat = potion.Owner.Creature.CombatState;
            if (combat is null)
                return;
            var card = combat.CreateCard<ManipulateDistanceToken>(potion.Owner);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, potion.Owner);
        }
    }
}

[HarmonyPatch(typeof(SpiderLilyPotion), "OnUse")]
internal static class SpiderLilyPotionAwaitPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SpiderLilyPotion __instance, ref Task __result)
    {
        __result = Add(__instance);
        return false;
    }

    private static async Task Add(SpiderLilyPotion potion)
    {
        for (var i = 0; i < potion.DynamicVars.Cards.IntValue; i++)
        {
            var combat = potion.Owner.Creature.CombatState;
            if (combat is null)
                return;
            var card = combat.CreateCard<SpiderLily>(potion.Owner);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, potion.Owner);
        }
    }
}

[HarmonyPatch(typeof(ExtinctFaunaPower), nameof(ExtinctFaunaPower.AfterCardPlayed))]
internal static class ExtinctFaunaCloneGuardPatch
{
    private static readonly FieldInfo SourceField = AccessTools.Field(typeof(ExtinctFaunaPower), "source");

    [HarmonyPrefix]
    private static bool Prefix(ExtinctFaunaPower __instance, PlayerChoiceContext choiceContext,
        CardPlay cardPlay, ref Task __result)
    {
        __result = Run(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task Run(ExtinctFaunaPower power, PlayerChoiceContext context, CardPlay play)
    {
        var player = power.Owner.Player;
        if (player is null || play.Card.Owner != player || ReferenceEquals(play.Card, SourceField.GetValue(power)))
            return;

        var exhaust = PileType.Exhaust.GetPile(player);
        var matches = exhaust.Cards.Where(c => c.GetType() == play.Card.GetType()).ToList();
        if (matches.Count == 0 && power.Value1 > 0)
        {
            if (play.Card.Keywords.Contains(KomachiKeywords.Clone)
                || play.Card.Keywords.Contains(KomachiKeywords.Unclonable))
                return;
            var copy = play.Card.CreateClone();
            copy.AddKeyword(KomachiKeywords.Clone);
            copy.EnergyCost.SetThisCombat(power.DynamicVars.Energy.IntValue, true);
            await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Exhaust, player);
            await CardCmd.Exhaust(context, copy);
            power.Value1--;
        }
        else if (matches.Count > 0 && power.Value2 > 0)
        {
            var prompt = new LocString("powers", power.Id.Entry + ".selectionScreenPrompt");
            var chosen = (await CardSelectCmd.FromSimpleGrid(context, matches, player,
                new CardSelectorPrefs(prompt, 0, 1))).FirstOrDefault();
            if (chosen is not null)
                await CardPileCmd.Add(chosen, PileType.Hand);
            power.Value2--;
        }
    }
}

[HarmonyPatch]
internal static class KomachiCardPortraitFallbackPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(STS_Komachi_OnozukaCard), nameof(CardModel.PortraitPath));

    [HarmonyPostfix]
    private static void Postfix(STS_Komachi_OnozukaCard __instance, ref string __result) =>
        __result = __instance.CustomPortraitPath;
}

[HarmonyPatch]
internal static class SpiritBarrierBigIconFallbackPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(SpiritBarrierPower), "CustomBigIconPath");

    [HarmonyPostfix]
    private static void Postfix(SpiritBarrierPower __instance, ref string __result) =>
        __result = __instance.CustomPackedIconPath;
}
