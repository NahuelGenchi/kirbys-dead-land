using System.Buffers.Binary;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Applies <see cref="SlideHold"/> to the original machine: before a source
/// frame it writes the slide's held counters and full-speed velocity into the
/// controller block, and afterwards it tells the rule whether the slide moved.
/// </summary>
public sealed class KirbySlideHold
{
    private readonly SlideHold rule = new();
    private int xBefore;

    /// <summary>The slide is held in a roll-zone tunnel for the source frame being run.</summary>
    public bool Holding { get; private set; }

    /// <summary>
    /// Run before <c>nds.Step</c>, after the window is placed. <paramref name="vx"/> is the
    /// original's current horizontal velocity; <paramref name="inside"/> that Kirby's body
    /// overlaps a roll-zone cell; <paramref name="recoil"/> that a hit reaches the original this frame.
    /// </summary>
    public void Begin(NdsRuntime nds, ushort action, int vx, bool inside, bool recoil)
    {
        Holding = false;
        if (action != KirbyActions.Slide || Controller(nds) is not { } controller) { rule.Reset(); return; }
        Span<byte> value = stackalloc byte[2];
        var counters = rule.Begin(action, vx, inside, recoil, () =>
        {
            Span<byte> one = stackalloc byte[1];
            nds.Read(controller + SlideHold.StepsOffset, one);
            byte steps = one[0];
            nds.Read(controller + SlideHold.PhaseOffset, one);
            return (steps, one[0]);
        });
        if (counters is not { } write) return;
        value[0] = write.Steps;
        nds.Write(controller + SlideHold.StepsOffset, value[..1]);
        value[0] = write.Phase;
        nds.Write(controller + SlideHold.PhaseOffset, value[..1]);
        if (!rule.Holding) return;
        Holding = true;
        NdsState state = nds.State;
        xBefore = state.XRaw;
        // The signed velocity is what the original reports and the magnitude at +0x98 is what it
        // moves on. Both are already full speed in the first part of the slide; a tunnel entered
        // during the slowdown is rolled through at full speed as well.
        BinaryPrimitives.WriteInt16LittleEndian(value, (short)(state.VxRaw < 0 ? -SlideHold.Velocity : SlideHold.Velocity));
        nds.Write(state.Player + 0x88, value);
        BinaryPrimitives.WriteInt16LittleEndian(value, SlideHold.Velocity);
        nds.Write(state.Player + 0x98, value);
    }

    /// <summary>Run after <c>nds.Step</c>.</summary>
    public void Finish(NdsRuntime nds)
    {
        if (Holding) rule.Finish(nds.State.XRaw != xBefore);
    }

    public void Reset() { rule.Reset(); Holding = false; }

    private static uint? Controller(NdsRuntime nds)
    {
        Span<byte> value = stackalloc byte[4];
        nds.Read(nds.State.Player + 0x40, value);
        uint controller = BinaryPrimitives.ReadUInt32LittleEndian(value);
        return controller is < 0x02000000 or >= 0x023FF000 ? null : controller;
    }
}
