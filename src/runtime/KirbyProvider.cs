using dc.en;
using dc.tool;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Contract for the source-accurate controller port. No implementation is bundled:
/// presenting Dead Cells physics or substitute weapons as NDS gameplay is incorrect.
/// Providers must run on the game thread and validate their source timing/physics.
/// </summary>
public interface IKirbyGameplayProvider
{
    IReadOnlyCollection<string> ImplementedAbilityIds { get; }
    void OnActivated(KirbySession session);
    void OnDeactivated(KirbySession session);
    void OnFrame(KirbySession session, double engineDelta);
    void OnFixedUpdate(KirbySession session, Action originalHeroUpdate);
    bool TryAbilityAttack(KirbySession session, string abilityId, Weapon inputWeapon);
    void OnAbilityChanged(KirbySession session, string? abilityId);
}

/// <summary>Host state available to a validated controller and ability port.</summary>
public sealed class KirbySession
{
    private readonly IKirbyGameplayProvider provider;
    internal KirbySession(Hero hero, IKirbyGameplayProvider provider)
    {
        Hero = hero;
        this.provider = provider;
        Rules = new KirbyRules(provider.ImplementedAbilityIds);
    }

    public Hero Hero { get; }
    public KirbyRules Rules { get; }

    /// <summary>
    /// Invoke only at the end of the original inhale/swallow sequence, after
    /// consumeEnemy has removed this exact live enemy through the host lifecycle.
    /// Ordinary enemy death never calls this method. No copy is awarded on failure.
    /// </summary>
    public string? CompleteAbsorption(Mob enemy, Func<bool> consumeEnemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(consumeEnemy);
        if (!Rules.IsBaseForm || enemy.destroyed || enemy.life <= 0 ||
            enemy._level.HashlinkPointer != Hero._level.HashlinkPointer)
            return null;
        long enemyId = enemy.__uid;
        if (!consumeEnemy() || (!enemy.destroyed && enemy.life > 0))
            return null;
        string? ability = Rules.Absorb(enemyId);
        if (ability is not null)
            provider.OnAbilityChanged(this, ability);
        return ability;
    }

    public void DropAbility()
    {
        Rules.DropAbility();
        provider.OnAbilityChanged(this, null);
    }
}
