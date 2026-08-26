// =============================================================================
// OneWayPlatform.cs   —   Assets/Scripts/World/
//
// A passive marker for a one-way platform surface. It holds NO landing logic —
// all of that lives in OneWayPlatformRider on the player.
//
// WHY THE REWRITE:
// Previous versions asked the physics engine to resolve one-way collisions by
// toggling Physics2D.IgnoreCollision every frame. That can never be pixel-exact:
//
//   • Re-enabling collision while the player already overlaps makes Unity
//     depenetrate them, pushing them out in whatever direction is shortest —
//     that is the 1–3px "hovering above the platform" pop.
//   • Physics steps are discrete. Falling fast, the player can pass from above
//     the surface to several pixels below it inside a single FixedUpdate, so
//     collision resolves from INSIDE the strip — that is the "stuck 2–3px in
//     the tile" bug.
//   • At 1 pixel-per-unit, contact offsets and depenetration slop are the same
//     order of magnitude as a whole pixel, so the error is always visible.
//
// The fix is to stop using collision resolution for this at all. The collider
// here is a TRIGGER, so physics never pushes the player anywhere. The rider
// sweeps the player's feet between physics steps, detects the exact frame they
// cross the surface, and snaps them onto it. Landing becomes exact by
// construction rather than by tuning tolerances.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class OneWayPlatform : MonoBehaviour
{
    /// <summary>Every active one-way platform, for the rider to query cheaply.</summary>
    public static readonly List<OneWayPlatform> All = new List<OneWayPlatform>();

    [Tooltip("Seconds this platform stays intangible after a deliberate drop-through.")]
    public float dropThroughTime = 0.35f;

    private Collider2D _col;
    private float      _dropTimer;

    /// <summary>World-space Y of the surface the player stands on.</summary>
    public float SurfaceY => _col.bounds.max.y;

    /// <summary>Horizontal extent of the surface.</summary>
    public float MinX => _col.bounds.min.x;
    public float MaxX => _col.bounds.max.x;

    /// <summary>True while a drop-through is in progress; the rider skips this platform.</summary>
    public bool IsDropping => _dropTimer > 0f;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();

        // A trigger never resolves collision, so the player can never be pushed
        // out of, embedded in, or popped above this platform. Support while
        // standing is applied by the rider instead.
        _col.isTrigger = true;
    }

    private void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void Update()
    {
        if (_dropTimer > 0f) _dropTimer -= Time.deltaTime;
    }

    /// <summary>Makes this platform intangible briefly so the player can drop through.</summary>
    public void DropThrough() => _dropTimer = dropThroughTime;

    /// <summary>True if the given horizontal span overlaps this platform's surface.</summary>
    public bool OverlapsHorizontally(float minX, float maxX)
    {
        return maxX > MinX && minX < MaxX;
    }
}
