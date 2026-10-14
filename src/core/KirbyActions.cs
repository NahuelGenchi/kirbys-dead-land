namespace Kirby.Core;

/// <summary>
/// Original controller action codes, the 16-bit value at 0x020BAF80. The same
/// codes are used in base form and with copy abilities (verified on the
/// supplied ROM with base, Sword, Beam and Wing Kirby).
/// </summary>
public static class KirbyActions
{
    public const ushort Standing = 0x0406, Landing = 0x0507, Skid = 0x060A, Walk = 0x060B, Dash = 0x060C,
        Jump = 0x070D, Fall = 0x070E, Float = 0x070F, Slide = 0x0B18, Climb = 0x0916, Hurt = 0x0F1D;

    /// <summary>
    /// Ability use: inhaling, exhaling and spitting, and every copy-ability
    /// attack, including those that carry Kirby (Wheel's roll, Jet's dash,
    /// Yo-Yo's and Suplex's dashes).
    /// </summary>
    public const ushort Ability = 0x142C;

    /// <summary>Rising, falling, floating or holding a ladder or chain: the states a stomp can start from.</summary>
    public static bool CanStomp(ushort action) => action is Jump or Fall or Float or Climb;

    /// <summary>On the ground and free to start a slide.</summary>
    public static bool CanSlide(ushort action) => action is Standing or Walk or Dash or Landing;

    /// <summary>The original controller has put Kirby back on the ground.</summary>
    public static bool OnGround(ushort action) => action is Standing or Landing or Walk or Dash or Slide;
}
