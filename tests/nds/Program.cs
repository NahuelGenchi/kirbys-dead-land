using Kirby.Runtime;
using System.Runtime.InteropServices;
if(args.Length!=3) throw new ArgumentException("native-library ROM private-seed-state");
if(Marshal.SizeOf<NdsState>()!=100 || Marshal.SizeOf<NdsHit>()!=52) throw new Exception("Native ABI sizes changed");
using var nds=new NdsRuntime(args[0],args[1],args[2]);
NdsState initial=nds.State;
if(initial.Kind!=56 || initial.Ability!=1) throw new Exception("Seed is not base Kirby in gameplay");
nds.Step(0,60);
NdsState idle=nds.State;
if(idle.XRaw!=initial.XRaw || idle.YRaw!=initial.YRaw) throw new Exception("Seed is not stationary");
nds.Step(16,20);
NdsState walking=nds.State;
if(walking.XRaw-idle.XRaw!=1646592 || walking.VxRaw!=352) throw new Exception("Original walking trajectory changed");
Span<byte> read=stackalloc byte[8];nds.Read(walking.Player+0x80,read);
if(BitConverter.ToInt32(read)!=walking.XRaw) throw new Exception("RAM/struct ABI mismatch");
try {nds.Read(0x01ffffff,read);throw new Exception("Out-of-bounds read accepted");} catch(InvalidOperationException){}
Span<NdsHit> hits=stackalloc NdsHit[256];int count=nds.ReadHits(hits);
if(count<1)throw new Exception("Original live collision graph is empty");
Console.WriteLine($"Native ABI, seed, deterministic original motion, RAM bounds and {count} live hits verified.");
