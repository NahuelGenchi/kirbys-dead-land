namespace Kirby.Runtime;

/// <summary>Opt-in offscreen diagnostic milestones; normal play performs no file writes.</summary>
internal static class LabActivationTrace
{
    // The machine may be opened on a worker thread while the game thread marks too (#193).
    private static readonly object Writing = new();
    /// <summary>HashLink's collection count and memory, set by the lab's perf probe when it reads them.</summary>
    internal static Func<string>? HashlinkGc { get; set; }

    /// <summary>
    /// <paramref name="gc"/> adds HashLink's collection count and memory, read only
    /// on the game thread.
    /// </summary>
    internal static void Mark(string stage, bool gc = false)
    {
        string? root = Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT");
        if (root is null || Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") == "1") return;
        if (gc) stage += HashlinkGc?.Invoke();
        lock (Writing)
            System.IO.File.AppendAllText(Path.Combine(root, "activation.txt"),
                $"{DateTime.UtcNow:O} thread{Environment.CurrentManagedThreadId}: {stage}\n");
    }
}
