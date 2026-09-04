// =============================================================================
// CharacterController2D.cs   —   Assets/Scripts/Player/
//
// RIGIDBODY2D INSPECTOR SETTINGS:
//   Gravity Scale       : 0   (script applies gravity manually)
//   Mass                : 150
//   Linear Drag         : 0
//   Collision Detection : Continuous
//   Freeze Rotation Z   : ✓
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class CharacterController2D : MonoBehaviour
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Movement & Jump  (original fields preserved)")]
    public LayerMask  ground;
    public Transform  groundCheck;
    public float      speed      = 96f;
    public float      jumpHeight = 320f;

    [Header("Jumps")]
    [Tooltip("Max jumps before landing. 1 = normal, 2 = double-jump powerup.")]
    [Min(1)]
    public int maxJumps = 1;

    [Header("Movement Feel")]
    [Tooltip("Ground acceleration (px/s²). Default 9999 = instant (original feel). " +
             "Terrain modifiers can lower this for ice/mud.")]
    public float groundAcceleration    = 9999f;
    [Tooltip("Air acceleration (px/s²).")]
    public float airAcceleration       = 9999f;
    [Tooltip("Multiplier on acceleration when decelerating (no input). Higher = snappier stops.")]
    public float decelerationMultiplier = 2.5f;

    [Header("Gravity Tuning — 1 PPU")]
    [Tooltip("Scales Physics2D.gravity.y. Use 100 at 1 PPU → effective -981 px/s².")]
    public float gravityMultiplier      = 100f;
    [Tooltip("Extra gravity while falling. 2.5 = punchy Mario arc. 1 = symmetric.")]
    public float fallGravityMultiplier  = 2.5f;
    [Tooltip("Extra gravity on early Jump release.")]
    public float lowJumpMultiplier      = 2.0f;
    [Tooltip("Maximum downward speed (px/s) gravity may reach while grounded. " +
             "Presses the player onto the surface so any small gap — such as the " +
             "one a knockback pop leaves behind — closes within a frame or two, " +
             "while standing still never accumulates fall speed.")]
    public float groundSettleSpeed      = 60f;

    [Header("Variable Jump & Feel")]
    [Range(0f, 1f)]
    [Tooltip("Upward-velocity fraction kept on early Jump release. 0.4 = Mario half-cut.")]
    public float jumpCutFraction = 0.4f;
    [Tooltip("Seconds after leaving a ledge that a jump is still allowed.")]
    public float coyoteTime      = 0.12f;
    [Tooltip("Seconds before landing that a Jump press fires on touch-down.")]
    public float jumpBufferTime  = 0.15f;
    [Tooltip("Terminal fall speed cap (px/s).")]
    public float maxFallSpeed    = 1200f;

    [Header("Crouch / Look Up")]
    [Tooltip("Speed multiplier while crouch-walking (holding Down + a direction).")]
    public float sneakSpeedMultiplier = 0.55f;
    [Tooltip("Axis magnitude past which Up/Down counts as held.")]
    public float verticalDeadzone = 0.4f;

    [Header("Knockback")]
    [Tooltip("Seconds the player loses horizontal control after being knocked back. " +
             "Without this the controller's acceleration (which is near-instant) " +
             "overwrites the knockback velocity on the very next physics step, " +
             "which is why knockback appeared to do almost nothing.")]
    public float knockbackRecovery = 0.22f;

    [Header("Landing")]
    [Tooltip("Seconds after landing before another jump is allowed on solid ground.")]
    public float landingJumpDelay = 0.04f;
    [Tooltip("Seconds after landing before another jump is allowed inside a volume " +
             "(water, etc.). Larger than the ground value so swimming feels weighty " +
             "instead of letting the player bounce off the bottom instantly.")]
    public float volumeLandingJumpDelay = 0.02f;

    [Header("Volume Resistance")]
    [Tooltip("Velocity retained per second while inside a volume. 1 = no drag, " +
             "0.85 = noticeably heavy water. Applies to both axes.")]
    public float volumeDrag = 0.88f;
    [Tooltip("Extra velocity bleed applied ONLY to upward motion in a volume. This " +
             "is what stops the player rocketing off the bottom: rising is damped " +
             "hard while sinking stays natural. Lower = more resistance.")]
    public float volumeUpwardDrag = 0.55f;

    // =========================================================================
    // Private state
    // =========================================================================

    private PlayerBaseInput playerBaseInputs;
    private Rigidbody2D     rb;
    private Animator        animator;

    private float horizontal;
    private float vertical;
    private bool  jumpPressedThisFrame;
    private bool  jumpHeld;

    private bool  isGrounded;
    private bool  wasGrounded;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float jumpGroundCooldown;
    private float landedAtTime = -99f;
    private OneWayPlatformRider platformRider;
    private float knockbackUntil;
    private bool  wasGroundedLastFrame;
    private int   jumpsRemaining;
    private const float JUMP_COOLDOWN = 0.12f;

    // Animation state
    private enum State     { Idle, Walking, Ducking, Sneaking, Attacking, Jumping, Falling }
    private enum Direction { Left, Right }
    private State     state     = State.Idle;
    private Direction direction = Direction.Right;

    // Surface terrain (collision-based — TerrainType component)
    private TerrainModifiers activeTerrain;

    // Volume terrain (trigger-based — TerrainVolume component, e.g. water / quicksand)
    private TerrainModifiers activeVolume;
    private int   volumeCount = 0;      // tracks overlapping volumes
    private float lethalTimer = 0f;

    // ── Terrain-aware effective values ────────────────────────────────────────

    private float EffectiveSpeed =>
        speed * (activeTerrain?.speedMultiplier ?? 1f) * (activeVolume?.speedMultiplier ?? 1f);

    private float EffectiveJumpHeight =>
        jumpHeight * (activeTerrain?.jumpMultiplier ?? 1f) * (activeVolume?.jumpMultiplier ?? 1f);

    // Combined fall gravity: base × surface friction × volume floatiness
    private float EffectiveFallGravity =>
        fallGravityMultiplier
        * (activeTerrain?.fallGravityMultiplier ?? 1f)
        * (activeVolume?.fallGravityMultiplier  ?? 1f);

    // Volume-wide gravity scale — applied to ascent and apex too (water whole-arc floatiness)
    private float EffectiveVolumeGravScale => activeVolume?.volumeGravityScale ?? 1f;

    private float EffectiveGroundAccel =>
        (activeTerrain != null && activeTerrain.overrideAcceleration)
            ? activeTerrain.groundAcceleration
            : groundAcceleration;

    private float EffectiveDecelMult =>
        decelerationMultiplier * (activeTerrain?.frictionMultiplier ?? 1f);

    private bool JumpBlockedByTerrain =>
        (activeTerrain != null && activeTerrain.preventJump) ||
        (activeVolume  != null && activeVolume.preventJump);

    private bool InfiniteJumpInVolume =>
        activeVolume != null && activeVolume.infiniteJump && !activeVolume.preventJump;

    // =========================================================================
    // Public API
    // =========================================================================

    public Vector3 velocity  => rb != null ? (Vector3)rb.velocity : Vector3.zero;
    public bool    IsGrounded => isGrounded;
    public bool    IsInVolume => activeVolume != null;

    /// <summary>True when the player is facing right.</summary>
    public bool FacingRight => direction == Direction.Right;

    /// <summary>Raw vertical axis value (-1 down … +1 up).</summary>
    public float CurrentVerticalInput => vertical;

    /// <summary>Raw horizontal axis value (-1 left … +1 right).</summary>
    public float CurrentHorizontalInput => horizontal;

    // ── Directional gating ────────────────────────────────────────────────────
    // Up and Down INTERRUPT left/right rather than combining with it:
    //   Up (alone or diagonal)  → stationary. Placeholder for a future "look up",
    //                             and it lets the player aim a diagonal bow shot
    //                             without walking out from under it.
    //   Down + left/right       → crouch-walk (Sneak animations), reduced speed.
    //   Down alone              → stationary duck.
    //   Left/right alone        → normal walk.

    private bool UpHeld   => vertical >  verticalDeadzone;
    private bool DownHeld => vertical < -verticalDeadzone;

    /// <summary>Horizontal input after the up/down interrupt rules are applied.</summary>
    private float GatedHorizontal => UpHeld ? 0f : horizontal;

    /// <summary>Move speed for this frame, reduced while crouch-walking.</summary>
    private float CurrentMoveSpeed =>
        DownHeld ? EffectiveSpeed * sneakSpeedMultiplier : EffectiveSpeed;

    /// <summary>True while a jump is blocked by the post-landing settle delay.</summary>
    private bool InLandingDelay =>
        Time.time < landedAtTime + (IsInVolume ? volumeLandingJumpDelay : landingJumpDelay);

    /// <summary>
    /// Set to true by an ability (e.g. WhipAbility while swinging) to pause all
    /// CharacterController2D physics so the ability can drive the Rigidbody directly.
    /// Remember to restore to false when the ability finishes.
    /// </summary>
    public bool IsExternallyControlled { get; set; }

    // =========================================================================
    // MonoBehaviour
    // =========================================================================

    public void Awake()
    {
        platformRider = GetComponent<OneWayPlatformRider>();
        playerBaseInputs = new PlayerBaseInput();
        playerBaseInputs.Overworld.Disable();
        playerBaseInputs.Character.Enable();

        animator       = GetComponent<Animator>();
        rb             = GetComponent<Rigidbody2D>();
        direction      = Direction.Right;
        state          = State.Idle;
        jumpsRemaining = maxJumps;
        rb.gravityScale = 0f;
    }

    private void OnDisable()
    {
        playerBaseInputs?.Character.Disable();
        playerBaseInputs?.Dispose();
    }

    private void FixedUpdate()
    {
        // An ability (e.g. WhipAbility while swinging) can pause all physics here
        if (IsExternallyControlled) return;

        // ── Timers ─────────────────────────────────────────────────────────
        if (jumpGroundCooldown > 0f) jumpGroundCooldown -= Time.fixedDeltaTime;
        if (jumpBufferTimer    > 0f) jumpBufferTimer    -= Time.fixedDeltaTime;

        // ── Ground detection ───────────────────────────────────────────────
        wasGrounded = isGrounded;
        isGrounded  = CheckGrounded();

        // Note the instant we touch down so the landing settle delay can run.
        if (isGrounded && !wasGroundedLastFrame) landedAtTime = Time.time;
        wasGroundedLastFrame = isGrounded;

        // ── Land ───────────────────────────────────────────────────────────
        if (isGrounded && !wasGrounded)
        {
            jumpsRemaining = maxJumps;

            if (jumpBufferTimer > 0f)
            {
                jumpBufferTimer = 0f;
                ExecuteJump();
            }
            else
            {
                RestoreGroundAnimation();
            }
        }

        // ── Stuck-state recovery ──────────────────────────────────────────
        // If we're on the ground and not moving upward, an airborne state is
        // wrong by definition. Without this, anything that sets Jumping/Falling
        // while the player never actually leaves the floor would freeze the
        // animation system until the next real jump.
        if (isGrounded && rb.velocity.y <= 0.01f && !InKnockback
            && (state == State.Jumping || state == State.Falling))
        {
            RestoreGroundAnimation();
        }

        // ── Coyote ────────────────────────────────────────────────────────
        if (wasGrounded && !isGrounded && rb.velocity.y <= 0f)
            coyoteTimer = coyoteTime;
        else if (isGrounded)
            coyoteTimer = 0f;
        else
            coyoteTimer -= Time.fixedDeltaTime;

        // ── Jump decision ─────────────────────────────────────────────────
        if (jumpPressedThisFrame)
        {
            jumpPressedThisFrame = false;
            bool useCoyote = coyoteTimer > 0f && !isGrounded && jumpsRemaining == maxJumps;

            if ((isGrounded || useCoyote || jumpsRemaining > 0 || InfiniteJumpInVolume)
                && !JumpBlockedByTerrain
                && !InLandingDelay)   // brief settle after touching down
            {
                if (useCoyote) coyoteTimer = 0f;
                ExecuteJump();
            }
            else
            {
                jumpBufferTimer = jumpBufferTime;
            }
        }

        // ── Physics ───────────────────────────────────────────────────────
        ApplyGravity();
        ApplyHorizontalMovement();
        ApplyVolumeDrag();

        // Resolve one-way platforms LAST, after this step's gravity and movement
        // have been applied, so the sweep sees the player's true end position and
        // any snap is the final word on where they are this step.
        if (platformRider != null) platformRider.TickPlatforms();

        // ── Volume continuous downforce (quicksand sinking, etc.) ─────────
        if (activeVolume != null && activeVolume.continuousDownforce > 0f)
        {
            float newVY = rb.velocity.y - activeVolume.continuousDownforce * Time.fixedDeltaTime;
            newVY = Mathf.Max(newVY, -maxFallSpeed);
            rb.velocity = new Vector2(rb.velocity.x, newVY);
        }

        // ── Volume lethal timer ────────────────────────────────────────────
        if (activeVolume != null && activeVolume.isLethal)
        {
            lethalTimer -= Time.fixedDeltaTime;
            if (lethalTimer <= 0f)
            {
                lethalTimer = 0f;
                // Lethal terrain (lava, spikes, drowning). Pierces invulnerability
                // so i-frames can't save the player from standing in it.
                var self = GetComponent<AbstractCharacter>();
                if (self != null)
                {
                    var lethal = new DamageInfo(DamageType.Physical, 9999)
                    {
                        ignoresInvulnerability = true
                    };
                    self.TakeDamage(lethal);
                }
            }
        }

        // ── Apex: switch Jump → Fall animation ────────────────────────────
        if (!isGrounded && state == State.Jumping && rb.velocity.y <= 0f)
        {
            state = State.Falling;
            SetAnim(direction == Direction.Right ? "FallRight" : "FallLeft");
        }
    }

    private void LateUpdate()
    {
        transform.position = new Vector3(
            Mathf.Round(transform.position.x),
            Mathf.Round(transform.position.y),
            transform.position.z);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, 6f);
    }

    // =========================================================================
    // Terrain detection — surface (collision) and volume (trigger)
    // =========================================================================

    // Surface terrain: fires when the character stands ON a TerrainType object
    private void OnCollisionEnter2D(Collision2D col)
    {
        foreach (ContactPoint2D c in col.contacts)
        {
            if (c.normal.y > 0.5f)
            {
                activeTerrain = col.gameObject.GetComponent<TerrainType>()?.modifiers;
                return;
            }
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        activeTerrain = null;
    }

    // Volume terrain: fires when the character ENTERS a water / quicksand trigger
    private void OnTriggerEnter2D(Collider2D other)
    {
        // GetComponent only searches the collider's own GameObject.
        // GetComponentInParent also checks up the hierarchy — needed when
        // the Tilemap (with TerrainVolume attached) is a child of a Grid parent
        // and the CompositeCollider2D fires as the triggering collider.
        var vol = other.GetComponent<TerrainVolume>()
               ?? other.GetComponentInParent<TerrainVolume>();

        if (vol == null || vol.modifiers == null) return;

        volumeCount++;
        activeVolume = vol.modifiers;

        if (activeVolume.isLethal)
            lethalTimer = activeVolume.lethalDelay;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var vol = other.GetComponent<TerrainVolume>()
               ?? other.GetComponentInParent<TerrainVolume>();

        if (vol == null || vol.modifiers == null) return;

        volumeCount = Mathf.Max(0, volumeCount - 1);
        if (volumeCount == 0)
        {
            activeVolume = null;
            lethalTimer  = 0f;          // reset death clock on exit
        }
    }

    // =========================================================================
    // Physics helpers
    // =========================================================================

    private void ApplyGravity()
    {
        // ── Who owns vertical position ──────────────────────────────────────
        // The one-way platform rider authors the player's position directly while
        // it supports them, so gravity must not interfere there.
        if (platformRider != null && platformRider.IsSupported && rb.velocity.y <= 0f)
            return;

        // Everything else gets gravity, ALWAYS — including while "grounded".
        //
        // Earlier versions skipped gravity whenever isGrounded was true, then
        // tried to narrow that with a small contact radius. Both fail for the
        // same reason: the ground check is a circle of radius R, so the player
        // reads as grounded while up to R pixels ABOVE the floor. Any version
        // that skips gravity inside that band lets a knockback pop leave the
        // player hovering in the gap with nothing acting on them — which is why
        // a 2px hover took ~20 seconds to resolve, and only via physics drift.
        //
        // Gravity now always runs, so any gap closes immediately. The downward
        // speed is clamped while grounded (below) so simply standing around never
        // accumulates fall velocity, which is what the original early-return was
        // really protecting against.

        float baseGravity = Physics2D.gravity.y * gravityMultiplier;
        float mult;

        if (rb.velocity.y < 0f)
        {
            // Falling — extra gravity, scaled by surface + volume
            mult = EffectiveFallGravity;
        }
        else if (rb.velocity.y > 0f && !jumpHeld)
        {
            // Ascending, button released — cut arc; also floaty in water
            mult = lowJumpMultiplier * EffectiveVolumeGravScale;
        }
        else
        {
            // Full ascent — still floaty in water at the apex
            mult = 1f * EffectiveVolumeGravScale;
        }

        float newVY = rb.velocity.y + baseGravity * mult * Time.fixedDeltaTime;
        newVY = Mathf.Max(newVY, -maxFallSpeed);

        // While grounded, cap how fast gravity may pull. This keeps the player
        // pressed onto the surface — closing any small gap in a frame or two —
        // without letting velocity build up while they are just standing there.
        if (isGrounded && newVY < 0f)
            newVY = Mathf.Max(newVY, -groundSettleSpeed);

        rb.velocity = new Vector2(rb.velocity.x, newVY);
    }

    /// <summary>
    /// Bleeds off velocity while submerged so water feels thick. Applied after
    /// the normal movement/gravity so it damps both swimming and falling.
    /// </summary>
    private void ApplyVolumeDrag()
    {
        if (!IsInVolume) return;

        float k = Mathf.Pow(volumeDrag, Time.fixedDeltaTime);
        float vx = rb.velocity.x * k;
        float vy = rb.velocity.y * k;

        // Damp RISING much harder than falling. Water should make a jump feel
        // laboured without making the player sink like a stone.
        if (vy > 0f)
            vy *= Mathf.Pow(volumeUpwardDrag, Time.fixedDeltaTime);

        rb.velocity = new Vector2(vx, vy);
    }

    /// <summary>
    /// Applies a knockback impulse and suspends horizontal control briefly so the
    /// impulse actually reads on screen instead of being cancelled immediately.
    /// Called by AbstractCharacter when a hit carries knockback.
    /// </summary>
    public void ApplyKnockback(Vector2 velocity)
    {
        rb.velocity    = velocity;
        knockbackUntil = Time.time + knockbackRecovery;

        // Deliberately DON'T force an airborne state here. If the impulse doesn't
        // actually lift the player clear of the ground, the landing transition
        // (isGrounded && !wasGrounded) never fires, so the state would stay
        // Falling forever and the movement handler would early-return — that is
        // the "animation stuck on idle until I jump" bug. The normal apex/landing
        // logic and the recovery guard below handle the state correctly on their own.
    }

    /// <summary>
    /// Launches the player upward — springboards, bouncy creatures, trampolines.
    /// Unlike knockback this does NOT suspend control: the player keeps steering
    /// mid-bounce, which is what makes a spring feel good rather than punishing.
    /// Air jumps are refreshed so a bounce can be chained into a jump.
    /// </summary>
    public void Bounce(float upwardVelocity)
    {
        rb.velocity    = new Vector2(rb.velocity.x, upwardVelocity);
        jumpsRemaining = maxJumps;
        state          = State.Jumping;   // lets the existing apex logic take over
    }

    /// <summary>True while a knockback impulse still owns the player's movement.</summary>
    public bool InKnockback => Time.time < knockbackUntil;

    private void ApplyHorizontalMovement()
    {
        // While knocked back the impulse owns horizontal velocity. Re-applying
        // input-driven movement here would cancel it out within a frame.
        if (InKnockback) return;

        float h         = GatedHorizontal;
        float targetVX  = h * CurrentMoveSpeed;
        float accelBase = isGrounded ? EffectiveGroundAccel : airAcceleration;
        float accel     = Mathf.Abs(h) > 0.01f
                          ? accelBase
                          : accelBase * EffectiveDecelMult;

        float newVX = Mathf.MoveTowards(rb.velocity.x, targetVX, accel * Time.fixedDeltaTime);
        rb.velocity = new Vector2(newVX, rb.velocity.y);
    }

    private void ExecuteJump()
    {
        rb.velocity = new Vector2(rb.velocity.x, EffectiveJumpHeight);
        // Don't consume a jump charge while swimming — infinite jump keeps the pool full
        if (!InfiniteJumpInVolume)
            jumpsRemaining = Mathf.Max(0, jumpsRemaining - 1);
        if (isGrounded) jumpGroundCooldown = JUMP_COOLDOWN;
        state = State.Jumping;
        SetAnim(direction == Direction.Right ? "JumpRight" : "JumpLeft");
    }

    private bool CheckGrounded()
    {
        if (jumpGroundCooldown > 0f) return false;

        // Standing on a one-way platform counts as grounded. The rider owns that
        // state because platform colliders are triggers and are deliberately not
        // part of physics collision at all.
        if (platformRider != null && platformRider.IsSupported) return true;

        if (groundCheck == null) return false;

        // Solid ground only — skip trigger colliders so a platform strip the
        // player is passing UP through never reads as ground.
        var hits = Physics2D.OverlapCircleAll(groundCheck.position, 6f, ground);
        for (int i = 0; i < hits.Length; i++)
            if (hits[i] != null && !hits[i].isTrigger) return true;

        return false;
    }

    /// <summary>
    /// Drops the player through a one-way platform they're standing on.
    /// Both hand-placed platforms and the map's generated PLATFORM strips use
    /// the OneWayPlatform component, so a single path handles them all.
    /// Returns true if a drop was triggered.
    /// </summary>
    private bool TryDropThroughPlatform()
    {
        // The rider already knows exactly which platform is supporting us, so
        // there is nothing to search for here.
        return platformRider != null && platformRider.TryDropThrough();
    }

    // =========================================================================
    // Landing animation restoration
    // =========================================================================

    private void RestoreGroundAnimation()
    {
        if (vertical < 0f && Mathf.Abs(horizontal) > 0.01f)
        {
            direction = horizontal > 0f ? Direction.Right : Direction.Left;
            state = State.Sneaking;
            SetAnim(direction == Direction.Right ? "SneakRight" : "SneakLeft");
        }
        else if (vertical < 0f)
        {
            state = State.Ducking;
            SetAnim(direction == Direction.Right ? "DuckRight" : "DuckLeft");
        }
        else if (Mathf.Abs(horizontal) > 0.01f)
        {
            direction = horizontal > 0f ? Direction.Right : Direction.Left;
            state = State.Walking;
            SetAnim(direction == Direction.Right ? "WalkRight" : "WalkLeft");
        }
        else
        {
            state = State.Idle;
            SetAnim(direction == Direction.Right ? "IdleRight" : "IdleLeft");
        }
    }

    // =========================================================================
    // Input callbacks
    // =========================================================================

    public void Movement(InputAction.CallbackContext context)
    {
        horizontal = context.ReadValue<Vector2>().x;
        vertical   = context.ReadValue<Vector2>().y;

        // A canceled Value action means the stick/keys returned to neutral.
        if (context.canceled) { horizontal = 0f; vertical = 0f; }

        if (state == State.Jumping || state == State.Falling || state == State.Attacking) return;

        bool up   = vertical >  verticalDeadzone;
        bool down = vertical < -verticalDeadzone;
        bool left  = horizontal < -0.01f;
        bool right = horizontal >  0.01f;

        // UP interrupts everything and holds position (future "look up" hook).
        // Diagonal up-left / up-right also stays put so the player can line up a
        // diagonal bow shot without drifting.
        if (up)
        {
            state = State.Idle;
            SetAnim(direction == Direction.Right ? "IdleRight" : "IdleLeft");
            return;
        }

        // DOWN + a direction crouch-walks; DOWN alone ducks in place.
        if (down)
        {
            if (left)       { SetAnim("SneakLeft");  direction = Direction.Left;  state = State.Sneaking; }
            else if (right) { SetAnim("SneakRight"); direction = Direction.Right; state = State.Sneaking; }
            else
            {
                SetAnim(direction == Direction.Right ? "DuckRight" : "DuckLeft");
                state = State.Ducking;
            }
            return;
        }

        // Plain left/right walking.
        if (left)       { SetAnim("WalkLeft");  direction = Direction.Left;  state = State.Walking; }
        else if (right) { SetAnim("WalkRight"); direction = Direction.Right; state = State.Walking; }
        else
        {
            state = State.Idle;
            SetAnim(direction == Direction.Right ? "IdleRight" : "IdleLeft");
        }
    }

    public void Jump(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            // Holding DOWN + Jump while standing on a one-way platform = drop through it
            if (vertical < -0.3f && TryDropThroughPlatform())
                return;   // consumed by drop-through; don't also jump

            jumpPressedThisFrame = true;
            jumpHeld = true;
        }
        if (context.canceled)
        {
            jumpHeld = false;
            if (rb.velocity.y > 0f)
                rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * jumpCutFraction);
        }
    }

    public void Attack(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            if (direction != Direction.Right)
                SetAnim(state == State.Ducking ? "DuckAttackLeft"  : "AttackLeft");
            else
                SetAnim(state == State.Ducking ? "DuckAttackRight" : "AttackRight");
        }
        if (context.canceled)
        {
            if (direction != Direction.Right)
            {
                if      (state == State.Ducking) SetAnim("DuckLeft");
                else if (state == State.Walking) SetAnim("WalkLeft");
                else                             SetAnim("IdleLeft");
            }
            else
            {
                if      (state == State.Ducking) SetAnim("DuckRight");
                else if (state == State.Walking) SetAnim("WalkRight");
                else                             SetAnim("IdleRight");
            }
        }
    }

    // =========================================================================
    // Utilities
    // =========================================================================

    public void SetAnim(string animName)
    {
        if (animator == null) return;
        IEnumerable<string> others = from s in animator.parameters
                                     where s.name != animName
                                     select s.name;
        foreach (string s in others) animator.SetBool(s, false);
        animator.SetBool(animName, true);
    }
}
