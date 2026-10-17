namespace Kirby.Core;

/// <summary>
/// The original discarded ability star (object kind 64, update routine
/// 0x02166A54). Its constructor (0x02166D80) stores the copy byte at +0x49 and
/// a countdown of 480 frames (0x1E0, 8 s) at +0x4C, a halfword the update
/// counts down; the star ends when it reaches zero. +0x60 reads 1 while the
/// star bounces. The original inhale sets it to 0 in the frame it catches the
/// star, whose collision record is gone the next frame; Kirby's mouth fills
/// with the star's copy about 11 frames later.
/// </summary>
public static class AbilityStar
{
    public const int AbilityOffset = 0x49;
    public const int CountdownOffset = 0x4C;
    public const int StateOffset = 0x60;
    /// <summary>An original object's 16.16 position (x, y), and the copy it is drawn from, which the bridge's World moves with it.</summary>
    public const int PositionOffset = 0x80;
    public const int DrawnOffset = 0x78;
    /// <summary>The value at <see cref="StateOffset"/> once the original inhale has caught the star.</summary>
    public const byte Caught = 0;
    /// <summary>The original countdown at spawn.</summary>
    public const int OriginalLifetime = 480;
    /// <summary>Owner decision (#54, #68): a dropped star and its item last 7 seconds, 420 original frames.</summary>
    public const int Lifetime = 420;

    /// <summary>
    /// The countdown to write over a star first seen with this one, or null to
    /// leave it. Only a countdown above <see cref="Lifetime"/> is still the
    /// original's, so every star loses the same 60 frames once, even when it is
    /// first seen late or comes back with a restored save.
    /// </summary>
    public static int? Shortened(int countdown) =>
        countdown > Lifetime ? countdown - (OriginalLifetime - Lifetime) : null;
}

/// <summary>One original ability star in a source frame: its address, copy byte, countdown and state.</summary>
public readonly record struct StarSighting(uint Address, byte Ability, int Countdown, byte State);

/// <summary>
/// Ability items carried by the stars Kirby drops (#68). When Kirby loses his
/// copy, its item joins the star the original drops with that copy byte
/// around the same frame. A star the original inhale catches hands its item
/// to Kirby's mouth; a star that runs out, or leaves the original any other
/// way, takes its item with it, and so does a lost copy no star comes for.
/// </summary>
public sealed class StarItems<T> where T : class
{
    /// <summary>Source frames a lost item waits for its star, and how far apart the loss and the star's first frame may be.</summary>
    public const int PairFrames = 10;
    /// <summary>Source frames a star may be missing from the original's collision records before it counts as gone.</summary>
    public const int MissingFrames = 8;

    private sealed class Star(byte ability, int seen)
    {
        public readonly byte Ability = ability;
        public readonly int Seen = seen;
        public int Countdown;
        public byte State;
        public int Last;
        public T? Item;
    }

    private readonly Dictionary<uint, Star> stars = [];
    private readonly List<(T Item, byte Ability, int Frame)> waiting = [];
    private readonly List<uint> ended = [];
    private readonly List<StarSighting> newStars = [];
    private readonly List<(uint Star, T Item)> paired = [];
    private readonly List<T> caught = [];
    private readonly List<T> gone = [];
    private int frame;

    /// <summary>Stars first seen in the last observed frame.</summary>
    public IReadOnlyList<StarSighting> NewStars => newStars;
    /// <summary>Lost items that joined their star in the last observed frame.</summary>
    public IReadOnlyList<(uint Star, T Item)> Paired => paired;
    /// <summary>Items whose star the original inhale caught, found in the last observed frame.</summary>
    public IReadOnlyList<T> Caught => caught;
    /// <summary>Items gone in the last observed frame: their star ran out or left, or never came.</summary>
    public IReadOnlyList<T> Gone => gone;
    /// <summary>The item a star carries, if any.</summary>
    public T? ItemOf(uint star) => stars.TryGetValue(star, out var s) ? s.Item : null;
    public int Carried => stars.Values.Count(star => star.Item is not null);
    public int Waiting => waiting.Count;

    /// <summary>
    /// Kirby lost the copy with this copy byte. Its item joins that copy's
    /// star if one appeared in the last few frames, and returns its address;
    /// otherwise it waits a few frames for the star.
    /// </summary>
    public uint? Lose(T item, byte ability)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Pair(item, ability, frame) is { } star) return star;
        waiting.Add((item, ability, frame));
        return null;
    }

    /// <summary>Follow the original ability stars through one source frame.</summary>
    public void Observe(IReadOnlyList<StarSighting> sightings)
    {
        ArgumentNullException.ThrowIfNull(sightings);
        newStars.Clear(); paired.Clear(); caught.Clear(); gone.Clear();
        if (sightings.Count == 0 && stars.Count == 0 && waiting.Count == 0) return;
        frame++;
        foreach (var sighting in sightings)
        {
            // A countdown that grew, or another copy byte, is a new star at a freed address.
            if (stars.TryGetValue(sighting.Address, out var star) &&
                (sighting.Countdown > star.Countdown || sighting.Ability != star.Ability))
            {
                End(star);
                star = null;
            }
            if (star is null)
            {
                stars[sighting.Address] = star = new Star(sighting.Ability, frame);
                newStars.Add(sighting);
            }
            star.Countdown = sighting.Countdown;
            star.State = sighting.State;
            star.Last = frame;
        }
        ended.Clear();
        foreach (var (address, star) in stars)
            // A caught or spent star is over at once; any other may only have missed a frame.
            if (star.Last != frame && (star.State == AbilityStar.Caught || star.Countdown <= 1 || frame - star.Last > MissingFrames))
                ended.Add(address);
        foreach (uint address in ended)
        {
            End(stars[address]);
            stars.Remove(address);
        }
        for (int i = 0; i < waiting.Count; i++)
        {
            var (item, ability, lost) = waiting[i];
            if (Pair(item, ability, lost) is { } address) paired.Add((address, item));
            else if (frame - lost < PairFrames) continue;
            else gone.Add(item);
            waiting.RemoveAt(i--);
        }
    }

    /// <summary>Forget every star and item: the original machine was replaced (a new run, a restored save, a revive).</summary>
    public void Clear()
    {
        stars.Clear(); waiting.Clear(); ended.Clear();
        newStars.Clear(); paired.Clear(); caught.Clear(); gone.Clear();
    }

    /// <summary>The newest free star of this copy first seen around the loss, now carrying the item.</summary>
    private uint? Pair(T item, byte ability, int lost)
    {
        uint? best = null;
        int seen = int.MinValue;
        foreach (var (address, star) in stars)
            if (star.Item is null && star.Ability == ability && star.Last == frame && star.State != AbilityStar.Caught &&
                Math.Abs(star.Seen - lost) <= PairFrames && star.Seen > seen)
            {
                best = address;
                seen = star.Seen;
            }
        if (best is { } found) stars[found].Item = item;
        return best;
    }

    /// <summary>A star left the original: caught, its item goes to Kirby's mouth; otherwise it is gone.</summary>
    private void End(Star star)
    {
        if (star.Item is not { } item) return;
        if (star.State == AbilityStar.Caught) caught.Add(item);
        else gone.Add(item);
        star.Item = null;
    }
}
