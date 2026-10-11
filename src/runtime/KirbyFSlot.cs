using dc.en;
using dc.en.inter;
using dc.tool;
using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// The F slot (#102). Limited-use abilities (Crash, Paint, Mike, Cook) are never
/// swallowed: their floor ability item is taken with the host Interact key and
/// kept here, beside Kirby's main copy. F (the host Heal binding, action 7, freed
/// by #101) fires the stored ability through the original machine
/// (<see cref="FSequence"/>): the ROM's own ability routine switches Kirby to it,
/// the attack button fires it once, the ROM returns him to base form after that
/// use, and the same routine gives his main copy back. The slot is drawn in the
/// HUD's own F slot (the flask's), with Mike's remaining shouts as its count.
/// </summary>
internal static class KirbyFSlot
{
    private static bool previousF, hudDirty = true;
    private static string? shownAbility;
    private static int shownUses = -1;
    private static nint shownSkill, shownTile;

    /// <summary>The stored ability's item: its level, "+" and affixes as for any ability item.</summary>
    public static InventItem? Item { get; private set; }
    public static FSequence Sequence { get; } = new();
    /// <summary>The F binding is held this host frame (read by KirbyInput).</summary>
    public static bool Held { get; set; }

    /// <summary>Lab-only observer of each F press outcome: (stored ability, outcome).</summary>
    internal static Action<string, FOutcome>? TestFinished;
    /// <summary>Lab-only observer of each take: (taken ability, replaced ability or null).</summary>
    internal static Action<string, string?>? TestTaken;

    public static void Install()
    {
        // The host redraws its F slot (the flask) when the HUD is built or the flask changes.
        dc.ui.Hook_HUD.setHealings += (orig, hud, healings, max) => { orig(hud, healings, max); hudDirty = true; };
    }

    /// <summary>Interact on a floor limited-use ability item: take it into the F slot.</summary>
    public static bool Take(Hero by, ItemDrop drop)
    {
        if (KirbyMod.Instance?.ActiveSession is not { } session || session.Hero.HashlinkPointer != by.HashlinkPointer ||
            KirbyAbilityItems.AbilityOf(drop.item) is not { } ability || !LimitedAbilities.IsLimited(ability)) return false;
        if (Sequence.Active)
        {
            drop.popError("Wait for the ability to finish".AsHaxeString(), null);
            return true;
        }
        var previous = Item;
        string? replaced = session.Rules.Stored.Take(ability);
        Item = drop.item;
        drop.removeLargeTip(HaxeProxy.Runtime.Ref<bool>.In(true));
        drop.destroy();
        // The replaced ability goes back to the floor, as Dead Cells swaps a weapon.
        if (replaced is not null && previous is not null) KirbyAbilityItems.DropOnFloor(by, previous);
        hudDirty = true;
        TestTaken?.Invoke(ability, replaced);
        return true;
    }

    /// <summary>
    /// Advance an F press by one source frame, before the source steps. Returns
    /// the buttons left to the player, whether an F press owns Kirby's copy this
    /// frame (the provider's copy tracking then leaves the rules alone), and
    /// whether the attack button is pressed for the stored ability.
    /// </summary>
    public static (ushort Mask, bool Busy, bool PressAttack) Step(KirbySession session, NdsRuntime nds, ushort mask)
    {
        bool pressed = Held && !previousF;
        previousF = Held;
        if (!NdsRuntime.CanChangeAbility) return (mask, false, false);
        if (!Sequence.Active && pressed && session.Rules.Stored.Ability is { } stored)
            Sequence.Begin(Byte(stored), session.Rules.AbilityId is { } main ? Byte(main) : FSequence.BaseForm);
        if (!Sequence.Active) return (mask, false, false);
        // Kirby's own attack, discard and helper buttons wait for the press to end.
        mask &= unchecked((ushort)~(2 | 4 | 1024));
        // Free as the original give-ability routine counts it: standing, walking, airborne or swimming.
        bool free = (nds.Action >> 8) is 4 or 6 or 7 or 8;
        var command = Sequence.Advance(nds.State.Ability, nds.AbilityChangePending, free, nds.RequestAbility);
        if (command.LimitShouts) nds.SetMikeShouts(1);
        if (command.Finished is { } outcome) Finish(session, outcome);
        return (mask, true, command.PressAttack);
    }

