namespace Kirby.Core;

/// <summary>
/// Limited-use copy abilities (#102): Paint, Mike, Crash and Cook. They are never
/// swallowed. Their floor ability item is taken with the host Interact key into
/// the F slot, and F fires the original attack while Kirby keeps his main copy.
/// The original gives Mike three shouts and the others one use.
/// </summary>
public static class LimitedAbilities
{
    public static IReadOnlyList<string> Ids { get; } = ["Paint", "Mike", "Crash", "Cook"];

    public static bool IsLimited(string? abilityId) => abilityId is not null && Ids.Contains(abilityId, StringComparer.Ordinal);

    /// <summary>Uses of a newly taken ability: Mike's three shouts, one for the others.</summary>
    public static int Uses(string abilityId) => abilityId == "Mike" ? 3 : 1;
}

/// <summary>The F slot: at most one limited-use ability and its remaining uses.</summary>
public sealed class FSlot
{
    public string? Ability { get; private set; }
    public int Uses { get; private set; }
    public bool IsEmpty => Ability is null;

    /// <summary>Take a limited ability with its full uses; returns the ability it replaced, if any.</summary>
    public string? Take(string abilityId)
    {
        if (!LimitedAbilities.IsLimited(abilityId))
            throw new ArgumentException("Only a limited-use ability goes in the F slot.", nameof(abilityId));
        string? replaced = Ability;
        Ability = abilityId;
        Uses = LimitedAbilities.Uses(abilityId);
        return replaced;
    }

    /// <summary>One use was fired; the slot empties after the last one. Returns true when it emptied.</summary>
    public bool Spend()
    {
        if (Ability is null) return false;
        if (--Uses > 0) return false;
        Clear();
        return true;
    }

    /// <summary>Restore saved contents after validating them.</summary>
    public void Restore(string? abilityId, int uses)
    {
        if (abilityId is null) { Clear(); return; }
        if (!LimitedAbilities.IsLimited(abilityId) || uses < 1 || uses > LimitedAbilities.Uses(abilityId))
            throw new ArgumentException("Saved F slot does not hold a valid limited-use ability.", nameof(abilityId));
        Ability = abilityId;
        Uses = uses;
    }

    public void Clear()
    {
        Ability = null;
        Uses = 0;
    }
}

public enum FStep { Idle, Waiting, Switching, Firing, Attacking, Restoring }

/// <summary>A saved F press: its step, abilities and whether the attack already ran.</summary>
public readonly record struct FProgress(FStep Step, byte Stored, byte Main, bool RestoreRequested, bool Used);

/// <summary>What the runtime does for the source frame about to run.</summary>
public readonly record struct FCommand(bool PressAttack, bool LimitShouts, FOutcome? Finished);

/// <summary>How an F press ended: whether the attack ran, and whether the main copy came back.</summary>
public readonly record struct FOutcome(bool Used, bool MainRestored);

/// <summary>
/// One F press, driven through the original machine (#102). The ROM's own
/// ability routine switches Kirby to the stored ability (no inhale or swallow),
/// the attack button fires it once, the ROM returns Kirby to base form after
/// that use, and the same routine gives his main copy back. Ability IDs are the
/// original bytes: 1 is base form.
/// </summary>
public sealed class FSequence
{
    public const byte BaseForm = 1;
    /// <summary>Source frames an F press waits for Kirby to be free (standing, walking, airborne or swimming).</summary>
    public const int WaitLimit = 30;
    /// <summary>Frames the attack button is held to fire the stored ability.</summary>
    public const int FireFrames = 3;
    /// <summary>Frames allowed for each switch; the original loads its graphics in 2–5.</summary>
    public const int SwitchLimit = 60;
    /// <summary>Safety limit for one stored attack; the longest original one (Cook) takes 445 frames.</summary>
    public const int AttackLimit = 1200;
    /// <summary>Frames the main copy may wait for Kirby to be free again, for example after a hit.</summary>
    public const int RestoreLimit = 600;
    /// <summary>
    /// Frames Kirby may stand free still holding the stored ability before the
    /// attack button is pressed again: a hit's recoil can swallow the first press.
    /// </summary>
    public const int RetryAfter = 15;
    public const int MaxRetries = 3;

    private int frames, freeFrames, retries;
    private bool restoreRequested, used;

    public FStep Step { get; private set; }
    public byte Stored { get; private set; }
    public byte Main { get; private set; } = BaseForm;
    public bool Active => Step != FStep.Idle;

