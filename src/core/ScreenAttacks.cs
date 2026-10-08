namespace Kirby.Core;

/// <summary>
/// Paint, Mike and Crash hit "every enemy on screen". In the ROM each wave is one
/// attack record of Kirby's, a 512×512 box around him that lasts one frame
/// (Crash 160 damage, Paint 70, Mike 40 or 60 per shout). The original only
/// collides objects inside its activity window, the 256×192 screen plus 32 px on
/// each side, and the host only gives source objects to enemies near Kirby. The
/// Dead Cells view is much larger, so the host strikes the enemies on its own
/// screen that the source cannot reach.
/// </summary>
public static class ScreenAttacks
{
    /// <summary>Hit-list slot of Kirby's damaging attacks (Cook's screen grab is slot 1).</summary>
    public const uint AttackSlot = 2;
    public const int ScreenWidth = 256, ScreenHeight = 192;
    /// <summary>
    /// Measured on the bridge: a source object at camera x + 288 was hit and one
    /// at + 292 was not; y − 18 and + 234 were hit, − 22 and + 238 were not.
    /// </summary>
    public const int ActiveMargin = 32;

    /// <summary>Where the original collides objects, in source world pixels, inclusive.</summary>
    public static StarBox SourceWindow(int cameraX, int cameraY) => new(cameraX - ActiveMargin, cameraY - ActiveMargin,
        cameraX + ScreenWidth + ActiveMargin, cameraY + ScreenHeight + ActiveMargin);

    /// <summary>
    /// A damaging attack record that covers the whole source window. The caller
    /// also requires that Kirby owns it: in Mike's shouts only Kirby's own record
    /// dealt damage, never the unowned one that follows it.
    /// </summary>
    public static bool IsScreenWide(uint slot, int damage, StarBox box, int cameraX, int cameraY) =>
        slot == AttackSlot && damage > 0 && box.Contains(SourceWindow(cameraX, cameraY));

    /// <summary>The original collides a source object at this position.</summary>
    public static bool SourceReaches(double x, double y, int cameraX, int cameraY)
    {
        var window = SourceWindow(cameraX, cameraY);
        return x >= window.Left && x <= window.Right && y >= window.Top && y <= window.Bottom;
    }

    /// <summary>
    /// The host strikes an enemy on its screen unless the original already does:
    /// a paired enemy whose source object is inside the window, or one that lost
    /// source HP in this frame.
    /// </summary>
    public static bool HostStrikes(bool paired, bool sourceReaches, bool sourceHit) =>
        !paired || !sourceReaches && !sourceHit;
}
