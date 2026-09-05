// =============================================================================
// BouncyBody.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Makes a creature springy to land on or bump into — the Pregnant Landlouse.
// The player bounces off the top and is shoved aside on a side hit. It deals NO
// damage on its own: leave ContactDamage off and collision is purely a movement
// obstacle, exactly as the design doc describes.
//
// WHY POSITION TESTS RATHER THAN COLLISION NORMALS:
// Relying on OnCollisionEnter2D normals fails on a capsule (they curve away near
// the top, so a centred landing often doesn't register and the player sinks into
// the sprite) and on a box (the bounce only fires near the corners, where the
// normal is unambiguous). Comparing the player's feet against the creature's top
// surface every physics step is unambiguous for any collider shape.
//
// TWO SEPARATE COOLDOWNS:
// Landing and being shoved aside are tracked independently. Sharing one timer
// meant a glancing side contact on the way up — which is exactly what happens
// approaching the top centre — locked out the landing bounce for the whole, much
// longer shove cooldown, so the player stuck to the top instead of bouncing.
// =============================================================================

using UnityEngine;

public class BouncyBody : MonoBehaviour
{
    [Header("Bounce (landing on top)")]
    [Tooltip("Upward velocity given to whoever lands on top (px/s). The player's " +
             "own jumpHeight is 320, so higher values launch them further than " +
             "they could jump unaided — which is what makes it feel springy.")]
    public float bounceForce = 420f;

    [Tooltip("How far above the top surface the player's feet may be and still " +
             "count as a landing (px).")]
    public float topTolerance = 6f;

    [Tooltip("Seconds before another landing bounce is allowed. Keep this short — " +
             "it only exists to stop one landing firing across several frames.")]
    public float bounceCooldown = 0.15f;

    [Header("Side hit")]
    [Tooltip("Horizontal shove when struck from the side rather than above (px/s).")]
    public float sideKnockback = 180f;

    [Tooltip("Small upward component on a side shove, so the player is nudged up " +
             "and over rather than simply stopped (px/s).")]
    public float sideLift = 90f;

    [Tooltip("Seconds between side shoves. Much longer than the bounce cooldown: " +
             "the creature has a solid body, so a player standing beside it is " +
             "permanently overlapping, and without a long gap here they are shoved " +
             "every few frames and can never close far enough to land a hit.")]
    public float shoveCooldown = 1.2f;

    [Tooltip("Don't shove a target that is mid-attack, so a swing gets a chance to " +
             "connect instead of being pushed out of reach before it lands.")]
    public bool allowAttacksWhileTouching = true;

    [Header("Detection")]
    [Tooltip("Tag of the character that can bounce off this creature.")]
    public string targetTag = "Player";

    // ── Runtime ──────────────────────────────────────────────────────────────
    private Collider2D            _col;
    private Transform             _target;
    private Collider2D            _targetCol;
    private Rigidbody2D           _targetRb;
    private CharacterController2D _targetCC;

    // Independent timers — see the header note.
    private float _bounceCooldown;
    private float _shoveCooldown;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
    }

    private void Start()
    {
        var t = GameObject.FindWithTag(targetTag);
        if (t != null)
        {
            _target    = t.transform;
            _targetCol = t.GetComponent<Collider2D>();
            _targetRb  = t.GetComponent<Rigidbody2D>();
            _targetCC  = t.GetComponent<CharacterController2D>();
        }
    }

    private void FixedUpdate()
    {
        if (_bounceCooldown > 0f) _bounceCooldown -= Time.fixedDeltaTime;
        if (_shoveCooldown  > 0f) _shoveCooldown  -= Time.fixedDeltaTime;

        if (_col == null || _targetCol == null || _targetRb == null) return;

        // Only consider a target that is touching us — with a small tolerance.
        //
        // A strict Intersects() test fails exactly when the player lands squarely
        // on the top surface: physics resolves the contact so the bounds TOUCH
        // without overlapping, so no bounce fires until random jitter creates an
        // overlap. That is the "sticks at the top centre" behaviour. Landing near
        // a corner sinks in slightly, overlaps immediately, and feels fine —
        // which is why the edges seemed more responsive than the middle.
        Bounds mine = _col.bounds;
        mine.Expand(new Vector3(0f, topTolerance, 0f));
        if (!mine.Intersects(_targetCol.bounds)) return;

        float myTop      = _col.bounds.max.y;
        float targetFeet = _targetCol.bounds.min.y;
        bool  descending = _targetRb.velocity.y <= 0.01f;

        // ── Landing on top ──────────────────────────────────────────────────
        // Position-based, so it works anywhere along the surface with any
        // collider shape. Answers only to its own timer.
        if (descending && targetFeet >= myTop - topTolerance)
        {
            if (_bounceCooldown <= 0f) Bounce();
            return;
        }

        // ── Side / underside hit ────────────────────────────────────────────
        if (allowAttacksWhileTouching && TargetIsAttacking()) return;
        if (_shoveCooldown > 0f) return;

        Shove();
    }

    // =========================================================================
    // Reactions
    // =========================================================================

    private void Bounce()
    {
        _bounceCooldown = bounceCooldown;

        if (_targetCC != null) _targetCC.Bounce(bounceForce);
        else _targetRb.velocity = new Vector2(_targetRb.velocity.x, bounceForce);
    }

    private void Shove()
    {
        // Deliberately does NOT touch the bounce timer, so being nudged aside
        // never prevents a bounce the moment the player gets on top.
        _shoveCooldown = shoveCooldown;

        float dir = Mathf.Sign(_target.position.x - transform.position.x);
        if (dir == 0f) dir = 1f;

        Vector2 v = new Vector2(dir * sideKnockback, Mathf.Max(_targetRb.velocity.y, sideLift));

        // Route through the controller so it suspends input briefly and the shove
        // actually reads, instead of being erased by the next movement step.
        if (_targetCC != null) _targetCC.ApplyKnockback(v);
        else                   _targetRb.velocity = v;
    }

    /// <summary>
    /// True while the target has an active attack hitbox. Detected by looking for
    /// an enabled DamageDealer with an enabled collider under the target — which
    /// is exactly what an attack animation switches on — so this needs no extra
    /// state or events.
    /// </summary>
    private bool TargetIsAttacking()
    {
        if (_target == null) return false;

        foreach (var dealer in _target.GetComponentsInChildren<DamageDealer>())
        {
            if (dealer == null || !dealer.enabled) continue;
            var col = dealer.GetComponent<Collider2D>();
            if (col != null && col.enabled) return true;
        }
        return false;
    }
}
