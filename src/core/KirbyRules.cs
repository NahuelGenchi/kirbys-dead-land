namespace Kirby.Core;

public enum HostWeaponKind { None, Sword, Bow, Shield, Other }
public enum AttackRoute { None, HostWeapon, Ability, Parry }

/// <summary>
/// The requested changes to Kirby's rules, independent of either game's engine.
/// This is not a Kirby physics or ability-moveset reimplementation.
/// Absorb must only be called after the engine has confirmed an actual absorption.
/// </summary>
public sealed class KirbyRules
{
    private readonly string[] abilityIds;
    private readonly Func<int, int> chooseIndex;
    private readonly HashSet<long> consumedEnemies = [];

    public KirbyRules(IEnumerable<string> abilityIds, Func<int, int>? chooseIndex = null)
    {
        ArgumentNullException.ThrowIfNull(abilityIds);
        this.abilityIds = abilityIds.ToArray();
        if (this.abilityIds.Length == 0 ||
            this.abilityIds.Any(string.IsNullOrWhiteSpace) ||
            this.abilityIds.Distinct(StringComparer.Ordinal).Count() != this.abilityIds.Length)
            throw new ArgumentException("A nonempty set of unique ability IDs is required.", nameof(abilityIds));
        this.chooseIndex = chooseIndex ?? Random.Shared.Next;
    }

    public string? AbilityId { get; private set; }
    public bool IsBaseForm => AbilityId is null;
    public string? RetainedShieldId { get; private set; }

    public bool CanEquip(HostWeaponKind kind) => IsBaseForm &&
        kind is HostWeaponKind.Sword or HostWeaponKind.Bow or HostWeaponKind.Shield;

    /// <summary>Record the stable ID of the actual equipped shield, never its display name.</summary>
    public bool EquipShield(string? id)
    {
        if (id is not null && string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Use null for no shield.", nameof(id));
        if (!IsBaseForm)
            return StringComparer.Ordinal.Equals(RetainedShieldId, id);
        RetainedShieldId = id;
        return true;
    }

    public bool CanUse(HostWeaponKind kind, string? itemId = null)
    {
        if (IsBaseForm)
            return kind is HostWeaponKind.Sword or HostWeaponKind.Bow or HostWeaponKind.Shield;
        return kind == HostWeaponKind.Shield && RetainedShieldId is not null &&
               StringComparer.Ordinal.Equals(itemId, RetainedShieldId);
    }

    public AttackRoute ResolveAttack(HostWeaponKind kind, string? itemId = null)
    {
        if (kind == HostWeaponKind.Shield)
            return CanUse(kind, itemId) ? AttackRoute.Parry : AttackRoute.None;
        if (!IsBaseForm)
            return AttackRoute.Ability;
        return CanUse(kind, itemId) ? AttackRoute.HostWeapon : AttackRoute.None;
    }

    /// <summary>
    /// Complete a confirmed absorption and choose one copy ability uniformly when
    /// using the default RNG. Repeated notifications cannot reroll one enemy.
    /// Kirby must drop a copy ability before inhaling again, as in the source game.
    /// </summary>
    public string? Absorb(long enemyId)
    {
        if (!IsBaseForm || consumedEnemies.Contains(enemyId))
            return null;
        int index = chooseIndex(abilityIds.Length);
        if ((uint)index >= (uint)abilityIds.Length)
            throw new InvalidOperationException("Ability selector returned an out-of-range index.");
        consumedEnemies.Add(enemyId);
        AbilityId = abilityIds[index];
        return AbilityId;
    }

    public void DropAbility() => AbilityId = null;

    /// <summary>Entity IDs can be reused in a different level; equipment and copy state survive.</summary>
    public void ResetForLevel() => consumedEnemies.Clear();

    public void Reset()
    {
        AbilityId = null;
        RetainedShieldId = null;
        consumedEnemies.Clear();
    }
}
