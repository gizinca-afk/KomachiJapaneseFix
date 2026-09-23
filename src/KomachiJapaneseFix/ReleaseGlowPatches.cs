using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Attack;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Abilities;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Spirits;

namespace KomachiJapaneseFix;

public static class KomachiReleaseGlow
{
    private static readonly HashSet<Type> ReleaseCardTypes =
    [
        typeof(ChainDetonation),
        typeof(DivineProtection),
        typeof(EndlessWay),
        typeof(FerriageDeepFog),
        typeof(FreeTrauma),
        typeof(GrudgingStrike),
        typeof(ShortLifeExpectancy),
        typeof(SpiritBarrier),
        typeof(StrongestSpirit),
        typeof(TalkativeFerryman),
        typeof(VengefulerSweep),
        typeof(VengefulSweep),
    ];

    public static int ReleaseCardTypeCount => ReleaseCardTypes.Count;

    public static bool IsReleaseChoiceCardType(Type type) =>
        ReleaseCardTypes.Contains(type);

    public static bool CanAffordRelease(
        int guidedSpirits,
        int divineSpirits,
        int releaseCost,
        bool freeRelease) =>
        freeRelease || guidedSpirits + divineSpirits >= releaseCost;

    public static bool IsReleaseAvailable(CardModel card)
    {
        if (!IsReleaseChoiceCardType(card.GetType())
            || card.IsCanonical
            || card.CombatState is null)
        {
            return false;
        }

        Creature owner = card.Owner.Creature;
        int guided = owner.GetPower<GuidedSpiritPower>()?.Amount ?? 0;
        int divine = owner.GetPower<DivineSpiritPower>()?.Amount ?? 0;
        bool freeRelease = owner.HasPower<EikiFreeReleasePower>();

        if (card is ShortLifeExpectancy shortLife)
        {
            if (!shortLife.IsEliteRoom())
            {
                return CanAffordRelease(
                    guided,
                    divine,
                    shortLife.Value1,
                    freeRelease);
            }

            // Elite/boss primary targets use Value2, while secondary enemies
            // still use Value1. Glow when at least one legal target can release.
            return shortLife.CombatState!.HittableEnemies.Any(enemy =>
                CanAffordRelease(
                    guided,
                    divine,
                    enemy.IsSecondaryEnemy ? shortLife.Value1 : shortLife.Value2,
                    freeRelease));
        }

        return card is STS_Komachi_OnozukaCard releaseCard
            && CanAffordRelease(
                guided,
                divine,
                releaseCard.ReleaseCost,
                freeRelease);
    }

    public static bool IsReleaseResource(PowerModel power) =>
        power is GuidedSpiritPower
            or DivineSpiritPower
            or EikiFreeReleasePower;

    internal static void RefreshHand(
        Creature? owner = null,
        bool shortLifeOnly = false)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room?.Ui?.Hand is not { } hand || !hand.IsNodeReady())
        {
            return;
        }

        foreach (NHandCardHolder holder in hand.ActiveHolders)
        {
            if (holder.CardModel is not { } candidate
                || (shortLifeOnly && candidate is not ShortLifeExpectancy)
                || (!shortLifeOnly && !IsReleaseChoiceCardType(candidate.GetType()))
                || (owner is not null
                    && !ReferenceEquals(candidate.Owner.Creature, owner)))
            {
                continue;
            }

            holder.UpdateCard();
        }
    }
}

[HarmonyPatch(typeof(CardModel), "get_ShouldGlowGold")]
internal static class KomachiReleaseGoldGlowPatch
{
    [HarmonyPostfix]
    private static void GlowWhenReleaseIsAvailable(
        CardModel __instance,
        ref bool __result)
    {
        __result |= KomachiReleaseGlow.IsReleaseAvailable(__instance);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.ApplyPowerInternal))]
internal static class KomachiReleaseGlowPowerAppliedPatch
{
    [HarmonyPostfix]
    private static void RefreshAfterPowerApplied(
        Creature __instance,
        PowerModel power)
    {
        if (KomachiReleaseGlow.IsReleaseResource(power))
        {
            KomachiReleaseGlow.RefreshHand(__instance);
        }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.InvokePowerModified))]
internal static class KomachiReleaseGlowPowerModifiedPatch
{
    [HarmonyPostfix]
    private static void RefreshAfterPowerModified(
        Creature __instance,
        PowerModel power)
    {
        if (KomachiReleaseGlow.IsReleaseResource(power))
        {
            KomachiReleaseGlow.RefreshHand(__instance);
        }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
internal static class KomachiReleaseGlowPowerRemovedPatch
{
    [HarmonyPostfix]
    private static void RefreshAfterPowerRemoved(
        Creature __instance,
        PowerModel power)
    {
        if (KomachiReleaseGlow.IsReleaseResource(power))
        {
            KomachiReleaseGlow.RefreshHand(__instance);
        }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.InvokeDiedEvent))]
internal static class KomachiShortLifeGlowDeathPatch
{
    [HarmonyPostfix]
    private static void RefreshAfterTargetDies()
    {
        KomachiReleaseGlow.RefreshHand(shortLifeOnly: true);
    }
}
