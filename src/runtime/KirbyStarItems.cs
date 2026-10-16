using System.Buffers.Binary;
using dc.en;
using dc.tool;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// A lost copy's ability item rides the original ability star dropped with it
/// (#68). Every ability star lasts 7 seconds instead of the original 8. The
/// original inhale catching the star puts its item in Kirby's mouth beside the
/// star's copy, so the original swallow gives that same item back and a spit
/// loses it with the copy; a star that runs out takes its item with it.
/// </summary>
internal sealed class KirbyStarItems
{
    /// <summary>Source frames a caught star's item waits for the original mouth to fill; it took 11.</summary>
    private const int MouthFrames = 60;
    private readonly StarItems<InventItem> stars = new();
    private readonly List<(InventItem Item, int Frames)> caught = [];
    /// <summary>Lab-only observer of each step of a star's item: (event, its item, details).</summary>
    internal static Action<string, InventItem?, object?>? TestObserver;

    /// <summary>Kirby's copy left the weapon slots with this item: it joins the star dropped with that copy.</summary>
    public void Lose(InventItem item, string ability)
    {
        int index = Array.IndexOf(KirbyNdsProvider.Abilities, ability);
        if (index < 0) return;
        uint? star = stars.Lose(item, checked((byte)(index + 2)));
        TestObserver?.Invoke(star is null ? "lost" : "carried", item, star);
    }

    /// <summary>
    /// Follow the ability stars after a source step: shorten each new one, and
    /// once the original mouth has filled, put a caught star's item in the
    /// mouthful. True when an item joined it: the copy is then pending.
    /// </summary>
    public bool Follow(NdsRuntime nds, IReadOnlyList<StarSighting> sightings, Hero hero, MouthfulItems<KirbyAbilityItems.Held> mouthful)
    {
        stars.Observe(sightings);
        Span<byte> countdown = stackalloc byte[2];
        foreach (var star in stars.NewStars)
        {
            if (AbilityStar.Shortened(star.Countdown) is not { } shortened) continue;
            BinaryPrimitives.WriteInt16LittleEndian(countdown, (short)shortened);
            nds.Write(star.Address + AbilityStar.CountdownOffset, countdown);
            TestObserver?.Invoke("shortened", null, new { star = star.Address, star.Ability, from = star.Countdown, to = shortened });
        }
        foreach (var (star, item) in stars.Paired) TestObserver?.Invoke("carried", item, star);
        foreach (var item in stars.Gone) TestObserver?.Invoke("gone", item, null);
        foreach (var item in stars.Caught)
        {
            caught.Add((item, 0));
            TestObserver?.Invoke("caught", item, null);
        }
        if (caught.Count == 0) return false;
        // The caught star flies into Kirby's mouth before the original fills it.
        if ((nds.ActionFlags & 2) != 0)
        {
            foreach (var (item, _) in caught)
            {
                mouthful.Add(new KirbyAbilityItems.Held(item, hero.cx, hero.cy, hero._level.HashlinkPointer, Star: true),
                    KirbyAbilityItems.AbilityOf(item)!);
                TestObserver?.Invoke("in-mouth", item, null);
            }
            caught.Clear();
            return true;
        }
        for (int i = caught.Count - 1; i >= 0; i--)
        {
            var (item, frames) = caught[i];
            if (frames < MouthFrames) { caught[i] = (item, frames + 1); continue; }
            caught.RemoveAt(i);
            TestObserver?.Invoke("gone", item, "never reached the mouth");
        }
        return false;
    }

    /// <summary>A star's item spat out of the mouth: it goes with the copy the spit loses.</summary>
    public static void Spat(InventItem item) => TestObserver?.Invoke("gone", item, "spat");

    /// <summary>The original machine was replaced (a new run, a restored save, a revive): its stars are gone.</summary>
    public void Clear()
    {
        stars.Clear();
        caught.Clear();
    }
}
