namespace Kirby.Runtime;

/// <summary>Opt-in offscreen diagnostic milestones; normal play performs no file writes.</summary>
internal static class LabActivationTrace
{
    internal static void Mark(string stage)
    {
        string? root = Environment.GetEnvironmentVariable("KIRBY_LAB_RUN_ROOT");
        if (root is null || Environment.GetEnvironmentVariable("KIRBY_LAB_PLAY") == "1") return;
        System.IO.File.AppendAllText(Path.Combine(root, "activation.txt"),
            $"{DateTime.UtcNow:O} thread{Environment.CurrentManagedThreadId}: {stage}\n");
    }
}
