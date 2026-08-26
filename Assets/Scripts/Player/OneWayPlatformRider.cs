// =============================================================================
// OneWayPlatformRider.cs   —   Assets/Scripts/Player/
//
// Attach to the Player, alongside CharacterController2D.
//
// Owns ALL one-way platform behaviour, deterministically and pixel-exactly:
//
//   LANDING  — Each physics step it sweeps the player's feet from where they
//              were to where they now are. If that segment crosses a platform
//              surface while descending, the player is snapped so the bottom of
//              their collider sits EXACTLY on the surface and vertical velocity
//              is zeroed. Because it tests the swept segment rather than the
//              current position, a fast fall can never tunnel past a platform,
//              and the player can never end up inside one.
//
//   STANDING — While supported, the feet are re-snapped to the surface every
//              step. Platform colliders are triggers, so nothing else is holding
//              the player up and nothing can push them around.
//
//   RELEASE  — Jumping, walking off the end, or a deliberate Down+Jump drop.
//
// Because platform colliders are triggers, the physics engine is never asked to
// resolve a one-way collision, which removes every source of the embedding and
// hovering artefacts entirely.
//
// CharacterController2D calls TickPlatforms() at the end of its FixedUpdate, so
// this always runs AFTER gravity and movement have been applied for the step.
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class OneWayPlatformRider : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("The player's main collider. Auto-found if left empty.")]
    public Collider2D playerCollider;

    [Header("Tuning")]
    [Tooltip("Extra distance added to the downward sweep (px). Covers the small " +
             "gap between a resting position and the surface. 1–2 is plenty.")]
    public float sweepSkin = 1.5f;

    [Tooltip("How far below the surface the feet may drift before the player is " +
             "considered to have left the platform (px).")]
    public float releaseSlack = 4f;

    [Tooltip("Once the feet are this far below a platform being dropped through, " +
             "it stops being ignored (px). Small — it only needs to clear the surface.")]
    public float dropClearMargin = 2f;

    [Tooltip("Safety cap on a drop-through, in seconds. Normally the drop clears " +
             "as soon as the feet pass the surface; this only matters if something " +
             "interrupts the fall.")]
    public float dropSafetyTime = 0.5f;

    [Tooltip("Horizontal span used when deciding the player has LEFT a platform, " +
             "as a fraction of their width. Slightly under 1 stops them clinging " +
             "by the very edge of their sprite.")]
    [Range(0.5f, 1f)] public float widthFraction = 0.9f;

    [Tooltip("Horizontal span used when LANDING, as a fraction of the player's " +
             "width. Full width (1) is correct here — anything narrower lets a " +
             "player who is drifting sideways slip past a platform they visibly " +
             "overlapped, which looks like clipping straight through it.")]
    [Range(0.5f, 1.2f)] public float landWidthFraction = 1f;

    [Tooltip("Maximum downward speed (px/s) while dropping through a platform. " +
             "Caps how far the player can travel in one physics step so they " +
             "cannot tunnel past the next platform down when platforms are close " +
             "together. 0 disables the cap.")]
    public float dropFallSpeedCap = 260f;

    // ── State ─────────────────────────────────────────────────────────────────
    private Rigidbody2D    _rb;
    private OneWayPlatform _current;      // platform currently supporting us
    private float          _prevFeetY;
    private bool           _hasPrev;
    // The specific platform being dropped through. Only this one is ignored, so
    // the player can still land on the very next platform underneath it.
    private OneWayPlatform _droppingThrough;
    private float          _dropSafetyTimer;

    /// <summary>True while standing on a one-way platform (counts as grounded).</summary>
    public bool IsSupported => _current != null;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (playerCollider == null) playerCollider = GetComponent<Collider2D>();
    }

    private void OnDisable()
    {
        _current = null;
        _hasPrev = false;
    }

    // =========================================================================
    // Main step — called by CharacterController2D after movement is applied
    // =========================================================================

    public void TickPlatforms()
    {
        if (playerCollider == null) return;

        float feetY = playerCollider.bounds.min.y;

        // A drop-through ignores ONE platform, and only until the feet have
        // actually passed below it. Previously this was a blanket timer that
        // ignored EVERY platform for its duration, which is why dropping from the
        // top platform sailed straight past the next one down.
        if (_droppingThrough != null)
        {
            _dropSafetyTimer -= Time.fixedDeltaTime;
            bool cleared = feetY < _droppingThrough.SurfaceY - dropClearMargin;
            if (cleared || _dropSafetyTimer <= 0f)
                _droppingThrough = null;
        }
        float minX  = playerCollider.bounds.center.x - (playerCollider.bounds.extents.x * widthFraction);
        float maxX  = playerCollider.bounds.center.x + (playerCollider.bounds.extents.x * widthFraction);

        if (_current != null)
        {
            UpdateSupported(feetY, minX, maxX);
        }
        else
        {
            // Cap fall speed while dropping through, so closely-stacked platforms
            // cannot be skipped by a single large physics step.
            if (_droppingThrough != null && dropFallSpeedCap > 0f &&
                _rb.velocity.y < -dropFallSpeedCap)
            {
                _rb.velocity = new Vector2(_rb.velocity.x, -dropFallSpeedCap);
            }

            float landMinX = playerCollider.bounds.center.x - (playerCollider.bounds.extents.x * landWidthFraction);
            float landMaxX = playerCollider.bounds.center.x + (playerCollider.bounds.extents.x * landWidthFraction);
            TryLand(feetY, landMinX, landMaxX);
        }

        // Record AFTER resolving, so next step compares against a settled value.
        _prevFeetY = playerCollider.bounds.min.y;
        _hasPrev   = true;
    }

    // =========================================================================
    // Landing — swept crossing test
    // =========================================================================

    private void TryLand(float feetY, float minX, float maxX)
    {
        if (!_hasPrev)            { return; }
        if (_rb.velocity.y > 0f)  return;   // rising: pass straight through

        // The band the feet travelled through this step, plus a small skin so a
        // player resting a hair above a surface still registers.
        float top    = _prevFeetY + 0.01f;
        float bottom = feetY - sweepSkin;

        OneWayPlatform best      = null;
        float          bestY     = float.NegativeInfinity;

        var list = OneWayPlatform.All;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (p == null) continue;
            if (p == _droppingThrough) continue;   // the one we're dropping through
            if (!p.OverlapsHorizontally(minX, maxX)) continue;

            float surface = p.SurfaceY;

            // Did the feet cross this surface downward during the step?
            if (surface <= top && surface >= bottom)
            {
                // If several qualify (stacked rows), take the highest — that is
                // the first one the falling player would have met.
                if (surface > bestY) { bestY = surface; best = p; }
            }
        }

        if (best != null) Land(best, bestY);
    }

    private void Land(OneWayPlatform platform, float surfaceY)
    {
        _current = platform;

        // Touching down ends the drop outright. Without this, a drop state could
        // survive the landing and let the very next step fall straight back out
        // of the platform we just landed on.
        _droppingThrough = null;
        _dropSafetyTimer = 0f;

        SnapToSurface(surfaceY);
    }

    // =========================================================================
    // Standing — hold the player exactly on the surface
    // =========================================================================

    private void UpdateSupported(float feetY, float minX, float maxX)
    {
        // Jumped, or knocked upward → let go immediately.
        if (_rb.velocity.y > 0.01f)                      { Release(); return; }
        if (_current == null)                            { Release(); return; }
        if (_current == _droppingThrough)                { Release(); return; }

        // Walked off the end of the platform.
        if (!_current.OverlapsHorizontally(minX, maxX))  { Release(); return; }

        float surfaceY = _current.SurfaceY;

        // Fell too far below it (e.g. the platform moved, or an odd shove).
        if (feetY < surfaceY - releaseSlack)             { Release(); return; }

        SnapToSurface(surfaceY);
    }

    /// <summary>
    /// Places the bottom of the player's collider exactly on the surface and
    /// cancels downward velocity. This is the whole trick: position is authored
    /// directly rather than negotiated with the collision solver, so the result
    /// is exact every time instead of within a pixel or two.
    /// </summary>
    private void SnapToSurface(float surfaceY)
    {
        float feetY  = playerCollider.bounds.min.y;
        float delta  = surfaceY - feetY;

        if (Mathf.Abs(delta) > 0.0001f)
            _rb.position = new Vector2(_rb.position.x, _rb.position.y + delta);

        if (_rb.velocity.y < 0f)
            _rb.velocity = new Vector2(_rb.velocity.x, 0f);
    }

    private void Release() => _current = null;

    // =========================================================================
    // Drop-through
    // =========================================================================

    /// <summary>
    /// Called by the controller on Down+Jump. Returns true if the player was on a
    /// platform and is now falling through it.
    /// </summary>
    public bool TryDropThrough()
    {
        if (_current == null) return false;

        // Ignore ONLY this platform, and only until the feet clear it. Every other
        // platform stays active, so the player lands on the next one down rather
        // than falling through the whole stack.
        _droppingThrough = _current;
        _dropSafetyTimer = dropSafetyTime;
        Release();
        return true;
    }
}
