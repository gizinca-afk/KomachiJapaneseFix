using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.addons.mega_text;
using STS_Komachi_Onozuka.STS_Komachi_OnozukaCode.Cards;

namespace KomachiJapaneseFix;

// Match the Mokou/Keine presentation: preserve the original credit data, but
// render it as one unframed line above an inspected card. Ordinary artist hover
// tips are removed from combat, grids, rewards, and event previews.
public static class KomachiArtistCredit
{
    public const string LabelName = "KomachiJapaneseFixArtistCredit";
    public const string TipPrefix =
        "LocString with Title=static_hover_tips.KOMACHI-ARTIST-TITLE and Description=cards.STS_KOMACHI_ONOZUKA-";
    public const string DisplaceKeywordId =
        "card_keywords.STS_KOMACHI_ONOZUKA-DISPLACE.";
    private const float ScreenMargin = 8f;

    private static readonly FieldInfo CardField =
        AccessTools.Field(typeof(NInspectCardScreen), "_card")
        ?? throw new MissingFieldException(typeof(NInspectCardScreen).FullName, "_card");

    private static readonly ConditionalWeakTable<NHoverTipSet, CreditState> Credits = new();

    private sealed class CreditState(NInspectCardScreen screen, HoverTip[] credits)
    {
        internal NInspectCardScreen Screen { get; } = screen;
        internal HoverTip[] Tips { get; } = credits;
        internal MegaLabel? Label { get; set; }
        internal string? Text { get; set; }
    }

    public static bool IsArtistTip(IHoverTip tip) =>
        tip is HoverTip
        && tip.Id is { } id
        && id.StartsWith(TipPrefix, StringComparison.Ordinal)
        && id.EndsWith(".artist", StringComparison.Ordinal);

    // "間合い変更" only restates that a card changes distance.  Keep the
    // useful "間合い" power tooltip, but omit this redundant keyword box.
    public static bool IsSuppressedMechanicalTip(IHoverTip tip) =>
        tip is HoverTip
        && tip.Id is { } id
        && id.Contains(DisplaceKeywordId, StringComparison.Ordinal);

    public static (IHoverTip[] Remaining, HoverTip[] Credits) SplitTips(
        IEnumerable<IHoverTip> tips)
    {
        IHoverTip[] materialized = tips.ToArray();
        return (
            materialized.Where(tip =>
                !IsArtistTip(tip) && !IsSuppressedMechanicalTip(tip)).ToArray(),
            materialized.Where(IsArtistTip).Cast<HoverTip>().ToArray());
    }

    internal static void Filter(
        NHoverTipSet set,
        Control owner,
        ref IEnumerable<IHoverTip> hoverTips)
    {
        var split = SplitTips(hoverTips);
        hoverTips = split.Remaining;

        // Filter before the game measures tooltip boxes. Exact Komachi IDs keep
        // identically titled credits from other mods untouched.
        MegaLabel? existingLabel = set.GetNodeOrNull<MegaLabel>(LabelName);
        if (IsLive(existingLabel))
        {
            existingLabel!.Visible = false;
        }
        Credits.Remove(set);
        if (split.Credits.Length > 0 && owner is NInspectCardScreen screen)
        {
            CreditState state = new(screen, split.Credits)
            {
                Label = IsLive(existingLabel) ? existingLabel : null,
            };
            Credits.Add(set, state);
        }
    }

    private static bool IsLive(Node? node) =>
        node is not null
        && GodotObject.IsInstanceValid(node)
        && !node.IsQueuedForDeletion()
        && node.IsInsideTree();

