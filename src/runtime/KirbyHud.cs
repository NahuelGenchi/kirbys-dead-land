using System.Buffers.Binary;
using System.Runtime.InteropServices;
using dc.h2d;
using dc.h3d.mat;
using dc.hxd;
using dc.tool;
using dc.ui.hud;
using HaxeProxy.Runtime;
using Kirby.Core;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// Shows copied Kirby's ability in the HUD weapon slots that fire it (every
/// non-shield slot, as in KirbyInput). The host draws the slot from a colorless
/// stand-in item, so its frame and colours are exactly those of host colorless
/// gear. The icon is the original bottom-screen ability icon, decoded from the
/// running machine once it is steady after each copy, so the original's copy
/// flash and hurt variants do not flicker in the slot. It is drawn at the host
/// item icon size in whole screen pixels. The slot keeps its real item for all
/// other host logic.
/// </summary>
internal static class KirbyHud
{
    /// <summary>Source frames the original icon must stay unchanged before it is shown.</summary>
    private const int SteadyFrames = 30;
    private const string StandInItem = "StartSword";
    private static readonly byte[] display = new byte[4], oam = new byte[1024], tiles = new byte[AbilityIcon.TileBytes];
    private static readonly byte[] palette = new byte[32], candidate = new byte[AbilityIcon.Size * AbilityIcon.Size * 4];
    private static readonly byte[] rgba = new byte[AbilityIcon.Size * AbilityIcon.Size * 4];
    /// <summary>Slots currently drawn as the ability, with their original disableAmmo value.</summary>
    private static readonly Dictionary<nint, bool> overlaidSlots = [];
    private static KirbySession? overlaid;
    private static Pixels? pixels;
    private static Texture? texture;
    private static InventItem? standIn;
    private static string? iconAbility;
    private static (int, int, int) shownTiers;
    private static ulong iconHash, pendingHash;
    private static uint pendingSince, lastRead;
    private static bool iconReady, hasPending, standInRefused;
    /// <summary>The pause card being drawn as the ability, while the host builds it.</summary>
    private static nint drawingCard;
    /// <summary>Lab-only observer of each pause equipment card, and whether it shows the ability.</summary>
    internal static Action<dc.ui.NewItemDesc, bool>? TestCardDrawn;

    /// <summary>The ability, tiers and icon currently drawn in the HUD, for lab evidence.</summary>
    internal static (string? Ability, (int, int, int) Tiers, ulong IconHash, bool Active) Shown =>
        (overlaid is null ? null : iconAbility, shownTiers, iconHash, overlaid is not null);
    internal static ReadOnlySpan<byte> IconPixels => rgba;

    public static void Install()
    {
        // Item cards describe their item through getGameplayDesc. A card drawn
        // from the colorless stand-in (the pause equipment card) describes the
        // current copy ability instead of the stand-in sword.
        dc.tool.Hook_InventItem.getGameplayDesc += (orig, item, level) =>
            standIn is not null && item.HashlinkPointer == standIn.HashlinkPointer && iconAbility is { } ability
                ? AbilityDescriptions.Describe(ability).AsHaxeString()
                : orig(item, level);
        Hook_Skill.useItem += UseItem;
        Hook_Skill.onResize += (orig, skill) =>
        {
            orig(skill);
            if (overlaidSlots.ContainsKey(skill.HashlinkPointer)) FitIcon(skill);
        };
        dc.ui.Hook_NewItemDesc.onResize += DrawCard;
    }

    /// <summary>Call once per host frame after the original machine stepped.</summary>
    public static void Update(KirbySession session, NdsRuntime nds)
    {
        if (session.Rules.IsBaseForm) { Clear(session); return; }
        var hero = session.Hero;
        if (session.Rules.AbilityId != iconAbility)
        {
            // A new ability waits for its own steady icon; never show the previous one.
            Clear(session);
            iconAbility = session.Rules.AbilityId;
        }
        uint frame = nds.State.Frame;
        if (!iconReady && frame != lastRead)
        {
            lastRead = frame;
            ReadSteadyIcon(nds, frame);
        }
        var tiers = (hero.brutalityTier, hero.tacticTier, hero.survivalTier);
        if (!iconReady || (overlaid == session && shownTiers == tiers)) return;
        // Scroll changes recolor the host's colorless slot, so redraw it then too.
        overlaid = session;
        shownTiers = tiers;
        hero._level?.game?.hud?.refreshItems();
    }