    private static void Finish(KirbySession session, FOutcome outcome)
    {
        string? stored = session.Rules.Stored.Ability;
        if (outcome.Used && session.Rules.Stored.Spend()) Item = null;
        // The main copy could not come back (for example Kirby was held in a
        // recoil for ten seconds): it is lost, as a hit would lose it.
        if (!outcome.MainRestored && !session.Rules.IsBaseForm) session.DropAbility();
        hudDirty = true;
        if (stored is not null) TestFinished?.Invoke(stored, outcome);
    }

    private static byte Byte(string ability) => checked((byte)(Array.IndexOf(KirbyNdsProvider.Abilities, ability) + 2));

    /// <summary>Keep the item and the HUD slot in step with the rules; call once per host frame.</summary>
    public static void Update(KirbySession session)
    {
        var slot = session.Rules.Stored;
        if (slot.Ability is null) Item = null;
        else if (KirbyAbilityItems.AbilityOf(Item) != slot.Ability)
            Item = KirbyAbilityItems.Create(slot.Ability, session.Hero._level?.map?.lootLevel ?? 1);
        DrawHud(session.Hero, slot.Ability, slot.Uses);
    }

    private static void DrawHud(Hero hero, string? ability, int uses)
    {
        if (hero._level?.game?.hud?.skillPotion is not { } skill) return;
        // The host also hides or rebuilds its F slot with no flask call (a new
        // level's HUD), so the slot is checked as drawn, not only on events (#109).
        bool intact = ability is null || skill.HashlinkPointer == shownSkill && skill.visible &&
            (shownTile == 0 || skill.icon?.tile?.HashlinkPointer == shownTile);
        if (!hudDirty && intact && ability == shownAbility && uses == shownUses) return;
        hudDirty = false;
        shownAbility = ability;
        shownUses = uses;
        shownSkill = skill.HashlinkPointer;
        shownTile = 0;
        if (ability is null)
        {
            skill.setIcon(null!);
            skill.disableCount();
            return;
        }
        skill.visible = true;
        skill.setItemIcon(AbilityItems.ItemId(ability).AsHaxeString());
        if (LimitedAbilities.Uses(ability) > 1) skill.setCount(uses, null);
        else skill.disableCount();
        shownTile = skill.icon?.tile?.HashlinkPointer ?? 0;
    }

    /// <summary>
    /// A new session or machine: the HUD is redrawn and no F press is running, unless
    /// <paramref name="keepPress"/> leaves one to finish on the machine that started it.
    /// </summary>
    public static void Reset(bool keepPress = false)
    {
        if (!keepPress) Sequence.Reset();
        previousF = Held = false;
        hudDirty = true;
    }

    public static FSlotSnapshot? Capture(KirbySession session)
    {
        var slot = session.Rules.Stored;
        if (slot.Ability is null && !Sequence.Active) return null;
        var progress = Sequence.Capture();
        return new FSlotSnapshot
        {
            Ability = slot.Ability, Uses = slot.Uses,
            ItemLevel = Item?.getRawItemLevel() ?? 1, QualityUps = Item?.getUpgradeLevel() ?? 0,
            Legendary = Item?.hasAffix("Legendary".AsHaxeString()) ?? false,
            Affixes = KirbyAbilityItems.Affixes(Item),
            Step = progress.Step, Stored = progress.Stored, Main = progress.Main,
            RestoreRequested = progress.RestoreRequested, Used = progress.Used,
        };
    }

    public static void Restore(KirbySession session, FSlotSnapshot? saved)
    {
        Reset();
        session.Rules.Stored.Restore(saved?.Ability, saved?.Uses ?? 0);
        Item = saved?.Ability is { } ability
            ? KirbyAbilityItems.Create(ability, saved.ItemLevel, saved.QualityUps, saved.Legendary, saved.Affixes)
            : null;
        if (saved is not null)
            Sequence.Resume(new FProgress(saved.Step, saved.Stored, saved.Main, saved.RestoreRequested, saved.Used));
    }
}

/// <summary>The F slot and any F press in progress, in the Kirby save.</summary>
internal sealed class FSlotSnapshot
{
    public string? Ability { get; set; }
    public int Uses { get; set; }
    public int ItemLevel { get; set; } = 1;
    public int QualityUps { get; set; }
    public bool Legendary { get; set; }
    public string[] Affixes { get; set; } = [];
    public FStep Step { get; set; }
    public byte Stored { get; set; }
    public byte Main { get; set; } = FSequence.BaseForm;
    public bool RestoreRequested { get; set; }
    public bool Used { get; set; }
}
