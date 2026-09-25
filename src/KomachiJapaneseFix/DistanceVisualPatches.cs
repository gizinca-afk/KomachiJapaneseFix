using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Attack;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards.Tokens;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Commands;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Patches;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Powers.Distance;

namespace KomachiJapaneseFix;

public enum KomachiDistanceChoice
{
    None,
    ApproachFour,
    ApproachThree,
    ApproachTwo,
    ApproachOne,
    Hold,
    RetreatOne,
    RetreatTwo,
    RetreatThree,
    RetreatFour,
}

public static class KomachiDistanceVisuals
{
    // Audited against every direct DistanceCmd.Displace / ChooseAndDisplace
    // call in the upstream card sources. Delayed power-only displacement is
    // deliberately outside this card-target preview registry.
    public static IReadOnlySet<Type> AuditedDistanceCardTypes { get; } = new HashSet<Type>
    {
        typeof(ManipulateDistanceToken),
        typeof(MoveAndShoot),
        typeof(ShootAndMove),
        typeof(ScytheOfFinalJudgement),
        typeof(StygianReel),
        typeof(Sweep),
        typeof(TasteOfDeath),
        typeof(EndlessWay),
        typeof(HandyRetreat),
        typeof(ProtectiveSanzuBoat),
        typeof(EqualizeDistance),
        typeof(FerriageDeepFog),
        typeof(IgnoreNagging),
        typeof(PushDraw),
        typeof(RitualOfEcstasy),
    };

    private static readonly Dictionary<KomachiDistanceChoice, string> AssetNames = new()
    {
        [KomachiDistanceChoice.ApproachFour] = "distance_approach_2.png",
        [KomachiDistanceChoice.ApproachThree] = "distance_approach_2.png",
        [KomachiDistanceChoice.ApproachTwo] = "distance_approach_2.png",
        [KomachiDistanceChoice.ApproachOne] = "distance_approach_1.png",
        [KomachiDistanceChoice.Hold] = "distance_hold.png",
        [KomachiDistanceChoice.RetreatOne] = "distance_retreat_1.png",
        [KomachiDistanceChoice.RetreatTwo] = "distance_retreat_2.png",
        [KomachiDistanceChoice.RetreatThree] = "distance_retreat_2.png",
        [KomachiDistanceChoice.RetreatFour] = "distance_retreat_2.png",
    };

    private static readonly Dictionary<KomachiDistanceChoice, Texture2D> Textures = [];
    private static readonly Dictionary<int, Texture2D> DistanceIconTextures = [];
    private static Texture2D? _distanceIconSource;

    private static readonly FieldInfo PowerOwnerField =
        AccessTools.Field(typeof(PowerModel), "_owner")
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_owner");

    private static readonly FieldInfo PowerAmountField =
        AccessTools.Field(typeof(PowerModel), "_amount")
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_amount");

    private static readonly FieldInfo CreaturePowersField =
        AccessTools.Field(typeof(Creature), "_powers")
        ?? throw new MissingFieldException(typeof(Creature).FullName, "_powers");

    public static KomachiDistanceChoice GetChoice(CardModel card) => card switch
    {
        ManipulateNoDistanceToken => KomachiDistanceChoice.Hold,
        ManipulateDistanceToken token => GetChoice(token.AltDescription, token.Value1),
        _ => KomachiDistanceChoice.None,
    };

    public static KomachiDistanceChoice GetChoice(int altDescription, int amount) =>
        (altDescription, amount) switch
        {
            (1, 4) => KomachiDistanceChoice.ApproachFour,
            (1, 3) => KomachiDistanceChoice.ApproachThree,
            (1, 2) => KomachiDistanceChoice.ApproachTwo,
            (1, 1) => KomachiDistanceChoice.ApproachOne,
            (2, 1) => KomachiDistanceChoice.RetreatOne,
            (2, 2) => KomachiDistanceChoice.RetreatTwo,
            (2, 3) => KomachiDistanceChoice.RetreatThree,
            (2, 4) => KomachiDistanceChoice.RetreatFour,
            _ => KomachiDistanceChoice.None,
        };

