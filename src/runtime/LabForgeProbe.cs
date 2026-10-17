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
/// KIRBY_LAB_FORGE_LEGENDARY=1 gives Kirby a shield first, which the forge must
/// list beside the hidden Rusty Sword, and makes the copy's item Legendary, as
/// an Elite's copy is. With the copy, the forge must list both items; the
/// Legendary item's upgrade must be refused (the forge's own check), its reroll
/// must keep it Legendary, and the shield's upgrade must add a "+".
/// KIRBY_LAB_FORGE_NPC=1 takes a level exit to the passage after the swallow
/// and opens the forge there through the forge NPC's own activation, as a
/// player does (KIRBY_LAB_EXIT picks the exit by destination). Its first
/// meeting cinematic is skipped by marking the NPC as met.
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
    // KIRBY_LAB_FORGE_LEGENDARY: a shield in a slot and a Legendary copy item.
    private static readonly bool legendary = Environment.GetEnvironmentVariable("KIRBY_LAB_FORGE_LEGENDARY") == "1";
    private static InventItem? shield;
    private static int copyChoice = -1, shieldChoice = -1, levelBefore, shieldLevelBefore;
    private static bool shieldListedBefore, legendaryRefineRefused, legendaryRerollKept, shieldRefined;
    // KIRBY_LAB_FORGE_NPC: the passage forge NPC opens the window.
    private static readonly bool viaNpc = Environment.GetEnvironmentVariable("KIRBY_LAB_FORGE_NPC") == "1";
    private static Exit? exit;
    private static nint levelAtExit;
    private static dc.en.inter.npc.SmallBlacksmith? npc;
    private static bool npcOpening, openedByNpc;

    internal static void Install(string output)
    {
        run = output;
        dc.tool.Hook_Tutorial.tryToStart += (_, _, _) => false;
        KirbyForge.TestUnfiltered = unfiltered;
        KirbyForge.TestListed = (id, shown) => listed.Add((id, shown));
        KirbyInput.TestOverride = Input;
        if (!viaNpc) return;
        // The window the NPC opens: its first item is listed from the forge's own constructor.
        dc.ui.Hook_ForgeUnderground.addItem += (orig, opened, entry, parent) =>
        {
            if (npcOpening && forge is null)
            {
                npcOpening = false;
                openedByNpc = true;
                forge = opened;
                forgeStep = "copy";
                forgeFrames = 0;
                listed.Clear();
            }
            return orig(opened, entry, parent);
        };
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
            case 0: // Only the starting Rusty Sword: the forge must list nothing (with a shield: the shield alone).
            {
                if (legendary && shield is null)
                {
                    // The normal inventory path, as a taken shield goes (LabCombatProbe).
                    shield = new InventItem(new InventItemKind.Weapon("StartShield".AsHaxeString()));
                    hero.inventory.add(shield);
                    if (hero.weaponsManager is dc.tool.hero.weap.BeheadedWeaponsManager manager)
                        manager.onEquippedItemsUpdated(Ref<bool>.In(false));
                    phaseAt = frame;
                    return 0;
                }
                if (legendary && t < 30) return 0;
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
                // With KIRBY_LAB_FORGE_LEGENDARY, Legendary as an Elite's copy item is (#56) and otherwise the same
                // item, so Legendary is the only difference from the item the forge upgraded in #58.
                item = KirbyAbilityItems.Create("Sword", 1, 1, legendary, []);
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
                if (viaNpc) { Next(frame, 10); return 0; }
                pendingOpen = "copy";
                Next(frame, 4);
                return 0;
            case 4: // The forge has closed and the game runs again.
                if (t >= 60) Finish(session, frame, results.ContainsKey("forgeWithCopy") ? null : "the forge never opened with the copy");
                return 0;
            case 10: // To the passage: stand beside a door, as LabLifecycleProbe does, and take it.
            {
                string? wanted = Environment.GetEnvironmentVariable("KIRBY_LAB_EXIT");
                var exits = new List<object>();
                var entities = hero._level.entities;
                for (int j = 0; j < entities.length; j++)
                    if (entities.getDyn(j) is Exit { destroyed: false } candidate)
                    {
                        string? destination = candidate.destLevel?.ToString();
                        exits.Add(new { destination, candidate.locked });
                        if (candidate.locked || string.IsNullOrEmpty(destination)) continue;
                        if (exit is null || (destination == wanted && exit.destLevel?.ToString() != wanted)) exit = candidate;
                    }
                results["exits"] = exits;
                if (exit is null) { Finish(session, frame, "no unlocked exit in the start level"); return 0; }
                levelAtExit = hero._level.HashlinkPointer;
                hero.setPosPixel((exit.cx + exit.xr - 1.5) * 24, (exit.cy + exit.yr) * 24);
                if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
                exit.onActivate(hero, false);
                Next(frame, 11);
                return 0;
            }
            case 11: // In the passage: find the forge NPC and stand beside it.
            {
                if (hero._level.HashlinkPointer == levelAtExit)
                {
                    if (t > 1800) Finish(session, frame, "the exit never led to another level");
                    return 0;
                }
                var npcs = new List<string>();
                var entities = hero._level.entities;
                for (int j = 0; j < entities.length; j++)
                    if (entities.getDyn(j) is Npc { destroyed: false } other)
                    {
                        npcs.Add(other.GetType().FullName ?? "");
                        if (other is dc.en.inter.npc.SmallBlacksmith smith) npc ??= smith;
                    }
                results["passage"] = new { destination = exit?.destLevel?.ToString(), npcs, money = hero._level.game.data.money };
                if (npc is null) { Finish(session, frame, "no forge NPC in this passage"); return 0; }
                hero.setPosPixel((npc.cx + npc.xr - 1.5) * 24, (npc.cy + npc.yr) * 24);
                if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
                hero.dx = hero.dy = hero.bdx = hero.bdy = 0;
                Next(frame, 12);
                return 0;
            }
            case 12: // Talk to it: its own activation opens the forge.
                if (t < 60) return 0;
                results["npc"] = new { npc!.cx, npc.cy, progressBefore = npc.progress, hero = new { hero.cx, hero.cy } };
                // A first meeting plays a cinematic before the forge; this lab profile has not met it yet.
                npc.progress = 1;
                npcOpening = true;
                npc.onActivate(hero, false);
                Next(frame, 4);
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
            shieldListedBefore = listed.Any(entry => entry.Id == "StartShield" && entry.Shown);
            results["forgeBefore"] = new { listed = Listed(listed), choices = choicesBefore, card = CardTexts() };
            Close(game);
            return;
        }
        if (legendary) { PollLegendary(game); return; }
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

    /// <summary>
    /// KIRBY_LAB_FORGE_LEGENDARY with the copy: the Legendary item's upgrade,
    /// then its reroll, then the shield's upgrade. Each listed item has two
    /// choices in listing order, its reroll and then its upgrade.
    /// </summary>
    private static void PollLegendary(dc.pr.Game game)
    {
        var data = game.data;
        switch (forgeFrames)
        {
            case 30:
                choicesWithCopy = forge!.choices.length;
                listedWithCopy = [.. listed];
                results["forgeWithCopy"] = new { listed = Listed(listed), choices = choicesWithCopy, card = CardTexts() };
                copyChoice = 2 * ShownIndex(AbilityItems.ItemId("Sword"));
                shieldChoice = 2 * ShownIndex("StartShield");
                moneyBefore = data.money;
                levelBefore = item!.getUpgradeLevel();
                results["beforeRefine"] = Describe(item);
                if (copyChoice >= 0) Choose(copyChoice + 1);
                return;
            case 45:
                // The forge's refine check refuses a Legendary item (ForgeUnderground.hx:328).
                legendaryRefineRefused = copyChoice >= 0 && data.money == moneyBefore && item!.getUpgradeLevel() == levelBefore &&
                    !item.hasAffix("ForgeRefined".AsHaxeString()) && item.hasAffix("Legendary".AsHaxeString());
                results["legendaryRefine"] = new { moneyBefore, moneyAfter = data.money, after = Describe(item!), card = CardTexts() };
                return;
            case 60:
                moneyBefore = data.money;
                cost = item!.getForgeRerollCost();
                affixesBefore = Rolled(item);
                if (copyChoice >= 0) Choose(copyChoice);
                return;
            case 75:
                rerollTookPrice = cost > 0 && data.money == moneyBefore - cost;
                affixesChanged = !Rolled(item!).SequenceEqual(affixesBefore);
                rerolledMark = item!.hasAffix("Rerolled".AsHaxeString());
                legendaryRerollKept = rerollTookPrice && item.hasAffix("Legendary".AsHaxeString()) && item.hasAffix("Colorless".AsHaxeString()) &&
                    item.getRawItemLevel() == 1 && item.getUpgradeLevel() == levelBefore &&
                    KirbyAbilityItems.CopyItem(game.hero)?.HashlinkPointer == item.HashlinkPointer;
                results["legendaryReroll"] = new
                {
                    cost, moneyBefore, moneyAfter = data.money, affixesBefore, affixesAfter = Rolled(item), after = Describe(item), card = CardTexts(),
                };
                return;
            case 90:
                moneyBefore = data.money;
                cost = shield?.getForgeRefineCost() ?? 0;
                shieldLevelBefore = shield?.getUpgradeLevel() ?? -1;
                results["shieldBefore"] = shield is null ? null : Describe(shield);
                if (shieldChoice >= 0) Choose(shieldChoice + 1);
                return;
            case 105:
                // The host prices it from the item's own cost, which is 0 for the starting shield (run 20261016-021905).
                shieldRefined = shield is not null && shieldChoice >= 0 && data.money == moneyBefore - cost &&
                    shield.getUpgradeLevel() == shieldLevelBefore + 1 && shield.hasAffix("ForgeRefined".AsHaxeString());
                results["shieldRefine"] = new { cost, moneyBefore, moneyAfter = data.money, after = shield is null ? null : Describe(shield), card = CardTexts() };
                return;
            case 120:
                Close(game);
                return;
        }
    }

    /// <summary>The position of an item among the listed ones, or -1.</summary>
    private static int ShownIndex(string id)
    {
        int index = 0;
        foreach (var entry in listed)
        {
            if (!entry.Shown) continue;
            if (entry.Id == id) return index;
            index++;
        }
        return -1;
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
        // With KIRBY_LAB_FORGE_LEGENDARY the shield is listed too.
        bool onlyCopyListed = listedWithCopy.Any(entry => entry.Shown) &&
            listedWithCopy.All(entry => entry.Shown == (entry.Id == AbilityItems.ItemId("Sword") || (legendary && entry.Id == "StartShield")));
        bool before = rustySwordInSlot && rustySwordForgeable &&
            (unfiltered ? rustySwordListed && choicesBefore == (legendary ? 4 : 2)
                : !rustySwordListed && (legendary ? shieldListedBefore && choicesBefore == 2 : choicesBefore == 0));
        bool reached = !viaNpc || openedByNpc;
        bool passed = error is null && before && slotHoldsItem && onlyCopyListed && reached && copyKept && (legendary
            ? choicesWithCopy == 4 && legendaryRefineRefused && legendaryRerollKept && affixesChanged && rerolledMark && shieldRefined
            : choicesWithCopy == 2 && rerollTookPrice && rerollKept && affixesChanged && rerolledMark &&
              refineTookPrice && refineAddedPlus && refinedMark && bonusRose && cardShowsBonus);
        File.WriteAllText(Path.Combine(run!, "forge.json"), JsonSerializer.Serialize(new
        {
            unfiltered, legendary, viaNpc, openedByNpc, error, phase, results, rustySwordInSlot, rustySwordForgeable, rustySwordListed,
            shieldListedBefore, choicesBefore, slotHoldsItem, choicesWithCopy, onlyCopyListed, rerollTookPrice, rerollKept, affixesChanged,
            rerolledMark, refineTookPrice, refineAddedPlus, refinedMark, bonusRose, cardShowsBonus, legendaryRefineRefused,
            legendaryRerollKept, shieldRefined, copyKept, itemAfter = item is null ? null : Describe(item), passed,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
