using System.Globalization;
using dc.en;
using dc.en.inter;
using dc.tool;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// Kirby ability items (#55). While Kirby plays, a new floor item holding a
/// host weapon other than a shield, or a skill, becomes one of the mod's
/// colorless KirbyAbility&lt;Name&gt; items. It cannot be picked up: the original
/// inhale pulls it in through a paired proxy (KirbyEnemies) and the original
/// swallow copies its ability. While copied, the ability's item sits in a
/// weapon slot, so host screens that read the slots see a real item.
/// </summary>
internal static class KirbyAbilityItems
{
    /// <summary>An ability item that went into Kirby's mouth, and where it lay.</summary>
    internal sealed record Held(InventItem Item, int Cx, int Cy, nint Level);

    private static string outfitId = "";
    /// <summary>Lab-only observer of each replaced floor item: (original item ID, ability).</summary>
    internal static Action<string, string>? TestReplaced;
    /// <summary>Lab-only observer of each slot change: (ability or null, item level, came from the floor).</summary>
    internal static Action<string?, int, bool>? TestSlot;
    /// <summary>Lab-only observer of each copy attack press that used the item's affixes.</summary>
    internal static Action<InventItem>? TestUsed;
    /// <summary>Lab-only observer of each item put back on the floor: (floor item, target cell).</summary>
    internal static Action<ItemDrop, int, int>? TestReturned;

    public static void Install(string outfit)
    {
        outfitId = outfit;
        Hook_ItemDrop.initItem += InitItem;
        InstallRewards();
        // Weapon.create throws for an item ID it does not know. An ability
        // item's weapon object is the starting sword's class over its own
        // weapon row; Kirby's weapon hook never lets it attack.
        Hook__Weapon.create += (orig, hero, item) =>
            IsAbilityItem(item) ? new dc.tool.weap.StartSword(hero, item) : orig(hero, item);
        // Every host item card (floor tip, pause card, forge) describes an
        // ability item with the same text as the copied ability's pause card.
        Hook_InventItem.getGameplayDesc += (orig, item, level) =>
            AbilityOf(item) is { } ability ? AbilityDescriptions.Describe(ability).AsHaxeString() : orig(item, level);
        dc.ui.Hook_NewItemDesc.onResize += (orig, card, width, height) =>
        {
            orig(card, width, height);
            if (IsAbilityItem(card.ii)) KirbyHud.ReplaceInfoLine(card, AbilityItems.InfoLine(card.ii.getDamageBonus()));
        };
        Hook_ItemDrop.onActivate += (orig, drop, by, longPress) =>
        {
            // A short press would show the host's "not useful" message; say how
            // to take it instead. A long press still recycles it for gold.
            // A long press normally recycles it; with the host's long-press pickup
            // option it would pick it up instead, which Kirby never does.
            // A limited-use ability is never swallowed: Interact takes it into the F slot (#102).
            if ((!longPress || drop.longPressPickup) && IsAbilityItem(drop.item) && !drop.destroyed)
            {
                if (IsLimited(drop.item) && KirbyFSlot.Take(by, drop)) return;
                drop.popError("Inhale and swallow it to copy".AsHaxeString(), null);
            }
            else orig(drop, by, longPress);
        };
    }

    public static string? AbilityOf(InventItem? item) =>
        item is null ? null : AbilityItems.AbilityOf(item.getItemKind()?.ToString());

    public static bool IsAbilityItem(InventItem? item) => AbilityOf(item) is not null;

    /// <summary>A limited-use ability's item (#102): taken into the F slot, never inhaled.</summary>
    public static bool IsLimited(InventItem? item) => LimitedAbilities.IsLimited(AbilityOf(item));

    /// <summary>
    /// A floor ability item Kirby can inhale, the only kind of floor item paired
    /// with a proxy. A limited-use ability's item is never inhaled (#102).
    /// </summary>
    public static bool IsAbilityDrop(dc.Entity entity) =>
        entity is ItemDrop { destroyed: false } drop && IsAbilityItem(drop.item) && !IsLimited(drop.item);

    /// <summary>The original copy byte (2..25) of a floor ability item, or 0.</summary>
    public static byte CopyByte(dc.Entity entity)
    {
        if (entity is not ItemDrop drop || AbilityOf(drop.item) is not { } ability) return 0;
        int index = Array.IndexOf(KirbyNdsProvider.Abilities, ability);
        return index < 0 ? (byte)0 : checked((byte)(index + 2));
    }

