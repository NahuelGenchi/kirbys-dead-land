using System.Collections;
using System.Reflection;
using Hashlink.Proxy.Clousre;
using Hashlink.Reflection.Types;
using Hashlink.Wrapper.Callbacks;
using ModCore.Modules;

namespace Kirby.Runtime;

/// <summary>
/// Takes DCCM's detour off host functions that have no handler left, and writes it again
/// before the next handler is added (#178).
/// <para>
/// DCCM 35.13.6 detours a hooked host function into .NET for the rest of the process:
/// <c>HashlinkHookManager.RemoveHook</c> only takes the handler out of its list, so every
/// later call still crosses into .NET and back, about 0.4 µs and 56 bytes of garbage a
/// call. The manager's <c>NativeHooks.HookHandle.Disable</c> undoes the detour, and the
/// function runs as it did before it was hooked; <c>Enable</c> writes it again. MonoMod
/// frees the trampoline to the original when it undoes a detour and makes a new one when
/// it applies it, so the manager's closure over the original is made again from the new
/// address, as its constructor makes it.
/// </para>
/// <para>
/// The callback's own route back to the original (<c>HlCallback.RedirectTarget</c>), which
/// the manager's constructor sets and its first <c>AddHook</c> clears before any call can
/// take it, is left alone: turned back on, it crashed the game in 3 of 4 lab runs. The
/// manager's fields are private and are found by reflection; when they are not there,
/// nothing changes.
/// </para>
/// </summary>
internal static class DccmDetours
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type? Manager = typeof(HashlinkHooks).Assembly.GetType("ModCore.Hooks.HashlinkHookManager");
    private static readonly FieldInfo? Managers = typeof(HashlinkHooks).GetField("managers", Members);
    private static readonly FieldInfo? Handlers = Manager?.GetField("hooks", Members);
    private static readonly FieldInfo? Callback = Manager?.GetField("callback", Members);
    private static readonly FieldInfo? Detour = Manager?.GetField("hook", Members);
    private static readonly FieldInfo? Function = Manager?.GetField("function", Members);
    private static readonly FieldInfo? FuncType = Manager?.GetField("funcType", Members);
    private static readonly FieldInfo? Original = Manager?.GetField("origClosure", Members);
    // Managers whose detour this class undid and has not written again.
    private static readonly HashSet<object> detached = new(ReferenceEqualityComparer.Instance);
    private static bool missing, logged;

    /// <summary>
    /// Undo the detour of each named host function (as <c>en.Mob.canUpdate</c>) that has
    /// no handler left. Call it between host frames, never from inside one of them.
    /// </summary>
    internal static int Detach(params string[] functions)
    {
        int count = 0;
        foreach (var (manager, handle) in Find(functions))
        {
            if (detached.Contains(manager) || Handlers!.GetValue(manager) is not ICollection { Count: 0 } ||
                // A HarmonyX processor takes the place of DCCM's adapter and must keep its calls.
                (Callback!.GetValue(manager) as HlCallback)?.Target?.Method.Name.StartsWith("hook_adapt_", StringComparison.Ordinal) != true)
                continue;
            handle.Disable();
            detached.Add(manager);
            count++;
        }
        if (!logged && count > 0)
        {
            KirbyNdsProvider.Log?.Invoke($"DCCM detour taken off {count} of {functions.Length} unhooked host functions.");
            logged = true;
        }
        return count;
    }

    /// <summary>
    /// Write again the detour of each named host function that <see cref="Detach"/> undid,
    /// so that the next handler added to it is called. Call it before adding that handler.
    /// </summary>
    internal static void Attach(params string[] functions)
    {
        if (detached.Count == 0) return;
        foreach (var (manager, handle) in Find(functions))
        {
            if (!detached.Remove(manager)) continue;
            handle.Enable();
            Original!.SetValue(manager, new HashlinkClosure((HashlinkFuncType)FuncType!.GetValue(manager)!, handle.Original, 0));
        }
    }

    private static IEnumerable<(object Manager, NativeHooks.HookHandle Handle)> Find(string[] functions)
    {
        if (Managers?.GetValue(HashlinkHooks.Instance) is not IDictionary managers || Handlers is null ||
            Callback is null || Detour is null || Function is null || FuncType is null || Original is null)
        {
            if (!missing) KirbyNdsProvider.Log?.Invoke("DCCM's hook managers were not found; unhooked host functions keep their detour.");
            missing = true;
            yield break;
        }
        foreach (object manager in managers.Values)
            if (Function.GetValue(manager) is Hashlink.Reflection.Members.HashlinkFunction function &&
                Array.IndexOf(functions, $"{function.DeclaringType}.{function.Name}") >= 0 &&
                Detour.GetValue(manager) is NativeHooks.HookHandle handle)
                yield return (manager, handle);
    }
}
