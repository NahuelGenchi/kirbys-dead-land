using Kirby.Core;
using Kirby.Runtime;
using static System.Linq.Enumerable;

// Kirby's movement against the original controller with SpeedMatch, replaying
// KirbyNdsProvider.Step's movement path on synthetic host terrain (24 px cells),
// checked against the Beheaded's speeds measured in the host.
if (args.Length != 3) throw new ArgumentException("native-library ROM private-seed-state");
using var nds = new NdsRuntime(args[0], args[1], args[2]);
string seed = args[2];
int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }
IEnumerable<ushort> R(ushort mask, int frames) => Repeat(mask, frames);
IEnumerable<ushort> P(params IEnumerable<ushort>[] parts) => parts.SelectMany(p => p);
double Speed(List<Row> r, int a, int b) => (r[b].X - r[a].X) / (b - a) * 60;

{
    var sim = new Sim(nds, seed);
    var r = sim.Run(P(R(16, 120), R(0, 20), R(16, 30), R(32, 40)));
    Check(Math.Abs(Speed(r, 60, 119) - 236.5) < 3, "Runs at the Beheaded's 236.5 px/s");
    Check(r[139].X - r[119].X < 4.5, "Releasing the direction stops within the Beheaded's distance");
    int full = Range(170, 40).FirstOrDefault(i => r[i].X - r[i - 1].X <= -3.9, -1);
    Check(full >= 0 && full - 170 <= 16, "Turning reaches full speed the other way within a quarter second");
}
{
    var sim = new Sim(nds, seed);
    for (int y = 20; y < 30; y++) sim.Tiles[(30, y)] = 3;
    var r = sim.Run(R(16, 200));
    Check(r.Max(x => x.X) <= 711.5 + 1e-6 && r.TakeLast(30).All(x => Math.Abs(x.X - 711.5) < 0.01),
        "Running into a wall stops at the original contact, never inside and without jitter");
}
{
    var sim = new Sim(nds, seed);
    var r = sim.Run(R(1, 60));
    int apex = Range(0, r.Count).MinBy(i => r[i].Feet);
    int land = Range(4, r.Count - 4).First(i => r[i].Feet >= 720 - 1e-6);
    Check(Math.Abs(720 - r[apex].Feet - 49.9) < 1.5 && apex + 1 is >= 14 and <= 16, "A jump keeps Kirby's 50 px and peaks after 0.25 s");
    Check(land + 1 is >= 30 and <= 38 && r.All(x => x.Feet <= 720 + 1e-6), "It lands after about half a second, never inside the floor");
}
{
    var sim = new Sim(nds, seed);
    var r = sim.Run(P(R(16, 60), R(17, 10), R(16, 40)));
    int land = Range(65, 45).First(i => r[i].Feet >= 720 - 1e-6);
    Check(Math.Abs(Speed(r, 62, land - 3) - 236.5) < 4, "A running jump keeps the run speed in the air");
}
{
    var sim = new Sim(nds, seed, floor: 60, feet: 720);
    for (int x = 0; x <= 13; x++) sim.Tiles[(x, 30)] = 3;
    var r = sim.Run(P(R(0, 10), R(16, 260)));
    double vmax = Range(11, r.Count - 12).Max(i => r[i].Feet - r[i - 1].Feet) * 60;
    Check(Math.Abs(vmax - 403) < 8 && r.All(x => x.Feet <= 1440 + 1e-6), "Falls at the Beheaded's 403 px/s and lands on the floor");
}
{
    var sim = new Sim(nds, seed);
    var r = sim.Run(P(R(1, 4), R(0, 4), new ushort[] { 1, 0, 1, 0, 1, 0 }, R(16, 80)));
    Check(Math.Abs(Speed(r, 40, 80) - 236.5) < 4, "Floats across at the run speed");
    var ceiling = new Sim(nds, seed);
    for (int x = 0; x < 40; x++) ceiling.Tiles[(x, 25)] = 3;
    var c = ceiling.Run(P(R(1, 4), R(0, 4), Range(0, 120).Select(i => (ushort)(i % 2))));
    Check(c.Min(x => x.Feet) >= 642.5 - 1e-6, "Flapping into a ceiling never puts Kirby's head inside it");
}
{
    var sim = new Sim(nds, seed);
    var r = sim.Run(P(R(128, 3), new ushort[] { 129 }, R(128, 50)));
    double peak = Range(1, r.Count - 1).Max(i => r[i].X - r[i - 1].X) * 60;
    Check(Math.Abs(r[^1].X - r[0].X - 131) < 4 && Math.Abs(peak - 339) < 10, "Slides as far and as fast as the Beheaded rolls");
    var wall = new Sim(nds, seed);
    for (int y = 20; y < 30; y++) wall.Tiles[(17, y)] = 3;
    Check(wall.Run(P(R(128, 3), new ushort[] { 129 }, R(128, 50))).Max(x => x.X) <= 399.5 + 1e-6, "A slide into a wall stops at the original contact");
}
{
    // A roll-zone tunnel is open to a slide (its cells reach the original as free space, under a ceiling
    // one cell above the floor) and longer than the 36-frame slide: one roll must reach the far end (#115).
    const int From = 14, To = 33;
    double entry = From * 24, exit = (To + 1) * 24;
    Sim Tube(bool hold, double x, int? wall = null)
    {
        var sim = new Sim(nds, seed, x: x) { Tunnel = (From, To), HoldSlides = hold };
        for (int c = From - 1; c <= To + 1; c++) sim.Tiles[(c, 28)] = SpeedMatch.Solid;
        if (wall is { } w) sim.Tiles[(w, 29)] = SpeedMatch.Solid;
        return sim;
    }
    int Run(List<Row> r, int from) => Range(from, r.Count - from).TakeWhile(i => r[i].Action == KirbyActions.Slide).Count();
    var plain = Tube(false, 300).Run(P(R(0, 5), R(128, 3), new ushort[] { 129 }, R(128, 3), R(0, 120)));
    Check(plain[^1].X < exit - 100 && plain[^1].Action == KirbyActions.Standing, $"Without the hold the slide stops inside a long tunnel (x {plain[^1].X:0}, tunnel ends {exit:0})");
    var rows = Tube(true, 300).Run(P(R(0, 5), R(128, 3), new ushort[] { 129 }, R(128, 3), R(0, 120)));
    int begin = rows.FindIndex(x => x.Action == KirbyActions.Slide);
    int length = Run(rows, begin);
    int inTunnel = Range(begin, length).Count(i => rows[i].X > entry && rows[i].X < exit);
    var full = Range(begin + 1, length - 1).Where(i => rows[i].X > entry + 20 && rows[i].X < exit - 20).ToList();
    Check(rows.Skip(begin).Take(length + 4).Count(x => x.Action == KirbyActions.Slide) == length && rows[^1].Action == KirbyActions.Standing,
        $"One slide rolls through the tunnel ({length} frames) and Kirby stands afterwards");
    Check(full.Count > 40 && full.All(i => Math.Abs(rows[i].Vx) >= SpeedMatch.SlideFullVelocity && Math.Abs(rows[i].X - rows[i - 1].X - 2.75 * SpeedMatch.SlideFastScale) < 0.1),
        $"He rolls at full speed the whole way ({full.Count} frames inside, {2.75 * SpeedMatch.SlideFastScale * 60:0} px/s)");
    Check(rows[^1].X > exit + 8 && rows[^1].X < exit + 70, $"He is out of the tunnel and slows to a stop beyond its end (x {rows[^1].X:0}, tunnel ends {exit:0})");
    Check(inTunnel > 60, $"The roll spends {inTunnel} frames inside the tunnel");
    // The same tunnel to the left: facing and velocity sign follow the slide.
    var left = Tube(true, (To + 1) * 24 + 78).Run(P(R(32, 3), R(0, 5), R(128, 3), new ushort[] { 129 }, R(128, 3), R(0, 160)));
    Check(left[^1].X < entry - 8 && left[^1].X > entry - 70 && left[^1].Action == KirbyActions.Standing,
        $"A slide to the left rolls out of the tunnel's other end (x {left[^1].X:0}, tunnel starts {entry:0}, {left.Count(x => x.Action == KirbyActions.Slide)} slide frames)");
    // A wall inside the tunnel stops it as it stops any slide, and the slide ends instead of staying pinned.
    var walled = Tube(true, 300, wall: 22).Run(P(R(0, 5), R(128, 3), new ushort[] { 129 }, R(128, 3), R(0, 160)));
    int pinned = walled.Count(x => x.Action == KirbyActions.Slide);
    Check(walled.Max(x => x.X) <= 22 * 24 - SpeedMatch.HalfWidth + 1e-6 && walled[^1].Action == KirbyActions.Standing && pinned < 80,
        $"A wall in the tunnel stops the roll and releases the slide ({pinned} slide frames, x {walled[^1].X:0})");
}
{
    // KirbyNdsProvider.Step: a Shift key bound to Dodge goes through SlideKey before the dash filter.
    List<Row> Keyed(IEnumerable<(ushort Mask, bool Key)> plan)
    {
        var sim = new Sim(nds, seed);
        var key = new SlideKey();
        ushort previous = 0, action = nds.Action;
        var rows = new List<Row>();
        foreach (var (mask, held) in plan)
        {
            previous = key.Filter(mask, held, action, previous);
            var row = sim.Step(previous);
            action = row.Action;
            rows.Add(row);
        }
        return rows;
    }
    IEnumerable<(ushort, bool)> K(ushort mask, bool key, int frames) => Repeat((mask, key), frames);
    int Slides(List<Row> r) => Range(1, r.Count - 1).Count(i => r[i].Action == KirbyActions.Slide && r[i - 1].Action != KirbyActions.Slide);
    var run = Keyed(K(16, false, 60).Concat(K(16, true, 1)).Concat(K(16, false, 60)));
    int start = run.FindIndex(x => x.Action == KirbyActions.Slide), end = run.FindLastIndex(x => x.Action == KirbyActions.Slide);
    Check(Slides(run) == 1 && end - start + 1 == 36 && Math.Abs(run[end].X - run[start - 1].X - 131) < 4,
        $"A Shift tap while running starts the whole original slide ({end - start + 1} frames, {run[end].X - run[start - 1].X:F0} px)");
    var hold = Keyed(K(0, false, 5).Concat(K(0, true, 90)));
    Check(Slides(hold) == 1 && hold.TakeLast(20).All(x => x.Action == KirbyActions.Standing), "Holding Shift slides once and does not crouch after");
    var plain = new Sim(nds, seed).Run(P(R(1, 8), R(0, 60))).Select(x => x.Action);
    var air = Keyed(K(1, false, 8).Concat(K(0, true, 60))).Select(x => x.Action);
    Check(air.SequenceEqual(plain), "Shift in the air changes nothing");
    var jumpHeld = Keyed(K(1, false, 80).Concat(K(1, true, 40)));
    Check(jumpHeld.Skip(80).Any(x => x.Action == KirbyActions.Slide), "Shift slides while jump is still held after landing");
}
{
    var sim = new Sim(nds, seed);
    for (int y = 15; y < 30; y++) sim.Tiles[(12, y)] = 134;
    sim.Tiles[(12, 14)] = SpeedMatch.OneWay;
    var up = sim.Run(R(64, 90));
    var down = sim.Run(R(128, 60));
    Check(Math.Abs(up[20].Feet - up[40].Feet - 138.75 * SpeedMatch.ClimbScale / 3) < 3 && Math.Abs((down[40].Feet - down[30].Feet) * 6 - 165 * SpeedMatch.ClimbScale) < 3,
        $"Climbs ladders as much faster than the original as the run (up {(up[20].Feet - up[40].Feet) * 3:F0}, down {(down[40].Feet - down[30].Feet) * 6:F0} px/s)");
}
// Every end of a ladder is reached as the original reaches it: the added climb
// stops a step short and the original makes the last steps. At an open top the
// original stands for a frame and falls; off a hanging ladder's bottom it falls.
foreach (var (name, top, bottom, start, plan) in new (string, ushort?, int, double?, ushort[])[]
{
    ("a one-way ladder top", SpeedMatch.OneWay, 29, null, P(R(64, 240), R(0, 10), R(128, 240)).ToArray()),
    ("a ladder under a solid block", SpeedMatch.Solid, 29, null, P(R(64, 240), R(128, 240)).ToArray()),
    ("a ladder ending in open air", null, 29, null, P(R(64, 240), R(128, 240)).ToArray()),
    ("a hanging ladder's bottom", SpeedMatch.OneWay, 22, 14 * 24, P(R(0, 10), R(128, 200)).ToArray()),
})
{
    List<Row> Climb(bool match)
    {
        var sim = new Sim(nds, seed, match: match, feet: start);
        for (int y = 15; y <= bottom; y++) sim.Tiles[(12, y)] = SpeedMatch.Ladder;
        if (top is { } t) sim.Tiles[(12, 14)] = t;
        return sim.Run(plan);
    }
    var native = Climb(false);
    var matched = Climb(true);
    // Where a run first lets go of the ladder (its last climbing frame) and what
    // the original does next. A matched fall lands sooner, so later grabs differ.
    (double Feet, ushort Action) Exit(List<Row> r) =>
        Range(1, r.Count - 1).Where(i => r[i].Action != KirbyActions.Climb && r[i - 1].Action == KirbyActions.Climb)
            .Select(i => (r[i - 1].Feet, r[i].Action)).DefaultIfEmpty((double.NaN, (ushort)0)).First();
    var (n, m) = (Exit(native), Exit(matched));
    Console.WriteLine($"  {name}: native lets go at {n.Feet:F2} ({n.Action:X4}), ends {native[^1].Feet:F2}; matched {m.Feet:F2} ({m.Action:X4}), ends {matched[^1].Feet:F2}");
    // Within one original climbing step (2.75 px down), the phase the two runs arrive in.
    Check(n.Action == m.Action && Math.Abs(n.Feet - m.Feet) < 2.75 && native[^1] == matched[^1], $"The faster climb leaves {name} where the original does");
}
foreach (ushort t in new ushort[] { SpeedMatch.SlopeUpRight, SpeedMatch.SlopeUpLeft })
{
    var sim = new Sim(nds, seed);
    int dir = t == SpeedMatch.SlopeUpRight ? 1 : -1;
    int Col(int k) => 12 + dir * k;
    sim.Tiles[(Col(4), 29)] = t; sim.Tiles[(Col(5), 28)] = t; sim.Tiles[(Col(5), 29)] = 3;
    sim.Tiles[(Col(6), 27)] = t; sim.Tiles[(Col(6), 28)] = 3; sim.Tiles[(Col(6), 29)] = 3;
    for (int k = 7; k < 14; k++) for (int y = 27; y < 30; y++) sim.Tiles[(Col(k), y)] = 3;
    ushort go = (ushort)(dir > 0 ? 16 : 32), back = (ushort)(dir > 0 ? 32 : 16);
    var r = sim.Run(P(R(go, 70), R(0, 10), R(back, 70)));
    Check(Math.Abs(r.Min(x => x.Feet) - 648) < 0.5 && r.Take(70).All(x => x.Action is not (KirbyActions.Fall or KirbyActions.Jump)) &&
          Math.Abs(r[^1].Feet - 720) < 0.01, $"Runs up slope {t} without leaving the ground and back down to the floor");
}
{
    var sim = new Sim(nds, seed);
    for (int x = 8; x < 20; x++) sim.Tiles[(x, 28)] = SpeedMatch.OneWay;
    Check(Math.Abs(sim.Run(R(1, 70))[^1].Feet - 672) < 0.01, "Jumps up through a two-cell one-way platform and lands on it");
}
{
    var sim = new Sim(nds, seed);
    for (int x = 16; x < 30; x++) sim.Tiles[(x, 28)] = 3;
    Check(Math.Abs(Speed(sim.Run(R(16, 150)), 100, 149) - 236.5) < 3, "Runs through a one-cell tunnel at full speed");
}
{
    var sim = new Sim(nds, seed, floor: 34, feet: 720);
    for (int x = 0; x <= 20; x++) sim.Tiles[(x, 30)] = 3;
    var r = sim.Run(R(16, 150));
    Check(r.FindIndex(x => x.Feet >= 34 * 24 - 1e-6) > 0 && r.All(x => x.Feet <= 34 * 24 + 1e-6), "Runs off a ledge and lands below, never inside the floor");
}
{
    // Bomb Kirby holds a bomb overhead, lobs it and runs off. Kirby's added speed
    // must not carry the bomb (or any other original object) along with him.
    List<double> BombPath(bool match)
    {
        var sim = new Sim(nds, seed, match: match);
        if (!sim.Transform(9)) throw new Exception("Bomb transformation failed");
        foreach (ushort m in R(2, 25).Concat(R(0, 3))) sim.Step(m);
        var path = new List<double>();
        for (int i = 0; i < 45; i++)
        {
            sim.Step(16);
            var bombs = sim.Actors().Where(a => a.Kind == 60).ToList();
            path.Add(bombs.Count == 1 ? bombs[0].X : double.NaN);
        }
        return path;
    }
    var native = BombPath(false);
    var matched = BombPath(true);
    int compared = Range(0, native.Count).Count(i => !double.IsNaN(native[i]) && !double.IsNaN(matched[i]));
    double worst = Range(0, native.Count).Where(i => !double.IsNaN(native[i]) && !double.IsNaN(matched[i]))
        .Select(i => Math.Abs(native[i] - matched[i])).DefaultIfEmpty(double.MaxValue).Max();
    Check(compared >= 30 && worst < 0.01, $"A lobbed bomb keeps its original path while Kirby runs off ({compared} frames, {worst:0.###} px)");
}
{
    // Copy abilities that carry Kirby faster than his dash run under the shared
    // ability action and keep their lead over the run: Wheel's roll, Jet's
    // charged dash, Suplex's grab.
    foreach (var (ability, name, plan) in new (byte, string, ushort[])[]
    {
        (8, "Wheel's roll", R(0, 5).Concat(R(2, 2)).Concat(R(0, 100)).ToArray()),
        (16, "Jet's charged dash", R(0, 5).Concat(R(2, 70)).Concat(R(0, 100)).ToArray()),
        (6, "Suplex's grab", R(0, 5).Concat(R(2, 2)).Concat(R(0, 100)).ToArray()),
    })
    {
        var ends = new List<(double X, double Feet)>();
        foreach (bool match in new[] { false, true })
        {
            var sim = new Sim(nds, seed, match: match);
            if (!sim.Transform(ability)) throw new Exception($"Ability {ability} transformation failed");
            Row last = default!;
            foreach (ushort m in plan) last = sim.Step(m);
            ends.Add((last.X - 300, last.Feet));
        }
        Check(Math.Abs(ends[0].X) > 50 && Math.Abs(ends[1].X / ends[0].X - SpeedMatch.GroundScale) < 0.1 && Math.Abs(ends[0].Feet - ends[1].Feet) < 0.01,
            $"{name} keeps its lead over the matched run ({ends[1].X:0} px, original {ends[0].X:0})");
    }
}
{
    // A dash attack outruns the dash it starts from by as much as the original's
    // does, so it stays faster than the matched run. It lasts as long as the
    // original, and a wall still stops it at the original contact.
    (List<Row> Rows, int Start, int End) DashAttack(byte ability, bool match, Sim? on = null)
    {
        var sim = on ?? new Sim(nds, seed, match: match);
        if (!sim.Transform(ability)) throw new Exception($"Ability {ability} transformation failed");
        var r = sim.Run(P(R(16, 40), R(18, 2), R(16, 70)));
        int a = Range(40, r.Count - 40).First(i => r[i].Action == KirbyActions.Ability), b = a;
        while (b + 1 < r.Count && r[b + 1].Action == KirbyActions.Ability) b++;
        return (r, a, b);
    }
    double Peak(List<Row> r, int a, int b) => Range(a, b - a + 1).Max(i => r[i].X - r[i - 1].X);
    foreach (var (ability, name) in new (byte, string)[] { (15, "Wing's Condor Head"), (6, "Suplex's dash grab"), (2, "Cutter's dash"), (5, "Fighter's dash"), (18, "Sword's dash") })
    {
        var o = DashAttack(ability, false);
        var m = DashAttack(ability, true);
        double oRatio = Peak(o.Rows, o.Start, o.End) / (o.Rows[39].X - o.Rows[38].X), mRun = m.Rows[39].X - m.Rows[38].X;
        double mRatio = Peak(m.Rows, m.Start, m.End) / mRun, distance = m.Rows[m.End].X - m.Rows[m.Start - 1].X;
        Check(oRatio > 1.3 && Math.Abs(mRatio - oRatio) < 0.02 && m.End - m.Start == o.End - o.Start &&
              Math.Abs(distance / (o.Rows[o.End].X - o.Rows[o.Start - 1].X) - SpeedMatch.GroundScale) < 0.1,
            $"{name} outruns the matched run as the original outruns its dash ({mRatio:0.00}x the run, {distance:0} px, {m.End - m.Start + 1} frames)");
    }
    var walled = new Sim(nds, seed);
    for (int y = 20; y < 30; y++) walled.Tiles[(32, y)] = 3;
    var w = DashAttack(6, true, walled);
    int contact = Range(w.Start, w.End - w.Start + 1).FirstOrDefault(i => Math.Abs(w.Rows[i].X - 759.5) < 0.01, -1);
    Check(contact >= 0 && w.Rows.Max(x => x.X) <= 759.5 + 1e-6 && w.Rows.Skip(contact).All(x => Math.Abs(x.X - 759.5) < 0.01),
        $"A dash attack into a wall stops at the original contact, never inside (frame {contact - w.Start + 1} of the attack)");
    // Doors resist the original grab, so the lunge itself bursts them as the
    // host's roll does: it must still count when Kirby meets the door.
    bool Grabbing(Row x) => HostStrikes.Grab(HostStrikes.Suplex, x.Motion, x.Vx);
    var free = DashAttack(6, true);
    int strikes = Range(free.Start, free.End - free.Start + 1).Count(i => Grabbing(free.Rows[i]));
    Check(strikes >= 10 && Range(0, free.Start).All(i => !Grabbing(free.Rows[i])) && Range(free.End + 1, free.Rows.Count - free.End - 1).All(i => !Grabbing(free.Rows[i])),
        $"Suplex's dash grab strikes doors during its lunge only ({strikes} of {free.End - free.Start + 1} frames)");
    // A wall within the lunge (not its whiff stumble afterwards) ends the grab
    // the frame Kirby meets it; the strike holds until he is a pixel from it.
    var near = new Sim(nds, seed);
    for (int y = 20; y < 30; y++) near.Tiles[(26, y)] = 3;
    var n = DashAttack(6, true, near);
    int met = Range(n.Start, n.Rows.Count - n.Start).FirstOrDefault(i => Math.Abs(n.Rows[i].X - 615.5) < 0.01, -1);
    Check(met > n.Start && n.Rows[met].Action != KirbyActions.Ability && Grabbing(n.Rows[met - 1]) && n.Rows[met - 1].X >= 614.5 - 1e-6,
        $"Suplex's dash grab strikes until its lunge is a pixel from a wall (frame {met - n.Start} of the attack)");
}
{
    // Fire's and Plasma's head emitters are placed by an offset from Kirby; the
    // window moving by whole cells while he climbs or runs must leave them on him.
    foreach (var (ability, name) in new (byte, string)[] { (11, "Fire's head flame"), (12, "Plasma's spark") })
    {
        var sim = new Sim(nds, seed);
        if (!sim.Transform(ability)) throw new Exception($"Ability {ability} transformation failed");
        for (int y = 0; y < 30; y++) sim.Tiles[(12, y)] = SpeedMatch.Ladder;
        double worst = 0;
        int seen = 0;
        foreach (ushort m in R(64, 50).Concat(R(128, 50)).Concat(R(16, 60)).Concat(R(32, 60)))
        {
            sim.Step(m);
            foreach (var (dx, dy) in sim.Emitters()) { worst = Math.Max(worst, Math.Max(Math.Abs(dx), Math.Abs(dy))); seen++; }
        }
        Check(seen >= 200 && worst < 6, $"{name} stays on Kirby while he climbs and runs ({worst:0.#} px)");
    }
}
{
    // The original draws only what is on its 256-pixel screen. A placement far from
    // the seed position frames Kirby at once, and the camera moves with the window,
    // so he stays near the centre while running instead of jumping at each cell.
    nds.LoadState(seed);
    var cells = new ushort[900];
    for (int y = 0; y < 9; y++) for (int x = 0; x < 100; x++) cells[y * 100 + x] = y >= 5 ? SpeedMatch.Solid : SpeedMatch.Free;
    nds.World(cells, 700 << 16, (int)(105.5 * 65536));
    nds.Step(0);
    NdsState placed = nds.State;
    int placedX = (int)placed.X - placed.CameraX;
    var sim = new Sim(nds, seed);
    var screen = new List<int>();
    foreach (ushort m in R(16, 120).Concat(R(32, 120))) { sim.Step(m); NdsState s = nds.State; screen.Add((int)s.X - s.CameraX); }
    int jump = Range(1, screen.Count - 1).Max(i => Math.Abs(screen[i] - screen[i - 1]));
    Check(placedX is >= 96 and <= 160 && screen.All(x => x is >= 96 and <= 160) && jump <= 4,
        $"Kirby is framed on the original screen after a placement (x {placedX}) and while running (x {screen.Min()}-{screen.Max()}, steps of {jump} px at most)");
}
{
    // Kirby's feet: the matched run covers about as much ground per walk cycle as
    // the original (41.3 px), instead of nearly twice as much.
    double PerCycle(bool match)
    {
        var sim = new Sim(nds, seed, match: match);
        foreach (ushort m in R(16, 30)) sim.Step(m);
        var poses = new List<(ushort Pose, double X)>();
        foreach (ushort m in R(16, 120)) { var r = sim.Step(m); poses.Add(((ushort)nds.State.Graphic, r.X)); }
        var starts = Range(1, poses.Count - 1).Where(i => poses[i].Pose == poses[0].Pose && poses[i - 1].Pose != poses[0].Pose).ToList();
        return (poses[starts[^1]].X - poses[starts[0]].X) / (starts.Count - 1);
    }
    double native = PerCycle(false), matched = PerCycle(true);
    Check(matched / native < 1.2, $"Kirby's walk cycle keeps up with the matched run ({matched:0.0} px a cycle, original {native:0.0})");
}
{
    // The original CPU helper (DS X, #105) is another controller instance; KirbyHelperMatch speeds
    // its motion up as Kirby's is, within the same terrain limits. The helper's AI follows Kirby
    // and attacks enemies, so a running Kirby shows whether it keeps up.
    (List<double> Gap, double Peak, double Last, bool Fell) Follow(bool helperMatch, int wallColumn = -1)
    {
        var sim = new Sim(nds, seed) { MatchHelper = helperMatch };
        if (wallColumn >= 0) for (int y = 20; y < 30; y++) sim.Tiles[(wallColumn, y)] = SpeedMatch.Solid;
        if (!sim.Transform(18)) throw new InvalidOperationException("Sword transform failed");
        sim.Step(1024);
        foreach (ushort m in R(0, 130)) sim.Step(m);
        var gap = new List<double>();
        double peak = 0, last = sim.HelperX ?? double.NaN;
        bool fell = false;
        foreach (ushort m in R(16, 150))
        {
            var kirby = sim.Step(m);
            if (sim.HelperX is not { } hx) { gap.Add(double.NaN); continue; }
            gap.Add(kirby.X - hx);
            peak = Math.Max(peak, Math.Abs(hx - last));
            last = hx;
            fell |= sim.HelperFeet > 720 + 1e-6;
        }
        return (gap, peak, last, fell);
    }
    var native = Follow(false);
    var matched = Follow(true);
    Console.WriteLine("  helper gap by 15 frames: native " + string.Join(' ', native.Gap.Where((_, i) => i % 15 == 14).Select(g => g.ToString("0"))) +
        "; matched " + string.Join(' ', matched.Gap.Where((_, i) => i % 15 == 14).Select(g => g.ToString("0"))));
    double nativeGap = native.Gap[^1], matchedGap = matched.Gap[^1];
    // The helper's AI trails a running Kirby at a fixed 60 px; at the original speed it falls behind without limit.
    Check(matched.Gap.All(g => !double.IsNaN(g)) && matchedGap < 70 && Math.Abs(matchedGap - matched.Gap[^60]) < 4 && nativeGap > 200,
        $"A running Kirby keeps his helper at its 60 px follow distance ({matchedGap:0} px after 2.5 s, steady for 1 s; original speed: {nativeGap:0} px behind)");
    Check(matched.Peak <= SpeedMatch.RunSpeed + 0.5 && !matched.Fell,
        $"The helper never moves faster than Kirby's run ({matched.Peak:0.00} px a frame) or into the floor");
    var wall = Follow(true, wallColumn: 20);
    Check(wall.Last <= 20 * 24 - SpeedMatch.HalfWidth + 1e-6 && !wall.Fell, $"The helper stops at a wall as the original does ({wall.Last:0.0} px)");
}
Console.WriteLine(failures == 0 ? "PASS: Kirby's movement matches the Beheaded's speeds on the original controller." : $"{failures} FAILED");
return failures;

