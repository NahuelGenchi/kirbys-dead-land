namespace Kirby.Core;

/// <summary>Kirby's original star objects, told apart by their update routine.</summary>
public enum StarKind { None, Spit, BigSpit, Ability }

/// <summary>Who resolves a star touching a host enemy.</summary>
public enum StarRoute
{
    /// <summary>The paired source enemy's own collision (proxy HP drop) decides; the host adds stagger.</summary>
    Source,
    /// <summary>The host applies the star's damage and stagger itself.</summary>
    Host,
}

/// <summary>Original world box, inclusive on every edge, as the source collision compares them.</summary>
public readonly record struct StarBox(int Left, int Top, int Right, int Bottom)
{
    public bool Overlaps(StarBox other) =>
        Left <= other.Right && other.Left <= Right && Top <= other.Bottom && other.Top <= Bottom;
    public StarBox Union(StarBox other) => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    public StarBox Offset(int x, int y) => new(Left + x, Top + y, Right + x, Bottom + y);
    public double CenterX => (Left + Right) / 2.0;
}

/// <summary>
/// Spat and discarded stars as host projectiles. The ROM spawns, moves and
/// removes the stars through the injected host terrain, so their wall behavior is
/// original: both spit stars burst on a wall and the discarded ability star bounces.
/// These rules only decide which host enemies a star strikes and how hard.
/// </summary>
public static class StarProjectiles
{
    /// <summary>Spit star from a one-object mouthful: 52 damage, bursts on the first enemy it hits.</summary>
    public const uint SpitUpdate = 0x020BD718;
    /// <summary>Big spit star from a mouthful of two or more: 60 damage, passes through enemies.</summary>
    public const uint BigSpitUpdate = 0x020BDD08;
    /// <summary>Discarded copy-ability star: a collectible with no damage in the ROM.</summary>
    public const uint AbilityUpdate = 0x02166A54;
    /// <summary>
    /// Deliberate deviation: the original discarded star deals no damage. It
    /// strikes host enemies like the single spit star's original 52 damage.
    /// </summary>
    public const int AbilityStarDamage = 52;

    public static StarKind Identify(uint update) => update switch
    {
        SpitUpdate => StarKind.Spit,
        BigSpitUpdate => StarKind.BigSpit,
        AbilityUpdate => StarKind.Ability,
        _ => StarKind.None,
    };

    /// <summary>Source damage of one strike: a spit star's own original damage word, or the deviation for the ability star.</summary>
    public static int SourceDamage(StarKind kind, int originalDamage) => kind switch
    {
        StarKind.Ability => AbilityStarDamage,
        StarKind.Spit or StarKind.BigSpit => Math.Max(0, originalDamage),
        _ => 0,
    };

    /// <summary>
    /// A spit star striking a paired enemy is already damaged (and burst) by the
    /// original collision, so the host must not hit it a second time. Unpaired
    /// enemies have no source object, and the ability star does no original
    /// damage, so the host strikes those itself.
    /// </summary>
    public static StarRoute Route(StarKind kind, bool paired) =>
        kind != StarKind.Ability && paired ? StarRoute.Source : StarRoute.Host;

    // The host dive's landing stagger (tool.mainSkills.DiveAttack.onOwnerLand):
    // affect 16 for 0.7 s and a horizontal bump before the hit; Stun (affect 8)
    // for the DiveAttack skill's props.affect, 1 s, after a successful hit.
    public const int StaggerAffect = 16;
    public const double StaggerSeconds = 0.7;
    public const int StunAffect = 8;
    public const double StunSeconds = 1;

    /// <summary>Bump speed, from the dive's 0.2 + random × 0.1.</summary>
    public static double BumpSpeed(double random01) => 0.2 + 0.1 * Math.Clamp(random01, 0, 1);

    /// <summary>Push away from the star; a centered hit follows the star's travel.</summary>
    public static int BumpDirection(double starX, double targetX, int travel) =>
        targetX > starX ? 1 : targetX < starX ? -1 : travel >= 0 ? 1 : -1;
}

/// <summary>
/// One strike per star per enemy. The single spit star bursts on its first hit;
/// the big star and the bouncing ability star pass through, and strike each
/// enemy they cross once.
/// </summary>
public sealed class StarStrikes
{
    private readonly Dictionary<uint, HashSet<long>> struck = [];

    /// <summary>Forget stars that are no longer present this source frame, so a reused object address starts fresh.</summary>
    public void Retain(IReadOnlyCollection<uint> present)
    {
        foreach (uint star in struck.Keys.Where(star => !present.Contains(star)).ToArray()) struck.Remove(star);
    }

    /// <summary>True the first time this star strikes this target.</summary>
    public bool TryStrike(uint star, long target)
    {
        if (!struck.TryGetValue(star, out var targets)) struck[star] = targets = [];
        return targets.Add(target);
    }

    public void Clear() => struck.Clear();
}
