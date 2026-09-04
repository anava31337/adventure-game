// =============================================================================
// LobbedProjectile.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// A thrown object that arcs like a real projectile, hurts what it hits WHILE IN
// MOTION, then comes to rest on the ground and PERSISTS as a world object —
// the Landlouse Egg.
//
// It derives from DamageDealer, so it uses the same hit pipeline as the sword,
// arrows, and contact damage: resistances, i-frames, hit flash, and knockback
// all work with no extra code. The only addition is that damage is gated on
// speed, matching the design doc: an egg is harmless sitting on the ground, but
// dangerous while airborne or being rolled around.
//
// The arc uses the same gravity model as the player's jump (and SpawnLaunch), so
// a lobbed egg falls on the same curve everything else in the game does.
//
// Pair with HatchAfterDelay to make the egg hatch into a creature.
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class LobbedProjectile : DamageDealer
{
    [Header("Flight")]
    [Tooltip("Downward acceleration in flight (px/s²). 981 matches the player's gravity.")]
    public float gravity = 981f;
    [Tooltip("Extra gravity multiplier while descending. 1 = symmetric arc.")]
    public float fallGravityMultiplier = 1f;
    [Tooltip("Terminal fall speed (px/s).")]
    public float maxFallSpeed = 1200f;

    [Header("Ground Contact")]
    [Tooltip("Layers treated as ground for settling.")]
    public LayerMask groundLayer;
    [Tooltip("How much horizontal speed survives a bounce off the ground (0–1). " +
             "Low values make the egg thud down and stay put; higher lets it roll.")]
    [Range(0f, 1f)] public float groundFriction = 0.35f;
    [Tooltip("Vertical bounce retained when it hits the ground (0–1). 0 = no bounce.")]
    [Range(0f, 1f)] public float groundBounce = 0.25f;
    [Tooltip("Below this speed the projectile is considered at rest (px/s).")]
    public float restSpeed = 12f;

    [Tooltip("Small extra reach on the ground sweep (px), so a projectile resting " +
             "just above the surface still registers.")]
    public float groundSkin = 2f;

    [Tooltip("Random variation applied to friction and bounce on each impact " +
             "(0 = identical every time, 0.3 = +/-30%). Stops a clutch of eggs " +
             "from all rolling to exactly the same resting place.")]
    [Range(0f, 0.9f)] public float bounceVariance = 0.35f;

    [Header("Damage Gating")]
    [Tooltip("Minimum speed at which this projectile can hurt anything (px/s). " +
             "At rest it is harmless — an egg only hurts while flying or rolling.")]
    public float minimumDamageSpeed = 40f;

    // ── State ─────────────────────────────────────────────────────────────────
    private Rigidbody2D       _rb;
    private AbstractCharacter _character;
    private bool        _launched;
    private bool        _atRest;

    /// <summary>True once the projectile has settled and stopped being dangerous.</summary>
    public bool AtRest => _atRest;

    protected override void Awake()
    {
        base.Awake();
        EnsureSetup();

        damageOnEnter = true;
        damageOnStay  = true;    // a rolling egg should hurt on continued contact

        // NOTE: there is no destroyOnHit flag on DamageDealer. Destruction is
        // handled by overriding OnHitTarget — Arrow uses it to embed or drop.
        // This class simply doesn't override it, so the projectile persists after
        // a hit, which is exactly what an egg should do.
    }

    /// <summary>
    /// Caches the Rigidbody2D, takes gravity into our own hands, and switches OFF
    /// the character's shared gravity.
    ///
    /// That last part matters: the egg has an Enemy component, so AbstractCharacter
    /// would otherwise apply gravity too. Both integrating into velocity.y each
    /// step makes it fall at roughly double rate, which outruns this class's own
    /// ground sweep — the egg drops straight through the floor and never hatches.
    /// SpawnLaunch suspends the shared gravity for the same reason.
    ///
    /// Called from Awake AND Launch, because Awake does not run on a prefab whose
    /// root GameObject was saved inactive.
    /// </summary>
    private void EnsureSetup()
    {
        if (_character == null) _character = GetComponent<AbstractCharacter>();
        if (_character != null) _character.GravitySuspended = true;

        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
        if (_rb == null)
        {
            Debug.LogError("[LobbedProjectile] No Rigidbody2D on '" + name +
                           "'. The projectile cannot be thrown.", this);
            return;
        }
        _rb.gravityScale = 0f;   // we own gravity, to match the game's arc model
    }

    /// <summary>Throws the projectile. `thrower` becomes its owner for targeting rules.</summary>
    public void Launch(Vector2 velocity, AbstractCharacter thrower = null)
    {
        EnsureSetup();
        if (_rb == null) return;

        // A prefab saved inactive stays inactive when instantiated.
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        if (thrower != null) owner = thrower;

        _rb.velocity = velocity;
        _launched    = true;
        _atRest      = false;
    }

    private void FixedUpdate()
    {
        if (!_launched || _atRest) return;

        // Same gravity model as the player's jump and SpawnLaunch.
        float mult  = _rb.velocity.y < 0f ? fallGravityMultiplier : 1f;
        float newVY = _rb.velocity.y - gravity * mult * Time.fixedDeltaTime;
        newVY = Mathf.Max(newVY, -maxFallSpeed);
        _rb.velocity = new Vector2(_rb.velocity.x, newVY);

        if (_rb.velocity.y <= 0f)
        {
            var hit = SweepToGround();
            if (hit.collider != null) HitGround(hit);
        }
    }

    /// <summary>
    /// Sweeps downward across the distance actually travelled this step and
    /// returns the ground hit, if any.
    ///
    /// A fixed-distance probe cannot work here: under 981 gravity the projectile
    /// covers several pixels per physics step, so a short ray simply misses the
    /// floor and the egg tunnels straight through it — never landing, never
    /// settling, and therefore never hatching. Sweeping the real travel distance
    /// makes the test independent of how fast it happens to be falling.
    /// </summary>
    private RaycastHit2D SweepToGround()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return default;

        // Distance covered this step, plus a small skin so a projectile already
        // resting just above the surface still registers.
        float travel = Mathf.Abs(_rb.velocity.y) * Time.fixedDeltaTime + groundSkin;

        Vector2 origin = new Vector2(col.bounds.center.x, col.bounds.min.y);
        return Physics2D.Raycast(origin, Vector2.down, travel, groundLayer);
    }

    private void HitGround(RaycastHit2D hit)
    {
        var col = GetComponent<Collider2D>();

        // Place the projectile exactly ON the surface rather than wherever the
        // step happened to end. This is what removes the 1–2px float.
        if (col != null)
        {
            float feet  = col.bounds.min.y;
            float delta = hit.point.y - feet;
            _rb.position = new Vector2(_rb.position.x, _rb.position.y + delta);
        }

        // Bleed off speed on impact. Each bounce is randomised slightly so a
        // clutch of eggs scatters instead of all coming to rest in one spot.
        float friction = groundFriction * Random.Range(1f - bounceVariance, 1f + bounceVariance);
        float bounce   = groundBounce   * Random.Range(1f - bounceVariance, 1f + bounceVariance);

        float vx = _rb.velocity.x * friction;
        float vy = -_rb.velocity.y * bounce;

        if (Mathf.Abs(vx) < restSpeed && vy < restSpeed)
        {
            // Settled. Stop entirely and stay in the world as a solid object.
            _rb.velocity = Vector2.zero;
            _atRest      = true;
        }
        else
        {
            _rb.velocity = new Vector2(vx, vy);
        }
    }

    // =========================================================================
    // Damage gating — only dangerous while actually moving
    // =========================================================================

    private bool FastEnoughToHurt => _rb != null && _rb.velocity.magnitude >= minimumDamageSpeed;

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if (FastEnoughToHurt) base.OnTriggerEnter2D(other);
    }

    protected override void OnTriggerStay2D(Collider2D other)
    {
        if (FastEnoughToHurt) base.OnTriggerStay2D(other);
    }

    protected override void OnCollisionEnter2D(Collision2D col)
    {
        if (FastEnoughToHurt) base.OnCollisionEnter2D(col);
    }

    protected override void OnCollisionStay2D(Collision2D col)
    {
        if (FastEnoughToHurt) base.OnCollisionStay2D(col);
    }
}
