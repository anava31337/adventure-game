// =============================================================================
// BowAbility.cs   —   Assets/Scripts/Player/Abilities/
//
// Attach to the Player GameObject (alongside PlayerAbilityManager).
// Assign to Ability2 slot via PlayerAbilityManager.AddAbility<BowAbility>().
//
// Requires the player to have BOTH a Bow AND arrows (tracked separately):
//   ability.HasBow   = true   (set when bow item is acquired)
//   ability.HasArrows = true  (set when arrow item/pickup is acquired)
//
// ARROW PREFAB:
//   Create a prefab with: SpriteRenderer, Rigidbody2D (gravity=0), small
//   CircleCollider2D (isTrigger), and the Arrow.cs component.
//   Assign it to the arrowPrefab field in the Inspector.
//
// FIRE DIRECTIONS:
//   Straight right / left  (facing direction)
//   Diagonal 45° upward    (when holding UP while firing)
//
// The arrow's arc comes from Arrow.cs applying gravity — no special setup needed.
// =============================================================================

using UnityEngine;

public class BowAbility : AbstractAbility
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Arrow")]
    [Tooltip("Prefab with SpriteRenderer + Rigidbody2D + Arrow.cs component.")]
    public GameObject arrowPrefab;

    [Tooltip("Arrow travel speed (px/s).")]
    public float arrowSpeed = 300f;

    [Range(0.5f, 4f)]
    [Tooltip("Multiplier on launch speed for diagonal (arcing) shots. " +
             "Higher = flatter, farther arc. 1 = same speed as straight shots.")]
    public float arcIntensity = 1.6f;

    [Tooltip("Cooldown between shots (seconds).")]
    public float fireCooldown = 0.4f;

    [Tooltip("Offset from player centre where arrows spawn (px). " +
             "Y raised to roughly chest/bow height.")]
    public Vector2 spawnOffset = new Vector2(8f, 24f);

    [Header("Ownership")]
    [Tooltip("Player has the bow item in inventory.")]
    public bool hasBow    = true;

    [Tooltip("Player has at least one arrow. Set false when quiver is empty.")]
    public bool hasArrows = true;

    // =========================================================================
    // Private
    // =========================================================================

    private float _cooldownTimer;

    // =========================================================================
    // AbstractAbility overrides
    // =========================================================================

    public override string AbilityName => "Bow";
    public override bool CanUse => hasBow && hasArrows && _cooldownTimer <= 0f;

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
    }

    // Called by PlayerAbilityManager when Ability2 is pressed
    public override void OnAbilityStarted()
    {
        if (!CanUse) return;
        FireArrow();
    }

    // =========================================================================
    // Fire
    // =========================================================================

    private void FireArrow()
    {
        Debug.Log("[BowAbility] Firing arrow!");
        if (arrowPrefab == null)
        {
            Debug.LogWarning("[BowAbility] arrowPrefab not assigned.");
            return;
        }

        Vector2 dir    = GetFireDirection();
        bool    diagonal = Controller != null && Controller.CurrentVerticalInput > 0.3f;

        // Spawn at chest/bow height in front of the player.
        // Horizontal component flips with facing; vertical stays at bow height.
        float   facingSign = (Controller != null && Controller.FacingRight) ? 1f : -1f;
        Vector2 origin = (Vector2)transform.position
                       + new Vector2(spawnOffset.x * facingSign, spawnOffset.y);

        var go    = Instantiate(arrowPrefab, origin, Quaternion.identity);
        var arrow = go.GetComponent<Arrow>();
        if (arrow == null)
        {
            Debug.LogWarning("[BowAbility] arrowPrefab has no Arrow component.");
            Destroy(go);
            return;
        }

        // Only diagonal shots arc under gravity; straight shots fly level.
        arrow.affectedByGravity = diagonal;

        // Tell the arrow who fired it. Without this the arrow has NO owner, and
        // CombatRules treats owner-less damage as an environmental hazard that
        // hits anyone — which is why arrows were damaging the player.
        arrow.owner = GetComponent<AbstractCharacter>();

        // Match the shooter's sorting layer/order so the arrow draws in the same
        // plane as the character instead of behind the map art.
        arrow.MatchSortingTo(GetComponentInChildren<SpriteRenderer>());
        // Diagonal shots launch faster (×arcIntensity) so the arc carries farther.
        float launchSpeed = diagonal ? arrowSpeed * arcIntensity : arrowSpeed;
        arrow.Launch(dir, launchSpeed, GetComponent<AbstractCharacter>());
        _cooldownTimer = fireCooldown;

        // TODO: consume one arrow from inventory, set hasArrows = false when empty
    }

    // =========================================================================
    // Direction
    // =========================================================================

    /// <summary>
    /// Four fire directions: left, right, diagonal-up-left, diagonal-up-right.
    /// Hold UP (vertical input > 0) for diagonal. Facing determines left vs right.
    /// The arrow then arcs naturally under gravity — no extra math needed.
    /// </summary>
    private Vector2 GetFireDirection()
    {
        bool facingRight = Controller != null ? Controller.FacingRight : true;
        bool upHeld      = Controller != null && Controller.CurrentVerticalInput > 0.3f;

        if (upHeld)
            return facingRight
                   ? new Vector2(0.707f, 0.707f)    // 45° up-right
                   : new Vector2(-0.707f, 0.707f);   // 45° up-left

        return facingRight ? Vector2.right : Vector2.left;
    }
}
