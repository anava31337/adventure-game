// =============================================================================
// EnemyJump.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Modular hop behaviour. Attach to any enemy with a Rigidbody2D and
// AbstractCharacter. The enemy periodically jumps while grounded.
// Pairs naturally with Patrol.cs for hopping patrollers.
// =============================================================================

using UnityEngine;

public class EnemyJump : MonoBehaviour
{
    [Header("Jump")]
    [Tooltip("Upward velocity applied on each jump (px/s).")]
    public float jumpHeight = 260f;

    [Tooltip("Seconds between jump attempts.")]
    public float jumpInterval = 2f;

    [Tooltip("Random +/- variation added to the interval so hops aren't robotic.")]
    public float intervalJitter = 0.5f;

    [Header("Ground Check")]
    [Tooltip("Empty child transform at the enemy's feet.")]
    public Transform groundCheck;
    [Tooltip("Radius of the ground overlap check (px).")]
    public float groundCheckRadius = 6f;
    [Tooltip("Layer(s) considered solid ground.")]
    public LayerMask groundLayer;

    // NOTE: gravity tunables used to live here. They now belong to
    // AbstractCharacter, so every character falls with identical numbers and
    // there is no per-component value to drift out of sync.

    private Rigidbody2D      rb;
    private AbstractCharacter character;
    private float           timer;
    private bool            isGrounded;

    private void OnEnable()
    {
        rb        = GetComponent<Rigidbody2D>();
        character = GetComponent<AbstractCharacter>();
        // Gravity is owned by AbstractCharacter now — one model for every
        // character — so this component only supplies the hop impulse.
        timer = NextInterval();
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        // Prefer the shared ground state from AbstractCharacter; fall back to a
        // local overlap check for objects that don't have one.
        isGrounded = character != null
                   ? character.IsOnGround
                   : (groundCheck != null &&
                      Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer));

        // Jump timing
        timer -= Time.fixedDeltaTime;
        if (timer <= 0f && isGrounded)
        {
            if (character != null) character.AddVerticalImpulse(jumpHeight);
            else                    rb.velocity = new Vector2(rb.velocity.x, jumpHeight);
            timer = NextInterval();
        }
    }

    private float NextInterval() => jumpInterval + Random.Range(-intervalJitter, intervalJitter);
}
