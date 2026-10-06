namespace Kirby.Core;

/// <summary>
/// Mod-written descriptions of the 24 copy abilities for the pause equipment
/// card. They name the mod's controls (the ROM's own text describes DS buttons)
/// and only well-established moves of the original game. They are not ROM text,
/// and not every listed move has been checked live in the mod.
/// </summary>
public static class AbilityDescriptions
{
    /// <summary>Controls shared by every ability, as routed by KirbyInput.</summary>
    public const string Controls = "Skill 1 or this button: attack. Skill 2: discard.";

    public static IReadOnlyDictionary<string, string> Summaries { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Cutter"] = "Throws a blade that curves back like a boomerang. Dash + Skill 1 slides forward with it.",
        ["Mirror"] = "Strikes with a mirror wand. Dodge raises a mirror guard that reflects shots.",
        ["Beam"] = "Lashes a whip of light in an arc. Hold Skill 1, then release, to fire a wave beam.",
        ["Fighter"] = "Rapid punches and kicks. Hold Skill 1, then release, to throw an energy blast.",
        ["Suplex"] = "Grabs an enemy and throws it. Dash + Skill 1 lunges into a grab.",
        ["YoYo"] = "Throws a yo-yo straight ahead and reels it back in.",
        ["Wheel"] = "Turns Kirby into a speeding wheel that runs over enemies. Skill 1 again stops.",
        ["Bomb"] = "Throws bombs in an arc that explode on contact.",
        ["Ice"] = "Breathes freezing air. Frozen enemies become ice blocks that slide into others.",
        ["Fire"] = "Breathes flames ahead. Dash + Skill 1 turns Kirby into a fireball.",
        ["Plasma"] = "Move back and forth to build up a charge; Skill 1 fires a bigger shot the more it is charged.",
        ["Ninja"] = "Throws knives from afar and slashes up close.",
        ["Stone"] = "Turns Kirby into an invincible stone; in the air he drops onto enemies. Skill 1 again turns back.",
        ["Wing"] = "Gives Kirby wings to fly freely and rush headfirst into enemies.",
        ["Jet"] = "Hold Skill 1 to charge the jet, then release to rocket forward.",
        ["Copy"] = "Fires a scanning beam that copies the ability an enemy holds.",
        ["Sword"] = "Swings a sword. In the air, Skill 1 spins the blade around Kirby.",
        ["Hammer"] = "Swings a giant hammer. Hold Skill 1, then release, for a flaming hammer swing.",
        ["Parasol"] = "Strikes with a parasol and floats down slowly beneath it, blocking attacks from above.",
        ["Paint"] = "Splashes paint across the whole screen, hitting every enemy in view. One use.",
        ["Mike"] = "Shouts into a microphone, hitting every enemy on screen. Three uses.",
        ["Crash"] = "A huge explosion hits every enemy on screen. One use.",
        ["Cook"] = "Cooks every enemy on screen into food; ordinary enemies drop Dead Cells healing food. One use.",
        ["Sleep"] = "Kirby falls asleep and cannot attack until he wakes up.",
    };

    /// <summary>The card text for an ability: its summary, then the shared controls.</summary>
    public static string Describe(string abilityId) =>
        Summaries.TryGetValue(abilityId, out string? summary) ? $"{summary}\n{Controls}" : Controls;
}