    internal static void Refresh(NHoverTipSet set)
    {
        if (!Credits.TryGetValue(set, out CreditState? state) || !IsLive(set))
        {
            return;
        }

        NInspectCardScreen screen = state.Screen;
        if (!IsLive(screen)
            || !screen.Visible
            || CardField.GetValue(screen) is not NCard card
            || !IsLive(card)
            || card.Model is not STS_Komachi_OnozukaCard)
        {
            if (IsLive(state.Label))
            {
                state.Label!.Visible = false;
            }
            return;
        }

        HoverTip source = state.Tips.FirstOrDefault(tip => tip.Id.EndsWith(
            " and Description=cards." + card.Model.Id.Entry + ".artist",
            StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(source.Description))
        {
            if (IsLive(state.Label))
            {
                state.Label!.Visible = false;
            }
            return;
        }

        if (!IsLive(state.Label))
        {
            if (screen.GetNodeOrNull<MegaLabel>("%ShowUpgradeLabel") is not { } template
                || template.Duplicate() is not MegaLabel label)
            {
                return;
            }

            label.Name = LabelName;
            label.UniqueNameInOwner = false;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.ZIndex = 1;
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            set.AddChild(label);
            state.Label = label;
        }

        MegaLabel credit = state.Label!;
        credit.Visible = true;
        string text = source.Title + "：" + source.Description.Trim();
        if (state.Text != text)
        {
            credit.SetTextAutoSize(text);
            credit.ResetSize();
            state.Text = text;
        }

        Rect2 cardRect = GetCardBounds(card);
        Rect2? prompt = null;
        foreach (string path in new[] { "%Upgrade", "%ShowUpgradeLabel" })
        {
            Control? control = screen.GetNodeOrNull<Control>(path);
            if (!IsLive(control) || !control!.IsVisibleInTree())
            {
                continue;
            }
            prompt = prompt is { } previous
                ? previous.Merge(control.GetGlobalRect())
                : control.GetGlobalRect();
        }

        Rect2 labelRect = credit.GetGlobalRect();
        Vector2 position = CalculatePosition(
            cardRect,
            labelRect.Size,
            prompt,
            set.GetViewportRect().Size);
        credit.GlobalPosition += position - labelRect.Position;
    }

    // Pure geometry is also exercised by isolated regression tests.
    public static Vector2 CalculatePosition(
        Rect2 card,
        Vector2 label,
        Rect2? upgradePrompt,
        Vector2 viewport)
    {
        float x = card.GetCenter().X - label.X * 0.5f;
        float y = upgradePrompt is { } prompt
            ? card.GetCenter().Y * 2f - prompt.GetCenter().Y - label.Y * 0.5f
            : card.Position.Y - label.Y - 5f;
        return new Vector2(
            Mathf.Clamp(x, ScreenMargin, Mathf.Max(ScreenMargin, viewport.X - ScreenMargin - label.X)),
            Mathf.Clamp(y, ScreenMargin, Mathf.Max(ScreenMargin, viewport.Y - ScreenMargin - label.Y)));
    }

    internal static Rect2 GetCardBounds(NCard card)
    {
        Transform2D transform = card.GetGlobalTransform();
        Vector2 half = NCard.defaultSize * 0.5f;
        Vector2 a = transform * -half;
        Vector2 b = transform * new Vector2(half.X, -half.Y);
        Vector2 c = transform * new Vector2(-half.X, half.Y);
        Vector2 d = transform * half;
        Vector2 start = new(
            Mathf.Min(Mathf.Min(a.X, b.X), Mathf.Min(c.X, d.X)),
            Mathf.Min(Mathf.Min(a.Y, b.Y), Mathf.Min(c.Y, d.Y)));
        Vector2 end = new(
            Mathf.Max(Mathf.Max(a.X, b.X), Mathf.Max(c.X, d.X)),
            Mathf.Max(Mathf.Max(a.Y, b.Y), Mathf.Max(c.Y, d.Y)));
        return new Rect2(start, end - start);
    }
}

[HarmonyPatch(typeof(NHoverTipSet), "Init")]
internal static class KomachiArtistTipFilterPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        NHoverTipSet __instance,
        Control owner,
        ref IEnumerable<IHoverTip> hoverTips) =>
        KomachiArtistCredit.Filter(__instance, owner, ref hoverTips);
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.SetAlignment))]
internal static class KomachiInspectCreditPatch
{
    [HarmonyPostfix]
    private static void Postfix(NHoverTipSet __instance) =>
        KomachiArtistCredit.Refresh(__instance);
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet._Process))]
internal static class KomachiInspectCreditPositionPatch
{
    [HarmonyPostfix]
    private static void Postfix(NHoverTipSet __instance) =>
        KomachiArtistCredit.Refresh(__instance);
}
