namespace Kirby.Core;

/// <summary>
/// Matches Kirby's movement to the Beheaded's, as measured in the host. The
/// original controller still runs every frame with its own states, animation
/// and collision; Kirby then moves on through free space along the motion it
/// made, so he runs, rolls, falls, flies and climbs at the Beheaded's speeds.
/// Added motion stops a pixel short of walls, floors and ceilings, so every
/// contact is still made by the original collision.
/// </summary>
public static class SpeedMatch
{
    // Beheaded (host lab, 30 steps a second): run and air control 236.5 px/s,
    // roll 131 px at up to 339 px/s, terminal fall 403 px/s, dive about
    // 900 px/s, ladders 97.5 px/s both ways, a 43 px jump peaking after 0.23 s,
    // and he stops within about 3 px when his direction is released or turned.
    // Original controller, per 60 Hz frame (256 = 1 px): walk 352, dash 528,
    // air 368, float 272, slide 704 then slowing for 71 px in all, climb 592
    // up and 704 down, fall 896, float descent 288.
    /// <summary>The Beheaded's running speed in host pixels per original frame.</summary>
    public const double RunSpeed = 236.5 / 60;
    public const double GroundScale = RunSpeed / (528 / 256.0);
    public const double AirScale = RunSpeed / (368 / 256.0);
    public const double FloatScale = RunSpeed / (272 / 256.0);
    /// <summary>The slide's full-speed part moves like the Beheaded's roll; its slowdown makes up the roll's length.</summary>
    public const double SlideFastScale = 2.05, SlideSlowScale = 1.5;
    public const int SlideFullVelocity = 704;
    public const double FallScale = 403.0 / 60 / (896 / 256.0);
    /// <summary>Floating rises and sinks at the Beheaded's tempo, as it moves across.</summary>
    public const double FlightScale = GroundScale;
    public const double ClimbUpScale = 97.5 / 60 / (592 / 256.0), ClimbDownScale = 97.5 / 60 / (704 / 256.0);
    /// <summary>Vertical velocity of the original's last take-off frame; the burst before it is kept.</summary>
    public const int JumpBurstEnd = -744;
    /// <summary>
    /// After take-off the original rises slowly to its 50 px peak after 0.33 s.
    /// Kirby keeps that height and reaches it after 0.25 s, as the Beheaded
    /// does: a faster climb out of the burst (velocity stored before the
    /// original adds its own 44 of gravity) and stronger gravity until the peak.
    /// </summary>
    public const int JumpRiseVelocity = -1034, JumpGravity = 46;

    // Kirby's original body: walls stop his centre 8.5 px away and ceilings
    // stop his feet 18.5 px below. On the ground the original follows slopes
    // from the centre, so the lowest part of the body does not meet walls.
    public const double HalfWidth = 8.5, Height = 18.5, GroundStep = 9, Margin = 1;
    /// <summary>
    /// The original's floor test starts from where its previous frame ended, so
    /// added descent leaves at least one full fall step (3.5 px) for it to land.
    /// </summary>
    public const double LandingMargin = 4;
    private const double Edge = 1e-3;

    // Terrain the bridge gives the original controller.
    public const ushort Free = 0, Solid = 3, Water = 130, OneWay = 131, SlopeUpRight = 132, SlopeUpLeft = 133, Ladder = 134;
    public delegate ushort Terrain(int cx, int cy);

    public enum Motion { None, Ground, Air, Float, Slide, Climb }

    /// <summary>
    /// The ability action (142C) is also the exhale that ends a float, with B or
    /// on landing. It moves like floating while it follows a float; copy-ability
    /// attacks under it keep their original motion.
    /// </summary>
    public sealed class ActionFilter
    {
        private ushort last;
        private bool exhale;
        /// <summary>The latest action as SpeedMatch should treat it.</summary>
        public ushort Effective { get; private set; }
        /// <summary>Record the original action after a frame and return it as SpeedMatch should treat it.</summary>
        public ushort Next(ushort action)
        {
            exhale = action == KirbyActions.Ability && (exhale || last == KirbyActions.Float);
            last = action;
            return Effective = exhale ? KirbyActions.Float : action;
        }
        public void Reset() { last = Effective = 0; exhale = false; }
    }

