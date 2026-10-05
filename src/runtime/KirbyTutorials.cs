using dc.en;
using dc.tool;
using ModCore.Modules;

namespace Kirby.Runtime;

/// <summary>Keep host prompts aligned with actions replaced by the original Kirby controller.</summary>
internal static class KirbyTutorials
{
    private static bool Replaced(string? id) => id is
        "jump" or "doubleJump" or "jumpDown" or "dodge1" or "dodge2" or "homunculus";

    public static void Install(string outfitId)
    {
        Hook_Tutorial.tryToStart += (orig, tutorial, id) =>
        {
            var hero = Game.Instance.HeroInstance;
            var user = hero?._level?.game?.user;
            if (Replaced(id?.ToString()) && user?.heroSkin?.ToString() == outfitId &&
                user.tutorial.HashlinkPointer == tutorial.HashlinkPointer)
                return false;
            return orig(tutorial, id);
        };
    }

    public static void OnActivated(Hero hero)
    {
        if (Replaced(hero._level.game.user.tutorial.current?.ToString()))
            hero.cancelTuto();
    }
}
