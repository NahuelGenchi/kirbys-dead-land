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
    private readonly HashSet<long> cookedEnemies = [];

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

    /// <summary>Copy damage multiplier while an Elite-granted ability is held.</summary>
    public const double EmpoweredDamageMultiplier = 2;

    public string? AbilityId { get; private set; }
    public bool IsBaseForm => AbilityId is null;
    /// <summary>The current copy came from swallowing an Elite and has not been lost since.</summary>
    public bool AbilityEmpowered { get; private set; }
    /// <summary>An Elite is in Kirby's mouth; its ability empowers Kirby only if the swallow yields it.</summary>
    public string? PendingEmpowerment { get; private set; }
    public double DamageMultiplier => AbilityEmpowered ? EmpoweredDamageMultiplier : 1;
    public string? RetainedShieldId { get; private set; }

    /// <summary>
    /// A shield can be taken in any form, as in Dead Cells: Take swaps it for
    /// the equipped one. Swords and bows need base form.
    /// </summary>
    public bool CanEquip(HostWeaponKind kind) => kind == HostWeaponKind.Shield ||
        IsBaseForm && kind is HostWeaponKind.Sword or HostWeaponKind.Bow;

    /// <summary>
    /// Record the stable ID of the shield the host actually equipped, never its
    /// display name. In copied form a newly taken shield replaces the retained one.
    /// </summary>
    public void EquipShield(string? id)
    {
        if (id is not null && string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Use null for no shield.", nameof(id));
        RetainedShieldId = id;
    }

    /// <summary>
    /// Follow the host's equipped shields: keep the retained shield while it is
    /// still equipped, otherwise retain the first equipped shield, or none.
    /// </summary>
    public void FollowEquippedShields(IEnumerable<string> equippedIds)
    {
        ArgumentNullException.ThrowIfNull(equippedIds);
        string[] ids = equippedIds.ToArray();
        if (RetainedShieldId is null || !ids.Contains(RetainedShieldId, StringComparer.Ordinal))
            EquipShield(ids.FirstOrDefault());
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

    /// <summary>
    /// Record an actual capture whose source enemy already carried this ability.
    /// An Elite capture marks its ability as a pending empowerment.
    /// </summary>
    public string? ConfirmAbsorption(long enemyId, string abilityId, bool elite = false)
    {
        if (!abilityIds.Contains(abilityId, StringComparer.Ordinal))
            throw new ArgumentException("Absorbed ability is not in the original catalog.", nameof(abilityId));
        if (!IsBaseForm || !consumedEnemies.Add(enemyId)) return null;
        if (elite) PendingEmpowerment = abilityId;
        return abilityId;
    }

    /// <summary>Losing the copy in any way (hit, discard, helper, spit) ends the empowerment.</summary>
    public void DropAbility()
    {
        AbilityId = null;
        AbilityEmpowered = false;
        PendingEmpowerment = null;
    }

    /// <summary>
    /// Decide whether an enemy actually consumed by the original Cook drops one
    /// host food item. Bosses and Elites never do, and repeated notifications
    /// for one entity cannot drop a second item. Cook damage is unaffected.
    /// </summary>
    public bool ClaimCookFood(long enemyId, bool boss, bool elite) =>
        !boss && !elite && cookedEnemies.Add(enemyId);

    /// <summary>
    /// Mirror a source transformation, including re-catching a discarded copy star.
    /// Only the swallow of a pending Elite empowers, and only if the original
    /// transform produced that Elite's ability; a re-caught star is never empowered.
    /// </summary>
    public void SetSourceAbility(string? abilityId)
    {
        if (abilityId is not null && !abilityIds.Contains(abilityId, StringComparer.Ordinal))
            throw new ArgumentException("Source copy ability is not in the original catalog.", nameof(abilityId));
        AbilityEmpowered = abilityId is not null && (PendingEmpowerment is not null
            ? StringComparer.Ordinal.Equals(PendingEmpowerment, abilityId)
            : AbilityEmpowered && StringComparer.Ordinal.Equals(AbilityId, abilityId));
        PendingEmpowerment = null;
        AbilityId = abilityId;
    }

    /// <summary>Restore this mod's slot state after validating its ability catalog.</summary>
    public void Restore(string? abilityId, string? retainedShieldId, bool empowered = false, string? pendingEmpowerment = null)
    {
        if (abilityId is not null && !abilityIds.Contains(abilityId, StringComparer.Ordinal))
            throw new ArgumentException("Saved copy ability is not in the original catalog.", nameof(abilityId));
        if (retainedShieldId is not null && string.IsNullOrWhiteSpace(retainedShieldId))
            throw new ArgumentException("Saved shield must identify a physical item.", nameof(retainedShieldId));
        if (empowered && abilityId is null)
            throw new ArgumentException("Only a copy ability can be empowered.", nameof(empowered));
        if (pendingEmpowerment is not null && (abilityId is not null ||
            !abilityIds.Contains(pendingEmpowerment, StringComparer.Ordinal)))
            throw new ArgumentException("A pending empowerment needs base form and a catalog ability.", nameof(pendingEmpowerment));
        AbilityId = abilityId;
        RetainedShieldId = retainedShieldId;
        AbilityEmpowered = empowered;
        PendingEmpowerment = pendingEmpowerment;
        consumedEnemies.Clear();
        cookedEnemies.Clear();
    }

    /// <summary>Entity IDs can be reused in a different level; equipment and copy state survive.</summary>
    public void ResetForLevel()
    {
        consumedEnemies.Clear();
        cookedEnemies.Clear();
    }

    public void Reset()
    {
        AbilityId = null;
        AbilityEmpowered = false;
        PendingEmpowerment = null;
        RetainedShieldId = null;
        consumedEnemies.Clear();
        cookedEnemies.Clear();
    }
}
