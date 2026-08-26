// =============================================================================
// Patrol.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Walks left/right for a stretch of time, pauses, then reverses.
//
// RANDOMISATION: when several creatures of the same type spawn together they
// otherwise march in perfect lockstep, which looks mechanical. Turning on
// "Randomize On Start" rolls the speed, leg duration, pause duration, and
// starting direction from the ranges below, so a group of Scuttlers scatters
// chaotically instead of mirroring each other.
//
// Timing is plain seconds now. The previous version added and destroyed a Timer
// component on every leg of every patrol; WaitForSeconds does the same job with
// no per-cycle allocation.
// =============================================================================

using System.Collections;
using UnityEngine;

public class Patrol : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 24f;
    [SerializeField] private bool  startLeft = true;

    [Header("Timing (seconds)")]
    [Tooltip("How long the creature walks before pausing.")]
    [SerializeField] private float walkTime = 2f;
    [Tooltip("How long it holds still before turning around.")]
    [SerializeField] private float holdTime = 1f;

    [Header("Randomisation")]
    [Tooltip("Roll speed/timing/direction on spawn so identical creatures don't " +
             "move in lockstep. Essential for erratic swarms like Scuttlers.")]
    public bool randomizeOnStart = false;

    [Tooltip("Speed is rolled between these two values.")]
    public MinMaxRange speedRange = new MinMaxRange(18f, 42f);
    [Tooltip("Walk-leg duration in seconds, rolled between these two values.")]
    public MinMaxRange walkTimeRange = new MinMaxRange(0.6f, 2.6f);
    [Tooltip("Pause duration in seconds, rolled between these two values.")]
    public MinMaxRange holdTimeRange = new MinMaxRange(0.15f, 1.2f);
    [Tooltip("Also randomise which way it sets off.")]
    public bool randomizeStartDirection = true;

    // ── Runtime ───────────────────────────────────────────────────────────────
    private float             horizontal;
    private bool              walkingLeft;
    private AbstractCharacter character;
    private Rigidbody2D       rb;
    private Coroutine         routine;

    private void OnEnable()
    {
        character = GetComponent<AbstractCharacter>();
        rb        = GetComponent<Rigidbody2D>();

        if (randomizeOnStart) Randomize();

        walkingLeft = startLeft;
        routine     = StartCoroutine(PatrolLoop());
    }

    private void OnDisable()
    {
        if (routine != null) StopCoroutine(routine);
        horizontal = 0f;
    }

    private void Update()
    {
        if (rb != null)
            rb.velocity = new Vector2(horizontal * speed, rb.velocity.y);
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Rolls new movement values from the ranges above. Called automatically when
    /// Randomize On Start is ticked, and can be called by a spawner so each child
    /// creature behaves differently from its siblings.
    /// </summary>
    public void Randomize()
    {
        speed    = speedRange.Random();
        walkTime = walkTimeRange.Random();
        holdTime = holdTimeRange.Random();
        if (randomizeStartDirection) startLeft = Random.value < 0.5f;
    }

    /// <summary>Force the patrol direction (used by spawners to fling siblings apart).</summary>
    public void SetDirection(bool goLeft)
    {
        startLeft   = goLeft;
        walkingLeft = goLeft;
        horizontal  = goLeft ? -1f : 1f;
    }

    /// <summary>
    /// Immediately turn around. Called by EnemyLedgeStop when the creature
    /// reaches a platform edge or wall.
    /// </summary>
    public void ReverseDirection()
    {
        walkingLeft = !walkingLeft;
        horizontal  = walkingLeft ? -1f : 1f;
        character?.SetAnim(walkingLeft ? "WalkLeft" : "WalkRight");
    }

    // =========================================================================
    // Patrol loop
    // =========================================================================

    private IEnumerator PatrolLoop()
    {
        while (true)
        {
            // ── Walk leg ────────────────────────────────────────────────────
            horizontal = walkingLeft ? -1f : 1f;
            character?.SetAnim(walkingLeft ? "WalkLeft" : "WalkRight");
            yield return new WaitForSeconds(walkTime);

            // ── Pause ───────────────────────────────────────────────────────
            horizontal = 0f;
            character?.SetAnim(walkingLeft ? "IdleLeft" : "IdleRight");
            yield return new WaitForSeconds(holdTime);

            // ── Turn around ─────────────────────────────────────────────────
            walkingLeft = !walkingLeft;

            // Re-roll timings each cycle when randomising, so the creature stays
            // unpredictable rather than settling into a fixed rhythm.
            if (randomizeOnStart)
            {
                walkTime = walkTimeRange.Random();
                holdTime = holdTimeRange.Random();
            }
        }
    }
}
