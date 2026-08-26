// =============================================================================
// SpawnLaunch.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Added automatically to a creature spawned by SpawnEnemy. It owns the pop-out
// arc, then hands the creature back to its own AI once it lands.
//
// WHY THIS LIVES ON THE SPAWNED CREATURE:
// The arc used to run as a coroutine on the SpawnEnemy component of the DYING
// parent. AbstractCharacter destroys that GameObject on the line immediately
// after firing OnDeath, so the coroutine was killed the instant it started.
// Gravity was never applied (the children only drifted down under whatever the
// prefab did on its own) and, worse, the line that re-enabled Patrol never ran —
// which is why spawned enemies ended up frozen in place forever.
//
// Running it here means the arc belongs to an object that is alive for its whole
// duration, so it always completes.
// =============================================================================

using UnityEngine;

public class SpawnLaunch : MonoBehaviour
{
    private Rigidbody2D _rb;
    private Patrol      _patrol;
    private float       _gravity;
    private float       _timeout;
    private LayerMask   _groundLayer;
    private float       _elapsed;
    private bool        _running;
    private bool        _leftGround;

    /// <summary>
    /// Starts the arc. The creature's own movement script is suspended until it
    /// lands, so the pop reads cleanly instead of being overwritten every frame.
    /// </summary>
    public void Begin(Vector2 velocity, float gravity, float timeout,
                      LayerMask groundLayer, Patrol patrol)
    {
        _rb          = GetComponent<Rigidbody2D>();
        _patrol      = patrol;
        _gravity     = gravity;
        _timeout     = timeout;
        _groundLayer = groundLayer;
        _elapsed     = 0f;
        _leftGround  = false;
        _running     = true;

        if (_patrol != null) _patrol.enabled = false;
        if (_rb != null)     _rb.velocity    = velocity;
    }

    private void FixedUpdate()
    {
        if (!_running || _rb == null) { return; }

        _elapsed += Time.fixedDeltaTime;

        // Own the gravity for the arc, independent of the prefab's gravityScale.
        // These enemy prefabs typically use gravityScale 0 with gravity supplied by
        // their movement scripts — which are suspended right now — so without this
        // there would be nothing pulling them back down.
        _rb.velocity = new Vector2(_rb.velocity.x,
                                   _rb.velocity.y - _gravity * Time.fixedDeltaTime);

        // Note when we've actually cleared the ground, so the landing test can't
        // fire on the very first step while still overlapping the spawn point.
        if (!_leftGround && _rb.velocity.y < 0f && !Grounded())
            _leftGround = true;

        bool landed = _leftGround && _rb.velocity.y <= 0f && Grounded();

        if (landed || _elapsed >= _timeout)
            Finish();
    }

    private bool Grounded()
    {
        // A short probe under the creature's own collider.
        var col = GetComponent<Collider2D>();
        if (col == null) return false;

        Vector2 origin = new Vector2(col.bounds.center.x, col.bounds.min.y + 0.5f);
        return Physics2D.Raycast(origin, Vector2.down, 2.5f, _groundLayer);
    }

    /// <summary>Hands control back to the creature's own behaviours.</summary>
    private void Finish()
    {
        _running = false;
        if (_patrol != null) _patrol.enabled = true;
        Destroy(this);   // job done; remove the component
    }
}