    /// <summary>A new colorless ability item at this item level, without other rolls.</summary>
    public static InventItem Create(string ability, int itemLevel)
    {
        var item = InventItem.Class.fromItem(AbilityItems.ItemId(ability).AsHaxeString(), null);
        item.setItemLevel(Math.Max(1, itemLevel));
        var colorless = "Colorless".AsHaxeString();
        item.addAffix(colorless, Ref<bool>.In(false));
        if (!item.hasAffix(colorless)) item.addAffix(colorless, Ref<bool>.In(true));
        return item;
    }

    /// <summary>A saved ability item: its level, "+", Legendary and carried affixes.</summary>
    public static InventItem Create(string ability, int itemLevel, int qualityUps, bool legendary, IEnumerable<string> affixes)
    {
        var item = Create(ability, itemLevel);
        AddQuality(item, qualityUps, legendary);
        foreach (string affix in affixes)
        {
            var id = affix.AsHaxeString();
            if (!item.hasAffix(id) && item.canReceiveAffix(id)) item.addAffix(id, Ref<bool>.In(false));
        }
        return item;
    }

    /// <summary>An ability item's own affixes, without Colorless and its quality ones.</summary>
    public static string[] Affixes(InventItem? item)
    {
        if (item?.affixes is not { } affixes) return [];
        var result = new List<string>();
        for (int i = 0; i < affixes.length; i++)
            if (affixes.getDyn(i)?.ToString() is { } affix && affix is not ("Colorless" or "QualityUp" or "Legendary" or "StartItem"))
                result.Add(affix);
        return [.. result];
    }

    /// <summary>Put an item back on the floor under the hero, with the host's loot bounce.</summary>
    public static void DropOnFloor(Hero hero, InventItem item) => Drop(hero, item, hero.cx, hero.cy, pop: true);

    private static bool KirbyRun() => string.Equals(dc.pr.Game.Class.ME?.user?.heroSkin?.ToString(), outfitId, StringComparison.Ordinal);

    /// <summary>
    /// ItemDrop's constructor passes its item through initItem before anything
    /// else. Every floor item is made there: level loot, chests, enemy drops and
    /// the run's starting items. A saved level is unserialized without it, so
    /// it keeps its items, and shop booths are a different entity.
    /// </summary>
    private static InventItem InitItem(Hook_ItemDrop.orig_initItem orig, ItemDrop drop, InventItem item)
    {
        var result = orig(drop, item);
        return result is null ? result! : Replacement(result) ?? result;
    }

    /// <summary>
    /// The ability item replacing a host weapon other than a shield, or a skill,
    /// in a Kirby run; null for any other item or outside a Kirby run.
    /// </summary>
    private static InventItem? Replacement(InventItem item)
    {
        if (!KirbyRun()) return null;
        bool weapon = item.kind is InventItemKind.Weapon, skill = item.kind is InventItemKind.Active;
        if (!AbilityItems.Replaces(weapon, skill, weapon && item.hasTag("Shield".AsHaxeString()), IsAbilityItem(item)))
            return null;
        string ability = KirbyNdsProvider.Abilities[Random.Shared.Next(KirbyNdsProvider.Abilities.Length)];
        TestReplaced?.Invoke(item.getItemKind().ToString(), ability);
        var replacement = Create(ability, item.getRawItemLevel());
        // The host's own quality roll of the item it replaces (#56): each "+"
        // (QualityUp) and a Legendary affix carry over.
        AddQuality(replacement, item.getUpgradeLevel(), item.hasAffix("Legendary".AsHaxeString()));
        CarryAffixes(item, replacement);
        return replacement;
    }

    /// <summary>Give an ability item this many "+" (QualityUp affixes) and, optionally, the Legendary affix.</summary>
    private static void AddQuality(InventItem item, int qualityUps, bool legendary)
    {
        var up = "QualityUp".AsHaxeString();
        for (int i = 0; i < qualityUps; i++) item.addAffix(up, Ref<bool>.In(true));
        if (legendary) item.addAffix("Legendary".AsHaxeString(), Ref<bool>.In(true));
    }

    /// <summary>
    /// The copy's ability item in a weapon slot, if any. Its host damage bonus
    /// (level, "+" and Legendary, as for any Dead Cells weapon) multiplies copy damage.
    /// </summary>
    public static InventItem? SlotItem(Hero hero, string? ability)
    {
        if (ability is null || hero.inventory is not { } inventory) return null;
        for (int slot = 0; slot < inventory.nbWeapons; slot++)
            if (inventory.getEquippedWeaponOn(slot) is { } item && AbilityOf(item) == ability) return item;
        return null;
    }

