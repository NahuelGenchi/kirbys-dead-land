using dc.en;
using Kirby.Core;
using Entity = dc.Entity;

namespace Kirby.Runtime;

/// <summary>
/// Paint, Mike and Crash reach the whole Dead Cells screen. After each source
/// frame, a damaging record of Kirby's that covers the original's activity
/// window strikes every host enemy in the host viewport that the original
/// cannot: enemies without a source object, and paired ones whose source object
/// lies outside that window. Damage is the record's own original word, scaled
/// like any other copy hit on that enemy.
/// </summary>
internal sealed unsafe class KirbyScreenAttacks
{
    private readonly StarStrikes strikes = new();
    private readonly List<uint> present = [];
    /// <summary>Lab-only observer of (target, source damage, host damage) for each host strike.</summary>
    internal static Action<Entity, int, int>? TestObserver;

    public void Clear() => strikes.Clear();

    /// <param name="records">The original collision records of the source frame just stepped.</param>
    /// <param name="sourceHit">Host enemies that lost source HP in that frame.</param>
    public void Strike(Hero hero, NdsRuntime nds, KirbyEnemies enemies, ReadOnlySpan<NdsHit> records,
        IReadOnlySet<nint> sourceHit, Func<Entity, double> scale)
    {
        present.Clear();
        var state = nds.State;
        for (int i = 0; i < records.Length; i++)
        {
            NdsHit hit = records[i];
            var box = new StarBox(hit.Bounds[0], hit.Bounds[1], hit.Bounds[2], hit.Bounds[3]);
            int damage = -(short)hit.Words[2];
            if (!ScreenAttacks.IsScreenWide(hit.Slot, damage, box, state.CameraX, state.CameraY) || !nds.Owns(hit.Owner)) continue;
            present.Add(hit.Address);
            StrikeView(hero, nds, enemies, hit.Address, damage, state, sourceHit, scale);
        }
        // A wave's record lasts one frame; a reused address is a new wave.
        strikes.Retain(present);
    }

    private void StrikeView(Hero hero, NdsRuntime nds, KirbyEnemies enemies, uint record, int sourceDamage, NdsState state,
        IReadOnlySet<nint> sourceHit, Func<Entity, double> scale)
    {
        // Viewport.getNormalizeScreenPos: the view is wid × hei level pixels centred on realX, realY.
        if (hero._level.viewport is not { destroyed: false } viewport) return;
        double halfWidth = viewport.wid / 2.0, halfHeight = viewport.hei / 2.0;
        var view = new StarBox((int)Math.Floor(viewport.realX - halfWidth), (int)Math.Floor(viewport.realY - halfHeight),
            (int)Math.Ceiling(viewport.realX + halfWidth), (int)Math.Ceiling(viewport.realY + halfHeight));
        var entities = hero._level.entities;
        for (int j = 0; j < entities.length; j++)
        {
            if (entities.getDyn(j) is not Mob mob || mob.destroyed || mob.life <= 0 || !mob.canBeHitBy(hero)) continue;
            if (!view.Overlaps(KirbyStars.HostBody(mob))) continue;
            var source = enemies.SourcePosition(nds, mob);
            bool reaches = source is { } at && ScreenAttacks.SourceReaches(at.X, at.Y, state.CameraX, state.CameraY);
            if (!ScreenAttacks.HostStrikes(source is not null, reaches, sourceHit.Contains(mob.HashlinkPointer)) ||
                !strikes.TryStrike(record, mob.HashlinkPointer)) continue;
            // The same conversion a paired enemy's HP drop would get.
            double multiplier = enemies.HostMultiplier(scale, mob);
            int life = mob.life;
            int damage = Math.Max(1, ColorlessDamage.HostDamage(sourceDamage,
                ColorlessDamage.SourceHp(life, multiplier, KirbyEnemies.MaxSourceHp), life, multiplier));
            TestObserver?.Invoke(mob, sourceDamage, damage);
            KirbyDamage.Hit(hero, mob, damage);
        }
    }
}
