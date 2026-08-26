// =============================================================================
// DamageDealer.cs   —   Assets/Scripts/Combat/
//
// The one component that makes a collider hurt things. Put it on ANY hitbox:
//
//   • The player's SwordHitBox child      (enabled during attack anim frames)
//   • An enemy's or NPC's weapon hitbox   (same pattern, child of the owner)
//   • An enemy's BODY collider            (contact damage — set damageOnStay)
//   • A spike trap, a lava tile, a projectile
//
// The owner is found automatically from the parent hierarchy, so a hitbox that
// is a child of the Player is owned by the Player, and one under an Enemy is
// owned by that Enemy. Nothing to assign.
//
// CONTACT vs. ATTACK is just two DamageDealers with different settings:
//   Contact  — on the body collider, low damage, damageOnStay = true,
//              small knockback, no knockdown.
//   Attack   — on a weapon hitbox enabled by animation, higher damage,
//              damageOnStay = false, little/no knockback, optional knockdown.
//
// PlayerSword and Arrow both derive from this, so all weapons share one code path.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class DamageDealer : MonoBehaviour, IDealDamage
{
    [Header("Damage")]
    [Tooltip("Damage this hitbox carries. Add a second entry (e.g. Fire) to make " +
             "it inherently multi-type — a flaming blade or a fire arrow.")]
    public List<DamagePacket> damage = new List<DamagePacket>
    {
        new DamagePacket(DamageType.Physical, 1)
    };

    [Header("Ownership")]
    [Tooltip("The character this hitbox belongs to. Left empty, it's found from " +
             "the parent — a SwordHitBox under the Player is owned by the Player.")]
    public AbstractCharacter owner;

    [Header("When it hits")]
    [Tooltip("Deal damage the moment a target enters the hitbox (attacks, projectiles).")]
    public bool damageOnEnter = true;
    [Tooltip("Keep dealing damage while a target stays inside (body contact damage).")]
    public bool damageOnStay = false;
    [Tooltip("Also react to solid (non-trigger) collisions, not just triggers.")]
    public bool damageOnCollision = false;
    [Tooltip("Seconds before the SAME target can be hit again by this hitbox.")]
    public float perTargetCooldown = 0.5f;

    [Header("Impact")]
    [Tooltip("Horizontal push applied to the target, away from this hitbox (px/s). " +
             "Contact damage wants a small value; heavy weapons want a large one.")]
    public float knockbackForce = 0f;
    [Tooltip("Upward push added to knockback (px/s).")]
    public float knockbackLift = 0f;
    [Tooltip("Seconds the target is knocked down and unable to act. Zero = none. " +
             "A knockdown also consumes the target's post-hit invulnerability.")]
    public float knockdownDuration = 0f;
    [Tooltip("This hit lands even during the target's invulnerability window.")]
    public bool ignoresInvulnerability = false;

    // Runtime enchantments layered on top of `damage` (fire oil, magic buff)
    private readonly List<DamagePacket> _bonus = new List<DamagePacket>();
    // Per-target cooldowns so one swing can't drain a target instantly
    private readonly Dictionary<GameObject, float> _recentHits = new Dictionary<GameObject, float>();

    // =========================================================================
    // Lifecycle
    // =========================================================================

    protected virtual void Awake()
    {
        // A hitbox is always a child of whoever swings it.
        if (owner == null) owner = GetComponentInParent<AbstractCharacter>();
    }

    protected virtual void Update()
    {
        if (_recentHits.Count == 0) return;
        var keys = new List<GameObject>(_recentHits.Keys);
        foreach (var k in keys)
        {
            if (k == null) { _recentHits.Remove(k); continue; }
            _recentHits[k] -= Time.deltaTime;
            if (_recentHits[k] <= 0f) _recentHits.Remove(k);
        }
    }

    private void OnEnable()
    {
        // A fresh swing can hit everything again.
        _recentHits.Clear();
    }

    // =========================================================================
    // IDealDamage
    // =========================================================================

    /// <summary>
    /// Builds the payload for one strike — base damage plus active enchantments.
    /// Rebuilt per hit so buffs applied mid-fight are included immediately.
    /// </summary>
    public virtual DamageInfo BuildDamage()
    {
        var info = new DamageInfo
        {
            source                 = gameObject,
            instigator             = owner,
            knockdownDuration      = knockdownDuration,
            ignoresInvulnerability = ignoresInvulnerability
        };
        foreach (var p in damage) info.Add(p.type, p.amount);
        foreach (var p in _bonus) info.Add(p.type, p.amount);
        return info;
    }

    /// <summary>Layer extra typed damage on top — fire oil, a magic enchantment.</summary>
    public void AddBonusDamage(DamageType type, int amount)
    {
        if (amount > 0) _bonus.Add(new DamagePacket(type, amount));
    }

    /// <summary>Clear all runtime enchantments.</summary>
    public void ClearBonusDamage() => _bonus.Clear();

    // =========================================================================
    // Collision entry points
    // =========================================================================

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if (damageOnEnter) TryHit(other);
    }

    protected virtual void OnTriggerStay2D(Collider2D other)
    {
        if (damageOnStay) TryHit(other);
    }

    protected virtual void OnCollisionEnter2D(Collision2D col)
    {
        if (damageOnCollision) TryHit(col.collider);
    }

    protected virtual void OnCollisionStay2D(Collision2D col)
    {
        if (damageOnCollision && damageOnStay) TryHit(col.collider);
    }

    // =========================================================================
    // The hit
    // =========================================================================

    protected void TryHit(Collider2D other)
    {
        // CombatRules decides whether this attacker may damage this target,
        // so every weapon in the game agrees on who can hurt whom.
        IDamageable target = CombatRules.ResolveTarget(other, owner);
        if (target == null) return;

        GameObject targetGO = target.DamageableObject;

        // One hit per target per cooldown window
        if (_recentHits.ContainsKey(targetGO)) return;
        _recentHits[targetGO] = perTargetCooldown;

        DamageInfo info = BuildDamage();
        info.hitPoint = other.ClosestPoint(transform.position);

        if (knockbackForce != 0f || knockbackLift != 0f)
        {
            float dir = Mathf.Sign(targetGO.transform.position.x - transform.position.x);
            if (dir == 0f) dir = 1f;
            info.knockback = new Vector2(dir * knockbackForce, knockbackLift);
        }

        target.TakeDamage(info);
        OnHitTarget(target, info, other);
    }

    /// <summary>
    /// Hook for subclasses to react after a successful hit — an arrow embeds
    /// itself, a projectile explodes, a weapon plays an impact effect.
    /// Base implementation does nothing.
    /// </summary>
    protected virtual void OnHitTarget(IDamageable target, DamageInfo info, Collider2D hitCollider) { }
}
