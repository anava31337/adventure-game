// =============================================================================
// CombatRules.cs   —   Assets/Scripts/Combat/
//
// One place that answers: "is this attacker allowed to damage that target?"
//
// Keeping it here means the sword, the whip, arrows, enemy bodies, and enemy
// weapons all agree on who can hurt whom, instead of each re-implementing the
// check slightly differently.
//
// Rules:
//   • Nothing damages itself or its own wielder.
//   • The player damages enemies, and NPCs only while they are hostile.
//   • Enemies and hostile NPCs damage the player.
//   • Enemies don't damage each other (change here if you ever want friendly fire).
// =============================================================================

using UnityEngine;

public static class CombatRules
{
    /// <summary>True if `attacker` is allowed to deal damage to `target`.</summary>
    public static bool CanDamage(AbstractCharacter attacker, AbstractCharacter target)
    {
        if (target == null) return false;
        if (attacker != null && attacker == target) return false;   // never self

        bool attackerIsPlayer = attacker is Player;
        bool targetIsPlayer   = target   is Player;

        // Player (and anything the player fired) hits enemies and hostile NPCs
        if (attackerIsPlayer)
            return target is Enemy || IsHostileNPC(target);

        // Enemies and hostile NPCs hit the player
        if (attacker is Enemy || IsHostileNPC(attacker))
            return targetIsPlayer;

        // Unowned damage (traps, environmental hazards) hits anyone
        if (attacker == null) return true;

        return false;
    }

    /// <summary>True if this character is an NPC currently flagged hostile.</summary>
    public static bool IsHostileNPC(AbstractCharacter c)
    {
        var npc = c as NPC;
        return npc != null && npc.isHostile;
    }

    /// <summary>
    /// Finds the IDamageable on a collider (or its parents) and checks whether
    /// `attacker` may damage it. Returns null when the hit isn't allowed.
    /// </summary>
    public static IDamageable ResolveTarget(Collider2D col, AbstractCharacter attacker)
    {
        if (col == null) return null;

        var target = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
        if (target == null || !target.IsAlive) return null;

        var targetChar = target.DamageableObject.GetComponent<AbstractCharacter>();
        if (targetChar == null) return target;          // destructible object, not a character

        return CanDamage(attacker, targetChar) ? target : null;
    }
}
