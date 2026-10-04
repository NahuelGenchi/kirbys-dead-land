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
    IOnHeroDispose, IOnAfterLoadingAssets
{
    private IKirbyGameplayProvider? provider;
    private KirbySession? session;
    private RuntimeConfig config = new();
    private bool warnedNoProvider;
    public static KirbyMod? Instance { get; private set; }
    public KirbySession? ActiveSession => session;
    public KirbyNdsProvider? NativeProvider => provider as KirbyNdsProvider;

    public override void Initialize()
    {
        LabIsolation.Install();
        Instance = this;
        string path = Info.ModRoot.GetFilePath("config.json");
        if (File.Exists(path))
            config = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Kirby runtime configuration is null.");
        KirbyInput.Install();
        KirbyEnemyHooks.Install();
        KirbyDamage.Install();
        if (config.EnableOriginalRuntime)
        {
            KirbyTutorials.Install(config.OutfitId);
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
        Hook_Hero.fixedUpdate += FixedUpdate;
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
            session = new KirbySession(hero, provider);
            CaptureShield(session);
            provider.OnActivated(session);
        }
        if (session.Rules.IsBaseForm)
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

    private static void CaptureShield(KirbySession state)
    {
        string? shieldId = null;
        for (int slot = 0; slot < state.Hero.inventory.nbWeapons; slot++)
        {
            InventItem? item = state.Hero.inventory.getEquippedWeaponOn(slot);
            if (item is not null && item.hasTag("Shield".AsHaxeString()))
            {
                shieldId = StableItemId(item);
                break;
            }
        }
        state.Rules.EquipShield(shieldId);
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
        AttackRoute route = state.Rules.ResolveAttack(Classify(weapon.item), StableItemId(weapon.item));
        return route switch
        {
            AttackRoute.HostWeapon or AttackRoute.Parry => orig(weapon),
            AttackRoute.Ability => provider!.TryAbilityAttack(state, state.Rules.AbilityId!, weapon),
            _ => false
        };
    }

    private bool Equip(Hook_Inventory.orig_equip orig, Inventory inventory, InventItem item)
    {
        KirbySession? state = Synchronize(Game.Instance.HeroInstance);
        if (state is null || inventory.HashlinkPointer != state.Hero.inventory.HashlinkPointer)
            return orig(inventory, item);
        // Non-combat progression items, outfits, consumables and mutations remain host-owned.
        if (item.kind is not InventItemKind.Weapon && item.kind is not InventItemKind.Active)
            return orig(inventory, item);
        if (!state.Rules.CanEquip(Classify(item)))
            return false;
        bool equipped = orig(inventory, item);
        if (equipped && state.Rules.IsBaseForm)
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
        KirbySession? state = Synchronize(Game.Instance.HeroInstance);
        if (state is not null)
            provider!.OnFrame(state, dt);
    }

    void IOnHeroDispose.OnHeroDispose() => Deactivate();
}

public sealed class RuntimeConfig
{
    public bool EnableOriginalRuntime { get; set; }
    public string RomPath { get; set; } = "/home/rayzza/Documents/ROMS/Nintendo DS/Kirby Super Star Ultra (USA).nds";
    public string SeedStatePath { get; set; } = "/home/rayzza/.local/share/kirby-dead-cells/oracle/ready2/000.state";
    public string OutfitId { get; set; } = "KirbySuperStarUltra";
    public bool LoadDecodedAssetAtlases { get; set; }
    public string[] SwordIds { get; set; } = [];
    public string[] BowIds { get; set; } = [];
}
