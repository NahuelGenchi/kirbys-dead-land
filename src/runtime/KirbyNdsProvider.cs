using Path = System.IO.Path;
using dc.en;
using dc.tool;
using dc.tool.atk;
using Kirby.Core;
using HaxeProxy.Runtime;
using ModCore.Storage;

namespace Kirby.Runtime;

/// <summary>Executes the original controller; translates host terrain and entities at the engine boundary.</summary>
public sealed unsafe class KirbyNdsProvider(RuntimeConfig config, string modRoot) : IKirbyGameplayProvider
{
    public static readonly string[] Abilities = ["Cutter", "Mirror", "Beam", "Fighter", "Suplex", "YoYo", "Wheel", "Bomb", "Ice", "Fire", "Plasma", "Ninja", "Stone", "Wing", "Jet", "Copy", "Sword", "Hammer", "Parasol", "Paint", "Mike", "Crash", "Cook", "Sleep"];
    public IReadOnlyCollection<string> ImplementedAbilityIds => Abilities;
    private NdsRuntime? nds;
    private KirbySpriteRenderer? renderer;
    private NdsAudio? audio;
    private readonly ushort[] cells = new ushort[900];
    private double credit;
    private bool pendingCopy;
    private byte previousAbility = 1;
    private readonly SaveData<KirbySave> save = new("KirbySuperStarUltra.OriginalRuntime");
    private KirbySave? boundSave;
    private KirbySession? lastSession;
    private readonly KirbyEnemies enemies = new();
    private readonly Queue<(dc.Entity? Source, int Damage, int Direction)> incoming = new();
    private bool applyingNativeDamage;
    private int sourceMaxLife = 76;
    private HostBodySnapshot? originalBody, savedHostBody;
    private bool importHostHealth;
    private bool firstRender;
    public bool ControlsEnemy(Mob mob) => enemies.Controls(mob);
    private ushort input;
    private nint terrainLevel;
    private nint sourceLevel;
    private readonly Dictionary<(int, int), bool> water = [];
    public NdsRuntime? Machine => nds;
    public void OnActivated(KirbySession session)
    {
        LabActivationTrace.Mark("OnActivated enter");
        bool firstActivation = nds is null;
        double hostHealthRatio = session.Hero.life / (double)Math.Max(1, session.Hero.maxLife);
        double hostMaxLife = Math.Max(1, session.Hero.maxLife);
        bool hostAlive = session.Hero.life > 0;
        originalBody = new(session.Hero.radius, session.Hero.hei, session.Hero.bodyHei, session.Hero.maxLife);
        // Migrate private development checkpoints written before bodyHei was
        // identified as a cell count. These values are native Hero defaults.
        if (originalBody.BodyHeight > 4) originalBody = new(16.8, 54, 2.25, originalBody.MaxLife);
        nds ??= new NdsRuntime(Path.Combine(modRoot, "libkssu_bridge.so"), config.RomPath, config.SeedStatePath);
        LabActivationTrace.Mark("Native runtime ready");
        session.Rules.Restore(lastSession?.Rules.AbilityId, lastSession?.Rules.RetainedShieldId ?? session.Rules.RetainedShieldId,
            lastSession?.Rules.AbilityEmpowered ?? false, lastSession?.Rules.PendingEmpowerment);
        lastSession = session;
        LabActivationTrace.Mark("BindSave begin");
        var binding = BindSave(session);
        if (binding.Restored) sourceLevel = session.Hero._level.HashlinkPointer;
        LabActivationTrace.Mark("BindSave complete");
        KirbyTutorials.OnActivated(session.Hero);
        sourceMaxLife = Math.Max(1, nds.MaxHealth);
        if (binding.Restored ? !binding.WasKirby : importHostHealth || firstActivation)
            nds.SetHealth((ushort)Math.Clamp((int)Math.Round(hostHealthRatio * sourceMaxLife), hostAlive ? 1 : 0, sourceMaxLife),
                (ushort)sourceMaxLife);
        importHostHealth = false;
        session.Hero.maxLife = sourceMaxLife;
        session.Hero.life = Math.Clamp(nds.Health, 0, sourceMaxLife);
        session.Hero.curRally *= sourceMaxLife / hostMaxLife;
        session.Hero.bonusLife = (int)Math.Round(session.Hero.bonusLife * sourceMaxLife / hostMaxLife);
        InitializeHealthDisplay(session.Hero);
        LabActivationTrace.Mark("Renderer begin");
        renderer = new KirbySpriteRenderer(session.Hero);
        firstRender = true;
        LabActivationTrace.Mark("Audio begin");
        audio = new NdsAudio(nds);
        LabActivationTrace.Mark("Audio complete");
        credit = 0; previousAbility = nds.State.Ability;
        session.Hero.radius = 9;
        session.Hero.bodyHei = 29.0 / 24; session.Hero.hei = 29;
        LabActivationTrace.Mark("OnActivated complete");
        // The host may initialize a character in a scripted arrival sequence;
        // source stepping begins after that sequence yields control.
    }
    public void OnDeactivated(KirbySession session)
    {
        renderer?.Dispose(); renderer = null;
        audio?.Dispose(); audio = null;
        incoming.Clear();
        if (!session.Hero.destroyed && originalBody is { } body)
        {
            var hero = session.Hero;
            importHostHealth = hero._level?.game?.user?.heroSkin?.ToString() != config.OutfitId;
            hero.radius = body.Radius; hero.hei = body.Height; hero.bodyHei = body.BodyHeight;
            double healthScale = body.MaxLife / (double)Math.Max(1, hero.maxLife);
            hero.life = (int)Math.Round(Math.Clamp(hero.life / (double)Math.Max(1, hero.maxLife), 0, 1) * body.MaxLife);
            hero.curRally *= healthScale;
            hero.bonusLife = (int)Math.Round(hero.bonusLife * healthScale);
            hero.maxLife = body.MaxLife;
            InitializeHealthDisplay(hero);
        }
        savedHostBody = originalBody;
        originalBody = null;
        if (nds is not null) enemies.Clear(nds);
    }
    public void ResetForNewRun()
    {
        save.Value = new KirbySave(); boundSave = null;
        lastSession?.Rules.Reset(); pendingCopy = false; importHostHealth = false; sourceLevel = 0;
        if (nds is not null) { enemies.Clear(nds); nds.LoadState(config.SeedStatePath); }
    }
    private (bool Restored, bool WasKirby) BindSave(KirbySession session)
    {
        if (ReferenceEquals(boundSave, save.Value)) return (false, false);
        bool wasKirby = false;
        string? encoded = save.Value.Snapshot;
        if (encoded is not null)
        {
            var value = KirbySnapshot.Decode(encoded);
            wasKirby = value.KirbyActive;
            string path = Path.Combine(modRoot, $".restore-{Guid.NewGuid():N}.state");
            try { System.IO.File.WriteAllBytes(path, value.Machine); nds!.LoadState(path); }
            finally { System.IO.File.Delete(path); }
            session.Rules.Restore(value.Ability, value.Shield, value.AbilityEmpowered, value.PendingEmpowerment);
            if (value.KirbyActive && value.HostBody is not null) originalBody = value.HostBody;
            pendingCopy = value.PendingCopy;
            enemies.Restore(value.Enemies);
        }
        else if (boundSave is not null)
        {
            nds!.LoadState(config.SeedStatePath); session.Rules.Reset(); pendingCopy = false;
        }
        boundSave = save.Value;
        boundSave.Capture = CaptureSave;
        savedHostBody = originalBody;
        return (encoded is not null, wasKirby);
    }
    private string? CaptureSave()
    {
        if (nds is null || lastSession is null) return null;
        string path = Path.Combine(modRoot, $".capture-{Guid.NewGuid():N}.state");
        try
        {
            nds.SaveState(path);
            return new KirbySnapshot { Ability = lastSession.Rules.AbilityId, Shield = lastSession.Rules.RetainedShieldId,
                PendingCopy = pendingCopy, AbilityEmpowered = lastSession.Rules.AbilityEmpowered,
                PendingEmpowerment = lastSession.Rules.PendingEmpowerment, KirbyActive = KirbyMod.Instance?.ActiveSession is not null,
                HostBody = originalBody ?? savedHostBody,
                Enemies = enemies.Capture(), Machine = System.IO.File.ReadAllBytes(path) }.Encode();
        }
        finally { System.IO.File.Delete(path); }
    }
    public void BeforeHostUpdate(KirbySession session, Action original)
    {
        input = KirbyInput.Read(session);
        KirbyInput.WithoutHostMovement(session.Hero, original);
    }
    /// <summary>Keep equipment/stat recalculation in the source health units before the HUD observes it.</summary>
    public void UpdateHostMaxLife(KirbySession session, Action original)
    {
        if (nds is null || originalBody is null) { original(); return; }
        var hero = session.Hero;
        int life = hero.life, bonus = hero.bonusLife;
        double rally = hero.curRally;
        original();
        // Retain the host's recalculated maximum for switching away from Kirby.
        // Avoid a round-trip through host HP: repeated floor conversions would
        // gradually lose wounded source HP and contaminate pickup HUD snapshots.
        originalBody = originalBody with { MaxLife = hero.maxLife };
        hero.maxLife = sourceMaxLife;
        hero.life = life;
        hero.curRally = rally;
        hero.bonusLife = bonus;
    }
    private static void InitializeHealthDisplay(Hero hero)
    {
        if (hero._level?.game?.hud?.heroLifeBar is not { } bar) return;
        bar.init(hero.life, hero.maxLife);
        hero.updateLifeBar();
    }
    public void ReceiveHostAttack(KirbySession session, AttackData attack, Action original)
    {
        if (applyingNativeDamage || nds is null || attack.finalDmg <= 0 || !attack.isSuccess())
        { original(); return; }
        // The host has already resolved its shield/parry path. Remaining hits
        // enter the next original DS collision tick, retaining source guard,
        // recoil, copy loss and invulnerability without advancing extra frames.
        if (incoming.Count < 64)
            incoming.Enqueue((attack.source, attack.finalDmg, -Math.Sign(attack.dirSourceToTarget())));
        attack.inflictedDmg = 0;
    }
    public void OnFixedUpdate(KirbySession session, Action originalHeroUpdate)
    {
        Hero h = session.Hero;
        if (!h.awake || h._level.game.hasCinematic()) { originalHeroUpdate(); return; }
        double gravity = h.gravity;
        var position = (h.cx, h.cy, h.xr, h.yr);
        h.dx = h.dy = h.bdx = h.bdy = 0; h.gravity = 0;
        h.radius = 9; h.bodyHei = 29.0 / 24; h.hei = 29;
        h.fallStartY = (h.cy + h.yr) * 24;
        try { KirbyInput.WithoutHostMovement(h, originalHeroUpdate); }
        finally
        {
            h.gravity = gravity; h.dx = h.dy = h.bdx = h.bdy = 0;
            // Host equipment and interactions still update, but its Beheaded
            // collision correction must not move the source physics anchor.
            h.cx = position.cx; h.cy = position.cy; h.xr = position.xr; h.yr = position.yr;
            h.radius = 9; h.bodyHei = 29.0 / 24; h.hei = 29;
        }
    }
    public void OnFrame(KirbySession session, double engineDelta)
    {
        if (nds is null) return;
        BindSave(session);
        var h = session.Hero;
        if (h.life <= 0 || h._level.game.paused || h._level.paused || !h.awake || h._level.game.hasCinematic())
        { credit = 0; audio?.Pause(); RenderSource(); return; }
        input = KirbyInput.Read(session);
        credit += Math.Clamp(engineDelta, 0, 0.25) * (33513982.0 / 560190.0);
        while (credit >= 1 && h.life > 0)
        {
            Step(session, input);
            credit--;
        }
        RenderSource();
        var options = dc.Main.Class.ME.options;
        audio?.Pump(nds, options.mute ? 0 : options.masterVolume * options.sfxVolume * options.sfxHeroVolume);
    }
    private void RenderSource()
    {
        if (nds is null || renderer is null) return;
        if (firstRender) LabActivationTrace.Mark("First source Render begin");
        renderer.Render(nds);
        if (firstRender) { LabActivationTrace.Mark("First source Render complete"); firstRender = false; }
    }
    private void Step(KirbySession session, ushort mask)
    {
        Hero hero = session.Hero;
        nint level = hero._level.HashlinkPointer;
        if (sourceLevel != 0 && sourceLevel != level)
        {
            enemies.Clear(nds!);
            nds!.CleanupRoom();
        }
        sourceLevel = level;
        if (hero.maxLife != sourceMaxLife && hero.maxLife > 0)
        {
            double scale = sourceMaxLife / (double)hero.maxLife;
            if (originalBody is not null) originalBody = originalBody with { MaxLife = hero.maxLife };
            hero.life = Math.Clamp((int)Math.Round(hero.life / (double)hero.maxLife * sourceMaxLife),
                hero.life > 0 ? 1 : 0, sourceMaxLife);
            hero.curRally *= scale;
            hero.bonusLife = (int)Math.Round(hero.bonusLife * scale);
            hero.maxLife = sourceMaxLife;
            InitializeHealthDisplay(hero);
        }
        hero.maxLife = sourceMaxLife;
        if (nds!.Health != hero.life && nds.Health >= 0)
            nds.SetHealth((ushort)Math.Clamp(hero.life, 0, sourceMaxLife), (ushort)sourceMaxLife);
        double hostX = (hero.cx + hero.xr) * 24;
        double centerY = (hero.cy + hero.yr) * 24 - 14.5;
        int originX = (int)Math.Floor(hostX / 24) - 16;
        int originY = (int)Math.Floor(centerY / 24) - 4;
        ExportTerrain(hero, originX, originY);
        nds!.World(cells, checked((int)Math.Round((hostX - originX * 24) * 65536)),
            checked((int)Math.Round((centerY - originY * 24) * 65536)));
        enemies.Prepare(session, nds, originX * 24, originY * 24);
        int healthBefore = nds.Health;
        dc.Entity? damageSource = null;
        if (incoming.TryDequeue(out var request))
        {
            damageSource = request.Source;
            nds.Hurt((ushort)Math.Clamp(request.Damage, 1, 32767), request.Direction == 0 ? 1 : request.Direction);
            // Original collision resolves one hurt state per source tick; its
            // immunity handles simultaneous contacts, rather than host FPS.
            incoming.Clear();
        }
        NdsAudio.PrepareSource(nds);
        nds.Step(mask);
        int healthAfter = nds.Health;
        // The bridge normalizes the original signed lethal HP sentinel to zero.
        // Stop source stepping here: the host owns the run's death transition,
        // and its last-hit protection must not resurrect the ROM player.
        if (healthBefore > 0 && healthAfter == 0)
        {
            hero.kill();
            return;
        }
        if (healthBefore >= 0 && healthAfter >= 0 && healthAfter != healthBefore)
        {
            if (healthAfter > healthBefore) hero.life = Math.Min(sourceMaxLife, hero.life + healthAfter - healthBefore);
            else
            {
                int damage = healthBefore - healthAfter;
                var attack = AttackUtils.Class.createFromMob(damageSource is { destroyed: false } ? damageSource : hero, damage, null);
                attack.hitResult = new dc.HitResult.Normal();
                attack.finalDmg = damage;
                attack.rawFinalDmg = damage;
                attack.lastHitTarget = hero;
                applyingNativeDamage = true;
                try { hero.applyAttackResult(attack); }
                finally { applyingNativeDamage = false; }
            }
        }
        if (enemies.Complete(session, nds, originX * 24, originY * 24) is not null) pendingCopy = true;
        NdsState state = nds.State;
        hero.setPosPixel(state.X + originX * 24, state.Y + originY * 24 + 14.5);
        // The host represents a foot exactly on a floor as yr=1 in the air
        // cell above it. setPosPixel chooses yr=0 inside the solid cell; that
        // invokes its stuck-in-wall rescue and would fight the source controller.
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
        hero.dir = (state.FacingRaw & 1) != 0 ? -1 : 1;
        hero.bodyHei = 29.0 / 24; hero.hei = 29;
        if (state.Ability != previousAbility)
        {
            if (state.Ability == 1 && !pendingCopy) session.DropAbility();
            previousAbility = state.Ability;
            if (state.Ability > 1)
            {
                session.Rules.SetSourceAbility(Abilities[state.Ability - 2]);
                pendingCopy = false;
            }
        }
        if (pendingCopy && state.Ability == 1 && (nds.ActionFlags & 2) == 0 && (mask & 2) != 0)
        { pendingCopy = false; session.DropAbility(); }
    }
    private void ExportTerrain(Hero hero, int originX, int originY)
    {
        var map = hero._level.map;
        if (terrainLevel != map.HashlinkPointer) { terrainLevel = map.HashlinkPointer; water.Clear(); }
        int* grid = (int*)map.collisions.bytes;
        for (int y = 0; y < 9; y++)
        for (int x = 0; x < 100; x++)
        {
            int cx = originX + x, cy = originY + y;
            int c = cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei ? 3 : grid[cy * map.wid + cx];
            ushort tile = (c & 512) != 0 ? ((c & 1024) != 0 ? (ushort)133 : (ushort)132)
                : (c & 8) != 0 ? (ushort)131 : (c & 3) != 0 ? (ushort)3 : (c & 16) != 0 ? (ushort)134 : (ushort)0;
            if (tile == 0 && cx >= 0 && cy >= 0 && cx < map.wid && cy < map.hei)
            {
                if (!water.TryGetValue((cx, cy), out bool submerged))
                {
                    submerged = map.getRoomAt(cx, cy) is not null && map.getWaterSurfaceY(cx, cy) > 0;
                    water[(cx, cy)] = submerged;
                }
                if (submerged) tile = 130;
            }
            cells[y * 100 + x] = tile;
        }
    }
    public bool TryAbilityAttack(KirbySession session, string abilityId, Weapon inputWeapon) => false;
    public void OnAbilityChanged(KirbySession session, string? abilityId) { }
}