    /// <summary>Restore the normal equipment slots, for base form or a deactivated session.</summary>
    public static void Clear(KirbySession session)
    {
        bool wasOverlaid = overlaid is not null;
        overlaid = null;
        iconAbility = null;
        iconReady = hasPending = false;
        if (wasOverlaid) session.Hero._level?.game?.hud?.refreshItems();
    }

    private static void ReadSteadyIcon(NdsRuntime nds, uint frame)
    {
        nds.ReadGpu(0x04001000, display);
        nds.ReadGpu(0x07000400, oam);
        if (AbilityIcon.Find(BinaryPrimitives.ReadUInt32LittleEndian(display), oam) is not { } sprite)
        {
            hasPending = false;
            return;
        }
        nds.ReadGpu(0x06600000 + (uint)sprite.TileByte, tiles);
        nds.ReadGpu(0x05000600 + (uint)sprite.Palette * 32, palette);
        AbilityIcon.Decode(sprite, tiles, palette, candidate);
        ulong hash = 14695981039346656037;
        foreach (byte value in candidate) hash = (hash ^ value) * 1099511628211;
        if (!hasPending || hash != pendingHash)
        {
            pendingHash = hash;
            pendingSince = frame;
            hasPending = true;
            return;
        }
        if (frame - pendingSince < SteadyFrames) return;
        candidate.CopyTo(rgba, 0);
        iconHash = hash;
        iconReady = true;
        Upload();
    }

    private static unsafe void Upload()
    {
        if (texture is null)
        {
            pixels = Pixels.Class.alloc(AbilityIcon.Size, AbilityIcon.Size, new PixelFormat.RGBA());
            texture = Texture.Class.fromPixels(pixels, null!);
            texture.name = "kirby-original-ability-icon".AsHaxeString();
            texture.set_filter(new Filter.Nearest());
            texture.preventAutoDispose();
        }
        Marshal.Copy(rgba, 0, pixels!.bytes.b, rgba.Length);
        texture.uploadPixels(pixels, Ref<int>.In(0), Ref<int>.In(0));
    }

    /// <summary>
    /// The pause screen draws each weapon as a NewItemDesc card from the slot's
    /// item. A copied slot's card is drawn from the colorless stand-in instead,
    /// so the host gives it the colorless card look, and then names the ability.
    /// The card keeps its real item afterward.
    /// </summary>
    private static void DrawCard(dc.ui.Hook_NewItemDesc.orig_onResize orig, dc.ui.NewItemDesc card, Ref<double> width, Ref<double> height)
    {
        InventItem? real = card.ii;
        bool ability = AbilityCard(card, real);
        if (!ability || ColorlessStandIn() is not { } colorless)
        {
            orig(card, width, height);
            if (card.pprocess is dc.ui.pause.DefaultPause) TestCardDrawn?.Invoke(card, false);
            return;
        }
        card.ii = colorless;
        drawingCard = card.HashlinkPointer;
        try { orig(card, width, height); }
        finally { drawingCard = 0; card.ii = real; }
        Relabel(card);
        TestCardDrawn?.Invoke(card, true);
    }

    /// <summary>
    /// The host card's first two direct text children are the item name and its
    /// main info line (the weapon's DPS). Name the ability in the host's colorless
    /// name color and replace the stand-in sword's DPS, which does not describe
    /// copy damage. The host's own "Colorless" affix line stays.
    /// </summary>
    private static void Relabel(dc.ui.NewItemDesc card)
    {
        string ability = iconAbility == "YoYo" ? "Yo-Yo" : iconAbility ?? "";
        var children = card.children;
        int texts = 0;
        for (int i = 0; i < children.length && texts < 2; i++)
        {
            if (children.getDyn(i) is not dc.ui.Text text) continue;
            if (texts++ == 0)
            {
                // Keep the host's markup (colorless name color); swap only the shown name.
                string raw = text.rawText?.ToString() ?? "";
                string shown = System.Text.RegularExpressions.Regex.Replace(text.text?.ToString() ?? "", "<[^>]*>", "");
                text.set_text((shown.Length > 0 && raw.Contains(shown, StringComparison.Ordinal) ? raw.Replace(shown, ability) : ability).AsHaxeString());
            }
            else text.set_text("Copy ability".AsHaxeString());
        }
    }

    /// <summary>
    /// Replace a card's main info line (the second direct text child, a weapon's
    /// DPS) with "Copy ability": an ability item's weapon row is the starting
    /// sword's, whose DPS does not describe copy damage.
    /// </summary>
    internal static void ReplaceInfoLine(dc.ui.NewItemDesc card)
    {
        var children = card.children;
        for (int i = 0, texts = 0; i < children.length; i++)
        {
            if (children.getDyn(i) is not dc.ui.Text text || texts++ == 0) continue;
            text.set_text("Copy ability".AsHaxeString());
            return;
        }
    }