    public static Motion Classify(ushort action) => action switch
    {
        KirbyActions.Standing or KirbyActions.Landing or KirbyActions.Walk or KirbyActions.Dash or KirbyActions.Skid => Motion.Ground,
        KirbyActions.Jump or KirbyActions.Fall => Motion.Air,
        KirbyActions.Float => Motion.Float,
        KirbyActions.Slide => Motion.Slide,
        KirbyActions.Climb => Motion.Climb,
        _ => Motion.None,
    };

    /// <summary>
    /// The original lets go of its speed gradually and skids to turn; the
    /// Beheaded stops when his direction is released and turns at once. True
    /// when the original horizontal velocity should be cleared before the next frame.
    /// </summary>
    public static bool Brakes(ushort action, int direction, int vx) =>
        vx != 0 && (direction == 0 || Math.Sign(vx) != direction) && Classify(action) is Motion.Ground or Motion.Air or Motion.Float;

    /// <summary>Vertical velocity for the next frame of a jump's rise.</summary>
    public static int RiseVelocity(ushort action, int vy)
    {
        if (action != KirbyActions.Jump || vy >= 0) return vy;
        if (vy == JumpBurstEnd) return JumpRiseVelocity;
        return vy > JumpRiseVelocity ? vy + JumpGravity : vy;
    }

    /// <summary>Horizontal motion for one original frame that moved <paramref name="dx"/>.</summary>
    public static double Horizontal(Motion motion, int direction, double dx, int vx)
    {
        if (dx == 0) return 0;
        if (motion == Motion.Slide) return dx * (Math.Abs(vx) >= SlideFullVelocity ? SlideFastScale : SlideSlowScale);
        // Only motion the player is asking for speeds up; skids and recoil keep their original speed.
        if (direction == 0 || Math.Sign(dx) != direction) return dx;
        double scale = motion switch { Motion.Ground => GroundScale, Motion.Air => AirScale, Motion.Float => FloatScale, _ => 1 };
        return Math.Sign(dx) * Math.Min(Math.Abs(dx) * scale, Math.Max(RunSpeed, Math.Abs(dx)));
    }

    /// <summary>Vertical motion for one original frame that moved <paramref name="dy"/> (positive is down).</summary>
    public static double Vertical(Motion motion, double dy) => motion switch
    {
        Motion.Air when dy > 0 => dy * FallScale,
        Motion.Float => dy * FlightScale,
        Motion.Climb => dy * (dy < 0 ? ClimbUpScale : ClimbDownScale),
        _ => dy,
    };

    /// <summary>
    /// Where Kirby ends one original frame. (x0, feet0) is where the frame
    /// started and (x1, feet1) where the original controller left him, in host
    /// pixels; <paramref name="action"/> is the original action after the frame.
    /// </summary>
    public static (double X, double Feet) Move(ushort action, int direction, int vx,
        double x0, double feet0, double x1, double feet1, Terrain tile)
    {
        var motion = Classify(action);
        if (motion == Motion.None) return (x1, feet1);
        double dx = x1 - x0, dy = feet1 - feet0;
        double ex = Horizontal(motion, direction, dx, vx) - dx;
        double ey = Vertical(motion, dy) - dy;
        double x = x1, feet = feet1;
        if (motion is Motion.Ground or Motion.Slide)
        {
            // The original keeps Kirby on the floor and follows slopes from his
            // centre; follow the same surface over the added distance.
            if (ex == 0) return (x, feet);
            if (OnSurface(tile, x, feet, ex) is { } full) return full;
            // Something is ahead: stop short of the first wall instead.
            ex = Ahead(tile, x, feet, ex, ground: true);
            return ex != 0 && OnSurface(tile, x, feet, ex) is { } shorter ? shorter : (x, feet);
        }
        if (ex != 0) x += Ahead(tile, x, feet, ex, ground: false);
        if (ey > 0) feet += Math.Min(ey, Math.Max(0, FreeBelow(tile, x, feet) - LandingMargin));
        else if (ey < 0) feet -= Math.Min(-ey, Math.Max(0, FreeAbove(tile, x, feet) - Margin));
        return (x, feet);
    }

