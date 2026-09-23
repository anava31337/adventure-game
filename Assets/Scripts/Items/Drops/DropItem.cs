using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class DropItem : AbstractItem
{
    // Destroy() is deferred to the end of the frame, so OnTriggerEnter2D can fire
    // more than once for the same item before it actually disappears — e.g. when
    // two colliders overlap it on the same step. Without this guard one coin could
    // be collected twice.
    private bool _collected;

    protected void OnTriggerEnter2D(Collider2D collider)
    {
        if (_collected) return;

        var player = collider.GetComponent<Player>();
        if (player == null) return;

        _collected = true;

        // Call Use directly. The previous `interact += Use; interact(...)` added
        // another subscription on every trigger, so the handler list grew with
        // each contact instead of firing once.
        Use(player);
    }
}