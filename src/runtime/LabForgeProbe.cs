using System.Text.Json;
using dc.en;
using dc.en.inter;
using dc.tool;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Kirby.Runtime;

/// <summary>
/// Opt-in live check of the forge with ability items (#58). After a new run
/// settles, Kirby holds only the starting Rusty Sword; the probe opens the
/// passage forge through the constructor the forge NPC's <c>openUI</c> uses, and
/// it must list nothing (KIRBY_LAB_FORGE_UNFILTERED=1 turns the Kirby filter off
/// for comparison: the host then lists the sword). Kirby then inhales and
/// swallows a Sword ability item at level I with one "+" and a host affix. With
/// 50,000 gold the forge must list that item alone. The probe picks its reroll,
/// then its upgrade, as a click does (select, then the host's own validate): the
/// reroll must keep the item, its level, "+" and Colorless and change its
/// affixes, the upgrade must add a "+", and each must take its price. The forge
/// then closes as its cancel key does, and the same item must still be the
/// copy's, in the slot and on the HUD.
/// </summary>
internal static class LabForgeProbe
{
    private static string? run;
    private static uint? start, phaseAt, finishedAt;
    private static int phase;
    private static readonly bool unfiltered = Environment.GetEnvironmentVariable("KIRBY_LAB_FORGE_UNFILTERED") == "1";
    private static dc.ui.ForgeUnderground? forge;
    private static string? pendingOpen, forgeStep;
    private static int forgeFrames;
    private static readonly List<(string Id, bool Shown)> listed = [];
    private static readonly Dictionary<string, object?> results = [];
    private static InventItem? item;
    private static int moneyBefore, cost;
    private static string[] affixesBefore = [];
    private static double bonusBefore;
    // What the pass criteria read.
    private static bool rustySwordInSlot, rustySwordForgeable, rustySwordListed, slotHoldsItem, rerollTookPrice, rerollKept,
        affixesChanged, rerolledMark, refineTookPrice, refineAddedPlus, refinedMark, bonusRose, cardShowsBonus;
    private static int choicesBefore = -1, choicesWithCopy = -1;
    private static List<(string Id, bool Shown)> listedWithCopy = [];

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyForge.TestUnfiltered = unfiltered;
        KirbyForge.TestListed = (id, shown) => listed.Add((id, shown));
        KirbyInput.TestOverride = Input;
    }

    private static string?[] Ids(Hero hero, Func<Inventory, int, InventItem?> get, int count) =>
        [.. Enumerable.Range(0, count).Select(i => get(hero.inventory, i)?.getItemKind()?.ToString())];

    private static object[] Listed(IEnumerable<(string Id, bool Shown)> entries) =>
        [.. entries.Select(entry => new { id = entry.Id, shown = entry.Shown })];

    private static string[] AllAffixes(InventItem item)
    {
        var result = new List<string>();
        for (int i = 0; i < item.affixes.length; i++)
            if (item.affixes.getDyn(i)?.ToString() is { } affix) result.Add(affix);
        return [.. result];
    }

    /// <summary>The item's own affixes: not Colorless, its quality, or the forge's own marks.</summary>
    private static string[] Rolled(InventItem item) =>
        [.. KirbyAbilityItems.Affixes(item).Where(a => a is not ("Rerolled" or "ForgeRefined")).Order()];

    private static object Describe(InventItem item) => new
    {
        pointer = item.HashlinkPointer.ToString("x"), item = item.getItemKind()?.ToString(), level = item.getRawItemLevel(),
        quality = item.getUpgradeLevel(), adjusted = item.getAdjustedItemLevel(), affixes = AllAffixes(item),
        bonus = item.getDamageBonus(),
    };

    /// <summary>Every text of the forge's item card.</summary>
    private static List<string> CardTexts()
    {
        var texts = new List<string>();
        void Collect(dc.h2d.Object node, int depth)
        {
            if (node is dc.ui.Text text) texts.Add(text.text?.ToString() ?? "");
            if (depth < 8)
                for (int i = 0; i < node.children.length; i++)
                    if (node.children.getDyn(i) is dc.h2d.Object child) Collect(child, depth + 1);
        }
        if (forge?.itemDesc is { } card) Collect(card, 0);
        return texts;
    }

    private static ushort? Input(KirbySession session)
    {
        var machine = KirbyMod.Instance?.NativeProvider?.Machine;
        if (machine is null || run is null || finishedAt is not null) return 0;
        var hero = session.Hero;
        uint frame = machine.State.Frame;
        start ??= frame;
        // The forge pauses the game; Poll drives it, on host frames.
        if (forge is not null || pendingOpen is not null) return 0;
        if (!hero.awake || hero._level.game.hasCinematic() || frame - start.Value < 180) return 0;
        uint t = phaseAt is { } at ? frame - at : 0;
        switch (phase)
        {
            case 0: // Only the starting Rusty Sword: the forge must list nothing.
            {
                var slots = Ids(hero, (inventory, i) => inventory.getEquippedWeaponOn(i), hero.inventory.nbWeapons);
                for (int i = 0; i < hero.inventory.nbWeapons; i++)
                    if (hero.inventory.getEquippedWeaponOn(i) is { } held && held.getItemKind()?.ToString() == "StartSword")
                    {
                        rustySwordInSlot = true;
                        rustySwordForgeable = held.canBeForged();
                    }
                results["slotsBefore"] = new
                {
                    weapons = slots, backpack = hero.inventory.getBackpackWeapon()?.getItemKind()?.ToString(),
                    skills = Ids(hero, (inventory, i) => inventory.getActiveOn(i), hero.inventory.nbActives),
                    rustySwordForgeable,
                };
                results["clearSpot"] = LabAbilityItemProbe.MoveToClearFloor(hero);
                pendingOpen = "before";
                Next(frame, 1);
                return 0;
            }
            case 1: // A Sword ability item, level I with one "+" and a host affix, in front of Kirby.
            {
                if (t < 30) return 0;
                int dir = hero.dir >= 0 ? 1 : -1;
                item = KirbyAbilityItems.Create("Sword", 1);
                item.addAffix("QualityUp".AsHaxeString(), Ref<bool>.In(true));
                item.addAffix("BleedOnHit".AsHaxeString(), Ref<bool>.In(true));
                var drop = new ItemDrop(hero._level, hero.cx + dir, hero.cy, item, true, Ref<bool>.In(false));
                drop.init();
                results["spawned"] = Describe(item);
                Next(frame, 2);
                return 0;
            }
            case 2: // Hold the original inhale until the item is in the mouth.
                if (LabAbilityItemProbe.FloorDrop(hero, item) is null) { results["inMouth"] = t; Next(frame, 3); return 0; }
                if (t > 300) { Finish(session, frame, "the item never reached the mouth"); return 0; }
                return t >= 30 ? (ushort)2 : (ushort)0;
            case 3: // Swallow with DOWN; the copy and its item must reach the slot. Then open the forge with gold.
                if (t < 200) return t is >= 10 and < 16 ? (ushort)128 : (ushort)0;
                slotHoldsItem = LabAbilityItemProbe.SlotHolds(hero, item);
                results["copied"] = new { ability = session.Rules.AbilityId, slotHoldsItem, hud = LabAbilityItemProbe.HudItems(hero) };
                if (!slotHoldsItem) { Finish(session, frame, "the swallowed item is not in a slot"); return 0; }
                hero._level.game.data.money = 50000;
                pendingOpen = "copy";
                Next(frame, 4);
                return 0;
            case 4: // The forge has closed and the game runs again.
                if (t >= 60) Finish(session, frame, null);
                return 0;
        }
        return 0;
    }

    internal static void Poll()
    {
        if (run is null || finishedAt is not null) return;
        if (pendingOpen is not null && forge is null)
        {
            listed.Clear();
            forgeStep = pendingOpen;
            pendingOpen = null;
            forge = new dc.ui.ForgeUnderground();
            forgeFrames = 0;
            return;
        }
        if (forge is null || dc.pr.Game.Class.ME is not { } game) return;
        forgeFrames++;
        if (forgeStep == "before")
        {
            if (forgeFrames < 30) return;
            choicesBefore = forge.choices.length;
            rustySwordListed = listed.Any(entry => entry.Id == "StartSword" && entry.Shown);
            results["forgeBefore"] = new { listed = Listed(listed), choices = choicesBefore, card = CardTexts() };
            Close(game);
            return;
        }
        var data = game.data;
        switch (forgeFrames)
        {
            case 30: // The ability item alone; pick its reroll (the first of its two choices).
                choicesWithCopy = forge.choices.length;
                listedWithCopy = [.. listed];
                results["forgeWithCopy"] = new { listed = Listed(listed), choices = choicesWithCopy, card = CardTexts() };
                moneyBefore = data.money;
                cost = item!.getForgeRerollCost();
                affixesBefore = Rolled(item);
                results["beforeReroll"] = Describe(item);
                Choose(0);
                return;
            case 45:
            {
                rerollTookPrice = cost > 0 && data.money == moneyBefore - cost;
                rerollKept = KirbyAbilityItems.AbilityOf(item) == "Sword" && item!.getRawItemLevel() == 1 &&
                    item.getUpgradeLevel() == 1 && item.hasAffix("Colorless".AsHaxeString()) &&
                    !item.hasAffix("Legendary".AsHaxeString()) && KirbyAbilityItems.CopyItem(game.hero)?.HashlinkPointer == item.HashlinkPointer;
                affixesChanged = !Rolled(item!).SequenceEqual(affixesBefore);
                rerolledMark = item!.hasAffix("Rerolled".AsHaxeString());
                results["reroll"] = new
                {
                    cost, moneyBefore, moneyAfter = data.money, affixesBefore, affixesAfter = Rolled(item), after = Describe(item),
                    card = CardTexts(),
                };
                return;
            }
            case 60: // Its upgrade, the second choice.
                moneyBefore = data.money;
                cost = item!.getForgeRefineCost();
                bonusBefore = item.getDamageBonus();
                Choose(1);
                return;
            case 75:
            {
                refineTookPrice = cost > 0 && data.money == moneyBefore - cost;
                refineAddedPlus = item!.getUpgradeLevel() == 2 && item.hasAffix("Colorless".AsHaxeString()) &&
                    KirbyAbilityItems.CopyItem(game.hero)?.HashlinkPointer == item.HashlinkPointer;
                refinedMark = item.hasAffix("ForgeRefined".AsHaxeString());
                bonusRose = item.getDamageBonus() > bonusBefore;
                var card = CardTexts();
                cardShowsBonus = card.Any(text => text.Contains(AbilityItems.InfoLine(item.getDamageBonus()), StringComparison.Ordinal));
                results["refine"] = new
                {
                    cost, moneyBefore, moneyAfter = data.money, bonusBefore, bonusAfter = item.getDamageBonus(), after = Describe(item), card,
                };
                return;
            }
            case 90:
                Close(game);
                return;
        }
    }

    /// <summary>Pick a choice as a click on it does: select it, then the forge's own validate.</summary>
    private static void Choose(int index)
    {
        forge!.currentIdx = index;
        forge.select(true, Ref<bool>.In(false));
        forge.onValidate();
    }

    /// <summary>The forge's own cancel path: resume the game, release the controller, end the process.</summary>
    private static void Close(dc.pr.Game game)
    {
        game.resume();
        forge!.controller?.dispose(Ref<bool>.In(false));
        forge.controller = null!;
        forge.destroy();
        forge = null;
    }

    private static void Next(uint frame, int next)
    {
        phase = next;
        phaseAt = frame;
    }

    private static void Finish(KirbySession session, uint frame, string? error)
    {
        finishedAt = frame;
        var hero = session.Hero;
        bool copyKept = session.Rules.AbilityId == "Sword" && item is not null && LabAbilityItemProbe.SlotHolds(hero, item) &&
            LabAbilityItemProbe.HudItems(hero).Contains(AbilityItems.ItemId("Sword")) &&
            KirbyAbilityItems.CopyItem(hero)?.HashlinkPointer == item.HashlinkPointer;
        bool onlyCopyListed = listedWithCopy.Any(entry => entry.Shown) &&
            listedWithCopy.All(entry => entry.Shown == (entry.Id == AbilityItems.ItemId("Sword")));
        bool before = rustySwordInSlot && rustySwordForgeable &&
            (unfiltered ? rustySwordListed && choicesBefore == 2 : !rustySwordListed && choicesBefore == 0);
        bool passed = error is null && before && slotHoldsItem && choicesWithCopy == 2 && onlyCopyListed &&
            rerollTookPrice && rerollKept && affixesChanged && rerolledMark &&
            refineTookPrice && refineAddedPlus && refinedMark && bonusRose && cardShowsBonus && copyKept;
        File.WriteAllText(Path.Combine(run!, "forge.json"), JsonSerializer.Serialize(new
        {
            unfiltered, error, phase, results, rustySwordInSlot, rustySwordForgeable, rustySwordListed, choicesBefore,
            slotHoldsItem, choicesWithCopy, onlyCopyListed, rerollTookPrice, rerollKept, affixesChanged, rerolledMark,
            refineTookPrice, refineAddedPlus, refinedMark, bonusRose, cardShowsBonus, copyKept,
            itemAfter = item is null ? null : Describe(item), passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
