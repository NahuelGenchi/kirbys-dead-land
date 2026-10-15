namespace Kirby.Core;

/// <summary>
/// Original move descriptors (#158, #166): the 24-byte entry the player record
/// points to at +0x808 while a move runs, which the bridge reads with the action.
/// One action can run several moves. The walk action (060B) walks on dry floor,
/// with a mouthful, or on the bottom of water (the wade); the ability action
/// (142C) is the inhale, the float's exhale, the water gun and every copy attack.
/// Measured on the supplied ROM with Normal, Sword and a mouthful, and the copy
/// tables with all 24 copies.
/// </summary>
public static class KirbyMoves
{
    public const uint Walk = 0x02131130, MouthfulWalk = 0x02131148, Wade = 0x02131160,
        Inhale = 0x021313A0, Exhale = 0x021313D0, WaterGun = 0x021313E8;

    /// <summary>
    /// The lowest move every form shares (the climb). A copy's own moves come from
    /// its move table (player record +0x800, 0 for Normal); all 24 tables lie below
    /// this one, from Cutter's at 0x02129DE8 to Sleep's at 0x02130B44.
    /// </summary>
    public const uint Shared = 0x02130C04;

    /// <summary>A move from the copy's own table: one of its attacks, or Sleep's nap.</summary>
    public static bool IsCopyMove(uint move, uint table) =>
        table != 0 && move >= table && move < Shared && (move - table) % 24 == 0;
}

/// <summary>
/// Which copy attack presses start an attack (#166), so each such press uses the
/// copy's item once (#57). From a stand, a walk or a jump the original starts a
/// copy move on the press or within a few frames of it. A press made during an
/// attack is kept until the attack can take its next move: Sword's next slash
/// started up to 10 frames after such a press. Some moves follow another by
/// themselves (Beam's whip after its wind-up, Jet's burst, Cutter's catch), and
/// many presses during an attack do nothing. So the next copy move to start uses
/// the item once if a press is waiting, and settles every press made before it.
/// A press waits as long as a copy move runs, then <see cref="Window"/> more
/// frames; one that starts nothing (in a recoil, during an ability change, on a
/// ladder, in water) uses nothing.
/// </summary>
public sealed class CopyAttackPresses
{
    public const int Window = 10;
    // Frames a waiting press has spent out of copy moves, or null when none waits.
    private int? waited;
    private uint lastMove, lastTable;

    /// <summary>
    /// Record one original frame: whether a new attack press went into it, and the
    /// move and copy table after it. True when the frame started a copy move for a press.
    /// </summary>
    public bool Next(bool pressed, uint move, uint table)
    {
        if (pressed) waited = 0;
        bool copy = KirbyMoves.IsCopyMove(move, table);
        bool started = copy && (move != lastMove || table != lastTable);
        lastMove = move;
        lastTable = table;
        if (started && waited is not null) { waited = null; return true; }
        if (waited is int frames && !copy) waited = frames + 1 < Window ? frames + 1 : null;
        return false;
    }

    /// <summary>Forget the press still waiting, as when another sequence owns the attack button.</summary>
    public void Drop() => waited = null;

    public void Reset() { waited = null; lastMove = lastTable = 0; }
}
