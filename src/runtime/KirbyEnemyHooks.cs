using dc;
using dc.en;

namespace Kirby.Runtime;

/// <summary>Keeps host AI and physics from moving an enemy or projectile held by the original engine.</summary>
internal static class KirbyEnemyHooks
{
    private static bool Controls(Entity entity) => entity is Mob or Bullet or Grenade &&
        !entity.destroyed && (entity.life > 0 || entity is not Mob) &&
        KirbyMod.Instance?.NativeProvider?.ControlsEnemy(entity) == true;

    public static void Install()
    {
        // The level checks canUpdate before invoking subclass updates. Mob's normal
        // implementation ignores isFreeze whenever it has an attack target.
        Hook_Mob.canUpdate += (orig, mob) => !Controls(mob) && orig(mob);
        Hook_Mob.fixedUpdate += (orig, mob) =>
        {
            if (!Controls(mob)) orig(mob);
        };
        // Bullets and grenades inherit Entity.canUpdate. Their own fixedUpdate
        // moves, collides and detonates them, so it is skipped while inhaled.
        Hook_Entity.canUpdate += (orig, entity) => !Controls(entity) && orig(entity);
        Hook_Bullet.fixedUpdate += (orig, bullet) =>
        {
            if (!Controls(bullet)) orig(bullet);
        };
        Hook_Grenade.fixedUpdate += (orig, grenade) =>
        {
            if (!Controls(grenade)) orig(grenade);
        };
        Hook_Entity.updatePositionXY += (orig, entity) =>
        {
            if (!Controls(entity)) orig(entity);
        };
    }
}
