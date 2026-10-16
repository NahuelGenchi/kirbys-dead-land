using dc;
using dc.en;

namespace Kirby.Runtime;

/// <summary>
/// Keeps host AI and physics from moving an enemy, projectile or ability item held by the original engine.
/// <para>
/// A hooked host function pays a managed call and its garbage on every call,
/// whatever the handler decides, and these functions run for every entity
/// several times a frame. The handlers only change anything while something
/// is held, so they are installed for that time alone: <see cref="Hold"/> the
/// moment an entity is held, <see cref="Relax"/> between host frames once
/// nothing of its kind has been for a while.
/// </para>
/// </summary>
internal static class KirbyEnemyHooks
{
    // Which groups of hooks are installed now.
    private static bool mobs, others, positions;
    // Host frames a group stays installed after it was last needed, so a fight
    // does not add and remove hooks around every hit.
    private const int Linger = 30;
    private static int mobIdle, otherIdle;

    private static bool Controls(Entity entity) => entity is Mob or Bullet or Grenade or dc.en.inter.ItemDrop &&
        !entity.destroyed && (entity.life > 0 || entity is not Mob) &&
        KirbyMod.Instance?.NativeProvider?.ControlsEnemy(entity) == true;

    /// <summary>The original engine now holds an enemy (<paramref name="mob"/>) or a projectile or item.</summary>
    internal static void Hold(bool mob)
    {
        if (mob) mobIdle = 0; else otherIdle = 0;
        if (mob && !mobs)
        {
            DccmDetours.Attach("en.Mob.canUpdate", "en.Mob.fixedUpdate");
            // The level checks canUpdate before invoking subclass updates. Mob's normal
            // implementation ignores isFreeze whenever it has an attack target. A held
            // mob stops here, so Entity.canUpdate is never asked about one.
            Hook_Mob.canUpdate += MobCanUpdate;
            Hook_Mob.fixedUpdate += MobFixedUpdate;
            mobs = true;
        }
        if (!mob && !others)
        {
            DccmDetours.Attach("Entity.canUpdate", "en.Bullet.fixedUpdate", "en.Grenade.fixedUpdate");
            // Bullets, grenades and items inherit Entity.canUpdate. A projectile's own
            // fixedUpdate moves, collides and detonates it, so it is skipped while inhaled.
            Hook_Entity.canUpdate += EntityCanUpdate;
            Hook_Bullet.fixedUpdate += BulletFixedUpdate;
            Hook_Grenade.fixedUpdate += GrenadeFixedUpdate;
            others = true;
        }
        if (!positions)
        {
            DccmDetours.Attach("Entity.updatePositionXY");
            Hook_Entity.updatePositionXY += UpdatePositionXY;
            positions = true;
        }
    }

    /// <summary>
    /// Remove the hooks nothing held needs any more. Called once between host
    /// frames, never from inside a hooked function: staying installed longer only
    /// costs time, as a handler with nothing held calls straight through. DCCM
    /// keeps an unhooked function detoured into .NET, so the detour of each one
    /// removed is taken off, and written again before it is next added (#178).
    /// </summary>
    internal static void Relax(bool mobHeld, bool otherHeld)
    {
        mobIdle = mobHeld ? 0 : System.Math.Min(mobIdle + 1, Linger + 1);
        otherIdle = otherHeld ? 0 : System.Math.Min(otherIdle + 1, Linger + 1);
        if (mobs && mobIdle > Linger)
        {
            Hook_Mob.canUpdate -= MobCanUpdate;
            Hook_Mob.fixedUpdate -= MobFixedUpdate;
            DccmDetours.Detach("en.Mob.canUpdate", "en.Mob.fixedUpdate");
            mobs = false;
        }
        if (others && otherIdle > Linger)
        {
            Hook_Entity.canUpdate -= EntityCanUpdate;
            Hook_Bullet.fixedUpdate -= BulletFixedUpdate;
            Hook_Grenade.fixedUpdate -= GrenadeFixedUpdate;
            DccmDetours.Detach("Entity.canUpdate", "en.Bullet.fixedUpdate", "en.Grenade.fixedUpdate");
            others = false;
        }
        if (positions && !mobs && !others)
        {
            Hook_Entity.updatePositionXY -= UpdatePositionXY;
            DccmDetours.Detach("Entity.updatePositionXY");
            positions = false;
        }
    }

    private static bool MobCanUpdate(Hook_Mob.orig_canUpdate orig, Mob mob)
    {
        LabPerfProbe.EnemyHook();
        return !Controls(mob) && orig(mob);
    }

    private static void MobFixedUpdate(Hook_Mob.orig_fixedUpdate orig, Mob mob)
    {
        LabPerfProbe.EnemyHook();
        if (!Controls(mob)) orig(mob);
    }

    private static bool EntityCanUpdate(Hook_Entity.orig_canUpdate orig, Entity entity)
    {
        LabPerfProbe.EnemyHook();
        return !Controls(entity) && orig(entity);
    }

    private static void BulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, Bullet bullet)
    {
        LabPerfProbe.EnemyHook();
        if (!Controls(bullet)) orig(bullet);
    }

    private static void GrenadeFixedUpdate(Hook_Grenade.orig_fixedUpdate orig, Grenade grenade)
    {
        LabPerfProbe.EnemyHook();
        if (!Controls(grenade)) orig(grenade);
    }

    private static void UpdatePositionXY(Hook_Entity.orig_updatePositionXY orig, Entity entity)
    {
        LabPerfProbe.EnemyHook();
        if (!Controls(entity)) orig(entity);
    }
}
