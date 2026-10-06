namespace Kirby.Core;

/// <summary>
/// Dead Cells elevators carry an entity whose feet are within 2 px of the
/// elevator's foot, and keep only the one cell row under that foot solid. While
/// an elevator moves, its surface lies between cell boundaries, but the original
/// sees whole cells: Kirby would settle on the row below the surface, out of the
/// host's reach, and be left behind. Over an elevator, the original's terrain
/// window is lifted by the surface's offset from that row, so its floor is the
/// surface, and Kirby standing on it moves with it by the host's own rule. The
/// host's carry alone misses him on a fast descent, and the original's fall
/// (at most 3.5 px a frame) never catches the elevator again.
/// </summary>
public static class ElevatorRide
{
    /// <summary>Host pixels per collision cell.</summary>
    public const double Cell = 24;
    /// <summary>How far above the surface Kirby is still over the elevator: a jump or float off it.</summary>
    public const double Reach = 3 * Cell;
    /// <summary>How far below the surface Kirby's feet may be: rounding, and the original's floor contact.</summary>
    public const double Tolerance = 4;
    /// <summary>The host carries an entity whose feet are this close to the elevator's foot before it moves.</summary>
    public const double Snap = 2;

    /// <param name="Lift">Offset (host pixels, -24 to 0) of the surface from the cell row the host marks solid under it.</param>
    /// <param name="Raise">Host pixels to move Kirby up (negative: down) to stand on the surface: the
    /// elevator carried him, or a rising elevator met his feet.</param>
    /// <param name="Moved">The elevator moved since the previous original frame, carrying Kirby with it.</param>
    public readonly record struct Ride(double Lift, double Raise, bool Moved)
    {
        /// <summary>The elevator moves Kirby this frame, rather than standing still under him.</summary>
        public bool Carrying => Moved || Lift != 0 || Raise != 0;
    }

    /// <summary>Host pixel height of an elevator's surface (its foot).</summary>
    public static double Surface(int elevatorCy, double elevatorYr) => (elevatorCy + elevatorYr) * Cell;

    /// <summary>
    /// How Kirby rides this elevator, or null when he is not over it. Across,
    /// this matches the host carry: Kirby's cell column within half the
    /// elevator's width of its centre column. Up, it reaches a jump or float
    /// off the elevator; down, a rising elevator also meets feet it passed
    /// since the previous original frame, as the host rescues the Beheaded.
    /// Kirby whose feet were on the surface at the previous original frame,
    /// within the host's 2 px, and whom the original is not moving upward
    /// (a jump), moves with it up or down, as the host carries its entities.
    /// </summary>
    /// <param name="kirbyCx">Kirby's host cell column.</param>
    /// <param name="feetY">Kirby's feet, host pixels.</param>
    /// <param name="elevatorCx">Elevator centre cell column.</param>
    /// <param name="widthCells">Elevator width in cells (cWid).</param>
    /// <param name="elevatorCy">Elevator cell row (cy); its solid row is the next one.</param>
    /// <param name="elevatorYr">Elevator position within that row (yr); 1 at rest.</param>
    /// <param name="rise">Host pixels the surface rose since the previous original frame (negative when it fell).</param>
    /// <param name="rising">The original moved Kirby upward on its previous frame (a jump or float).</param>
    public static Ride? Over(int kirbyCx, double feetY, int elevatorCx, int widthCells, int elevatorCy, double elevatorYr,
        double rise, bool rising)
    {
        double half = widthCells * 0.5;
        if (kirbyCx < elevatorCx - half || kirbyCx >= elevatorCx + half) return null;
        double surface = Surface(elevatorCy, elevatorYr), below = feetY - surface;
        double lift = Math.Clamp((elevatorYr - 1) * Cell, -Cell, 0);
        if (!rising && Math.Abs(feetY - (surface + rise)) <= Snap) return new Ride(lift, below, rise != 0);
        if (below < -Reach || below > Tolerance + Math.Max(0, rise)) return null;
        return new Ride(lift, Math.Max(0, below), rise != 0);
    }
}
