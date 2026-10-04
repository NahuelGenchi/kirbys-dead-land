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
/// Compiled DCCM adapter, deliberately dormant without an original-gameplay
/// provider. Loading decoded pose atlases is a separate, explicit developer option.
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

    public override void Initialize()
    {
        Instance = this;
        string path = Info.ModRoot.GetFilePath("config.json");
        if (File.Exists(path))
            config = JsonSerializer.Deserialize<RuntimeConfig>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Kirby runtime configuration is null.");
        Hook_Weapon.tryToUse += TryUseWeapon;
        Hook_Inventory.equip += Equip;
        Hook_Hero.fixedUpdate += FixedUpdate;
        Hook_HeroWeaponsManager.onLevelChanged += LevelChanged;
        Logger.Information("Kirby runtime adapter initialized. Faithful controller provider required; no gameplay approximation enabled.");
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
        // Decoded pose atlases are development resources, not a playable outfit.
        if (provider is null && !config.LoadDecodedAssetAtlases)
            return;
        string path = Info.ModRoot.GetFilePath("res.pak");
        if (!File.Exists(path))
        {
            Logger.Warning("Kirby res.pak is absent; no character assets were loaded.");
            return;
        }
        FsPak.Instance.FileSystem.loadPak(path.AsHaxeString());
        Logger.Information("Loaded decoded Kirby pose atlases from {Path}; no outfit or animation mapping is registered.", path);
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
                Logger.Warning("Kirby is not playable: NDS controller, inhale and ability implementations have not been supplied. Host controls remain unchanged.");
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
    public string OutfitId { get; set; } = "KirbySuperStarUltra";
    public bool LoadDecodedAssetAtlases { get; set; }
    public string[] SwordIds { get; set; } = [];
    public string[] BowIds { get; set; } = [];
}