    /// <summary>Start an F press for a stored ability; <paramref name="main"/> is the copy to give back (1 for none).</summary>
    public bool Begin(byte stored, byte main)
    {
        if (Active || stored <= BaseForm || stored > 25 || main < BaseForm || main > 25) return false;
        Stored = stored;
        Main = main;
        Step = FStep.Waiting;
        frames = freeFrames = retries = 0;
        restoreRequested = used = false;
        return true;
    }

    /// <summary>
    /// Advance one source frame. <paramref name="ability"/> is the original copy
    /// byte before the frame, <paramref name="changePending"/> whether a requested
    /// switch is still loading, <paramref name="free"/> whether Kirby stands,
    /// walks, is airborne or swims (not attacking, hurt or holding a mouthful), and
    /// <paramref name="requestChange"/> asks the bridge for a switch, returning
    /// false while the original refuses one.
    /// </summary>
    public FCommand Advance(byte ability, bool changePending, bool free, Func<byte, bool> requestChange)
    {
        ArgumentNullException.ThrowIfNull(requestChange);
        switch (Step)
        {
            case FStep.Waiting:
                if (requestChange(Stored)) { Step = FStep.Switching; frames = 0; }
                else if (++frames >= WaitLimit) return Finish(new FOutcome(false, true));
                return default;
            case FStep.Switching:
                if (!changePending && ability == Stored)
                {
                    Step = FStep.Firing; frames = 1;
                    return new FCommand(true, Stored == Mike, null);
                }
                // The switch did not take: nothing fired, give the main copy back.
                if (++frames >= SwitchLimit || (!changePending && frames > 1 && ability != Stored)) BeginRestore();
                return default;
            case FStep.Firing:
                if (++frames <= FireFrames) return new FCommand(true, false, null);
                Step = FStep.Attacking; frames = freeFrames = 0;
                goto case FStep.Attacking;
            case FStep.Attacking:
                // The original returns Kirby to base form after the use.
                if (ability != Stored) { used = true; BeginRestore(); goto case FStep.Restoring; }
                // Still holding it and free: the press was lost (a hit's recoil). Press again,
                // then give up and switch straight back, keeping the use.
                freeFrames = free ? freeFrames + 1 : 0;
                if (freeFrames >= RetryAfter)
                {
                    if (retries++ < MaxRetries) { Step = FStep.Firing; frames = 1; return new FCommand(true, false, null); }
                    BeginRestore();
                    goto case FStep.Restoring;
                }
                if (++frames >= AttackLimit) { used = true; BeginRestore(); }
                return default;
            case FStep.Restoring:
                if (!changePending && ability == Main) return Finish(new FOutcome(used, true));
                if (!restoreRequested || !changePending && frames > 1)
                {
                    // The bridge refuses while Kirby attacks or recoils; the original
                    // routine changes any copy, including a stored one still held.
                    if (requestChange(Main)) { restoreRequested = true; frames = 0; }
                    else if (++frames >= RestoreLimit) return Finish(new FOutcome(used, false));
                    return default;
                }
                ++frames;
                return default;
            default:
                return default;
        }
    }

    /// <summary>Where an F press stands, for a save taken during it.</summary>
    public FProgress Capture() => new(Step, Stored, Main, restoreRequested, used);

    /// <summary>
    /// Continue an F press from a save. A press still waiting is dropped (nothing
    /// happened yet); a switch or restore that was loading continues, as the
    /// bridge keeps its pending call in the machine snapshot.
    /// </summary>
    public void Resume(FProgress progress)
    {
        Reset();
        if (progress.Step is FStep.Idle or FStep.Waiting || progress.Stored <= BaseForm || progress.Stored > 25 ||
            progress.Main < BaseForm || progress.Main > 25) return;
        Step = progress.Step;
        Stored = progress.Stored;
        Main = progress.Main;
        restoreRequested = progress.RestoreRequested;
        used = progress.Used;
    }

    /// <summary>Clear an F press without a result, for example when the source machine is reloaded.</summary>
    public void Reset()
    {
        Step = FStep.Idle;
        frames = freeFrames = retries = 0;
        restoreRequested = used = false;
        Stored = 0;
        Main = BaseForm;
    }

    private const byte Mike = 22;

    private void BeginRestore()
    {
        Step = FStep.Restoring;
        frames = 0;
        restoreRequested = false;
    }

    private FCommand Finish(FOutcome outcome)
    {
        Reset();
        return new FCommand(false, false, outcome);
    }
}
