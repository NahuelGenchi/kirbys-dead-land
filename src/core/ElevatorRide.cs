namespace Kirby.Core;

/// <summary>
/// Dead Cells elevators carry an entity whose feet are within 2 px of the
/// elevator's foot, and keep only the one cell row under that foot solid. While
/// an elevator moves, its surface lies between cell boundaries, but the original
/// sees whole cells: Kirby would settle on the row below the surface, out of the
/// host's reach, and be left behind. Over an elevator, the original's terrain
/// window is lifted by the surface's offset from that row, so its floor is the
/// surface and the host's own carry moves Kirby with it.
/// </summary>
public static class ElevatorRide
{
    /// <summary>Host pixels per collision cell.</summary>
    public const double Cell = 24;
    /// <summary>How far above the surface Kirby is still over the elevator: a jump or float off it.</summary>
    public const double Reach = 3 * Cell;
    /// <summary>How far below the surface Kirby's feet may be: rounding, and the original's floor contact.</summary>
    public const double Tolerance = 4;

    /// <param name="Lift">Offset (host pixels, -24 to 0) of the surface from the cell row the host marks solid under it.</param>
    /// <param name="Raise">Host pixels to move Kirby up to stand on the surface: a rising elevator met his feet.</param>
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
    /// </summary>
    /// <param name="kirbyCx">Kirby's host cell column.</param>
    /// <param name="feetY">Kirby's feet, host pixels.</param>
    /// <param name="elevatorCx">Elevator centre cell column.</param>
    /// <param name="widthCells">Elevator width in cells (cWid).</param>
    /// <param name="elevatorCy">Elevator cell row (cy); its solid row is the next one.</param>
    /// <param name="elevatorYr">Elevator position within that row (yr); 1 at rest.</param>
    /// <param name="rise">Host pixels the surface rose since the previous original frame (negative when it fell).</param>
    public static Ride? Over(int kirbyCx, double feetY, int elevatorCx, int widthCells, int elevatorCy, double elevatorYr, double rise)
    {
        double half = widthCells * 0.5;
        if (kirbyCx < elevatorCx - half || kirbyCx >= elevatorCx + half) return null;
        double below = feetY - Surface(elevatorCy, elevatorYr);
        if (below < -Reach || below > Tolerance + Math.Max(0, rise)) return null;
        return new Ride(Math.Clamp((elevatorYr - 1) * Cell, -Cell, 0), Math.Max(0, below), rise != 0);
    }
}
