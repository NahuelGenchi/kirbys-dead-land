using File = System.IO.File;
using System.Globalization;
using System.Text.Json;
using dc.en;
using dc.tool;
using dc.tool.hero;
using Kirby.Core;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;

namespace Kirby.Runtime;

/// <summary>
/// DCCM host hooks for the selectable original-ROM Kirby character.
/// </summary>
public sealed class KirbyMod(ModInfo info) : ModBase(info), IOnHeroUpdate,
    IOnHeroDispose, IOnAfterLoadingAssets, IOnFrameUpdate
{
    private IKirbyGameplayProvider? provider;
    private KirbySession? session;
    private RuntimeConfig config = new();
    private bool warnedNoProvider;
    // The hero whose activation threw; it plays as the host character instead of retrying every frame.
    private nint failedHero;
    private KirbyCamera? camera;
    public static KirbyMod? Instance { get; private set; }
    public KirbySession? ActiveSession => session;
    public KirbyNdsProvider? NativeProvider => provider as KirbyNdsProvider;

    public override void Initialize()
    {
        LabIsolation.Install();
        Instance = this;
        if (LabPerfProbe.Bare)
        {
            LabPerfProbe.Install();
            Logger.Information("Kirby adapter in the bare lab mode: no gameplay hooks.");
            return;
        }
        string path = Info.ModRoot.GetFilePath("config.json");
        if (File.Exists(path))
            config = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Kirby runtime configuration is null.");
        KirbyInput.ControllerLayout = config.ControllerLayout;
        KirbyInput.Install();
        KirbyNdsProvider.Log = message => Logger.Information(message);
        KirbyDamage.Install();
        KirbyMutations.Install();
        KirbyMutationMenu.Install();
        KirbyCollector.Install();
        KirbyForge.Install();
        KirbyHealing.Install();
        KirbyVines.Install();
        KirbyBumps.Install();
        dc.en.inter.Hook_Elevator.postUpdate += (orig, elevator) => { orig(elevator); NativeProvider?.AlignToElevator(elevator); };
        if (config.EnableOriginalRuntime)
        {
            if (string.IsNullOrWhiteSpace(config.RomPath) || string.IsNullOrWhiteSpace(config.SeedStatePath))
                throw new InvalidDataException("config.json enables the original runtime without RomPath and SeedStatePath; scripts/stage_runtime.py writes both.");
            KirbyTutorials.Install(config.OutfitId);
            KirbyHostVisuals.Install(config.OutfitId);
            KirbyHostSkills.Install();
            KirbyHud.Install();
            KirbyAbilityItems.Install(config.OutfitId);
            KirbyFSlot.Install();
            camera = new KirbyCamera(config.CameraZoom);
            RegisterProvider(new KirbyNdsProvider(config, Info.ModRoot.GetFilePath(".")));
        }
        dc.Hook_User.newGame += (orig, user, seed, level, custom, scoring, mode) =>
        {
            (provider as KirbyNdsProvider)?.ResetForNewRun();
            orig(user, seed, level, custom, scoring, mode);
        };
        Hook_Hero.preUpdate += (orig, hero) =>
        {
            var state = Synchronize(hero);
            if (state is not null && provider is KirbyNdsProvider native)
                native.BeforeHostUpdate(state, () => orig(hero));
            else orig(hero);
        };
        Hook_Weapon.tryToUse += TryUseWeapon;
        Hook_Inventory.equip += Equip;
        Hook_InventItem.isUsefulFor += IsUsefulFor;
        Hook_Hero.fixedUpdate += FixedUpdate;
        Hook_Hero.updateMaxLife += (orig, hero) =>
        {
            if (session?.Hero.HashlinkPointer == hero.HashlinkPointer && IsKirby(hero) &&
                provider is KirbyNdsProvider native)
                native.UpdateHostMaxLife(session, () => orig(hero));
            else orig(hero);
        };
        Hook_Hero.updateBodyHeight += (orig, hero) =>
        {
            if (session?.Hero.HashlinkPointer != hero.HashlinkPointer) { orig(hero); return; }
            hero.bodyHei = 29.0 / 24;
            hero.hei = 29; hero.radius = 9;
            hero.smallHitbox = false;
        };
        Hook_Hero.applyAttackResult += (orig, hero, attack) =>
        {
            var state = Synchronize(hero);
            if (state is not null && provider is KirbyNdsProvider native)
                native.ReceiveHostAttack(state, attack, () => orig(hero, attack));
            else orig(hero, attack);
        };
        Hook_HeroWeaponsManager.onLevelChanged += LevelChanged;
        KirbyHurtbox.Install();
        LabLifecycleProbe.Install();
        LabPerfProbe.Install();
        Logger.Information("Kirby adapter initialized; original ROM provider enabled: {Enabled}.", provider is not null);
    }

    public void RegisterProvider(IKirbyGameplayProvider value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (provider is not null || session is not null)
            throw new InvalidOperationException("Register exactly one provider before activating Kirby.");
        // Validate the exact set used by the randomizer before accepting the provider.
        _ = new KirbyRules(value.ImplementedAbilityIds);
        provider = value;
        Logger.Information("Kirby gameplay provider registered with {Count} implemented abilities.", value.ImplementedAbilityIds.Count);
    }

    void IOnAfterLoadingAssets.OnAfterLoadingAssets()
    {
        if (provider is null && !config.LoadDecodedAssetAtlases) return;
        foreach (string name in new[] { "res.pak", "outfit.pak" })
        {
            string path = Info.ModRoot.GetFilePath(name);
            if (!File.Exists(path)) continue;
            FsPak.Instance.FileSystem.loadPak(path.AsHaxeString());
            Logger.Information("Loaded Kirby assets from {Path}.", path);
        }
        if (provider is not null)
            CDBManager.Instance.LoadJsonData(CDBManager.Instance.GetAlteredCDB());
    }

    private bool IsKirby(Hero hero) => !hero.destroyed &&
        Game.Instance.HeroInstance is { } active && active.HashlinkPointer == hero.HashlinkPointer &&
        hero._level?.game?.user is { } user &&
        string.Equals(user.heroSkin?.ToString(), config.OutfitId, StringComparison.Ordinal);

    private KirbySession? Synchronize(Hero? hero)
    {
        var mark = LabPerfProbe.Start();
        var state = SynchronizeHero(hero);
        LabPerfProbe.Stop(LabPerfProbe.Section.Sync, mark);
        return state;
    }

    private KirbySession? SynchronizeHero(Hero? hero)
    {
        if (provider is null || hero is null || !IsKirby(hero))
        {
            Deactivate();
            if (hero is not null && provider is null && !warnedNoProvider)
            {
                warnedNoProvider = true;
                Logger.Warning("Kirby original runtime is disabled in config.json. Enable it after staging the ROM bridge and seed state.");
            }
            return null;
        }
        if (session is null || session.Hero.HashlinkPointer != hero.HashlinkPointer)
        {
            Deactivate();
            if (failedHero == hero.HashlinkPointer) return null;
            session = new KirbySession(hero, provider);
            KirbyHostVisuals.ClearHead(hero);
            CaptureShield(session);
            try { provider.OnActivated(session); }
            catch (Exception error)
            {
                // A missing ROM, bridge or seed state, or an unreadable snapshot. Kept
                // active, the session pinned the hero with no machine behind it.
                failedHero = hero.HashlinkPointer;
                Logger.Error(error, "Kirby could not start; this hero plays as the host character.");
                try { Deactivate(); } catch (Exception) { session = null; }
                return null;
            }
        }
        CaptureShield(session);
        return session;
    }

    private void Deactivate()
    {
        if (session is null)
            return;
        provider?.OnDeactivated(session);
        session = null;
    }

    // Item permanentId identifies the physical shield, including when two shields
    // have the same database kind. Display text and item kind are insufficient.
    private static string StableItemId(InventItem item) => item.permanentId.ToString(CultureInfo.InvariantCulture);

    // In both forms the retained shield follows what the host has equipped, so
    // a shield taken while copied (Take swaps it in) becomes the one that parries.
    private static void CaptureShield(KirbySession state)
    {
        List<string> shields = [];
        for (int slot = 0; slot < state.Hero.inventory.nbWeapons; slot++)
        {
            InventItem? item = state.Hero.inventory.getEquippedWeaponOn(slot);
            if (item is not null && item.hasTag("Shield".AsHaxeString()))
                shields.Add(StableItemId(item));
        }
        state.Rules.FollowEquippedShields(shields);
    }

    private HostWeaponKind Classify(InventItem item)
    {
        if (item.hasTag("Shield".AsHaxeString()))
            return HostWeaponKind.Shield;
        string id = item.getItemKind().ToString();
        if (config.SwordIds.Contains(id, StringComparer.Ordinal))
            return HostWeaponKind.Sword;
        if (config.BowIds.Contains(id, StringComparer.Ordinal))
            return HostWeaponKind.Bow;
        return HostWeaponKind.Other;
    }

    private bool TryUseWeapon(Hook_Weapon.orig_tryToUse orig, Weapon weapon)
    {
        KirbySession? state = Synchronize(weapon.owner);
        if (state is null)
            return orig(weapon);
        // Inhale and copy attacks arrive as DS B through KirbyInput; a host
        // sword or bow never swings, even one already in the inventory.
        AttackRoute route = state.Rules.ResolveAttack(Classify(weapon.item), StableItemId(weapon.item));
        return route switch
        {
            AttackRoute.Parry => orig(weapon),
            AttackRoute.Ability => state.Rules.AbilityId is { } ability && provider!.TryAbilityAttack(state, ability, weapon),
            _ => false
        };
    }

    /// <summary>
    /// The host asks this before every pickup, purchase and altar or timed-door
    /// reward. When it is false the host shows its own "not useful right now"
    /// message and leaves the item where it is; a long press still recycles a
    /// dropped item. Kirby declines the equipment his rules do not allow here;
    /// a shield is allowed in any form.
    /// </summary>
    private bool IsUsefulFor(Hook_InventItem.orig_isUsefulFor orig, InventItem item, Hero hero)
    {
        KirbySession? state = Synchronize(hero);
        // Only Kirby takes an ability item, and always through the original
        // inhale and swallow: floor items never reach here (KirbyAbilityItems
        // handles their press), and altars, timed items and shops drop it.
        // For him it is "useful", so a shop short of gold says so.
        if (KirbyAbilityItems.IsAbilityItem(item)) return state is not null;
        // Non-combat progression items, outfits, consumables and mutations remain host-owned.
        if (state is null || (item.kind is not InventItemKind.Weapon && item.kind is not InventItemKind.Active))
            return orig(item, hero);
        return state.Rules.CanEquip(Classify(item)) && orig(item, hero);
    }

    private bool Equip(Hook_Inventory.orig_equip orig, Inventory inventory, InventItem item)
    {
        KirbySession? state = Synchronize(Game.Instance.HeroInstance);
        if (state is null || inventory.HashlinkPointer != state.Hero.inventory.HashlinkPointer)
            return orig(inventory, item);
        // Never refuse here: Inventory.add has already listed the item with
        // posID -1 and expects equip to give it a slot. A refusal left it
        // slotless and the host HUD threw "unexpected posId" on the pickup.
        // Kirby declines unusable items earlier, in IsUsefulFor.
        bool equipped = orig(inventory, item);
        // The newly taken shield is retained, even when another shield stays equipped.
        if (equipped && item.kind is InventItemKind.Weapon && item.hasTag("Shield".AsHaxeString()))
            state.Rules.EquipShield(StableItemId(item));
        if (equipped && item.kind is InventItemKind.Weapon)
            CaptureShield(state);
        return equipped;
    }

    private void FixedUpdate(Hook_Hero.orig_fixedUpdate orig, Hero hero)
    {
        KirbySession? state = Synchronize(hero);
        if (state is null)
            orig(hero);
        else
            provider!.OnFixedUpdate(state, () => orig(hero));
    }

    private void LevelChanged(Hook_HeroWeaponsManager.orig_onLevelChanged orig, HeroWeaponsManager manager)
    {
        orig(manager);
        if (session is not null && session.Hero.HashlinkPointer == manager.hero.HashlinkPointer)
            session.Rules.ResetForLevel();
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        if (LabPerfProbe.Bare) return;
        KirbySession? state = Synchronize(Game.Instance.HeroInstance);
        if (state is not null)
            provider!.OnFrame(state, dt);
    }

    void IOnHeroDispose.OnHeroDispose() { Deactivate(); failedHero = 0; }

    void IOnFrameUpdate.OnFrameUpdate(double dt)
    {
        if (provider is null)
        {
            if (LabPerfProbe.Bare) { LabPerfProbe.HostFrame(Game.Instance.HeroInstance); LabPerfProbe.Frame(); }
            return;
        }
        KirbyHostVisuals.OnFrame();
        NativeProvider?.OnHostFrame(dt);
        var native = NativeProvider;
        var held = native?.Enemies.Held ?? default;
        KirbyEnemyHooks.Relax(held.Mob, held.Other);
        camera?.Update(session?.Hero._level ?? native?.DeathLevel, session is not null || native?.PlayingDeath == true);
        if (session is null) LabPerfProbe.HostFrame(Game.Instance.HeroInstance);
        LabPerfProbe.Frame();
    }
}

