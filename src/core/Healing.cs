namespace Kirby.Core;

/// <summary>
/// How Kirby heals in a Kirby run (#101). He has no health flask: F is kept
/// for the stored limited-use ability. He heals by eating host food and at the
/// health fountain, which heals half his maximum health instead of all of it.
/// </summary>
public static class Healing
{
    /// <summary>Share of maximum health one fountain restores.</summary>
    public const double FountainShare = 0.5;

    /// <summary>The host heal amount for one fountain, rounded up; 0 without health.</summary>
    public static int FountainHeal(int maxLife) => maxLife <= 0 ? 0 : (int)Math.Ceiling(maxLife * FountainShare);
}
