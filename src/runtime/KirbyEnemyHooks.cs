using dc;
using dc.en;

namespace Kirby.Runtime;

/// <summary>Keeps host AI and physics from moving an enemy held by the original engine.</summary>
internal static class KirbyEnemyHooks
{
    private static bool Controls(Entity entity) => entity is Mob mob &&
        !mob.destroyed && mob.life > 0 && KirbyMod.Instance?.NativeProvider?.ControlsEnemy(mob) == true;

    public static void Install()
    {
        // The level checks canUpdate before invoking subclass updates. Mob's normal
        // implementation ignores isFreeze whenever it has an attack target.
        Hook_Mob.canUpdate += (orig, mob) => !Controls(mob) && orig(mob);
        Hook_Mob.fixedUpdate += (orig, mob) =>
        {
            if (!Controls(mob)) orig(mob);
        };
        Hook_Entity.updatePositionXY += (orig, entity) =>
        {
            if (!Controls(entity)) orig(entity);
        };
    }
}
