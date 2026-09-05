// =============================================================================
// ContactDamage.cs   —   Assets/Scripts/Combat/
//
// Passive "you walked into it" damage. Put this on an enemy, a hostile NPC, or
// a hazard like a spike patch. It hurts whatever touches it, using the same
// DamageInfo pipeline as every weapon, so hit flashes, resistances, HP bars,
// knockback, and invulnerability all behave identically.
//
// This is deliberately DISTINCT from an attack:
//
//   CONTACT (this)  — lower damage, a light knockback nudge, no knockdown.
//                     It fires while the player stays in the collider, gated by
//                     the target's own invulnerability window so it can't drain
//                     them instantly.
//
//   ATTACK          — a DamageDealer on a child weapon hitbox that the attack
//                     animation enables. Higher damage, optional knockdown.
//
// Enemy has [RequireComponent(typeof(ContactDamage))], so every enemy gets this
// automatically with sensible defaults and no setup.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class ContactDamage : MonoBehaviour
{
    [Header("Damage")]
    [Tooltip("Damage dealt by simply touching this character. Keep it lower than " +
             "a real attack — contact is meant to punish carelessness, not kill.")]
    public List<DamagePacket> damage = new List<DamagePacket>
    {
        new DamagePacket(DamageType.Physical, 1)
    };

    [Header("Impact")]
    [Tooltip("Horizontal nudge applied to whoever is touched (px/s). Contact " +
             "should push lightly; heavy weapons are what really launch you.")]
    public float knockbackForce = 220f;
    [Tooltip("Upward component of the nudge (px/s). The target pops up once and " +
             "gravity drops them straight back down. Lift is applied ONLY when " +
             "the target is standing on the ground, so repeated contact can never " +
             "ratchet an already-airborne target higher and higher.")]
    public float knockbackLift = 150f;
    [Tooltip("Seconds the target is knocked down. Contact normally leaves this at " +
             "0 — reserve knockdowns for heavy or specialised enemies.")]
    public float knockdownDuration = 0f;

    [Tooltip("How far above this creature's top the target's feet may be and still " +
             "count as landing ON it (px). A landing always applies upward " +
             "knockback so the player recoils instead of standing on the enemy.")]
    public float topContactTolerance = 6f;

    [Header("Rate")]
    [Tooltip("Minimum seconds between contact hits on the same target. The " +
             "target's own invulnerability window also applies on top of this.")]
    public float hitInterval = 0.5f;

    private AbstractCharacter _owner;
    private readonly Dictionary<GameObject, float> _cooldowns = new Dictionary<GameObject, float>();

    private void Awake()
    {
        _owner = GetComponent<AbstractCharacter>();
    }

    private void Update()
    {
        if (_cooldowns.Count == 0) return;
        var keys = new List<GameObject>(_cooldowns.Keys);
        foreach (var k in keys)
        {
            if (k == null) { _cooldowns.Remove(k); continue; }
            _cooldowns[k] -= Time.deltaTime;
            if (_cooldowns[k] <= 0f) _cooldowns.Remove(k);
        }
    }

    // Both trigger and solid collisions count as contact, and Stay is used so
    // standing against an enemy keeps hurting rather than only the first frame.
    private void OnTriggerEnter2D(Collider2D other)     => TryTouch(other);
    private void OnTriggerStay2D(Collider2D other)      => TryTouch(other);
    private void OnCollisionEnter2D(Collision2D col)    => TryTouch(col.collider);
    private void OnCollisionStay2D(Collision2D col)     => TryTouch(col.collider);

    private void TryTouch(Collider2D other)
    {
        // CombatRules decides who this character is allowed to hurt, so an enemy
        // damages the player but never another enemy.
        IDamageable target = CombatRules.ResolveTarget(other, _owner);
        if (target == null) return;

        GameObject go = target.DamageableObject;
        if (_cooldowns.ContainsKey(go)) return;
        _cooldowns[go] = hitInterval;

        var info = new DamageInfo
        {
            source            = gameObject,
            instigator        = _owner,
            hitPoint          = other.ClosestPoint(transform.position),
            knockdownDuration = knockdownDuration
        };
        foreach (var p in damage) info.Add(p.type, p.amount);

        if (knockbackForce != 0f || knockbackLift != 0f)
        {
            float dir = Mathf.Sign(go.transform.position.x - transform.position.x);
            if (dir == 0f) dir = 1f;

            // ── Lift only from the ground ───────────────────────────────────
            // Knockback SETS vertical velocity rather than adding to it, so an
            // airborne target getting hit again would be re-launched from its
            // current height — repeat contact would walk the player up into the
            // air a step at a time. Applying lift only when they're grounded
            // means every pop starts from the floor and gravity always brings
            // them straight back down.
            var trb       = go.GetComponent<Rigidbody2D>();
            var targetCC  = go.GetComponent<CharacterController2D>();
            bool grounded = targetCC != null ? targetCC.IsGrounded
                                             : (trb != null && Mathf.Abs(trb.velocity.y) < 1f);

            // Landing ON this creature from above should always throw the target
            // back up, whether or not they were grounded. Without this, jumping
            // onto an enemy applies only sideways knockback and the player keeps
            // descending — which reads as standing on it rather than recoiling.
            var myCol     = GetComponent<Collider2D>();
            var targetCol = go.GetComponent<Collider2D>();
            bool landedOnTop = myCol != null && targetCol != null &&
                               targetCol.bounds.min.y >= myCol.bounds.max.y - topContactTolerance &&
                               (trb == null || trb.velocity.y <= 0.01f);

            float vy;
            if ((grounded || landedOnTop) && knockbackLift > 0f)
                vy = knockbackLift;              // one clean pop
            else
                vy = trb != null ? trb.velocity.y : 0f;   // preserve their arc

            info.knockback = new Vector2(dir * knockbackForce, vy);
        }

        target.TakeDamage(info);
    }
}
