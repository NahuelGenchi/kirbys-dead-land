namespace Kirby.Core;

/// <summary>
/// Decides whether Dead Cells roll zones are open to Kirby. The host lets the
/// Beheaded through them only while he rolls, and extends the roll until he is
/// out; Kirby's slide is his roll. A zone entered while sliding stays open
/// until Kirby is clear of it, so a slide that ends inside never traps him,
/// while walking cannot enter one.
/// </summary>
public sealed class RollZoneGate
{
    private bool entered;

    public bool Open { get; private set; }

    /// <param name="sliding">Kirby is sliding, or starts a slide this frame.</param>
    /// <param name="inside">Kirby's body overlaps a roll-zone cell.</param>
    public bool Update(bool sliding, bool inside)
    {
        if (!inside) entered = false;
        else if (sliding) entered = true;
        return Open = sliding || entered;
    }

    public void Reset() => entered = Open = false;
}
