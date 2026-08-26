// =============================================================================
// Arrow.cs   —   Assets/Scripts/Player/Abilities/
//
// A fired projectile. Derives from DamageDealer, so it hits things through the
// same pipeline as the sword and enemy weapons — no duplicated hit logic.
//
// WHAT HAPPENS ON IMPACT is configurable, because different projectiles should
// feel different:
//
//   Arrows          — recoverable. On hit they either EMBED in the target for a
//                     moment or DROP to the ground so the player can pick them
//                     back up. Arrows are a resource worth being careful with.
//   Magic/elemental — consumed. Set destroyOnHit so the bolt vanishes on impact.
//
// PREFAB SETUP: SpriteRenderer + Rigidbody2D (gravity 0) + small trigger
// Collider2D + this component.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Arrow : DamageDealer
{
    [Header("Flight")]
    [Tooltip("Gravity pulling the arrow down (px/s²). Only applies when " +
             "affectedByGravity is true, so level shots stay level.")]
    public float arrowGravity = 600f;
    [Tooltip("Set by BowAbility: true for arcing diagonal shots, false for " +
             "straight level shots.")]
    public bool affectedByGravity = false;
    [Tooltip("Seconds before an arrow that hit nothing destroys itself.")]
    public float lifetime = 3f;

    [Header("On Impact")]
    [Tooltip("Consumed on hit — use for magic bolts and elemental projectiles. " +
             "Leave OFF for ordinary arrows so they can be recovered.")]
    public bool destroyOnHit = false;
    [Tooltip("Chance (0–1) the arrow embeds in the target instead of dropping. " +
             "Ignored when destroyOnHit is on.")]
    [Range(0f, 1f)] public float embedChance = 0.5f;
    [Tooltip("How long an embedded arrow stays stuck in the target before it " +
             "falls away (seconds).")]
    public float embedDuration = 4f;
    [Tooltip("Optional pickup prefab spawned where the arrow lands, so the " +
             "player can retrieve it. Leave empty until your item pickup exists — " +
             "the arrow will simply drop and fade instead.")]
    public GameObject recoverablePickupPrefab;

    // ── Private ───────────────────────────────────────────────────────────────
    private Rigidbody2D _rb;
    private float       _timer;
    private bool        _launched;
    private bool        _spent;      // already hit something; stops further hits

    // =========================================================================
    // Lifecycle
    // =========================================================================

    protected override void Awake()
    {
        base.Awake();
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0f;         // we apply gravity manually

        // A projectile strikes once, on contact.
        damageOnEnter = true;
        damageOnStay  = false;
    }

    protected override void Update()
    {
        base.Update();                 // DamageDealer's per-target cooldown ageing
        if (!_launched || _spent) return;

        _timer += Time.deltaTime;
        if (_timer >= lifetime) { Destroy(gameObject); return; }

        // Point the sprite along the direction of travel
        if (_rb.velocity.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(_rb.velocity.y, _rb.velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private void FixedUpdate()
    {
        if (!_launched || _spent || !affectedByGravity) return;
        _rb.velocity = new Vector2(_rb.velocity.x,
                                   _rb.velocity.y - arrowGravity * Time.fixedDeltaTime);
    }

    // =========================================================================
    // Firing
    // =========================================================================

    /// <summary>
    /// Fire the arrow. `shooter` becomes the owner, so the arrow can't hit whoever
    /// fired it and CombatRules knows which side it belongs to.
    /// </summary>
    /// <summary>
    /// Copies sorting layer and order from the shooter's renderer so the arrow
    /// draws in the same plane as the character that fired it. Without this the
    /// arrow renders on the default sorting layer and is hidden behind map art.
    /// </summary>
    public void MatchSortingTo(SpriteRenderer reference)
    {
        if (reference == null) return;
        var sr = GetComponent<SpriteRenderer>() ?? GetComponentInChildren<SpriteRenderer>();
        if (sr == null) return;

        sr.sortingLayerID = reference.sortingLayerID;
        sr.sortingOrder   = reference.sortingOrder + 1;   // just in front of the shooter
    }

    public void Launch(Vector2 direction, float arrowSpeed, AbstractCharacter shooter = null)
    {
        if (shooter != null) owner = shooter;

        // Safety net: an owner-less arrow is treated by CombatRules as an
        // environmental hazard, which means it damages ANY character it touches —
        // including the one who fired it.
        if (owner == null)
            Debug.LogWarning("[Arrow] Launched with no owner — it will damage any " +
                             "character it touches. Pass the shooter to Launch().", this);
        _rb.velocity = direction.normalized * arrowSpeed;
        _launched    = true;
        _spent       = false;
        _timer       = 0f;
    }

    // =========================================================================
    // Impact — DamageDealer calls this after a successful hit
    // =========================================================================

    protected override void OnHitTarget(IDamageable target, DamageInfo info, Collider2D hitCollider)
    {
        if (_spent) return;
        _spent = true;

        if (destroyOnHit)                       // magic bolt — consumed
        {
            Destroy(gameObject);
            return;
        }

        if (Random.value < embedChance)          // stick into the target
            Embed(target.DamageableObject.transform);
        else                                     // clatter to the ground
            DropToGround();
    }

    /// <summary>Sticks the arrow into whatever it hit, then drops away later.</summary>
    private void Embed(Transform host)
    {
        _launched = false;
        _rb.velocity     = Vector2.zero;
        _rb.bodyType     = RigidbodyType2D.Kinematic;
        DisableHitCollider();

        // Ride along with the target so it looks genuinely stuck in
        transform.SetParent(host, true);
        Destroy(gameObject, embedDuration);
    }

    /// <summary>Drops the arrow so the player can pick it back up.</summary>
    private void DropToGround()
    {
        _launched = false;
        DisableHitCollider();

        if (recoverablePickupPrefab != null)
        {
            // Hand off to the real item so it can be collected by the inventory
            Instantiate(recoverablePickupPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject);
            return;
        }

        // NOTE: no pickup prefab assigned yet. For now the arrow simply falls and
        // fades. Once your item/inventory system has an "arrow" pickup, assign it
        // above and recovered arrows will return to the player's quiver.
        _rb.bodyType     = RigidbodyType2D.Dynamic;
        _rb.gravityScale = 1f;
        Destroy(gameObject, 5f);
    }

    private void DisableHitCollider()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }
}
