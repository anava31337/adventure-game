// =============================================================================
// WhipAbility.cs   —   Assets/Scripts/Player/Abilities/
//
// Attach to the Player GameObject (alongside PlayerAbilityManager).
// Assign to Ability1 slot via PlayerAbilityManager.AddAbility<WhipAbility>().
//
// HOW IT WORKS:
//   Press Ability1 → whip extends forward (or diagonally up if holding UP)
//   Tip reaches a HookPoint → player latches on and swings (pendulum physics)
//   Hold LEFT / RIGHT while latched → "pump" the swing for more momentum
//   Release Ability1 → player is launched at current swing velocity
//   Tip misses everything → whip retracts automatically
//
// INPUT (tested with direct keyboard — wire to PlayerAbilityManager for production):
//   OnAbilityStarted()  → begin extending
//   OnAbilityHeld()     → poll for pumping while hooked
//   OnAbilityReleased() → detach
//
// SETUP:
//   • Add a LineRenderer component to the Player GameObject
//   • Assign your HookPoint GameObjects to the "Trigger" layer
//   • hookPointLayer auto-defaults to "Trigger" — no Inspector change needed
//     unless you want HookPoints on a different layer
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class WhipAbility : AbstractAbility
{
    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Whip")]
    [Tooltip("Max reach in pixels (3 blocks × 16 px = 48).")]
    public float maxWhipLength    = 48f;
    [Tooltip("Minimum reach before a HookPoint will latch. Prevents tiny awkward swings " +
             "when a hook is right next to the player.")]
    public float minHookLength    = 20f;
    [Tooltip("Speed the whip tip travels outward/inward (px/s).")]
    public float extensionSpeed   = 320f;
    [Tooltip("Height above the player's pivot (feet) where the whip emits (px). " +
             "Set to roughly hand/shoulder height.")]
    public float originHeight      = 24f;
    [Tooltip("Layer mask for HookPoint objects. Defaults to the 'Trigger' layer if left unset.")]
    public LayerMask hookPointLayer;
    [Tooltip("Radius of the tip-detection circle when sweeping for HookPoints (px).")]
    public float hookDetectRadius = 5f;

    [Header("Damage")]
    [Tooltip("Damage the whip tip deals on contact. Add a Fire entry for a " +
             "flaming whip; targets weak to that type take extra.")]
    public List<DamagePacket> damage = new List<DamagePacket>
    {
        new DamagePacket(DamageType.Physical, 1)
    };
    [Tooltip("Layers the whip can damage (your enemy layer).")]
    public LayerMask damageLayers = ~0;
    [Tooltip("Radius around the whip tip that registers a hit (px).")]
    public float damageRadius = 5f;
    [Tooltip("Horizontal push applied to whatever the whip strikes (px/s). 0 = none.")]
    public float knockbackForce = 60f;

    [Header("Swing")]
    [Tooltip("Effective gravity for the pendulum. Leave 0 to read from CharacterController2D.")]
    public float gravityOverride   = 0f;
    [Tooltip("Force added per second when the player pumps LEFT or RIGHT while hooked.")]
    public float pumpForce         = 120f;
    [Tooltip("Max swing angle from vertical (degrees). 85 = nearly horizontal at full pump.")]
    public float maxSwingAngleDeg  = 82f;
    [Tooltip("Slight air-resistance damping per second (0 = no damping).")]
    public float angularDamping    = 0.55f;

    [Header("Visuals")]
    [Tooltip("LineRenderer on the Player GameObject used to draw the whip rope.")]
    public LineRenderer whipLine;

    // =========================================================================
    // Private state
    // =========================================================================

    private enum WhipState { Idle, Extending, Hooked, Retracting }
    private WhipState _state = WhipState.Idle;

    // Whip tip in world space
    private Vector2 _tipPos;
    private Vector2 _whipDir;
    private float   _currentLength;   // whip length during extend/retract (px)
    // Targets already struck during the current swing (cleared on each new swing)
    private readonly HashSet<GameObject> _hitThisSwing = new HashSet<GameObject>();

    // Swing state
    private Vector2 _pivotPos;
    private float   _ropeLength;
    private float   _angle;          // radians from vertical downward
    private float   _angularVel;     // rad/s

    // Highlighted hook point (for visual feedback)
    private HookPoint _nearestHook;

    // =========================================================================
    // AbstractAbility overrides
    // =========================================================================

    public override string AbilityName => "Whip";

    protected override void Awake()
    {
        base.Awake();

        // Default to the Trigger layer so HookPoints don't need their own dedicated layer.
        // Override in the Inspector if you want a different layer.
        if (hookPointLayer.value == 0)
            hookPointLayer = LayerMask.GetMask("Trigger");

        if (whipLine == null) whipLine = GetComponent<LineRenderer>();
        if (whipLine != null)
        {
            whipLine.positionCount  = 2;
            whipLine.useWorldSpace  = true;       // we feed world-space positions
            whipLine.startWidth     = 1f;         // visible at 1 PPU pixel scale
            whipLine.endWidth       = 1f;
            whipLine.numCapVertices = 2;

            // Assign a default sprite/unlit material so it doesn't render magenta.
            // Sprites-Default is always available and respects vertex colour.
            if (whipLine.material == null || whipLine.material.shader == null)
                whipLine.material = new Material(Shader.Find("Sprites/Default"));

            whipLine.startColor = new Color(0.85f, 0.7f, 0.4f, 1f);  // whip-leather tan
            whipLine.endColor   = new Color(0.85f, 0.7f, 0.4f, 1f);

            // Render above world tiles — match or exceed your sprite sorting order
            whipLine.sortingLayerName = "Default";
            whipLine.sortingOrder     = 100;

            whipLine.enabled = false;
        }
    }

    public override void OnAbilityStarted()
    {
        if (_state != WhipState.Idle) return;
        BeginExtend();
    }

    public override void OnAbilityReleased()
    {
        if (_state == WhipState.Hooked)
            Detach();
    }

    // =========================================================================
    // Unity loop
    // =========================================================================

    private void Update()
    {
        switch (_state)
        {
            case WhipState.Extending:  UpdateExtending();  break;
            case WhipState.Retracting: UpdateRetracting(); break;
        }
        UpdateLineRenderer();
        UpdateNearestHookHighlight();
    }

    private void FixedUpdate()
    {
        if (_state == WhipState.Hooked)
            UpdateSwingPhysics();
    }

    // =========================================================================
    // State: Extend
    // =========================================================================

    /// <summary>The world-space point the whip emits from (player pivot + height).</summary>
    private Vector2 OriginPos => (Vector2)transform.position + new Vector2(0f, originHeight);

    private void BeginExtend()
    {
        Debug.Log("[WhipAbility] Whip extending!");
        _whipDir       = GetWhipDirection();
        _currentLength = 0f;
        _hitThisSwing.Clear();   // fresh swing — everything can be hit again
        _tipPos        = OriginPos;
        _state         = WhipState.Extending;
        if (whipLine) whipLine.enabled = true;
    }

    private void UpdateExtending()
    {
        // Damage anything the tip sweeps through — each target only once per swing.
        DamageAtTip();

        // Track length as a scalar; tip is always relative to the CURRENT origin,
        // so the whip stays straight even while the player jumps or moves.
        _currentLength += extensionSpeed * Time.deltaTime;
        _tipPos = OriginPos + _whipDir * _currentLength;

        // Check for HookPoint at the current tip — but only once past minHookLength
        if (_currentLength >= minHookLength)
        {
            Collider2D hit = Physics2D.OverlapCircle(_tipPos, hookDetectRadius, hookPointLayer);
            if (hit != null)
            {
                var hp = hit.GetComponent<HookPoint>() ?? hit.GetComponentInParent<HookPoint>();
                if (hp != null) { Attach(hp.transform.position); return; }
            }
        }

        // Max range reached — retract
        if (_currentLength >= maxWhipLength)
            BeginRetract();
    }

    // =========================================================================
    // State: Retract (missed)
    // =========================================================================

    private void BeginRetract()
    {
        _state = WhipState.Retracting;
    }

    private void UpdateRetracting()
    {
        // Shrink length; tip stays relative to current origin (follows the player)
        _currentLength -= extensionSpeed * Time.deltaTime;
        _tipPos = OriginPos + _whipDir * Mathf.Max(0f, _currentLength);

        if (_currentLength <= 1f)
        {
            _state = WhipState.Idle;
            if (whipLine) whipLine.enabled = false;
        }
    }

    // =========================================================================
    // State: Hooked — pendulum swing
    // =========================================================================

    private void Attach(Vector2 pivotWorldPos)
    {
        _pivotPos   = pivotWorldPos;
        _tipPos     = pivotWorldPos;
        _state      = WhipState.Hooked;
        _ropeLength = Vector2.Distance(transform.position, _pivotPos);

        // Initialise angle from current player offset
        Vector2 offset = (Vector2)transform.position - _pivotPos;
        _angle = Mathf.Atan2(offset.x, -offset.y);

        // Convert current linear velocity to angular velocity
        // tangential direction = perpendicular to rope
        Vector2 perpDir = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle));
        _angularVel = RB != null ? Vector2.Dot(RB.velocity, perpDir) / _ropeLength : 0f;

        // Hand over physics control.
        // Going KINEMATIC is the key fix for the jitter: while Dynamic, every
        // MovePosition is fought by the collision solver and any leftover
        // velocity, so the player visibly vibrates on the rope. Kinematic makes
        // our pendulum the single authority over position.
        if (RB != null)
        {
            RB.velocity        = Vector2.zero;
            RB.angularVelocity = 0f;
            RB.bodyType        = RigidbodyType2D.Kinematic;
        }
        Controller.IsExternallyControlled = true;
    }

    private void UpdateSwingPhysics()
    {
        float dt = Time.fixedDeltaTime;
        float g  = gravityOverride > 0f
                   ? gravityOverride
                   : Mathf.Abs(Physics2D.gravity.y * (Controller != null ? Controller.gravityMultiplier : 100f));

        // Pendulum equation: θ'' = -(g/L) * sin(θ)
        _angularVel += -(g / _ropeLength) * Mathf.Sin(_angle) * dt;

        // Player pumping: horizontal input adds angular impulse
        float h = 0f;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb.leftArrowKey.isPressed  || kb.aKey.isPressed)  h = -1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed)   h =  1f;
        }
        if (UnityEngine.InputSystem.Gamepad.current != null)
        {
            float gph = UnityEngine.InputSystem.Gamepad.current.leftStick.x.ReadValue();
            if (Mathf.Abs(gph) > 0.3f) h = gph;
        }
        if (Mathf.Abs(h) > 0.1f)
            _angularVel += pumpForce * h * dt / _ropeLength;

        // Air damping
        _angularVel *= Mathf.Pow(1f - angularDamping, dt);

        // Integrate angle
        _angle += _angularVel * dt;

        // Hard-clamp angle
        float maxRad = maxSwingAngleDeg * Mathf.Deg2Rad;
        if (Mathf.Abs(_angle) > maxRad)
        {
            _angle     = Mathf.Sign(_angle) * maxRad;
            _angularVel = -_angularVel * 0.1f; // slight bounce
        }

        // Settle: once the swing is nearly stopped near the bottom, snap it to
        // rest instead of letting a tiny residual oscillation bob forever.
        // CRITICAL: skip this while the player is pumping. Hanging straight down
        // means angle≈0 and angularVel≈0 every frame, so without this guard the
        // clamp erased the pump input the instant it was applied — which is why
        // left/right did nothing when hooked from directly below.
        bool pumping = Mathf.Abs(h) > 0.1f;
        if (!pumping && Mathf.Abs(_angularVel) < 0.05f && Mathf.Abs(_angle) < 0.02f)
        {
            _angularVel = 0f;
            _angle      = 0f;
        }

        // Move player to pendulum position
        Vector2 newPos = _pivotPos + new Vector2(Mathf.Sin(_angle), -Mathf.Cos(_angle)) * _ropeLength;
        RB.MovePosition(newPos);
    }

    private void Detach()
    {
        // Restore dynamic physics BEFORE applying the release velocity, or the
        // kinematic body would simply ignore it.
        Vector2 perpDir = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle));
        if (RB != null)
        {
            RB.bodyType = RigidbodyType2D.Dynamic;
            RB.velocity = perpDir * (_angularVel * _ropeLength);
        }

        Controller.IsExternallyControlled = false;
        _state = WhipState.Retracting;
    }

    // =========================================================================
    // Visuals
    // =========================================================================

    private void UpdateLineRenderer()
    {
        if (whipLine == null || !whipLine.enabled) return;
        whipLine.SetPosition(0, OriginPos);
        whipLine.SetPosition(1, _tipPos);
    }

    private void UpdateNearestHookHighlight()
    {
        // Highlight whichever HookPoint is closest to the current whip direction
        if (_state == WhipState.Idle || _state == WhipState.Retracting)
        {
            // Scan nearby HookPoints and highlight the one most in the whip direction
            Collider2D[] nearby = Physics2D.OverlapCircleAll(
                OriginPos, maxWhipLength, hookPointLayer);

            HookPoint best      = null;
            float      bestDot  = 0.5f; // only highlight if fairly well-aligned
            Vector2    fwd      = GetWhipDirection();

            foreach (var col in nearby)
            {
                var hp = col.GetComponent<HookPoint>() ?? col.GetComponentInParent<HookPoint>();
                if (hp == null) continue;
                Vector2 toHp = ((Vector2)col.transform.position - OriginPos).normalized;
                float   dot  = Vector2.Dot(fwd, toHp);
                if (dot > bestDot) { bestDot = dot; best = hp; }
            }

            if (_nearestHook != null && _nearestHook != best)
                _nearestHook.SetHighlighted(false);

            _nearestHook = best;
            if (_nearestHook != null) _nearestHook.SetHighlighted(true);
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Returns the whip fire direction based on player facing + vertical input.
    /// Straight left/right or 45° diagonally upward.
    /// </summary>
    /// <summary>
    /// Deals damage to anything the whip tip touches, using the same direct
    /// IDamageable call the sword and arrows use. Each target is hit at most
    /// once per swing.
    /// </summary>
    private void DamageAtTip()
    {
        // Owner of the whip = the player wielding it. CombatRules then decides
        // who may be struck, using the same rules as the sword and arrows.
        AbstractCharacter wielder = GetComponent<AbstractCharacter>();

        Collider2D[] hits = Physics2D.OverlapCircleAll(_tipPos, damageRadius, damageLayers);
        foreach (var h in hits)
        {
            IDamageable target = CombatRules.ResolveTarget(h, wielder);
            if (target == null) continue;

            GameObject go = target.DamageableObject;
            if (_hitThisSwing.Contains(go)) continue;   // one hit per target per swing
            _hitThisSwing.Add(go);

            var info = new DamageInfo
            {
                source     = gameObject,
                instigator = wielder,
                hitPoint   = _tipPos
            };
            foreach (var p in damage) info.Add(p.type, p.amount);

            if (knockbackForce != 0f)
            {
                float dir = Mathf.Sign(go.transform.position.x - transform.position.x);
                if (dir == 0f) dir = 1f;
                info.knockback = new Vector2(dir * knockbackForce, 0f);
            }

            target.TakeDamage(info);
        }
    }

    private Vector2 GetWhipDirection()
    {
        bool facingRight = Controller != null ? Controller.FacingRight : true;

        // Read the aim vector directly from devices so we always get BOTH axes
        // at the moment of firing, regardless of how the Move action is configured.
        Vector2 aim = Vector2.zero;

        var gp = UnityEngine.InputSystem.Gamepad.current;
        if (gp != null)
        {
            Vector2 stick = gp.leftStick.ReadValue();   // analog — exact angle
            Vector2 dpad  = gp.dpad.ReadValue();
            aim = stick.sqrMagnitude > 0.04f ? stick : dpad;
        }

        // Keyboard fallback / override when no gamepad input present
        if (aim.sqrMagnitude < 0.04f)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                float h = 0f, v = 0f;
                if (kb.leftArrowKey.isPressed  || kb.aKey.isPressed) h -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) h += 1f;
                if (kb.downArrowKey.isPressed  || kb.sKey.isPressed) v -= 1f;
                if (kb.upArrowKey.isPressed    || kb.wKey.isPressed) v += 1f;
                aim = new Vector2(h, v);
            }
        }

        Debug.Log($"[WhipAbility] aim input = {aim}");

        // No directional input → whip straight ahead based on facing
        if (aim.sqrMagnitude < 0.1f)
            return facingRight ? Vector2.right : Vector2.left;

        // Whip follows the exact stick/dpad/key angle (full 360° aiming)
        return aim.normalized;
    }
}
