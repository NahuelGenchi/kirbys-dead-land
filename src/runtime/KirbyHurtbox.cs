using dc.en;

namespace Kirby.Runtime;

/// <summary>Projects original collision bounds only while the host queries combat geometry.</summary>
internal static unsafe class KirbyHurtbox
{
    private static Projection? active;
    private static NdsRuntime? cachedMachine;
    private static uint cachedFrame;
    private static NdsState state;
    private static (int Left, int Top, int Right, int Bottom)? bounds;
    private static readonly NdsHit[] hits = new NdsHit[512];
    private static long projections, restorations;
    internal static object Diagnostics => new
    {
        projections, restorations, active = active?.IsProjected ?? false,
        sourceFrame = cachedFrame,
        sourceBounds = bounds is { } box ? new[] { box.Left, box.Top, box.Right, box.Bottom } : null,
    };

    internal static void Install()
    {
        dc.pr.Hook_Level.update += (orig, level) =>
        {
            using var scope = Project(level);
            orig(level);
        };
        // Skills can update as separate Process children outside Level.update.
        dc.tool.skill.Hook_SkillArea.touches += (orig, area, entity) =>
        { using var scope = Project(); return orig(area, entity); };
        dc.tool.skill.Hook_SkillArea.getTouchingOpponents += (orig, area) =>
        { using var scope = Project(); return orig(area); };
        dc.tool.skill.Hook_SkillArea.getTouchingAllies += (orig, area) =>
        { using var scope = Project(); return orig(area); };
        dc.tool.skill.Hook_SkillArea.getTouchingQuadElements += (orig, area) =>
        { using var scope = Project(); return orig(area); };
        dc.tool.skill.Hook_OldMobSkill.getTouchingAreaFor += (orig, skill, entity) =>
        { using var scope = Project(); return orig(skill, entity); };
        dc.tool.skill.Hook_OldMobSkill.executeOnArea += (orig, skill, area) =>
        { using var scope = Project(); orig(skill, area); };
        dc.tool.Hook_TargetHelper.filterByArea += (orig, helper, area) =>
        { using var scope = Project(); orig(helper, area); };
        // Own movement, equipment placement and save data must continue to use
        // the physical foot, even inside Level.update. The hero's preUpdate and
        // fixedUpdate run there; KirbyMod's one handler for each wraps its work
        // in Physical, since a second handler costs every call again (#177).
        // postUpdate and spriteUpdate run in Level.postUpdate, after the
        // projection is restored, so they need no hook.
        dc.Hook_Entity.serialize += (orig, entity, serializer) =>
            Physical(entity, () => orig(entity, serializer));
        // Pressure plates (including trap plates and pulley points) and hidden
        // triggers test whether the hero stands on the ground at their tile.
        // The projected hurtbox sits about 9 pixels above Kirby's foot, so they
        // must see the physical body, as they see the Beheaded's.
        dc.en.inter.Hook_PressurePlate.fixedUpdate += (orig, plate) => PhysicalHero(() => orig(plate));
        dc.en.inter.Hook_HiddenTrigger.fixedUpdate += (orig, trigger) => PhysicalHero(() => orig(trigger));
        // A carnivorous plant is a platform built around the Beheaded: it closes
        // on a hero standing on it, bites what is inside and throws what touches
        // it while closed. Kirby's hurtbox is far smaller and sits above his foot,
        // so a plant would ignore him over half its width and lose him after one
        // bite (#113, #138). Its own update and bite see the Beheaded's body at
        // Kirby's foot; onTouch tests the foot on the ground.
        dc.en.ltrap.Hook_CarnivorousPlant.fixedUpdate += (orig, plant) => AsBeheaded(() => { orig(plant); KirbyBumps.TouchClosed(plant); });
        dc.en.ltrap.Hook_CarnivorousPlant.close += (orig, plant) => AsBeheaded(() => KirbyBumps.Close(plant, () => orig(plant)));
        dc.en.ltrap.Hook_CarnivorousPlant.onTouch += (orig, plant, entity) => PhysicalHero(() => orig(plant, entity));
    }

    private static bool IsActive(Hero hero) => !hero.destroyed && hero.life > 0 &&
        KirbyMod.Instance?.ActiveSession?.Hero.HashlinkPointer == hero.HashlinkPointer &&
        ModCore.Modules.Game.Instance.HeroInstance?.HashlinkPointer == hero.HashlinkPointer;