    public static string GetChoiceTitle(KomachiDistanceChoice choice) => choice switch
    {
        KomachiDistanceChoice.ApproachFour => "4段階接近",
        KomachiDistanceChoice.ApproachThree => "3段階接近",
        KomachiDistanceChoice.ApproachTwo => "2段階接近",
        KomachiDistanceChoice.ApproachOne => "1段階接近",
        KomachiDistanceChoice.Hold => "距離維持",
        KomachiDistanceChoice.RetreatOne => "1段階後退",
        KomachiDistanceChoice.RetreatTwo => "2段階後退",
        KomachiDistanceChoice.RetreatThree => "3段階後退",
        KomachiDistanceChoice.RetreatFour => "4段階後退",
        _ => string.Empty,
    };

    public static string GetChoiceAssetName(KomachiDistanceChoice choice) => AssetNames[choice];

    public static Color GetDistanceColor(int level) => Math.Clamp(level, 1, 5) switch
    {
        1 => Color.FromHtml("ff4d5a"),
        2 => Color.FromHtml("ffad3d"),
        3 => Color.FromHtml("0e1b56"),
        4 => Color.FromHtml("48cfff"),
        5 => Color.FromHtml("8b78ff"),
        _ => Colors.White,
    };

    public static int[] OrderDisplacementsForScreen(IEnumerable<int> options) =>
        options.OrderByDescending(static option => option).ToArray();

    public static IReadOnlyDictionary<int, decimal> ReverseDamagePreview(
        IReadOnlyDictionary<int, decimal> damageByLevel)
    {
        Dictionary<int, decimal> reversed = new(5);
        for (int displayedLevel = 1; displayedLevel <= 5; displayedLevel++)
        {
            reversed[displayedLevel] = damageByLevel[6 - displayedLevel];
        }
        return reversed;
    }

    public static int ReverseDistanceLevel(int level) => 6 - Math.Clamp(level, 1, 5);

    public static bool IsDistanceManipulationCard(CardModel? card) =>
        card is not null && AuditedDistanceCardTypes.Contains(card.GetType());

    public static bool IsAllEnemyIntentPreviewCard(CardModel? card) =>
        card is PushNPull
        || (card is not null
            && card.TargetType is TargetType.AllEnemies or TargetType.RandomEnemy
            && IsDistanceManipulationCard(card));

    public static int[] GetAuditedDisplacements(CardModel card, int currentLevel) => card switch
    {
        ManipulateDistanceToken token => token.IsUpgraded ? [-2, -1, 1, 2] : [-1, 1],
        MoveAndShoot moveAndShoot => moveAndShoot.GetPossibleDisplacements(),
        ShootAndMove shootAndMove => shootAndMove.IsUpgraded ? [-2, -1, 0, 1, 2] : [-1, 0, 1],
        ScytheOfFinalJudgement judgement => judgement.GetPossibleDisplacements(),
        StygianReel reel => [-reel.Value1],
        Sweep sweep => [sweep.Value1],
        TasteOfDeath taste => [taste.Value1],
        EndlessWay endlessWay => [endlessWay.Value1],
        HandyRetreat retreat => retreat.IsUpgraded ? [1, 2] : [1],
        ProtectiveSanzuBoat => [1],
        EqualizeDistance => [3 - Math.Clamp(currentLevel, 1, 5)],
        FerriageDeepFog => [-3, -2, -1],
        IgnoreNagging nagging => nagging.IsUpgraded ? [-2, -1] : [-1],
        PushDraw pushDraw => pushDraw.IsUpgraded ? [1, 2] : [1],
        RitualOfEcstasy => [6 - 2 * Math.Clamp(currentLevel, 1, 5)],
        _ => [],
    };

    public static DistancePower.EnemyIntentDistancePreview? PreviewTargetedIntentDamage(
        Creature creature,
        IReadOnlyList<Creature> intentTargets)
    {
        if (creature.GetPower<DistancePower>() is not null)
        {
            return DistancePower.PreviewIntentDamageAcrossDistances(creature, intentTargets);
        }

        if (creature.Monster is not { IntendsToAttack: true } monster
            || !monster.NextMove.Intents.OfType<AttackIntent>().Any())
        {
            return null;
        }

        // The upstream preview requires an existing DistancePower, although
        // enemies without one are treated as distance 3. Attach a private,
        // event-free preview instance only for the synchronous damage query so
        // the real Hook.ModifyDamage path (including multi-hit flooring) is
        // reused without visibly applying a power or changing combat state.
        DistancePower previewPower = (DistancePower)ModelDb.Power<DistancePower>().ToMutable();
        PowerOwnerField.SetValue(previewPower, creature);
        PowerAmountField.SetValue(previewPower, DistancePower.DefaultLevel);

        List<PowerModel> powers = CreaturePowersField.GetValue(creature) as List<PowerModel>
            ?? throw new InvalidOperationException("Creature power storage is unavailable.");
        powers.Add(previewPower);
        try
        {
            return DistancePower.PreviewIntentDamageAcrossDistances(creature, intentTargets);
        }
        finally
        {
            powers.Remove(previewPower);
        }
    }

