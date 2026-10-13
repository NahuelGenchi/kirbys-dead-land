namespace Kirby.Core;

/// <summary>
/// Turns an analog stick into the original's digital directions as an 8-way
/// pad. A thumb pushing sideways drifts off the horizontal; read axis by axis
/// that drift is a held Down, which crouches, swallows a mouthful and switches
/// the dash assist off. Vertical counts only on a clear tilt, and each axis
/// only when it is not dwarfed by the other.
/// </summary>
public static class PadStick
{
    public const ushort Right = 16, Left = 32, Up = 64, Down = 128;
    /// <summary>Least vertical tilt that counts, whatever the dead zone.</summary>
    public const double VerticalTilt = 0.5;
    /// <summary>Vertical needs this share of the horizontal tilt: about 35 degrees off the horizontal.</summary>
    public const double VerticalRatio = 0.7;
    /// <summary>Horizontal needs this share of the vertical tilt: about 22 degrees off the vertical.</summary>
    public const double HorizontalRatio = 0.4;

    /// <param name="x">Horizontal tilt, positive right.</param>
    /// <param name="y">Vertical tilt, positive up.</param>
    /// <param name="gated">False reads each axis alone against its dead zone.</param>
    public static ushort Directions(double x, double y, double deadX, double deadY, bool gated)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y);
        bool horizontal = ax > deadX, vertical = ay > deadY;
        if (gated)
        {
            horizontal &= ax >= HorizontalRatio * ay;
            vertical &= ay > VerticalTilt && ay >= VerticalRatio * ax;
        }
        ushort keys = 0;
        if (horizontal) keys |= x < 0 ? Left : Right;
        if (vertical) keys |= y < 0 ? Down : Up;
        return keys;
    }
}

/// <summary>A button that acts only once it has been held: a trigger brushed by accident does nothing.</summary>
public sealed class HeldButton(double seconds)
{
    private double? since;

    public bool Update(bool down, double now)
    {
        if (!down) { since = null; return false; }
        since ??= now;
        return now - since >= seconds;
    }
}