    /// <summary>Kirby moved along the floor or slope under him, or null when his body would meet a wall there.</summary>
    private static (double X, double Feet)? OnSurface(Terrain tile, double x, double feet, double ex)
    {
        double nx = x + ex, reach = Math.Abs(ex) + 1;
        double nfeet = Surface(tile, nx, feet, reach) ?? feet;
        return BodyFree(tile, nx + Math.Sign(ex) * Margin, nfeet, ground: true) ? (nx, nfeet) : null;
    }

    /// <summary>Added horizontal motion limited to stop short of the first wall.</summary>
    public static double Ahead(Terrain tile, double x, double feet, double ex, bool ground)
    {
        int direction = Math.Sign(ex);
        double edge = x + direction * HalfWidth;
        int top = Cell(feet - Height), bottom = Cell(feet - (ground ? GroundStep : Edge));
        int first = Cell(edge + direction * Edge), last = Cell(edge + ex + direction * Margin);
        for (int cx = first; direction > 0 ? cx <= last : cx >= last; cx += direction)
            for (int cy = top; cy <= bottom; cy++)
                if (Blocks(tile(cx, cy), ground))
                {
                    double face = direction > 0 ? cx * 24 : (cx + 1) * 24;
                    double free = Math.Max(0, direction * (face - edge) - Margin);
                    return direction * Math.Min(Math.Abs(ex), free);
                }
        return ex;
    }

    /// <summary>Free pixels below Kirby's feet to the first floor within four cells. Ladders never stop a fall; water ends added descent.</summary>
    public static double FreeBelow(Terrain tile, double x, double feet)
    {
        int left = Cell(x - HalfWidth + Edge), right = Cell(x + HalfWidth - Edge), top = Cell(feet);
        for (int cy = top; cy < top + 4; cy++)
            for (int cx = left; cx <= right; cx++)
            {
                ushort t = tile(cx, cy);
                if (t is Free or Ladder || (t == OneWay && cy * 24 < feet - 0.5)) continue;
                return Math.Max(0, cy * 24 - feet);
            }
        return double.PositiveInfinity;
    }

    /// <summary>Free pixels above Kirby's head to the first ceiling within four cells; one-way platforms let him through.</summary>
    public static double FreeAbove(Terrain tile, double x, double feet)
    {
        double head = feet - Height;
        int left = Cell(x - HalfWidth + Edge), right = Cell(x + HalfWidth - Edge), bottom = Cell(head - Edge);
        for (int cy = bottom; cy > bottom - 4; cy--)
            for (int cx = left; cx <= right; cx++)
                if (Blocks(tile(cx, cy), ground: false)) return Math.Max(0, head - (cy + 1) * 24);
        return double.PositiveInfinity;
    }

    /// <summary>
    /// The floor or slope surface under Kirby's centre within <paramref name="reach"/>
    /// pixels of his feet. The original samples slopes at whole pixel columns.
    /// </summary>
    public static double? Surface(Terrain tile, double x, double feet, double reach)
    {
        int cx = Cell(x), column = (int)Math.Floor(x) - cx * 24;
        for (int cy = Cell(feet - reach); cy <= Cell(feet + reach); cy++)
        {
            ushort t = tile(cx, cy);
            double? surface = t switch
            {
                Solid or OneWay when !Blocks(tile(cx, cy - 1), ground: false) => cy * 24,
                SlopeUpRight => (cy + 1) * 24 - (column + 1),
                SlopeUpLeft => (cy + 1) * 24 - (24 - column),
                _ => null,
            };
            if (surface is { } s && s >= feet - reach && s <= feet + reach) return s;
        }
        return null;
    }

    /// <summary>Kirby's body overlaps no wall at this position.</summary>
    public static bool BodyFree(Terrain tile, double x, double feet, bool ground)
    {
        int left = Cell(x - HalfWidth + Edge), right = Cell(x + HalfWidth - Edge);
        int top = Cell(feet - Height), bottom = Cell(feet - (ground ? GroundStep : Edge));
        for (int cy = top; cy <= bottom; cy++)
            for (int cx = left; cx <= right; cx++)
                if (Blocks(tile(cx, cy), ground)) return false;
        return true;
    }

    // Walls for added motion. Kirby walks onto slopes; in the air they are
    // left to the original collision, as solid cells are.
    private static bool Blocks(ushort t, bool ground) => t == Solid || (!ground && t is SlopeUpRight or SlopeUpLeft);
    private static int Cell(double pixel) => (int)Math.Floor(pixel / 24);
}