    public static Color RecolorDistanceIconPixel(Color source, Color target)
    {
        if (source.A <= 0f)
        {
            return source;
        }

        // The original icon is a dark blue circle with a white arrow. Use the
        // minimum RGB channel as a white-arrow mask so the arrow and its
        // antialiased edge stay white while only the surrounding circle takes
        // the distance color.
        float minimumChannel = Mathf.Min(source.R, Mathf.Min(source.G, source.B));
        float arrowMask = Mathf.Clamp((minimumChannel - 0.16f) / 0.84f, 0f, 1f);
        Color recolored = target.Lerp(Colors.White, arrowMask);
        return new Color(recolored.R, recolored.G, recolored.B, source.A);
    }

    internal static bool TryGetPortrait(CardModel card, out Texture2D? texture)
    {
        KomachiDistanceChoice choice = GetChoice(card);
        if (choice == KomachiDistanceChoice.None)
        {
            texture = null;
            return false;
        }

        if (!Textures.TryGetValue(choice, out Texture2D? loaded)
            || !GodotObject.IsInstanceValid(loaded))
        {
            loaded = LoadTexture(GetChoiceAssetName(choice));
            if (loaded is null)
            {
                texture = null;
                return false;
            }
            Textures[choice] = loaded;
        }

        texture = loaded;
        return true;
    }

    internal static void ApplyDistanceColor(NPower powerNode)
    {
        if (powerNode.Model is not DistancePower distance
            || !powerNode.IsNodeReady()
            || powerNode.GetNodeOrNull<TextureRect>("%Icon") is not { } icon)
        {
            return;
        }

        Color color = GetDistanceColor(distance.Amount);
        if (_distanceIconSource is null
            || !GodotObject.IsInstanceValid(_distanceIconSource))
        {
            _distanceIconSource = icon.Texture;
            DistanceIconTextures.Clear();
        }

        if (_distanceIconSource is not null
            && GetDistanceIconTexture(distance.Amount, _distanceIconSource) is { } texture)
        {
            icon.Texture = texture;
        }
        icon.SelfModulate = Colors.White;
        if (powerNode.GetNodeOrNull<CpuParticles2D>("%PowerFlash") is { } flash)
        {
            flash.SelfModulate = color;
        }
    }

    private static Texture2D? GetDistanceIconTexture(int level, Texture2D source)
    {
        int clampedLevel = Math.Clamp(level, 1, 5);
        if (DistanceIconTextures.TryGetValue(clampedLevel, out Texture2D? cached)
            && GodotObject.IsInstanceValid(cached))
        {
            return cached;
        }

        using Image image = source.GetImage();
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            return null;
        }
        image.Convert(Image.Format.Rgba8);