record Row(ushort Action, double X, double Feet, int Vx, int Vy, ushort Motion = 0);

/// <summary>KirbyNdsProvider.Step's movement path: world rebase, velocity matching, original frame, added motion.</summary>
sealed class Sim
{
    readonly NdsRuntime nds;
    readonly DashAssist dash = new();
    readonly SpeedMatch.ActionFilter motionActions = new();
    readonly KirbyHelperMatch helper = new();
    readonly AnimationPacer walkCycle = new();
    readonly KirbySlideHold slideHold = new();
    public readonly Dictionary<(int, int), ushort> Tiles = [];
    readonly int floor;
    readonly bool match;
    double hostX = 12 * 24 + 12, feet;
    int lastVx, lastVy;
    (int X, int Y)? window;
    int ox, oy;
    public double X => hostX;
    /// <summary>KirbyNdsProvider's helper speed match (#105).</summary>
    public bool MatchHelper { get; init; }
    /// <summary>Host cell columns of a roll-zone tunnel: the slide is held while Kirby's body overlaps them (#115).</summary>
    public (int From, int To)? Tunnel { get; init; }
    /// <summary>The slide hold of the tunnel; off, a slide runs its 36 frames inside it.</summary>
    public bool HoldSlides { get; init; } = true;
    /// <summary>The original helper's centre x and feet y in host pixels; null while there is none.</summary>
    public double? HelperX => nds.TryHelper(out var h) ? h.X + ox * 24 : null;
    public double HelperFeet => nds.TryHelper(out var h) ? h.Y + oy * 24 + 14.5 : double.NaN;
    public Sim(NdsRuntime nds, string seed, int floor = 30, double? feet = null, bool match = true, double? x = null)
    {
        this.nds = nds; this.floor = floor; this.match = match;
        nds.LoadState(seed);
        this.feet = floor * 24;
        if (x is { } at) hostX = at;
        for (int i = 0; i < 20; i++) Step(0);
        if (feet is { } start) this.feet = start;
    }
    /// <summary>The original inhale, fed content and swallow, as in the lab ability probe.</summary>
    public bool Transform(byte ability)
    {
        for (int i = 0; i < 20; i++) Step(2);
        if ((nds.ActionFlags & 1) == 0 || !nds.Feed(ability)) return false;
        Step(2); Step(0); Step(128);
        for (int i = 0; i < 180; i++) Step(0);
        return nds.State.Ability == ability;
    }
    /// <summary>Original actors other than Kirby, by kind, in host pixels.</summary>
    public List<(uint Node, byte Kind, double X, double Y)> Actors()
    {
        var list = new List<(uint, byte, double, double)>();
        var word = new byte[4];
        uint Read32(uint a) { nds.Read(a, word); return BitConverter.ToUInt32(word); }
        uint player = nds.State.Player;
        for (uint slot = 0; slot < 7; slot++)
            for (uint a = Read32(0x02049dec + slot * 4), guard = 0; a >= 0x02000000 && a < 0x02400000 && guard < 256; a = Read32(a), guard++)
                if (a != player)
                {
                    byte kind = (byte)Read32(a + 0x16);
                    list.Add((a, kind, (int)Read32(a + 0x80) / 65536.0 + ox * 24, (int)Read32(a + 0x84) / 65536.0 + oy * 24));
                }
        return list;
    }
    /// <summary>Fire's and Plasma's head emitters (effect particles in state 1): drawn position relative to Kirby, in pixels.</summary>
    public List<(double Dx, double Dy)> Emitters()
    {
        var list = new List<(double, double)>();
        var word = new byte[4];
        uint Read32(uint a) { nds.Read(a, word); return BitConverter.ToUInt32(word); }
        NdsState s = nds.State;
        for (uint slot = 0; slot < 7; slot++)
            for (uint a = Read32(0x02049dec + slot * 4), guard = 0; a >= 0x02000000 && a < 0x02400000 && guard < 256; a = Read32(a), guard++)
                if (Read32(a + 0x30) is 0x020CD588 or 0x020CE8FC && (byte)(Read32(a + 0x44) >> 8) == 1)
                    list.Add((((int)Read32(a + 0x78) - s.XRaw) / 65536.0, ((int)Read32(a + 0x7C) - s.YRaw) / 65536.0));
        return list;
    }
    ushort TileAt(int cx, int cy) => Tiles.TryGetValue((cx, cy), out var t) ? t : cy >= floor ? SpeedMatch.Solid : SpeedMatch.Free;
    public List<Row> Run(IEnumerable<ushort> plan) => plan.Select(Step).ToList();
    public Row Step(ushort raw)
    {
        double centerY = feet - 14.5;
        ox = (int)Math.Floor(hostX / 24) - 16; oy = (int)Math.Floor(centerY / 24) - 4;
        // KirbyNdsProvider.HoldWindow: nothing moves under a logic pass the original has not finished.
        if (nds.PassUnfinished && window is { } held) (ox, oy) = held;
        var cells = new ushort[900];
        for (int y = 0; y < 9; y++) for (int x = 0; x < 100; x++) cells[y * 100 + x] = TileAt(ox + x, oy + y);
        // KirbyNdsProvider.PlaceSource: the window moves every object by whole
        // cells; host movement then moves Kirby alone.
        int tx = checked((int)Math.Round((hostX - ox * 24) * 65536)), ty = checked((int)Math.Round((centerY - oy * 24) * 65536));
        NdsState before = nds.State;
        if (window is { } previous)
        {
            int fx = before.XRaw + (previous.X - ox) * 24 * 65536, fy = before.YRaw + (previous.Y - oy) * 24 * 65536;
            nds.World(cells, fx, fy);
            if ((Math.Abs(tx - fx) > 1 || Math.Abs(ty - fy) > 1) && !nds.PassUnfinished) nds.MovePlayer(tx - fx, ty - fy);
        }
        else nds.World(cells, tx, ty);
        window = (ox, oy);
        ushort action = nds.Action;
        int direction = (raw & 48) switch { 16 => 1, 32 => -1, _ => 0 };
        NdsState current = nds.State;
        Span<byte> value = stackalloc byte[2];
        if (!match) { }
        else if (SpeedMatch.Brakes(motionActions.Effective, direction, current.VxRaw))
        {
            value.Clear();
            nds.Write(current.Player + 0x88, value);
            nds.Write(current.Player + 0x98, value);
        }
        int rise = match ? SpeedMatch.RiseVelocity(action, current.VyRaw) : current.VyRaw;
        if (rise != current.VyRaw)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(value, (short)rise);
            nds.Write(current.Player + 0x8A, value);
        }
        double x0 = hostX, f0 = feet;
        // KirbyNdsProvider.PaceWalkCycle: the pose count at player+0x40 -> +0x6C8.
        if (match && action is KirbyActions.Walk or KirbyActions.Dash)
        {
            Span<byte> word = stackalloc byte[4];
            nds.Read(current.Player + 0x40, word);
            uint count = BitConverter.ToUInt32(word) + 0x6C8;
            nds.Read(count, word[..1]);
            byte next = walkCycle.Next(SpeedMatch.GroundScale, word[0]);
            if (next != word[0]) { word[0] = next; nds.Write(count, word[..1]); }
        }
        else walkCycle.Reset();
        if (MatchHelper) helper.Begin(nds);
        // KirbyNdsProvider.Step: a slide inside a roll-zone tunnel keeps rolling (#115).
        bool inside = Tunnel is { } tunnel && Math.Floor((hostX + 8) / 24) >= tunnel.From && Math.Floor((hostX - 8) / 24) <= tunnel.To;
        if (HoldSlides) slideHold.Begin(nds, action, lastVx, inside, false);
        nds.Step(dash.Filter(raw, true, lastVx, lastVy, action));
        if (HoldSlides) slideHold.Finish(nds);
        NdsState s = nds.State;
        lastVx = s.VxRaw; lastVy = s.VyRaw;
        int vx = slideHold.Holding && s.VxRaw != 0 ? Math.Sign(s.VxRaw) * Math.Max(Math.Abs((int)s.VxRaw), SlideHold.Velocity) : s.VxRaw;
        if (MatchHelper) helper.Finish(nds, ox, oy, false, TileAt);
        ushort after = nds.Action;
        ushort motion = motionActions.Next(after, s.VxRaw);
        (hostX, feet) = match ? SpeedMatch.Move(motion, motionActions.Direction(direction), vx, x0, f0, s.X + ox * 24, s.Y + oy * 24 + 14.5, TileAt)
            : (s.X + ox * 24, s.Y + oy * 24 + 14.5);
        return new Row(after, hostX, feet, s.VxRaw, s.VyRaw, motion);
    }
}