public sealed class RuntimeConfig
{
    public bool EnableOriginalRuntime { get; set; }
    /// <summary>The owner's ROM and private seed state; scripts/stage_runtime.py writes both.</summary>
    public string RomPath { get; set; } = "";
    public string SeedStatePath { get; set; } = "";
    public string OutfitId { get; set; } = "KirbySuperStarUltra";
    public bool LoadDecodedAssetAtlases { get; set; }
    public string[] SwordIds { get; set; } = [];
    public string[] BowIds { get; set; } = [];
    /// <summary>
    /// Host camera zoom while Kirby plays. 1 keeps the Beheaded's camera; 2
    /// shows about 200 host pixels of height, the original DS screen's 192,
    /// so Kirby's size and speed on screen match the original.
    /// </summary>
    public double CameraZoom { get; set; } = 1;
    /// <summary>
    /// Shown share of the original hurt, guard and transformation palette
    /// flashes: 1 keeps the original 10 Hz orange/whitened alternation, 0
    /// removes it. 0.35 keeps the timing as a gentle tint.
    /// </summary>
    public double FlashStrength { get; set; } = 0.35;
    /// <summary>A held direction dashes through the original double-tap; a light analog tilt walks.</summary>
    public bool AlwaysDash { get; set; } = true;
    /// <summary>
    /// Controller bindings use Kirby's layout: an 8-way stick, Dodge slides,
    /// Skill 1 guards and Skill 2 discards the copy ability only when held.
    /// False reads the controller like the keyboard. The keyboard is unaffected.
    /// </summary>
    public bool ControllerLayout { get; set; } = true;
    /// <summary>
    /// Kirby runs, rolls, jumps, falls, floats and climbs at the Beheaded's
    /// measured speeds, and stops and turns as promptly. False keeps the
    /// original DS speeds. The stomp is the host's dive and always moves at its speed.
    /// </summary>
    public bool MatchHostSpeed { get; set; } = true;
    /// <summary>
    /// The original's processor runs at twice the DS clock, so the original
    /// slows down less when many enemies are near Kirby (#150, a deliberate
    /// departure from the hardware). False keeps the DS clock and its slowdown.
    /// </summary>
    public bool DoubleArm9Clock { get; set; } = true;
    /// <summary>
    /// The original game's touch damage: bumping into an enemy hurts Kirby
    /// and weak enemies. False (the default) follows Dead Cells, where only
    /// enemy attacks hurt the hero and touching hurts neither side; Kirby's
    /// inhale, slide and copy attacks still hit, and Dead Cells' own touch
    /// attacks (spiked and electric enemies) still hurt him.
    /// </summary>
    public bool OriginalTouchDamage { get; set; }
    /// <summary>
    /// Host damage per original damage point against a tier-1 enemy, before
    /// the host's own per-tier life scaling. The original game kills a basic
    /// 16-18 HP enemy with one ability hit. 2.625 (the earlier 3.5 less 25%,
    /// #128) makes a 120-life Zombie take three Sword hits.
    /// </summary>
    public double CopyDamageScale { get; set; } = 2.625;
    /// <summary>
    /// The same scale for hits the original CPU helper lands (#128): half of the
    /// earlier 3.5, so a Sword helper needs four hits on a 120-life Zombie. Zero
    /// or less gives the helper Kirby's scale.
    /// </summary>
    public double HelperCopyDamageScale { get; set; } = 1.75;
    /// <summary>
    /// The original runs on the emulator's recompiler, which does the same
    /// work in about three fifths of the time (#153). False runs it on the
    /// interpreter, the fallback if the recompiler ever misbehaves.
    /// </summary>
    public bool Recompiler { get; set; } = true;
    /// <summary>
    /// Critical hits of copy attacks (#88, a mod addition). Kirby has no base
    /// crit chance; mutations add timed chance. A crit multiplies the hit by
    /// <see cref="CopyCriticalDamage"/>.
    /// </summary>
    public double CopyCriticalChance { get; set; }
    public double CopyCriticalDamage { get; set; } = 1.5;
    /// <summary>Numbers of the Kirby cooldown mutations (#89).</summary>
    public MutationTuning CriticalMutations { get; set; } = new();
}