        Color target = GetDistanceColor(clampedLevel);
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                image.SetPixel(x, y, RecolorDistanceIconPixel(image.GetPixel(x, y), target));
            }
        }

        Texture2D texture = ImageTexture.CreateFromImage(image);
        DistanceIconTextures[clampedLevel] = texture;
        return texture;
    }

    private static Texture2D? LoadTexture(string assetName)
    {
        Assembly assembly = typeof(KomachiDistanceVisuals).Assembly;
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

internal static class KomachiTargetedIntentPreviewController
{
    private static readonly Dictionary<NCard, List<NEnemyIntentDistanceCluster>> ActiveClusters = [];
    private static readonly Dictionary<Creature, IReadOnlyList<Creature>> IntentTargets = [];

    internal static void RememberIntentTargets(NCreature creatureNode, IEnumerable<Creature> targets) =>
        IntentTargets[creatureNode.Entity] = targets.ToArray();

    internal static void ForgetCreature(NCreature creatureNode) =>
        IntentTargets.Remove(creatureNode.Entity);

    internal static void OnPreviewTargetChanged(NCard card, Creature? creature)
    {
        Clear(card);
        CardModel? model = card.Model;
        if (creature is null
            || model is null
            || model.TargetType != TargetType.AnyEnemy)
        {
            return;
        }

        if (TryCreateCluster(card, creature) is { } cluster)
        {
            ActiveClusters[card] = [cluster];
        }
    }

    internal static void OnMultiTargetPreviewRequested(NCard card)
    {
        Clear(card);
        CardModel? model = card.Model;
        if (model is null
            || !KomachiDistanceVisuals.IsAllEnemyIntentPreviewCard(model)
            || model.CombatState is null
            || model.Pile?.Type != PileType.Hand)
        {
            return;
        }

        List<NEnemyIntentDistanceCluster> clusters = [];
        foreach (Creature enemy in model.CombatState.HittableEnemies)
        {
            if (TryCreateCluster(card, enemy) is { } cluster)
            {
                clusters.Add(cluster);
            }
        }

        if (clusters.Count > 0)
        {
            ActiveClusters[card] = clusters;
        }
    }

    private static NEnemyIntentDistanceCluster? TryCreateCluster(NCard card, Creature creature)
    {
        CardModel? model = card.Model;
        int currentLevel = DistancePower.GetLevel(creature);
        if (model is null
            || (model is not PushNPull
                && (!KomachiDistanceVisuals.IsDistanceManipulationCard(model)
                    || KomachiDistanceVisuals.GetAuditedDisplacements(model, currentLevel).Length == 0))
            || !IntentTargets.TryGetValue(creature, out IReadOnlyList<Creature>? targets))
        {
            return null;
        }

        DistancePower.EnemyIntentDistancePreview? preview =
            KomachiDistanceVisuals.PreviewTargetedIntentDamage(creature, targets);
        if (preview is null)
        {
            return null;
        }

        // Enemies that already own DistancePower already have the upstream
        // always-on row. The card-target row is only needed for the default
        // distance-3 case, where upstream otherwise displays nothing.
        if (creature.GetPower<DistancePower>() is not null)
        {
            return null;
        }

        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (creatureNode is null)
        {
            return null;
        }

        NEnemyIntentDistanceCluster cluster = NEnemyIntentDistanceCluster.Create(
            preview.Value.DamageByLevel,
            preview.Value.CurrentLevel);
        creatureNode.AddChild(cluster);
        PositionCluster(creatureNode, cluster);
        return cluster;
    }

    private static void PositionCluster(
        NCreature creatureNode,
        NEnemyIntentDistanceCluster cluster)
    {
        Vector2 intentPosition = creatureNode.IntentContainer.Position;
        Vector2 intentSize = creatureNode.IntentContainer.Size;
        cluster.Position = new Vector2(
            intentPosition.X + intentSize.X * 0.5f - cluster.Size.X * 0.5f,
            intentPosition.Y - cluster.Size.Y - 12f);
    }

    internal static void Clear(NCard card)
    {
        if (!ActiveClusters.Remove(card, out List<NEnemyIntentDistanceCluster>? clusters))
        {
            return;
        }

        foreach (NEnemyIntentDistanceCluster cluster in clusters)
        {
            if (GodotObject.IsInstanceValid(cluster))
            {
                cluster.QueueFree();
            }
        }
    }
}

[HarmonyPatch(typeof(DistanceCmd), nameof(DistanceCmd.ChooseDisplacement))]
internal static class KomachiDistanceChoiceOrderPatch
{
    [HarmonyPrefix]
    private static void PutRetreatChoicesOnLeft(ref int[] options) =>
        options = KomachiDistanceVisuals.OrderDisplacementsForScreen(options);
}

[HarmonyPatch(typeof(NEnemyIntentDistanceCluster), nameof(NEnemyIntentDistanceCluster.Create))]
internal static class KomachiDistanceDamagePreviewOrderPatch
{
    [HarmonyPrefix]
    private static void PutFarDamageOnLeft(
        ref IReadOnlyDictionary<int, decimal> damageByLevel,
        ref int currentLevel)
    {
        damageByLevel = KomachiDistanceVisuals.ReverseDamagePreview(damageByLevel);
        currentLevel = KomachiDistanceVisuals.ReverseDistanceLevel(currentLevel);
    }

    [HarmonyPostfix]
    private static void ImproveContrast(NEnemyIntentDistanceCluster __result)
    {
        foreach (Label label in __result.GetChildren().OfType<Label>())
        {
            if (label.Modulate == StsColors.halfTransparentWhite)
            {
                label.Modulate = Colors.White;
            }
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AddThemeFontSizeOverride("font_size", 17);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        __result.MouseFilter = Control.MouseFilterEnum.Ignore;
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.UpdateIntent))]
internal static class KomachiRememberIntentTargetsPatch
{
    [HarmonyPostfix]
    private static void RememberTargets(NCreature __instance, IEnumerable<Creature> targets) =>
        KomachiTargetedIntentPreviewController.RememberIntentTargets(__instance, targets);
}

[HarmonyPatch(typeof(NCreature), "_ExitTree")]
internal static class KomachiForgetIntentTargetsPatch
{
    [HarmonyPostfix]
    private static void ForgetTargets(NCreature __instance) =>
        KomachiTargetedIntentPreviewController.ForgetCreature(__instance);
}

[HarmonyPatch(typeof(NCard), nameof(NCard.SetPreviewTarget))]
internal static class KomachiDistanceCardIntentPreviewPatch
{
    [HarmonyPostfix]
    private static void ShowTargetIntentDamage(NCard __instance, Creature? creature)
    {
        KomachiTargetedIntentPreviewController.OnPreviewTargetChanged(__instance, creature);
        KomachiAttackDistancePreviewController.OnPreviewTargetChanged(__instance, creature);
    }
}

[HarmonyPatch(typeof(NCardPlay), "ShowMultiCreatureTargetingVisuals")]
internal static class KomachiDistanceCardMultiIntentPreviewPatch
{
    [HarmonyPostfix]
    private static void ShowMultiTargetIntentDamage(NCardPlay __instance)
    {
        if (__instance.Holder?.CardNode is { } card)
        {
            KomachiTargetedIntentPreviewController.OnMultiTargetPreviewRequested(card);
            KomachiAttackDistancePreviewController.OnMultiTargetPreviewRequested(card);
        }
    }
}

[HarmonyPatch(typeof(NCardPlay), "HideTargetingVisuals")]
internal static class KomachiDistanceCardMultiIntentPreviewCleanupPatch
{
    [HarmonyPostfix]
    private static void ClearMultiTargetIntentDamage(NCardPlay __instance)
    {
        if (__instance.Holder?.CardNode is { } card)
        {
            KomachiTargetedIntentPreviewController.Clear(card);
            KomachiAttackDistancePreviewController.HideMultiTarget(card);
        }
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard.OnReturnedFromPool))]
internal static class KomachiDistanceCardIntentPreviewPoolCleanupPatch
{
    [HarmonyPostfix]
    private static void ClearTargetIntentDamage(NCard __instance)
    {
        KomachiTargetedIntentPreviewController.Clear(__instance);
        KomachiAttackDistancePreviewController.Hide(__instance);
    }
}

