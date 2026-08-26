// =============================================================================
// PlayerSword.cs   —   Assets/Scripts/Items/Weapons/
//
// The player's melee hitbox. Goes on the SwordHitBox child of the Player —
// the collider your animation enables during the AttackLeft / AttackRight frames.
//
// It is simply a DamageDealer with a sword-flavoured name, so it shares the exact
// same hit pipeline as enemy weapons, arrows, and contact damage. There is no
// `owner` field to set: the owner is found from the parent, which for the
// SwordHitBox is always the Player.
//
// When the sword eventually becomes its own sprite on a child GameObject, nothing
// here needs to change — the hitbox just moves with the art.
//
// Enchantments (a spell that sets the blade alight) are applied at runtime:
//     sword.EnchantWith(DamageType.Fire, 3);
//     sword.ClearEnchantments();
// =============================================================================

using UnityEngine;

public class PlayerSword : DamageDealer
{
    protected override void Awake()
    {
        base.Awake();   // finds the Player as owner via the parent hierarchy

        // A swung weapon should strike once per swing, not tick continuously.
        damageOnStay = false;
    }

    /// <summary>Temporarily add typed damage to the blade — e.g. a fire enchantment.</summary>
    public void EnchantWith(DamageType type, int amount) => AddBonusDamage(type, amount);

    /// <summary>Remove all temporary enchantments from the blade.</summary>
    public void ClearEnchantments() => ClearBonusDamage();
}