    /// <summary>
    /// The replaced item's own affixes (#57) that the host lets an ability item
    /// take (its Melee group, NoCritical tag and the affix's own tag rules);
    /// weapon- or skill-only ones are dropped. Quality affixes are #56's.
    /// </summary>
    private static void CarryAffixes(InventItem from, InventItem to)
    {
        if (from.affixes is not { } affixes) return;
        for (int i = 0; i < affixes.length; i++)
        {
            object? entry = affixes.getDyn(i);
            string? affix = entry?.ToString();
            if (affix is null or "Colorless" or "QualityUp" or "Legendary" or "StartItem") continue;
            var id = affix.AsHaxeString();
            if (!to.hasAffix(id) && to.canReceiveAffix(id)) to.addAffix(id, Ref<bool>.In(false));
        }
    }

    /// <summary>The copy's ability item for this hero while Kirby is copied, or null.</summary>
    public static InventItem? CopyItem(Hero hero)
    {
        if (KirbyMod.Instance?.ActiveSession is not { } session || session.Hero.HashlinkPointer != hero.HashlinkPointer ||
            session.Rules.AbilityId is not { } ability || hero.inventory is not { } inventory) return null;
        for (int slot = 0; slot < inventory.nbWeapons; slot++)
            if (inventory.getEquippedWeaponOn(slot) is { } item && AbilityOf(item) == ability) return item;
        return null;
    }

    /// <summary>
    /// A copy attack press uses the copy's item as a Dead Cells weapon attack
    /// uses its weapon (#57): Weapon.prepare clears affixesDone and throws the
    /// Grenade affix's grenade, then Weapon.onExecute runs executeAffixes (the
    /// arrow affixes and the on-use shield), each with the host's own cooldowns.
    /// </summary>
    public static void UseAffixes(Hero hero)
    {
        if (CopyItem(hero) is not { } item || hero.weaponsManager?.findItemInMain(item) is not { } weapon) return;
        weapon.affixesDone = false;
        if (item.hasAffix("Grenade".AsHaxeString())) hero.createGrenadeFromAffix(item);
        weapon.executeAffixes();
        TestUsed?.Invoke(item);
    }

    private static bool IsKirby(Hero? hero) =>
        hero is not null && KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer == hero.HashlinkPointer;

    /// <summary>
    /// Challenge rewards (item altars, including pick-one groups, and timed
    /// items) and shop booths take their item in their own constructors, not
    /// through ItemDrop (#61). Their item is replaced before their graphics are
    /// built, which also converts those in a saved level. Taking or buying an
    /// ability item follows the host's own step for that entity (the altar's
    /// disable, the timed item's emptying, the booth's buy) and drops the item on
    /// the floor, to be inhaled and swallowed like any floor ability item.
    /// </summary>
    private static void InstallRewards()
    {
        Hook_ItemAltar.initGfx += (orig, altar) =>
        {
            if (altar.isEnabled && altar.item is { } item && Replacement(item) is { } ability) altar.item = ability;
            orig(altar);
        };
        Hook_TimedItem.initGfx += (orig, timed) =>
        {
            if (timed.item is { } item && Replacement(item) is { } ability) timed.item = ability;
            orig(timed);
        };
        Hook_ShopBooth.initGfx += (orig, booth) =>
        {
            if (booth.item is { } item && Replacement(item) is { } ability) booth.item = ability;
            orig(booth);
        };
        Hook_ItemAltar.onActivate += (orig, altar, by, longPress) =>
        {
            // A long press sells it, as the host does.
            if (longPress || !altar.isEnabled || !IsAbilityItem(altar.item) || !IsKirby(by)) { orig(altar, by, longPress); return; }
            var item = altar.item;
            int cx = altar.cx, cy = altar.cy;
            var level = altar._level;
            altar.removeLargeTip(Ref<bool>.In(true));
            // cine.TierItemFound.destroyItem: a taken altar item disables its altar (and its link).
            altar.disable(true);
            Drop(level, item, cx, cy, pop: true);
        };
        Hook_TimedItem.onActivate += (orig, timed, by, longPress) =>
        {
            bool open = timed.frozenTime is not null || timed.limitS - timed._level.game.data.gameTimeS > 0;
            if (longPress || !open || !IsAbilityItem(timed.item) || !IsKirby(by)) { orig(timed, by, longPress); return; }
            var item = timed.item;
            // The host's own take callback: the pedestal empties.
            timed.removeAllAffects(31);
            timed.item = null!;
            if (timed.icon is { } icon) { icon.parent?.removeChild(icon); timed.icon = null!; }
            timed.updateRender();
            Drop(timed._level, item, timed.cx, timed.cy, pop: true);
        };
        Hook_ShopBooth.onActivate += (orig, booth, by, longPress) =>
        {
            // Without the gold the host shows its own message.
            if (longPress || booth.item is not { } item || !IsAbilityItem(item) || !IsKirby(by) ||
                by._level.game.data.money < booth.price) { orig(booth, by, longPress); return; }
            int cx = booth.cx, cy = booth.cy;
            var level = booth._level;
            booth.buy(by, Ref<bool>.In(true));
            Drop(level, item, cx, cy, pop: true);
        };
    }