[HarmonyPatch(typeof(NCard), "_ExitTree")]
internal static class KomachiDistanceCardIntentPreviewExitCleanupPatch
{
    [HarmonyPostfix]
    private static void ClearTargetIntentDamage(NCard __instance) =>
        KomachiTargetedIntentPreviewController.Clear(__instance);
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Title), MethodType.Getter)]
internal static class KomachiDistanceChoiceTitlePatch
{
    [HarmonyPostfix]
    private static void ShowSpecificChoiceName(CardModel __instance, ref string __result)
    {
        KomachiDistanceChoice choice = KomachiDistanceVisuals.GetChoice(__instance);
        if (choice != KomachiDistanceChoice.None)
        {
            __result = KomachiDistanceVisuals.GetChoiceTitle(choice);
        }
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Portrait), MethodType.Getter)]
internal static class KomachiDistanceChoicePortraitPatch
{
    [HarmonyPostfix]
    private static void ShowSpecificChoiceArt(CardModel __instance, ref Texture2D __result)
    {
        if (KomachiDistanceVisuals.TryGetPortrait(__instance, out Texture2D? texture)
            && texture is not null)
        {
            __result = texture;
        }
    }
}

[HarmonyPatch(typeof(NPower), "Reload")]
internal static class KomachiDistancePowerReloadPatch
{
    [HarmonyPostfix]
    private static void ColorDistanceIcon(NPower __instance) =>
        KomachiDistanceVisuals.ApplyDistanceColor(__instance);
}

[HarmonyPatch(typeof(NPower), "OnDisplayAmountChanged")]
internal static class KomachiDistancePowerAmountChangedPatch
{
    [HarmonyPostfix]
    private static void RecolorDistanceIcon(NPower __instance) =>
        KomachiDistanceVisuals.ApplyDistanceColor(__instance);
}
