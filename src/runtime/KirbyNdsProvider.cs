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
    // The original lethal recoil, star burst and spin-away take about three
    // seconds. Continuing past them starts the source's own life-lost scene
    // transition, which the host replaces with its own death sequence.
    private const int DeathAnimationFrames = 180;
    private readonly FramePacer pacer = new();
    private readonly DashAssist dash = new();
    private readonly SlideKey slideKey = new();
    private readonly RollZoneGate rollZones = new();
    private readonly StompController stomp = new();
    private readonly SpeedMatch.ActionFilter motionActions = new();
    private readonly AnimationPacer walkCycle = new();
    private readonly CinematicWalk cinematicWalk = new();
    // The host cell row whose top is the floor of a cinematic walk, and its row in the exported window.
    private int? cinematicFloor;
    private const int CinematicFloorRow = 5;
    private dc.tool.mainSkills.DiveAttack? activeDive;
    // One-way platforms and ladder tops the host dive passes through, down to this row.
    private int stompIgnoreRow = int.MinValue;
    private ushort previousInput;
    private bool previousDiveButton;
    // A key bound to both Up and jump is climbing, not jumping, until released.
    private bool upJumpClimbs;
    // Source frames after a stomp lands during which enemies it stunned cannot hurt Kirby by touch.
    private const int StompLandingGrace = 30;
    private int stompShield;
    private int lastVx, lastVy;
    private readonly Dictionary<int, double> tierLifeScale = [];
    private int deathFrames = -1;
    private bool sourceDead;
    private (int Cx, int Cy, double Xr, double Yr)? deathAnchor;
    private dc.pr.Level? deathLevel;
    private bool pendingCopy;
    private byte previousAbility = 1;
    private readonly SaveData<KirbySave> save = new("KirbySuperStarUltra.OriginalRuntime");
    private KirbySave? boundSave;
    private KirbySession? lastSession;
    private readonly KirbyEnemies enemies = new();
    /// <summary>Floor ability items in Kirby's mouth until the original swallow or spit settles them.</summary>
    private readonly MouthfulItems<KirbyAbilityItems.Held> mouthfulItems = new();
    /// <summary>
    /// Host life per original damage point for this target. Host enemy life
    /// grows with its tier; the same host curve keeps original abilities as
    /// effective against later enemies as against the first ones, while each
    /// enemy type's base life and elite or boss extra life still apply.
    /// </summary>
    private double SourceDamageScale(dc.Entity target)
    {
        double scale = config.CopyDamageScale > 0 ? config.CopyDamageScale : 1;
        if (target is not Mob mob) return scale;
        if (!tierLifeScale.TryGetValue(mob.trueLifeTier, out double tier))
        {
            tier = dc.Const.Class.scaleMobLifeToTier.Invoke(1000.0, mob.trueLifeTier) / 1000;
            if (!double.IsFinite(tier) || tier <= 0) tier = 1;
            tierLifeScale[mob.trueLifeTier] = tier;
        }
        return scale * tier;
    }
    private readonly Queue<(dc.Entity? Source, int Damage, int Direction)> incoming = new();
    private bool applyingNativeDamage;
    private int sourceMaxLife = 76;
    private HostBodySnapshot? originalBody, savedHostBody;
    private bool importHostHealth;
    private bool firstRender;
    public bool ControlsEnemy(dc.Entity entity) => enemies.Controls(entity);
    private ushort input;
    private nint terrainLevel;
    private nint sourceLevel;
    private readonly Dictionary<(int, int), bool> water = [];
    public NdsRuntime? Machine => nds;
    internal KirbyEnemies Enemies => enemies;
    /// <summary>
    /// Kirby is in the fast part of his slide. Dead Cells lets a rolling hero
    /// smash breakable tiles and burst doors; Kirby's slide is his roll.
    /// </summary>
    public bool SlideStrike { get; private set; }
    /// <summary>
    /// Suplex's grab lunge is carrying Kirby at speed (HostStrikes.Grab). Doors
    /// resist the original grab, so it bursts them like a roll.
    /// </summary>
    public bool GrabStrike { get; private set; }
    /// <summary>True while Kirby's stomp drives the host dive attack.</summary>
    public bool Stomping => stomp.Active;
    /// <summary>True while the original death animation finishes after the host destroyed the hero.</summary>
    public bool PlayingDeath => deathLevel is not null;
    public dc.pr.Level? DeathLevel => deathLevel;
    /// <summary>Lab-only event sink; null in normal play.</summary>
    internal static Action<string, object>? Diagnostic;
    /// <summary>Host log for the periodic frame-pacing summary.</summary>
    internal static Action<string>? Log;
    private readonly System.Diagnostics.Stopwatch paceClock = System.Diagnostics.Stopwatch.StartNew();
    private long paceStepTicks, paceRenderTicks;
    private int paceFrames, paceSteps, paceLocked, paceIdle, paceDoubled;
    private double paceStarted;
    public void OnActivated(KirbySession session)
    {
        LabActivationTrace.Mark("OnActivated enter");
        EndDetachedDeath();
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
        // The F slot carries over like the copy; a press in progress does not (#102).
        KirbyFSlot.Reset();
        if (lastSession is not null && !ReferenceEquals(lastSession, session))
            session.Rules.Stored.Restore(lastSession.Rules.Stored.Ability, lastSession.Rules.Stored.Uses);
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
        renderer = new KirbySpriteRenderer(session.Hero, config.FlashStrength);
        firstRender = true;
        LabActivationTrace.Mark("Audio begin");
        audio = new NdsAudio(nds);
        LabActivationTrace.Mark("Audio complete");
        pacer.Reset(); dash.Reset(); slideKey.Reset(); rollZones.Reset(); CancelStomp(); previousAbility = nds.State.Ability; window = null;
        motionActions.Reset();
        previousInput = 0; previousDiveButton = false; upJumpClimbs = false; SlideStrike = GrabStrike = false;
        deathFrames = -1; deathAnchor = null; sourceDead = nds.Health == 0;
        session.Hero.radius = 9;
        session.Hero.bodyHei = 29.0 / 24; session.Hero.hei = 29;
        LabActivationTrace.Mark("OnActivated complete");
        // The host may initialize a character in a scripted arrival sequence;
        // source stepping begins after that sequence yields control.
    }
    public void OnDeactivated(KirbySession session)
    {
        KirbyHud.Clear(session);
        CancelStomp();
        standingOn = null;
        SlideStrike = GrabStrike = false;
        // The host destroys a dead hero immediately and stages its corpse. The
        // original death animation continues from the host frame event instead.
        if (session.Hero.life <= 0 && renderer is not null && nds is not null && deathFrames < DeathAnimationFrames)
        {
            if (deathFrames < 0) BeginDeath(session);
            deathLevel = session.Hero._level;
            renderer.Detach();
        }
        else
        {
            renderer?.Dispose(); renderer = null;
            audio?.Dispose(); audio = null;
        }
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
        EndDetachedDeath();
        save.Value = new KirbySave(); boundSave = null;
        lastSession?.Rules.Reset(); pendingCopy = false; importHostHealth = false; sourceLevel = 0;
        mouthfulItems.Clear();
        deathFrames = -1; deathAnchor = null; sourceDead = false;
        if (nds is not null) { enemies.Clear(nds); nds.LoadState(config.SeedStatePath); }
        window = null;
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
            try { System.IO.File.WriteAllBytes(path, value.Machine); nds!.LoadState(path); window = null; }
            finally { System.IO.File.Delete(path); }
            session.Rules.Restore(value.Ability, value.Shield, value.AbilityEmpowered, value.PendingEmpowerment);
            if (value.KirbyActive && value.HostBody is not null) originalBody = value.HostBody;
            pendingCopy = value.PendingCopy;
            mouthfulItems.Clear();
            enemies.Restore(value.Enemies);
            KirbyFSlot.Restore(session, value.FSlot);
        }
        else if (boundSave is not null)
        {
            nds!.LoadState(config.SeedStatePath); session.Rules.Reset(); KirbyFSlot.Reset(); pendingCopy = false; window = null;
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
                HostBody = originalBody ?? savedHostBody, FSlot = KirbyFSlot.Capture(lastSession),
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
        if (session.Hero.life <= 0) { attack.inflictedDmg = 0; return; }
        // The host has already resolved its shield/parry path. Remaining hits
        // enter the next original DS collision tick, retaining source guard,
        // recoil, copy loss and invulnerability without advancing extra frames.
        Diagnostic?.Invoke("host-attack", new { source = attack.source?.GetType().FullName, attack.finalDmg,
            hit = attack.hitResult?.GetType().Name, weapon = attack.sourceWeapon?.GetType().FullName,
            carrier = attack.carrier?.GetType().FullName, dmgType = attack.dmgType?.GetType().Name });
        if (incoming.Count < 64)
            incoming.Enqueue((attack.source, ScaleIncomingDamage(attack.finalDmg), -Math.Sign(attack.dirSourceToTarget())));
        KirbyDamageLog.HostAttack(session.Hero, attack, ScaleIncomingDamage(attack.finalDmg));
        attack.inflictedDmg = 0;
    }
    /// <summary>
    /// Host damage is balanced against the Beheaded's health pool, including
    /// Survival upgrades. Convert it to the same fraction of Kirby's original
    /// health so the host difficulty and its health upgrades still apply.
    /// </summary>
    private int ScaleIncomingDamage(int damage) =>
        originalBody is { MaxLife: > 0 } body && body.MaxLife != sourceMaxLife
            ? Math.Max(1, (int)Math.Round(damage * (double)sourceMaxLife / body.MaxLife))
            : damage;
    public void OnFixedUpdate(KirbySession session, Action originalHeroUpdate)
    {
        Hero h = session.Hero;
        if (h.life <= 0 && deathAnchor is { } anchor)
        {
            // The host death sequence runs, but its knockback and gravity must
            // not carry the body while the original death animation plays.
            try { originalHeroUpdate(); }
            finally
            {
                h.dx = h.dy = h.bdx = h.bdy = 0;
                h.cx = anchor.Cx; h.cy = anchor.Cy; h.xr = anchor.Xr; h.yr = anchor.Yr;
            }
            return;
        }
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
        double delta = Math.Clamp(engineDelta, 0, 0.25);
        bool paused = h._level.game.paused || h._level.paused;
        if (h.life <= 0)
        {
            if (deathFrames < 0) BeginDeath(session);
            if (paused) { pacer.Reset(); audio?.Pause(); }
            else StepDeath(delta);
            RenderSource();
            return;
        }
        if (sourceDead) ReviveSource(session);
        if (paused || !h.awake || h._level.game.hasCinematic())
        {
            // A pause keeps a stomp in the air; a cinematic or a control lock ends it.
            if (!paused) CancelStomp();
            int walk = paused ? 0 : cinematicWalk.Update((h.cx + h.xr) * 24, delta);
            if (walk == 0) { pacer.Reset(); audio?.Pause(); RenderSource(); return; }
            int walkSteps = pacer.Steps(delta);
            for (int i = 0; i < walkSteps; i++) StepCinematicWalk(h, walk);
            RenderSource();
            PumpAudio();
            return;
        }
        cinematicWalk.Reset(); cinematicFloor = null;
        input = KirbyInput.Read(session);
        int steps = pacer.Steps(delta);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < steps && h.life > 0; i++)
            Step(session, input);
        long stepped = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderSource();
        KirbyAbilityItems.Sync(session, mouthfulItems, pendingCopy);
        KirbyFSlot.Update(session);
        KirbyHud.Update(session, nds);
        PumpAudio();
        RecordPacing(steps, stepped - started, System.Diagnostics.Stopwatch.GetTimestamp() - stepped);
    }
    /// <summary>
    /// Once a minute, log how the original clock met the host's frames. A
    /// smooth display shows one original frame on nearly every host frame.
    /// </summary>
    private void RecordPacing(int steps, long stepTicks, long renderTicks)
    {
        paceFrames++; paceSteps += steps; paceStepTicks += stepTicks; paceRenderTicks += renderTicks;
        if (pacer.Locked) paceLocked++;
        if (steps == 0) paceIdle++; else if (steps > 1) paceDoubled++;
        double now = paceClock.Elapsed.TotalSeconds, span = now - paceStarted;
        if (span < 60) return;
        double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Log?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Kirby pacing: host {paceFrames / span:0.0} fps, original {paceSteps / span:0.0} fps, locked {100.0 * paceLocked / paceFrames:0}%, frames without a step {paceIdle}, with several {paceDoubled}, original step {paceStepTicks * ms / Math.Max(1, paceSteps):0.00} ms, render {paceRenderTicks * ms / paceFrames:0.00} ms."));
        paceStarted = now;
        paceFrames = paceSteps = paceLocked = paceIdle = paceDoubled = 0;
        paceStepTicks = paceRenderTicks = 0;
    }
    private void PumpAudio()
    {
        var options = dc.Main.Class.ME.options;
        audio?.Pump(nds!, options.mute ? 0 : options.masterVolume * options.sfxVolume * options.sfxHeroVolume);
    }
    /// <summary>
    /// Continue the original machine through Kirby's own lethal recoil and
    /// spin-away while the host runs its death sequence. Input, terrain and
    /// enemy transfer stop; the host body stays where Kirby fell.
    /// </summary>
    private void BeginDeath(KirbySession session)
    {
        var hero = session.Hero;
        deathFrames = 0;
        pacer.Reset();
        CancelStomp();
        standingOn = null;
        SlideStrike = GrabStrike = false;
        incoming.Clear();
        deathAnchor = (hero.cx, hero.cy, hero.xr, hero.yr);
        // A host-side lethal effect still uses the original lethal recoil.
        if (nds!.Health > 0)
        {
            nds.SetHealth(1, (ushort)sourceMaxLife);
            nds.Hurt(32767, hero.dir >= 0 ? 1 : -1);
        }
    }
    private void StepDeath(double delta)
    {
        int steps = pacer.Steps(delta);
        for (int i = 0; i < steps && deathFrames < DeathAnimationFrames; i++, deathFrames++)
        {
            NdsAudio.PrepareSource(nds!);
            nds!.Step(0);
            if (nds.Health == 0) sourceDead = true;
            // Guard or source invulnerability can reject a host-side lethal
            // hit. Freeze Kirby rather than letting him idle through the host death.
            else if (deathFrames >= 20) deathFrames = DeathAnimationFrames;
        }
        PumpAudio();
    }
    /// <summary>Host frame event: finish an original death animation after the hero was destroyed.</summary>
    public void OnHostFrame(double engineDelta)
    {
        if (deathLevel is null) return;
        if (nds is null || renderer is not { Attached: true } || deathLevel.destroyed || deathFrames >= DeathAnimationFrames)
        { EndDetachedDeath(); return; }
        if (!deathLevel.paused && deathLevel.game?.paused != true) StepDeath(Math.Clamp(engineDelta, 0, 0.25));
        else audio?.Pause();
        renderer.RenderDetached(nds);
    }
    private void EndDetachedDeath()
    {
        if (deathLevel is null) return;
        deathLevel = null;
        renderer?.Dispose(); renderer = null;
        audio?.Dispose(); audio = null;
    }
    /// <summary>
    /// A host revival (for example an extra-life mutation) cannot resume an
    /// original player already in its death state. Reload the clean seed
    /// machine in base form; the retained shield and host health are kept.
    /// </summary>
    private void ReviveSource(KirbySession session)
    {
        sourceDead = false; deathFrames = -1; deathAnchor = null; pacer.Reset(); dash.Reset();
        rollZones.Reset(); CancelStomp();
        incoming.Clear();
        enemies.Clear(nds!);
        nds!.LoadState(config.SeedStatePath);
        window = null;
        renderer?.ResetPalette();
        pendingCopy = false;
        KirbyFSlot.Sequence.Reset();
        if (!session.Rules.IsBaseForm) session.DropAbility();
        previousAbility = nds.State.Ability;
        nds.SetHealth((ushort)Math.Clamp(session.Hero.life, 1, sourceMaxLife), (ushort)sourceMaxLife);
    }
    private void RenderSource()
    {
        if (nds is null || renderer is null) return;
        if (firstRender) LabActivationTrace.Mark("First source Render begin");
        renderer.Render(nds);
        if (firstRender) { LabActivationTrace.Mark("First source Render complete"); firstRender = false; }
    }
    private void EnterLevel(Hero hero)
    {
        nint level = hero._level.HashlinkPointer;
        if (sourceLevel != 0 && sourceLevel != level)
        {
            enemies.Clear(nds!);
            nds!.CleanupRoom();
            window = null;
            CancelStomp();
            rollZones.Reset();
        }
        sourceLevel = level;
    }
    /// <summary>
    /// One original frame while a host cinematic walks the hero, for example
    /// through a level's exit door. The original controller holds the same
    /// direction, so Kirby walks with his own animation and facing instead of
    /// gliding in his last pose. The host keeps the position; enemies, damage
    /// and abilities resume with normal play.
    /// </summary>
    private void StepCinematicWalk(Hero hero, int direction)
    {
        EnterLevel(hero);
        double hostX = (hero.cx + hero.xr) * 24;
        // An exit door lifts the hero up the steps inside its doorway, where
        // the host has no floor; the original would fall there. It walks on a
        // flat floor at the height the walk began. Kirby is drawn on the host
        // body, so he still climbs with it.
        int floor = cinematicFloor ??= (int)Math.Round(hero.cy + hero.yr);
        double centerY = floor * 24 - 14.5;
        int originX = (int)Math.Floor(hostX / 24) - 16;
        int originY = floor - CinematicFloorRow;
        cells.AsSpan(0, CinematicFloorRow * 100).Clear();
        cells.AsSpan(CinematicFloorRow * 100).Fill(3);
        PlaceSource(originX, originY, checked((int)Math.Round((hostX - originX * 24) * 65536)),
            checked((int)Math.Round((centerY - originY * 24) * 65536)), carried: false);
        PaceWalkCycle(nds!.Action, config.MatchHostSpeed);
        NdsAudio.PrepareSource(nds);
        nds.Step(direction > 0 ? (ushort)16 : (ushort)32);
    }
    private void Step(KirbySession session, ushort mask)
    {
        Hero hero = session.Hero;
        EnterLevel(hero);
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
        {
            if (hero.life < nds.Health) KirbyDamageLog.HostLife(hero, nds.Health, hero.life);
            nds.SetHealth((ushort)Math.Clamp(hero.life, 0, sourceMaxLife), (ushort)sourceMaxLife);
        }
        var map = hero._level.map;
        double hostX = (hero.cx + hero.xr) * 24;
        double centerY = (hero.cy + hero.yr) * 24 - 14.5;
        // Over a moving host elevator the original works in a window lifted to
        // its surface (ElevatorRide); centerY and the terrain tests below are in that frame.
        var riding = RideElevator(hero, hostX, centerY + 14.5);
        var ride = riding?.Ride ?? default;
        lift = ride.Lift;
        centerY -= ride.Raise + lift;
        if (ride.Raise != 0) Diagnostic?.Invoke("elevator-carry", new { ride.Raise, ride.Lift, action = nds!.Action });
        ushort action = nds!.Action;
        // F fires the stored limited-use ability through the original machine (#102).
        var (fMask, fBusy, fAttack) = KirbyFSlot.Step(session, nds, mask);
        mask = fMask;
        // At a ladder or chain the host climbs with a key bound to both Up and
        // jump; the original would jump on A first and grab the ladder after.
        // Keep only Up from that key while Kirby holds or overlaps a ladder,
        // and until it is released, so reaching the top does not jump either.
        if (!KirbyInput.JumpFromUpOnly) upJumpClimbs = false;
        else if (action == KirbyActions.Climb || AtLadder(map, hostX, centerY + 14.5)) upJumpClimbs = true;
        if (upJumpClimbs) mask &= unchecked((ushort)~1);
        mask = slideKey.Filter(mask, KirbyInput.SlideButton && !stomp.Active, action, previousInput);
        ushort raw = mask;
        int direction = (raw & 48) switch { 16 => 1, 32 => -1, _ => 0 };
        bool diveButton = KirbyInput.DiveButton;
        bool jumpPressed = (raw & 1) != 0 && (previousInput & 1) == 0;
        // Each copy attack press (DS B while copied) uses the copy item's affixes (#57).
        // A copied Kirby holds no mouthful (the original mouth-count bit stays stale after a swallow).
        if ((raw & 2) != 0 && (previousInput & 2) == 0 && !session.Rules.IsBaseForm)
            KirbyAbilityItems.UseAffixes(hero);
        if (!stomp.Active && KirbyActions.CanStomp(action) && DiveRequested(raw, diveButton))
            BeginStomp(hero, action, centerY + 14.5);
        previousInput = raw;
        previousDiveButton = diveButton;
        if (stomp.Active) centerY += stomp.Descent(FreeBelow(map, hostX, centerY + 14.5), action);
        // The stomp moves at the Beheaded's dive speed on its own.
        bool matched = config.MatchHostSpeed && !stomp.Active;
        // Down + jump on the ground starts the original slide during this
        // frame: open roll zones for it already, or the slide would hit them.
        bool sliding = action == KirbyActions.Slide ||
            (!stomp.Active && KirbyActions.CanSlide(action) && (raw & 128) != 0 && jumpPressed);
        bool rollThrough = rollZones.Update(sliding, TouchesRollZone(map, hostX, centerY + 14.5));
        int originX = (int)Math.Floor(hostX / 24) - 16;
        int originY = (int)Math.Floor(centerY / 24) - 4;
        ExportTerrain(hero, originX, originY, rollThrough);
        int? beforeY = riding is not null && Diagnostic is not null ? nds!.State.YRaw : null;
        PlaceSource(originX, originY, checked((int)Math.Round((hostX - originX * 24) * 65536)),
            checked((int)Math.Round((centerY - originY * 24) * 65536)), carried: ride.Carrying);
        if (riding is not null && Diagnostic is not null)
        {
            // Lab trace of each step on an elevator: placement and the terrain column under Kirby.
            int column = (int)Math.Floor(hostX / 24) - originX;
            Diagnostic("elevator-step", new { ride.Lift, ride.Raise, ride.Carrying, originY, centerY, beforeY, placedY = nds.State.YRaw,
                target = (int)Math.Round((centerY - originY * 24) * 65536), action = nds.Action,
                tiles = Enumerable.Range(0, 9).Select(row => cells[row * 100 + column]).ToArray(),
                elevator = new { riding.Value.Tracked.Elevator.cy, riding.Value.Tracked.Elevator.yr, riding.Value.Tracked.Sunk } });
        }
        if (stompShield > 0 && !stomp.Active) stompShield--;
        session.Rules.Critical.BaseChance = config.CopyCriticalChance;
        session.Rules.Critical.BaseDamageMultiplier = config.CopyCriticalDamage;
        session.Rules.Mutations.Tuning = config.CriticalMutations;
        enemies.FiringItem = KirbyFSlot.Sequence.Active ? KirbyFSlot.Item : null;
        enemies.Prepare(session, nds, originX * 24, originY * 24 + lift, SourceDamageScale,
            bodyContact: config.OriginalTouchDamage && !stomp.Active && stompShield == 0);
        int healthBefore = nds.Health;
        dc.Entity? damageSource = null;
        bool hurt = false;
        if (incoming.TryDequeue(out var request))
        {
            hurt = true;
            damageSource = request.Source;
            nds.Hurt((ushort)Math.Clamp(request.Damage, 1, 32767), request.Direction == 0 ? 1 : request.Direction);
            // Original collision resolves one hurt state per source tick; its
            // immunity handles simultaneous contacts, rather than host FPS.
            incoming.Clear();
        }
        NdsAudio.PrepareSource(nds);
        if (stomp.Active)
        {
            mask = stomp.Mask(mask, action);
            // Straight down at the original fall cap; the original clamps the
            // speed to its current state (falling, or exhaling while it lasts).
            Span<byte> velocity = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(velocity, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(velocity[2..], StompVelocity);
            uint player = nds.State.Player;
            nds.Write(player + 0x88, velocity);
            // The original moves on the copy of the horizontal velocity at 0x98.
            nds.Write(player + 0x98, velocity[..2]);
        }
        else
        {
            mask = dash.Filter(mask, config.AlwaysDash && !KirbyInput.AnalogWalk, lastVx, lastVy, action);
            // A recoil from a hit this frame keeps its original velocity.
            if (matched && !hurt) MatchVelocity(action, motionActions.Effective, direction);
        }
        PaceWalkCycle(action, matched);
        if (fAttack) mask |= 2;
        nds.Step(mask);
        if (riding is not null) Diagnostic?.Invoke("elevator-stepped", new { action = nds.Action, y = nds.State.YRaw, vy = nds.State.VyRaw });
        int healthAfter = nds.Health;
        if (healthBefore >= 0 && healthAfter >= 0 && healthAfter < healthBefore)
            KirbyDamageLog.SourceHit(hero, nds, enemies, healthBefore, healthAfter, damageSource);
        // The bridge normalizes the original signed lethal HP sentinel to zero.
        // Stop normal stepping here: only the original death animation runs on,
        // the host owns the run's death transition, and its last-hit protection
        // must not resurrect the ROM player.
        if (healthBefore > 0 && healthAfter == 0)
        {
            sourceDead = true;
            hero.kill();
            return;
        }
        if (healthBefore >= 0 && healthAfter >= 0 && healthAfter != healthBefore)
        {
            Diagnostic?.Invoke("source-health", new { healthBefore, healthAfter, queued = damageSource?.GetType().FullName, hostLife = hero.life });
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
                KirbyHostVisuals.SuppressHitBlink = true;
                try { hero.applyAttackResult(attack); }
                finally { applyingNativeDamage = false; KirbyHostVisuals.SuppressHitBlink = false; }
            }
        }
        if (enemies.Complete(session, nds, originX * 24, originY * 24 + lift, SourceDamageScale) is not null) pendingCopy = true;
        foreach (var swallowed in enemies.Swallowed) mouthfulItems.Add(swallowed, KirbyAbilityItems.AbilityOf(swallowed.Item)!);
        enemies.Swallowed.Clear();
        NdsState state = nds.State;
        lastVx = state.VxRaw; lastVy = state.VyRaw;
        ushort after = nds.Action;
        ushort motion = motionActions.Next(after, state.VxRaw);
        double x = state.X + originX * 24, feet = state.Y + originY * 24 + 14.5;
        if (matched)
        {
            terrainMap = map; terrainRollThrough = rollThrough;
            (x, feet) = SpeedMatch.Move(motion, motionActions.Direction(direction), state.VxRaw, hostX, centerY + 14.5, x, feet, terrain ??= MatchTerrain);
        }
        hero.setPosPixel(x, feet + lift);
        // The host represents a foot exactly on a floor as yr=1 in the air
        // cell above it. setPosPixel chooses yr=0 inside the solid cell; that
        // invokes its stuck-in-wall rescue and would fight the source controller.
        if (hero.yr == 0) { hero.cy--; hero.yr = 1; }
        // On an elevator, take its cell position exactly, as its own carry does.
        // A pixel conversion can land a rounding error below the ground height
        // the elevator sets for its cell, and the host then lands the hero again.
        if (riding?.Tracked is { Sunk: false, Elevator: var carrier } && Math.Abs(feet + lift - ElevatorRide.Surface(carrier.cy, carrier.yr)) < 1e-6)
        { hero.cy = carrier.cy; hero.yr = carrier.yr; }
        // Kirby standing on an elevator is drawn with its platform (AlignToElevator);
        // off it, that offset eases back over a few frames instead of jumping.
        standingOn = riding?.Tracked is { } on && Math.Abs(feet + lift - on.Surface) < 0.01 ? on.Elevator : null;
        if (standingOn is null && renderer is { DrawOffset: not 0 } drawn) drawn.SetDrawOffset(drawn.DrawOffset * 0.5);
        // Host shields block from the front and host weapons aim by this facing.
        hero.dir = state.FacesLeft ? -1 : 1;
        hero.bodyHei = 29.0 / 24; hero.hei = 29;
        if (stomp.Active) FinishStompFrame(hero, map, after, healthAfter < healthBefore);
        SlideStrike = HostStrikes.Slide(after, state.VxRaw);
        GrabStrike = HostStrikes.Grab(state.Ability, motion, state.VxRaw);
        // An F press switches the original copy and back; the rules keep the main copy.
        if (fBusy) previousAbility = state.Ability;
        else if (state.Ability != previousAbility)
        {
            if (state.Ability == 1 && !pendingCopy) session.DropAbility();
            previousAbility = state.Ability;
            if (state.Ability > 1)
            {
                session.Rules.SetSourceAbility(Abilities[state.Ability - 2]);
                pendingCopy = false;
            }
        }
        if (!fBusy && pendingCopy && state.Ability == 1 && (nds.ActionFlags & 2) == 0 && (mask & 2) != 0)
        { pendingCopy = false; session.DropAbility(); }
        mouthfulItems.Observe((nds.ActionFlags & 2) != 0, state.Ability == 1);
    }
    // Host collision bits (CollisionData.fromColType): 1|2 solid, 8 one-way,
    // 16 ladder, 512/1024 slopes, and 64 roll zone. A roll zone is solid for
    // the Beheaded (bit 1) except while he rolls.
    private const int RollZoneBit = 64;
    private const short StompVelocity = 0x700;
    // Kirby's original body: about 16 pixels wide and 22 tall above his feet.
    private const double BodyHalfWidth = 8, BodyHeight = 22;
    private void ExportTerrain(Hero hero, int originX, int originY, bool rollThrough)
    {
        var map = hero._level.map;
        int* grid = Grid(map);
        for (int y = 0; y < 9; y++)
        for (int x = 0; x < 100; x++)
            cells[y * 100 + x] = TileAt(map, grid, originX + x, originY + y, rollThrough);
    }
    private int* Grid(dc.level.LevelMap map)
    {
        if (terrainLevel != map.HashlinkPointer) { terrainLevel = map.HashlinkPointer; water.Clear(); }
        return (int*)map.collisions.bytes;
    }
    private ushort TileAt(dc.level.LevelMap map, int* grid, int cx, int cy, bool rollThrough)
    {
        int c = cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei ? 3 : grid[cy * map.wid + cx];
        // The host dive falls through the one-way platform or ladder top it starts above.
        bool passed = stomp.Active && cy <= stompIgnoreRow;
        ushort tile = (c & 512) != 0 ? ((c & 1024) != 0 ? (ushort)133 : (ushort)132)
            : (c & RollZoneBit) != 0 ? (rollThrough ? (ushort)0 : (ushort)3)
            : (c & 8) != 0 ? (passed ? (ushort)0 : (ushort)131)
            : (c & 3) != 0 ? (ushort)3
            : (c & 16) != 0 ? (passed ? (ushort)0 : (ushort)134) : (ushort)0;
        // A grown vine is a ladder, and the solid blocks it passes through or
        // ends under are ladder tops, so Kirby climbs onto them as the Beheaded does.
        if (tile is 0 or 3 && KirbyVines.At(map, cx, cy) is var vine and not KirbyVines.Cell.None)
        {
            ushort vineTile = tile == 3 ? (ushort)131 : vine == KirbyVines.Cell.Ladder ? (ushort)134 : (ushort)0;
            if (vineTile != 0) tile = passed ? (ushort)0 : vineTile;
        }
        if (tile == 0 && cx >= 0 && cy >= 0 && cx < map.wid && cy < map.hei)
        {
            if (!water.TryGetValue((cx, cy), out bool submerged))
            {
                submerged = map.getRoomAt(cx, cy) is not null && map.getWaterSurfaceY(cx, cy) > 0;
                water[(cx, cy)] = submerged;
            }
            if (submerged) tile = 130;
        }
        return tile;
    }
    // Window origin (cells) of the latest World call, while the source still
    // holds Kirby where that call and the frames after it put him.
    private (int X, int Y)? window;
    /// <summary>Window origin (cells) of the original's current coordinates (lab diagnostics).</summary>
    internal (int X, int Y)? SourceWindow => window;
    // Host pixels the current step's window is lifted by (ElevatorRide): host y = original-frame y + lift.
    private double lift;
    // The elevator Kirby stood on at the end of the latest original frame.
    private dc.en.inter.Elevator? standingOn;
    // Host pixels the elevator sprite may lag its physical surface: over a tick of the fastest elevators.
    private const double MaxElevatorDrawLag = 48;
    /// <summary>
    /// Host frame event, after an elevator placed its sprite: draw Kirby standing
    /// on it with the same lag behind its physical surface (see SetDrawOffset).
    /// </summary>
    internal void AlignToElevator(dc.en.inter.Elevator elevator)
    {
        if (renderer is null || standingOn?.HashlinkPointer != elevator.HashlinkPointer || elevator.spr is not { } sprite) return;
        double lag = sprite.y - ElevatorRide.Surface(elevator.cy, elevator.yr);
        renderer.SetDrawOffset(Math.Abs(lag) <= MaxElevatorDrawLag ? lag : 0);
    }
    // Host elevators on this level, with each one's surface at the previous original frame.
    private sealed class TrackedElevator(dc.en.inter.Elevator elevator)
    {
        public readonly dc.en.inter.Elevator Elevator = elevator;
        public double Surface = ElevatorRide.Surface(elevator.cy, elevator.yr);
        /// <summary>The cell the surface is in is solid level floor under Kirby: an overshoot into its stop.</summary>
        public bool Sunk;
    }
    private readonly List<TrackedElevator> elevators = [];
    private nint elevatorLevel;
    private int elevatorScan;
    /// <summary>
    /// How Kirby rides a host elevator (all elevator types share
    /// Elevator.fixedUpdate), or null. Elevators are listed again on a new level
    /// and once a second, rather than scanning every entity on every frame.
    /// </summary>
    private (ElevatorRide.Ride Ride, TrackedElevator Tracked)? RideElevator(Hero hero, double hostX, double feetY)
    {
        var level = hero._level;
        if (elevatorLevel != level.HashlinkPointer) { elevatorLevel = level.HashlinkPointer; elevators.Clear(); elevatorScan = 0; }
        if (--elevatorScan < 0)
        {
            elevatorScan = 60;
            var entities = level.entities;
            for (int i = 0; i < entities.length; i++)
                if (entities.getDyn(i) is dc.en.inter.Elevator elevator && !elevator.destroyed &&
                    !elevators.Exists(t => t.Elevator.HashlinkPointer == elevator.HashlinkPointer))
                    elevators.Add(new TrackedElevator(elevator));
        }
        elevators.RemoveAll(t => t.Elevator.destroyed);
        (ElevatorRide.Ride, TrackedElevator)? found = null;
        foreach (var tracked in elevators)
        {
            var elevator = tracked.Elevator;
            bool sunk = SolidUnder(hero._level.map, hostX, elevator.cy);
            double surface = ElevatorRide.Effective(elevator.cy, elevator.yr, sunk);
            if (found is null && ElevatorRide.Over(hero.cx, feetY, elevator.cx, elevator.cWid, elevator.cy, elevator.yr,
                    tracked.Surface - surface, rising: lastVy < 0, sunk) is { } ride)
            {
                found = (ride, tracked);
                tracked.Sunk = sunk;
            }
            else if (found is null && Diagnostic is not null && Math.Abs(hero.cx - elevator.cx) <= elevator.cWid / 2 &&
                     Math.Abs(feetY - surface) < 48 && tracked.Surface != surface)
                Diagnostic("elevator-miss", new { feetY, surface, previous = tracked.Surface, lastVy, elevator.cy, elevator.yr });
            tracked.Surface = surface;
        }
        return found;
    }
    /// <summary>A solid or one-way host cell in this row under Kirby's body.</summary>
    private static bool SolidUnder(dc.level.LevelMap map, double hostX, int cy)
    {
        if (cy < 0 || cy >= map.hei) return false;
        int* grid = (int*)map.collisions.bytes;
        for (int cx = (int)Math.Floor((hostX - BodyHalfWidth) / 24); cx <= (int)Math.Floor((hostX + BodyHalfWidth) / 24); cx++)
            if (cx >= 0 && cx < map.wid && (grid[cy * map.wid + cx] & (1 | 2 | 8)) != 0) return true;
        return false;
    }
    // Host movement the original did not make itself (speed match, stomp): at most
    // a few pixels a frame. Larger differences are host teleports.
    private const int MaxPlayerMove = 64 << 16;
    /// <summary>
    /// Give the original the terrain window and Kirby's host position. A window
    /// that moves by whole cells takes every original object along, as before;
    /// the remaining difference is host movement of Kirby alone, so bombs, other
    /// thrown objects, effects and the helper stay where the original put them.
    /// Kirby carried by an elevator is the exception: the window moves with
    /// him and takes every original object along. Moving the objects and then
    /// Kirby back by himself leaves the parts attached to him a step off, and
    /// the original drops a standing Kirby for a frame.
    /// </summary>
    private void PlaceSource(int originX, int originY, int targetX, int targetY, bool carried)
    {
        if (window is { } previous && NdsRuntime.CanMovePlayer && !carried)
        {
            NdsState current = nds!.State;
            long frameX = current.XRaw + (long)(previous.X - originX) * 24 * 65536;
            long frameY = current.YRaw + (long)(previous.Y - originY) * 24 * 65536;
            long dx = targetX - frameX, dy = targetY - frameY;
            if (Math.Abs(dx) <= MaxPlayerMove && Math.Abs(dy) <= MaxPlayerMove)
            {
                nds.World(cells, (int)frameX, (int)frameY);
                // One raw unit is host floating-point rounding, not movement.
                if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1) nds.MovePlayer((int)dx, (int)dy);
                window = (originX, originY);
                return;
            }
        }
        nds!.World(cells, targetX, targetY);
        window = (originX, originY);
    }
    // Terrain for SpeedMatch: the cells the original saw this frame.
    private SpeedMatch.Terrain? terrain;
    private dc.level.LevelMap? terrainMap;
    private bool terrainRollThrough;
    private ushort MatchTerrain(int cx, int cy) => TileAt(terrainMap!, Grid(terrainMap!), cx, cy, terrainRollThrough);
    // The player's controller block (player+0x40) counts the current pose's
    // remaining frames at +0x6C8.
    private const uint PoseCountOffset = 0x6C8;
    /// <summary>
    /// Play the original walk and dash cycles faster at the matched speed, so
    /// Kirby's feet slide less over the ground he covers (see AnimationPacer).
    /// </summary>
    private void PaceWalkCycle(ushort action, bool matched)
    {
        if (!matched || action is not (KirbyActions.Walk or KirbyActions.Dash)) { walkCycle.Reset(); return; }
        Span<byte> value = stackalloc byte[4];
        nds!.Read(nds.State.Player + 0x40, value);
        uint controller = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(value);
        if (controller is < 0x02000000 or >= 0x023FF000) return;
        nds.Read(controller + PoseCountOffset, value[..1]);
        byte next = walkCycle.Next(SpeedMatch.GroundScale, value[0]);
        if (next == value[0]) return;
        value[0] = next;
        nds.Write(controller + PoseCountOffset, value[..1]);
    }
    /// <summary>
    /// The Beheaded stops when his direction is released and turns at once,
    /// and his jump peaks sooner than the original's; set the original
    /// velocities for the next frame accordingly (see SpeedMatch).
    /// </summary>
    private void MatchVelocity(ushort action, ushort motion, int direction)
    {
        NdsState current = nds!.State;
        uint player = current.Player;
        Span<byte> value = stackalloc byte[2];
        if (SpeedMatch.Brakes(motion, direction, current.VxRaw))
        {
            // The original moves on the copy at 0x98; 0x88 is what it reports.
            value.Clear();
            nds.Write(player + 0x88, value);
            nds.Write(player + 0x98, value);
        }
        int rise = SpeedMatch.RiseVelocity(action, current.VyRaw);
        if (rise != current.VyRaw)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(value, (short)rise);
            nds.Write(player + 0x8A, value);
        }
    }
    /// <summary>
    /// Free pixels between Kirby's feet and the first floor under him, within
    /// four cells: what stops the original fall. Ladders and chains never do,
    /// and a one-way platform only from above. Water ends the added descent, so
    /// Kirby enters it at the original speed, but it is not a floor to land on.
    /// </summary>
    private double FreeBelow(dc.level.LevelMap map, double hostX, double feetY, bool waterStops = true)
    {
        int* grid = Grid(map);
        int left = (int)Math.Floor((hostX - BodyHalfWidth) / 24), right = (int)Math.Floor((hostX + BodyHalfWidth) / 24);
        int top = (int)Math.Floor(feetY / 24);
        for (int cy = top; cy < top + 4; cy++)
            for (int cx = left; cx <= right; cx++)
            {
                ushort tile = TileAt(map, grid, cx, cy, rollZones.Open);
                if (tile is 0 or 134 || (tile == 130 && !waterStops) || (tile == 131 && cy * 24 < feetY - 0.5)) continue;
                return Math.Max(0, cy * 24 - feetY);
            }
        return double.PositiveInfinity;
    }
    /// <summary>Kirby's body overlaps a host ladder, chain or vine cell, where the original Up grabs it.</summary>
    private static bool AtLadder(dc.level.LevelMap map, double hostX, double feetY)
    {
        int cx = (int)Math.Floor(hostX / 24), cy = (int)Math.Floor((feetY - BodyHeight / 2) / 24);
        if (cx < 0 || cy < 0 || cx >= map.wid || cy >= map.hei) return false;
        int c = ((int*)map.collisions.bytes)[cy * map.wid + cx];
        return (c & 3) == 0 && ((c & 16) != 0 || KirbyVines.At(map, cx, cy) == KirbyVines.Cell.Ladder);
    }
    private static bool TouchesRollZone(dc.level.LevelMap map, double hostX, double feetY)
    {
        int* grid = (int*)map.collisions.bytes;
        int left = (int)Math.Floor((hostX - BodyHalfWidth) / 24), right = (int)Math.Floor((hostX + BodyHalfWidth) / 24);
        int top = (int)Math.Floor((feetY - BodyHeight) / 24), bottom = (int)Math.Floor((feetY - 0.5) / 24);
        for (int cy = top; cy <= bottom; cy++)
            for (int cx = left; cx <= right; cx++)
                if (cx >= 0 && cy >= 0 && cx < map.wid && cy < map.hei && (grid[cy * map.wid + cx] & RollZoneBit) != 0)
                    return true;
        return false;
    }
    /// <summary>The player's own dive binding (Options, "Dive attack"), read like the host's DiveAttack.isUsingInputs.</summary>
    private bool DiveRequested(ushort raw, bool diveButton)
    {
        bool down = (raw & 128) != 0 && (raw & 64) == 0;
        return dc.Main.Class.ME.options.diveAttackBind switch
        {
            1 => down,
            2 => down && (previousInput & 128) == 0,
            3 => diveButton && !previousDiveButton,
            _ => down && (raw & 1) != 0 && (previousInput & 1) == 0,
        };
    }
    private static dc.tool.mainSkills.DiveAttack? FindDive(Hero hero)
    {
        var skills = hero.mainSkillsManager?.skills;
        if (skills is null) return null;
        for (int i = 0; i < skills.length; i++)
            if (skills.getDyn(i) is dc.tool.mainSkills.DiveAttack dive && dive is not dc.tool.mainSkills.ForcedDiveAttack && dive.isEnabled())
                return dive;
        return null;
    }
    /// <summary>
    /// Start the host's own dive attack: its sounds, fast-fall effects, ladder
    /// and platform handling and, on landing, its shockwave, stun, damage and
    /// breakable floors. Its usual rules apply, including its minimum height.
    /// </summary>
    private void BeginStomp(Hero hero, ushort action, double feetY)
    {
        var dive = FindDive(hero);
        if (dive is null || !dive.canUse() || !stomp.TryStart(action, feetY)) return;
        activeDive = dive;
        dive.start();
        stompIgnoreRow = hero.ignoreGround is { } ignore ? ignore.untilY : int.MinValue;
        // From a ladder, chain or vine the dive also passes the row Kirby's feet
        // climb in: climbing down from the top, that is the ladder top itself,
        // which would hold him when the original lets go of the ladder.
        if (action == KirbyActions.Climb) stompIgnoreRow = Math.Max(stompIgnoreRow, (int)Math.Floor(feetY / 24));
        Diagnostic?.Invoke("stomp-start", new { action, feetY, dive.duration, dive.cooldown, dive.hold,
            ignoreRow = stompIgnoreRow, ground = hero.distToGroundCase(), active = dive.isActive() });
    }
    private void FinishStompFrame(Hero hero, dc.level.LevelMap map, ushort action, bool hurt)
    {
        double hostX = (hero.cx + hero.xr) * 24, feetY = (hero.cy + hero.yr) * 24 - lift;
        bool landed = KirbyActions.OnGround(action) || FreeBelow(map, hostX, feetY, waterStops: false) <= 0.5;
        bool submerged = TileAt(map, Grid(map), (int)Math.Floor(hostX / 24), (int)Math.Floor((feetY - BodyHeight / 2) / 24), rollZones.Open) == 130;
        bool lost = activeDive is not { } dive || dive.hero is not { destroyed: false } || !dive.isActive();
        double height = stomp.Height(feetY);
        // Deep water ends the dive; a floor under shallow water still takes the landing.
        switch (stomp.Advance(landed, hurt || lost || (submerged && !landed)))
        {
            case StompController.Outcome.Landed:
                var landing = activeDive!;
                activeDive = null; stompIgnoreRow = int.MinValue; dash.Reset();
                stompShield = StompLandingGrace;
                Diagnostic?.Invoke("stomp-land", new { height, feetY, action });
                landing.onOwnerLand(height);
                break;
            case StompController.Outcome.Cancelled:
                Diagnostic?.Invoke("stomp-cancel", new { hurt, submerged, lost, action, feetY });
                CancelStomp();
                break;
        }
    }
    private void CancelStomp()
    {
        var dive = activeDive;
        if (stomp.Active || dive is not null) dash.Reset();
        stomp.Reset();
        activeDive = null;
        stompIgnoreRow = int.MinValue;
        stompShield = 0;
        if (dive?.hero is { destroyed: false } && dive.isActive()) dive.cancel();
    }
    public bool TryAbilityAttack(KirbySession session, string abilityId, Weapon inputWeapon) => false;
    public void OnAbilityChanged(KirbySession session, string? abilityId) { }
}
