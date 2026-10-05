using dc.en;
using dc.tool;
using Kirby.Core;

namespace Kirby.Runtime;

/// <summary>
/// Boundary between the host lifecycle and original-ROM controller.
/// Providers run on the game thread and retain source timing and physics.
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
    /// Invoke only when original suction has placed an enemy in Kirby's mouth, after
    /// consumeEnemy has removed this exact live enemy through the host lifecycle.
    /// Ordinary enemy death never calls this method. The selected contents are
    /// passed to the ROM; the current form changes only on its swallow transition.
    /// An Elite's ability empowers Kirby if that transition produces it.
    /// </summary>
    public string? CompleteAbsorption(Mob enemy, string abilityId, Func<bool> consumeEnemy)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(consumeEnemy);
        if (!Rules.IsBaseForm || enemy.destroyed || enemy.life <= 0 ||
            enemy._level.HashlinkPointer != Hero._level.HashlinkPointer)
            return null;
        long enemyId = enemy.__uid;
        bool elite = enemy.elite;
        if (!consumeEnemy() || (!enemy.destroyed && enemy.life > 0))
            return null;
        string? ability = Rules.ConfirmAbsorption(enemyId, abilityId, elite);
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
