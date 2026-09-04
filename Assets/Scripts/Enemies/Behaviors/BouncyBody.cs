// =============================================================================
// BouncyBody.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Makes a creature springy to stand on or bump into — the Pregnant Landlouse.
// The player bounces off it instead of landing, and is shoved aside on a side
// hit. It deals NO damage on its own: leave ContactDamage off the creature and
// collision is purely a movement obstacle, exactly as the design doc describes.
//
// WHY IT DOESN'T USE COLLISION CALLBACKS:
// Relying on OnCollisionEnter2D contact normals is what caused the two problems
// you hit. With a CapsuleCollider2D the normals curve away near the top, so a
// centred landing often failed to register and the player sank into the sprite.
// With a BoxCollider2D the bounce only fired near the corners, where the normal
// was unambiguous — and only on a second jump, because the first contact was
// consumed resolving the landing.
//
// Instead this does a position test every physics step, the same approach that
// fixed the one-way platforms: if the player is above the creature's top surface
// and descending, they bounce. That is unambiguous regardless of collider shape.
//
// SETUP:
//   • Give the creature a SOLID (non-trigger) collider so it still blocks
//     movement — a BoxCollider2D is easiest and shape barely matters now.
//   • Add this component. Do NOT add ContactDamage if it should be harmless.
// =============================================================================

using UnityEngine;

public class BouncyBody : MonoBehaviour
{
    [Header("Bounce")]
    [Tooltip("Upward velocity given to whoever lands on top (px/s). The player's " +
             "own jumpHeight is 320, so values above that launch them higher than " +
             "they could jump unaided — which is what makes it feel springy.")]
    public float bounceForce = 420f;

    [Tooltip("How far above this creature's top surface the player's feet may be " +
             "and still register as a landing (px).")]
    public float topTolerance = 6f;

    [Tooltip("Seconds before the same target can bounce again. Stops a single " +
             "landing from firing repeatedly across consecutive frames.")]
    public float bounceCooldown = 0.15f;

    [Header("Side Hit")]
    [Tooltip("Horizontal shove when struck from the side rather than above (px/s). " +
             "Makes the creature awkward to squeeze past without hurting the player.")]
    public float sideKnockback = 180f;

    [Tooltip("Small upward component on a side shove so the player is nudged up " +
             "and over rather than simply stopped (px/s).")]
    public float sideLift = 90f;

    [Header("Detection")]
    [Tooltip("Tag of the character that can bounce off this creature.")]
    public string targetTag = "Player";

    private Collider2D _col;
    private Transform  _target;
    private Collider2D _targetCol;
    private Rigidbody2D _targetRb;
    private CharacterController2D _targetCC;
    private float _cooldown;

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
        if (_cooldown > 0f) _cooldown -= Time.fixedDeltaTime;
        if (_col == null || _targetCol == null || _targetRb == null) return;
        if (_cooldown > 0f) return;

        // Only consider a target that is actually overlapping or touching us.
        if (!_col.bounds.Intersects(_targetCol.bounds)) return;

        float myTop      = _col.bounds.max.y;
        float targetFeet = _targetCol.bounds.min.y;
        bool  descending = _targetRb.velocity.y <= 0.01f;

        // ── Landing on top ──────────────────────────────────────────────────
        // Position-based, so it works anywhere along the surface and with any
        // collider shape — no dependence on contact normals.
        if (descending && targetFeet >= myTop - topTolerance)
        {
            Bounce();
            return;
        }

        // ── Side / underside hit ────────────────────────────────────────────
        Shove();
    }

    private void Bounce()
    {
        _cooldown = bounceCooldown;

        if (_targetCC != null) _targetCC.Bounce(bounceForce);
        else _targetRb.velocity = new Vector2(_targetRb.velocity.x, bounceForce);
    }

    private void Shove()
    {
        _cooldown = bounceCooldown;

        float dir = Mathf.Sign(_target.position.x - transform.position.x);
        if (dir == 0f) dir = 1f;

        Vector2 v = new Vector2(dir * sideKnockback, Mathf.Max(_targetRb.velocity.y, sideLift));

        // Route through the controller so it suspends input briefly and the
        // shove actually reads, instead of being erased by the next movement step.
        if (_targetCC != null) _targetCC.ApplyKnockback(v);
        else                   _targetRb.velocity = v;
    }
}