    private static Projection? Project(dc.pr.Level? level = null)
    {
        if (active is { IsProjected: true }) return null;
        var mod = KirbyMod.Instance;
        if (mod?.ActiveSession?.Hero is not { } hero || !IsActive(hero) ||
            !hero.awake || hero._level.game.hasCinematic() ||
            (level is not null && level.HashlinkPointer != hero._level.HashlinkPointer) ||
            mod.NativeProvider?.Machine is not { } machine) return null;
        var source = machine.State;
        if (cachedMachine != machine || cachedFrame != source.Frame)
        {
            cachedMachine = machine; cachedFrame = source.Frame; state = source; bounds = null;
            int count = machine.ReadHits(hits);
            for (int i = 0; i < count; i++)
            {
                ref var hit = ref hits[i];
                if (hit.Slot != 14 || hit.Owner != source.Player) continue;
                int left = hit.Bounds[0], top = hit.Bounds[1], right = hit.Bounds[2], bottom = hit.Bounds[3];
                if (right <= left || bottom <= top) continue;
                bounds = bounds is { } previous
                    ? (Math.Min(previous.Left, left), Math.Min(previous.Top, top),
                        Math.Max(previous.Right, right), Math.Max(previous.Bottom, bottom))
                    : (left, top, right, bottom);
            }
        }
        if (bounds is not { } box) return null;
        var value = new Projection(hero, box, state, active);
        active = value;
        value.Apply();
        return value;
    }

    internal static void Physical(dc.Entity entity, Action original)
    {
        if (active?.Hero.HashlinkPointer != entity.HashlinkPointer) { original(); return; }
        PhysicalHero(original);
    }

    /// <summary>Run host code with Kirby's physical foot and body instead of the projected hurtbox.</summary>
    private static void PhysicalHero(Action original)
    {
        var value = active;
        if (value is not { IsProjected: true }) { original(); return; }
        value.Restore();
        try { original(); }
        finally { if (IsActive(value.Hero)) value.Apply(); }
    }

    /// <summary>
    /// Run host code with the Beheaded's body where Kirby's foot is; on the floor,
    /// while Kirby is in the original's landing hop (the Beheaded has none).
    /// </summary>
    private static void AsBeheaded(Action original)
    {
        if (KirbyMod.Instance is not { ActiveSession.Hero: { } hero, NativeProvider: { } provider } || !IsActive(hero))
        { original(); return; }
        PhysicalHero(() =>
        {
            double radius = hero.radius, height = hero.hei, yr = hero.yr;
            int cy = hero.cy;
            hero.radius = Kirby.Core.BumpLaunch.BeheadedRadius; hero.hei = Kirby.Core.BumpLaunch.BeheadedHeight;
            if (provider.BounceFloor is double floor)
            {
                // A foot exactly on a floor is the bottom of the cell above it (yr 1).
                hero.cy = (int)Math.Ceiling(floor / 24) - 1; hero.yr = floor / 24 - hero.cy;
            }
            try { original(); }
            finally { hero.radius = radius; hero.hei = height; hero.cy = cy; hero.yr = yr; }
        });
    }

    private sealed class Projection(Hero hero, (int Left, int Top, int Right, int Bottom) box,
        NdsState source, Projection? parent) : IDisposable
    {
        internal Hero Hero => hero;
        internal bool IsProjected { get; private set; }
        private int cx, cy;
        private double xr, yr, height, radius;
        private bool rectangle;
        internal void Apply()
        {
            if (IsProjected) return;
            cx = hero.cx; cy = hero.cy; xr = hero.xr; yr = hero.yr;
            height = hero.hei; radius = hero.radius; rectangle = hero.useRectHitbox;
            double originX = (cx + xr) * 24 - source.X;
            double originY = (cy + yr) * 24 - 14.5 - source.Y;
            double x = originX + (box.Left + box.Right) * 0.5;
            double y = originY + box.Bottom;
            hero.cx = (int)Math.Floor(x / 24); hero.xr = x / 24 - hero.cx;
            hero.cy = (int)Math.Floor(y / 24); hero.yr = y / 24 - hero.cy;
            hero.radius = (box.Right - box.Left) * 0.5;
            hero.hei = box.Bottom - box.Top;
            hero.useRectHitbox = true;
            IsProjected = true;
            projections++;
        }
        internal void Restore()
        {
            if (!IsProjected) return;
            hero.cx = cx; hero.cy = cy; hero.xr = xr; hero.yr = yr;
            hero.hei = height; hero.radius = radius; hero.useRectHitbox = rectangle;
            IsProjected = false;
            restorations++;
        }
        public void Dispose()
        {
            try { Restore(); }
            finally { active = parent; }
        }
    }
}
