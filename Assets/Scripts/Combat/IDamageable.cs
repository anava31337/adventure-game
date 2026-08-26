// =============================================================================
// IDamageable.cs   —   Assets/Scripts/Combat/
//
// The two contracts of the combat system.
//
//   IDamageable — anything that can be hurt: players, enemies, hostile NPCs,
//                 destructible crates, breakable walls, burnable bushes.
//   IDealDamage — anything that hurts: swords, arrows, whips, enemy bodies,
//                 spike traps.
//
// A hit is always a DIRECT call from dealer to target, using the collider the
// physics system already handed us. Nothing is broadcast to uninvolved objects.
// =============================================================================

using UnityEngine;

/// <summary>Implemented by anything that can receive damage.</summary>
public interface IDamageable
{
    /// <summary>Apply a hit. The implementer decides resistances, effects, death.</summary>
    void TakeDamage(DamageInfo info);

    /// <summary>False when already dead/destroyed so dealers can skip it.</summary>
    bool IsAlive { get; }

    /// <summary>The GameObject being damaged (for effects, knockback, lookups).</summary>
    GameObject DamageableObject { get; }
}

/// <summary>Implemented by anything that produces damage.</summary>
public interface IDealDamage
{
    /// <summary>
    /// Builds the payload for one hit. Called per-strike so modifiers
    /// (enchantments, buffs, charge level) can be folded in at the moment of impact.
    /// </summary>
    DamageInfo BuildDamage();
}