    /// <summary>
    /// Settle the mouthful once the original swallow or spit has happened, and
    /// keep the weapon slots in step with Kirby's copy: the copy's item in one
    /// non-shield slot, no ability item in base form. Call once per host frame.
    /// </summary>
    public static void Sync(KirbySession session, MouthfulItems<Held> mouthful, bool mouthfulPending)
    {
        var hero = session.Hero;
        if (hero.destroyed || hero.inventory is not { } inventory) return;
        Held? taken = null;
        if (mouthful.Count > 0 && (!mouthfulPending || mouthful.Emptied))
        {
            var (item, returned) = mouthful.Resolve(session.Rules.AbilityId);
            taken = item;
            foreach (var back in returned) Return(hero, back);
        }
        string? wanted = session.Rules.AbilityId;
        int count = inventory.nbWeapons;
        var contents = new SlotContent[count];
        var items = new InventItem?[count];
        int retained = -1;
        bool changed = false;
        for (int slot = 0; slot < count; slot++)
        {
            var item = items[slot] = inventory.getEquippedWeaponOn(slot);
            string? ability = AbilityOf(item);
            if (item is null) contents[slot] = SlotContent.Empty;
            else if (ability is not null)
            {
                if (ability == wanted) { contents[slot] = SlotContent.Ability; continue; }
                // Base form, or another copy's item: it leaves the slot.
                inventory.remove(item);
                items[slot] = null;
                contents[slot] = SlotContent.Empty;
                changed = true;
                TestSlot?.Invoke(null, item.getRawItemLevel(), false);
            }
            else if (item.hasTag("Shield".AsHaxeString()))
            {
                contents[slot] = SlotContent.Shield;
                if (session.Rules.RetainedShieldId == item.permanentId.ToString(CultureInfo.InvariantCulture)) retained = slot;
            }
            else contents[slot] = SlotContent.Other;
        }
        if (wanted is not null && !contents.Contains(SlotContent.Ability))
        {
            int target = AbilityItems.Slot(contents, retained);
            if (target >= 0)
            {
                if (items[target] is { } occupant)
                {
                    inventory.remove(occupant);
                    // A second shield goes back to the floor; any other host
                    // weapon is unusable to Kirby and is simply replaced.
                    if (occupant.hasTag("Shield".AsHaxeString())) Drop(hero, occupant, hero.cx, hero.cy, pop: true);
                }
                var copy = taken?.Item is { } swallowed && AbilityOf(swallowed) == wanted
                    ? swallowed : Create(wanted, hero._level?.map?.lootLevel ?? 1);
                // A copy from an Elite is a Legendary item (#56); other enemies give base items.
                if (!ReferenceEquals(copy, taken?.Item) && session.Rules.AbilityEmpowered) AddQuality(copy, 0, legendary: true);
                inventory.add(copy);
                changed = true;
                TestSlot?.Invoke(wanted, copy.getRawItemLevel(), ReferenceEquals(copy, taken?.Item));
            }
        }
        if (!changed) return;
        if (hero.weaponsManager is dc.tool.hero.weap.BeheadedWeaponsManager manager)
            manager.onEquippedItemsUpdated(Ref<bool>.In(false));
        // HUD.refreshItems only redraws each slot's current item; the host
        // re-reads the inventory into its slots with Hero.hudInitItems.
        if (hero._level?.game?.hud is not null) hero.hudInitItems();
    }

    /// <summary>A spat-out item goes back where it lay, or under Kirby in another level.</summary>
    private static void Return(Hero hero, Held held)
    {
        bool sameLevel = hero._level is { } level && level.HashlinkPointer == held.Level;
        Drop(hero, held.Item, sameLevel ? held.Cx : hero.cx, sameLevel ? held.Cy : hero.cy, pop: false);
    }

    /// <summary>A new floor item; <paramref name="pop"/> gives it the host's loot bounce.</summary>
    private static void Drop(Hero hero, InventItem item, int cx, int cy, bool pop)
    {
        if (hero._level is { } level) Drop(level, item, cx, cy, pop);
    }

    private static void Drop(dc.pr.Level level, InventItem item, int cx, int cy, bool pop)
    {
        var drop = new ItemDrop(level, cx, cy, item, false, Ref<bool>.In(false));
        drop.init();
        if (pop) drop.onDropAsLoot();
        TestReturned?.Invoke(drop, cx, cy);
    }
}