    private static bool AbilityCard(dc.ui.NewItemDesc card, InventItem? item)
    {
        if (overlaid is not { } session || !iconReady || texture is null || item is null) return false;
        if (card.pprocess is not dc.ui.pause.DefaultPause) return false;
        var inventory = session.Hero.inventory;
        for (int slot = 0; slot < inventory.nbWeapons; slot++)
            if (inventory.getEquippedWeaponOn(slot) is { } equipped && equipped.HashlinkPointer == item.HashlinkPointer)
                // A real ability item (#55) is drawn as itself, with its level,
                // quality and affixes; only another weapon needs the stand-in.
                return !equipped.hasTag("Shield".AsHaxeString()) && !KirbyAbilityItems.IsAbilityItem(equipped);
        return false;
    }

    private static void UseItem(Hook_Skill.orig_useItem orig, Skill skill, InventItem? item)
    {
        if (drawingCard != 0 && item is not null && item.HashlinkPointer == standIn?.HashlinkPointer)
        {
            // The ability card's own icon slot: host colorless frame, original icon.
            orig(skill, item);
            skill.setIcon(new Tile(texture!, 0, 0, AbilityIcon.Size, AbilityIcon.Size, Ref<int>.In(0), Ref<int>.In(0)));
            FitIcon(skill);
            return;
        }
        if (!AbilitySlot(skill) || ColorlessStandIn() is not { } colorless)
        {
            if (overlaidSlots.Remove(skill.HashlinkPointer, out bool disableAmmo))
            {
                skill.disableAmmo = disableAmmo;
                // useItem keeps the icon when given the item it already holds.
                skill.ii = null;
            }
            orig(skill, item);
            return;
        }
        // useItem's same-item-kind path colors the slot from its previous item
        // before switching to the new one (Skill.hx:540-545). With the starting
        // sword in the slot that gave the sword's Brutality red. Without a slot
        // item, the host assigns the colorless stand-in first and picks the
        // highest scroll stat (Skill.hx:521-522, 390-391).
        skill.ii = null;
        orig(skill, colorless);
        // Inventory, equip and cooldown logic still see the real equipment.
        skill.ii = item;
        if (overlaidSlots.TryAdd(skill.HashlinkPointer, skill.disableAmmo)) skill.disableAmmo = true;
        skill.setIcon(new Tile(texture!, 0, 0, AbilityIcon.Size, AbilityIcon.Size, Ref<int>.In(0), Ref<int>.In(0)));
        FitIcon(skill);
    }

    /// <summary>Draw the 32-pixel original icon near the host item icon size, in whole screen pixels.</summary>
    private static void FitIcon(Skill skill)
    {
        if (skill.icon is not { } icon) return;
        double scale = System.Math.Max(1, System.Math.Floor(skill.get_pixelScale() * Skill.Class.ICON_SIZE / AbilityIcon.Size));
        icon.posChanged = true;
        icon.scaleX = icon.scaleY = scale;
    }

    /// <summary>Copied Kirby's weapon slots fire the ability unless they hold a shield, as in KirbyInput.</summary>
    private static bool AbilitySlot(Skill skill)
    {
        if (overlaid is not { } session || !iconReady || texture is null) return false;
        if (session.Hero._level?.game?.hud is not { } hud) return false;
        var weapons = hud.skillWeapons;
        for (int slot = 0; slot < weapons.length; slot++)
            if (weapons.getDyn(slot) is Skill candidate && candidate.HashlinkPointer == skill.HashlinkPointer)
                return session.Hero.inventory.getEquippedWeaponOn(slot)?.hasTag("Shield".AsHaxeString()) != true;
        return false;
    }

    private static InventItem? ColorlessStandIn()
    {
        if (standIn is not null || standInRefused) return standIn;
        var item = InventItem.Class.fromItem(StandInItem.AsHaxeString(), null);
        var colorless = "Colorless".AsHaxeString();
        item.addAffix(colorless, Ref<bool>.In(false));
        if (!item.hasAffix(colorless)) item.addAffix(colorless, Ref<bool>.In(true));
        if (!item.hasAffix(colorless))
        {
            standInRefused = true;
            KirbyNdsProvider.Log?.Invoke("Kirby HUD: the host refused a Colorless stand-in item; ability slots keep their equipment.");
            return null;
        }
        return standIn = item;
    }
}
