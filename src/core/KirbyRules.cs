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
    /// Choose the contents of a confirmed absorption uniformly. Form changes
    /// only when SetSourceAbility observes the original swallow/transform state;
    /// multiple enemies may enter one mouthful before that happens. Repeated
    /// notifications cannot reroll one enemy.
    /// </summary>
    public string? Absorb(long enemyId)
    {
        if (!IsBaseForm || consumedEnemies.Contains(enemyId))
            return null;
        return ConfirmAbsorption(enemyId, SelectCopyAbility());
    }

    /// <summary>Assign a stable random source ability to a live enemy before it is inhaled.</summary>
    public string SelectCopyAbility()
    {
        int index = chooseIndex(abilityIds.Length);
        if ((uint)index >= (uint)abilityIds.Length)
            throw new InvalidOperationException("Ability selector returned an out-of-range index.");
        return abilityIds[index];
    }

    /// <summary>Record an actual capture whose source enemy already carried this ability.</summary>
    public string? ConfirmAbsorption(long enemyId, string abilityId)
    {
        if (!abilityIds.Contains(abilityId, StringComparer.Ordinal))
            throw new ArgumentException("Absorbed ability is not in the original catalog.", nameof(abilityId));
        if (!IsBaseForm || !consumedEnemies.Add(enemyId)) return null;
        return abilityId;
    }

    public void DropAbility() => AbilityId = null;

    /// <summary>Mirror a source transformation, including re-catching a discarded copy star.</summary>
    public void SetSourceAbility(string? abilityId)
    {
        if (abilityId is not null && !abilityIds.Contains(abilityId, StringComparer.Ordinal))
            throw new ArgumentException("Source copy ability is not in the original catalog.", nameof(abilityId));
        AbilityId = abilityId;
    }

    /// <summary>Restore this mod's slot state after validating its ability catalog.</summary>
    public void Restore(string? abilityId, string? retainedShieldId)
    {
        if (abilityId is not null && !abilityIds.Contains(abilityId, StringComparer.Ordinal))
            throw new ArgumentException("Saved copy ability is not in the original catalog.", nameof(abilityId));
        if (retainedShieldId is not null && string.IsNullOrWhiteSpace(retainedShieldId))
            throw new ArgumentException("Saved shield must identify a physical item.", nameof(retainedShieldId));
        AbilityId = abilityId;
        RetainedShieldId = retainedShieldId;
        consumedEnemies.Clear();
    }

    /// <summary>Entity IDs can be reused in a different level; equipment and copy state survive.</summary>
    public void ResetForLevel() => consumedEnemies.Clear();

    public void Reset()
    {
        AbilityId = null;
        RetainedShieldId = null;
        consumedEnemies.Clear();
    }
}
