// =============================================================================
// Enemy.cs   —   Assets/Scripts/Enemies/
//
// An enemy is just an AbstractCharacter. All damage RECEIVING — resistances,
// invulnerability, knockback, knockdown, hit flash, death, and the HP-bar
// broadcast — lives in AbstractCharacter, so there is no damage code here and
// NPCs get identical behaviour for free.
//
// DEALING damage is deliberately OPT-IN. An enemy is not inherently dangerous:
// a passive critter, a fleeing creature, or a purely decorative one should be
// able to exist without hurting anything. You compose danger by adding the
// piece you want:
//
//   CONTACT — add a ContactDamage component to hurt the player on touch.
//             Lower damage, a light knockback nudge, no knockdown.
//             Use for most ordinary enemies.
//
//   ATTACK  — add a child "WeaponHitBox" GameObject with a collider and a
//             DamageDealer, enabled by the attack animation frames. Higher
//             damage, little or no knockback, and knockdownDuration > 0 for
//             heavy or specialised enemies. The owner is found automatically
//             from this parent, so nothing to assign.
//
// An enemy can have neither (harmless), either, or both (it hurts on touch AND
// swings a weapon). The same two components work on hostile NPCs and on
// environmental hazards, so there is one damage vocabulary across the game.
//
// A knockdown consumes the player's post-hit invulnerability, so heavy blows
// genuinely leave them exposed instead of safe.
// =============================================================================

using UnityEngine;

public class Enemy : AbstractCharacter
{
    private void Start()
    {
        base.Start();
        this.SetLevelData(1);
        this.Hp        = this.MaxHp;
        this.Mp        = this.MaxMp;
        this.Endurance = this.MaxEndurance;

        // Publish full health so the HP bar shows the correct sprite from the
        // moment this character exists. Without it a bar only ever updates on
        // the first hit, so spawned creatures start out looking empty.
        RefreshHealthDisplay();
    }
}
