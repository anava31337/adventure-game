// =============================================================================
// StaminaSystem.cs   —   Assets/Scripts/Player/
//
// The yellow meter, which is really TWO nested resources sharing one bar:
//
//   ENDURANCE  — the outer capacity. Drains by DISTANCE TRAVELLED on the
//                overworld: roughly one point per tile crossed, not per second.
//                Standing still costs nothing, so the player is spending a
//                journey allowance rather than racing a clock.
//
//   STAMINA    — the inner, fast resource, capped at CURRENT endurance rather
//                than max. Spent by attacking, jumping and swinging in
//                side-scrolling levels, and refills quickly between actions.
//
// Entering a side-scrolling stage sets Stamina to whatever Endurance currently
// is. So a long overworld trek leaves you with a genuinely shorter stamina bar
// for the dungeon at the end of it — which is what ties the two scales together.
//
// FATIGUE:
// Emptying stamina is punished rather than merely inconvenient. The player is
// Fatigued and cannot act AT ALL until the bar refills COMPLETELY — not merely
// above zero — and it refills more slowly while fatigued. That asymmetry makes
// running dry a real mistake, so the player paces themselves instead of mashing
// and waiting out a short cooldown.
// =============================================================================

using System;
using UnityEngine;

// Runs AFTER Player (default 0), so its Start() reads endurance only once
// SetLevelData has populated it. Otherwise it could publish zeros, leaving the
// endurance bar empty for the whole stage — endurance never changes in a
// side-scroller, so nothing would ever correct it.
[DefaultExecutionOrder(50)]
public class StaminaSystem : MonoBehaviour
{
    public enum Mode
    {
        Overworld,     // endurance drains with distance; stamina is not used
        Sidescroller   // stamina is spent on actions and regenerates
    }

    [Header("Mode")]
    [Tooltip("Set on scene load. Auto Detect looks for the map's GROUND layer, " +
             "which only side-scrolling stages generate.")]
    public Mode mode = Mode.Sidescroller;

    [Tooltip("Work the mode out automatically at startup from the loaded map.")]
    public bool autoDetectMode = true;

    [Header("Overworld — endurance by distance")]
    [Tooltip("World units travelled per point of endurance spent. One tile is 16 " +
             "at 1 pixel-per-unit, so 16 costs one point per tile crossed.")]
    public float unitsPerEndurancePoint = 16f;


    [Header("Sidescroller — stamina regeneration")]
    [Tooltip("Points restored per second normally.")]
    public float regenPerSecond = 22f;

    [Tooltip("Points per second while FATIGUED. Deliberately slower, so emptying " +
             "the bar costs meaningfully more than merely running it low.")]
    public float fatiguedRegenPerSecond = 8f;

    [Tooltip("Seconds after spending before regeneration resumes, so the bar " +
             "doesn't refill between the frames of a combo.")]
    public float regenDelay = 0.5f;

    [Header("Action costs")]
    public int attackCost = 12;
    public int jumpCost   = 8;
    public int whipCost   = 15;
    public int bowCost    = 10;
    public int dashCost   = 14;

    // ── Events for the HUD ───────────────────────────────────────────────────
    /// <summary>(currentStamina, currentEndurance) — stamina's cap IS endurance.</summary>
    public event Action<int, int> OnStaminaChanged;
    /// <summary>(currentEndurance, maxEndurance) — the outer bar.</summary>
    public event Action<int, int> OnEnduranceChanged;
    /// <summary>Fatigue started or ended.</summary>
    public event Action<bool> OnFatigueChanged;

    /// <summary>
    /// Raised when an action was refused — the hook for the fatigue reaction: a
    /// stagger animation, a grunt, a flash on the bar.
    ///
    /// Takes no argument because there is now only one reason to be refused:
    /// being fatigued. Running short mid-swing no longer blocks anything, it
    /// just empties the bar and triggers fatigue.
    /// </summary>
    public event Action OnActionBlocked;

    // ── State ────────────────────────────────────────────────────────────────
    private AbstractCharacter _character;

    // Stamina itself lives on AbstractCharacter, so every character has it and
    // the save system reads one place. This tracks the fractional remainder
    // between whole points, which the int property cannot hold.
    private float   _staminaFraction;
    private float   _distanceAccum;
    private Vector3 _lastPosition;
    private float   _regenBlockedUntil;
    private bool    _fatigued;

    public int  Stamina      => _character != null ? _character.Stamina : 0;
    public int  Endurance    => _character != null ? _character.Endurance : 0;
    public int  MaxEndurance => _character != null ? _character.MaxEndurance : 0;
    public bool IsFatigued   => _fatigued;

    /// <summary>The single check every action makes before it is allowed to happen.</summary>
    public bool CanAct => !_fatigued;

    // =========================================================================
    // Lifecycle
    // =========================================================================

    private void Awake()
    {
        _character = GetComponent<AbstractCharacter>();
    }

    private void Start()
    {
        if (autoDetectMode) mode = DetectMode();

        _lastPosition = transform.position;

        // Entering a stage sets stamina to whatever endurance is left, so a long
        // overworld journey shortens the dungeon's usable bar.
        if (_character != null) _character.Stamina = Endurance;
        _staminaFraction = 0f;

        PublishAll();
    }

    /// <summary>
    /// Side-scrolling stages are the ones whose Tiled map has a GROUND layer;
    /// MapManager names the generated tilemap after the layer, so its presence is
    /// a reliable signal without hard-coding a list of scene names.
    /// </summary>
    private Mode DetectMode()
    {
        return GameObject.Find("GROUND") != null ? Mode.Sidescroller : Mode.Overworld;
    }

    private void Update()
    {
        if (MaxEndurance <= 0) return;

        if (mode == Mode.Overworld) TickOverworldTravel();
        else                        TickStaminaRegen();
    }

    // =========================================================================
    // Overworld — endurance spent per tile travelled
    // =========================================================================

    private void TickOverworldTravel()
    {
        Vector3 delta = transform.position - _lastPosition;
        _lastPosition = transform.position;

        // The overworld is top-down, so screen-vertical movement is walking north
        // or south — every direction is simply travel. Distance is therefore the
        // full magnitude, with no axis treated differently.
        float distance = delta.magnitude;
        if (distance <= 0f) return;   // standing still costs nothing

        _distanceAccum += distance;

        // Spend whole points as each tile's worth of travel accumulates.
        while (_distanceAccum >= unitsPerEndurancePoint && Endurance > 0)
        {
            _distanceAccum -= unitsPerEndurancePoint;
            SetEndurance(Endurance - 1);
        }
    }

    // =========================================================================
    // Sidescroller — spend and regenerate
    // =========================================================================

    private void TickStaminaRegen()
    {
        if (Time.time < _regenBlockedUntil) return;
        if (Stamina >= Endurance) return;       // capped by CURRENT endurance

        float rate = _fatigued ? fatiguedRegenPerSecond : regenPerSecond;

        // Accumulate fractionally, then commit whole points, so a regen rate
        // slower than one point per frame still makes progress.
        _staminaFraction += rate * Time.deltaTime;
        int whole = Mathf.FloorToInt(_staminaFraction);
        if (whole > 0)
        {
            _staminaFraction -= whole;
            SetStamina(Stamina + whole);
        }

        // Fatigue lifts only at a full bar, not merely above zero.
        if (_fatigued && Stamina >= Endurance) SetFatigued(false);
    }

    /// <summary>
    /// Attempts to spend stamina. Returns false — and spends nothing — when
    /// fatigued or short. Callers treat false as "this action does not happen".
    /// </summary>
    public bool TrySpend(int amount)
    {
        if (_fatigued) return false;               // fatigued: nothing is allowed
        if (mode == Mode.Overworld) return true;   // overworld actions aren't gated
        if (amount <= 0) return true;

        // The LAST action always lands, even when it costs more stamina than
        // remains. Overdrawing is precisely what causes fatigue.
        //
        // Refusing an action for being a few points short would put the player in
        // a dead zone: not fatigued, so no animation or feedback explains
        // anything, yet unable to act. Letting the swing through and collapsing
        // straight into fatigue makes the consequence legible — it is always
        // something you just did, not a threshold you silently bumped into.
        SetStamina(Mathf.Max(0, Stamina - amount));
        _regenBlockedUntil = Time.time + regenDelay;

        if (Stamina <= 0) SetFatigued(true);
        return true;
    }

    /// <summary>
    /// Announces that an action was just refused. Called by input handlers after
    /// a failed TrySpend so the character can react visibly, instead of the
    /// player seeing nothing happen with no explanation.
    /// </summary>
    public void ReportBlockedAction()
    {
        OnActionBlocked?.Invoke();
    }

    // Wrappers so callers don't repeat cost values.
    public bool TryAttack() => TrySpend(attackCost);
    public bool TryJump()   => TrySpend(jumpCost);
    public bool TryWhip()   => TrySpend(whipCost);
    public bool TryBow()    => TrySpend(bowCost);
    public bool TryDash()   => TrySpend(dashCost);

    // =========================================================================
    // Restoration
    // =========================================================================

    /// <summary>Restores stamina — resting, an item.</summary>
    public void RestoreStamina(int amount)
    {
        SetStamina(Stamina + amount);
        if (_fatigued && Stamina >= Endurance) SetFatigued(false);
    }

    /// <summary>Restores endurance — food, an inn, a campsite.</summary>
    public void RestoreEndurance(int amount)
    {
        SetEndurance(Endurance + amount);
    }

    /// <summary>Full recovery, as at an inn. Also refills stamina to the new cap.</summary>
    public void RestoreAll()
    {
        SetEndurance(MaxEndurance);
        SetStamina(MaxEndurance);
        SetFatigued(false);
    }

    /// <summary>Called when entering a side-scrolling stage from the overworld.</summary>
    public void EnterStage()
    {
        mode = Mode.Sidescroller;
        SetStamina(Endurance);     // stamina starts at whatever endurance remains
        _staminaFraction = 0f;
        SetFatigued(false);
        _regenBlockedUntil = 0f;
    }

    /// <summary>Called when returning to the overworld.</summary>
    public void EnterOverworld()
    {
        mode = Mode.Overworld;
        _lastPosition  = transform.position;
        _distanceAccum = 0f;
    }

    // =========================================================================
    // Internals
    // =========================================================================

    private void SetStamina(int value)
    {
        if (_character == null) return;

        int clamped = Mathf.Clamp(value, 0, Endurance);
        if (clamped == _character.Stamina) return;

        _character.Stamina = clamped;
        OnStaminaChanged?.Invoke(clamped, Endurance);
    }

    private void SetEndurance(int value)
    {
        if (_character == null) return;

        int clamped = Mathf.Clamp(value, 0, MaxEndurance);
        if (clamped == _character.Endurance) return;

        _character.Endurance = clamped;

        // Stamina can never exceed current endurance, so shrinking the outer bar
        // pushes the inner one down with it.
        if (_character.Stamina > clamped) SetStamina(clamped);

        OnEnduranceChanged?.Invoke(clamped, MaxEndurance);
        OnStaminaChanged?.Invoke(Stamina, clamped);
    }

    private void SetFatigued(bool value)
    {
        if (_fatigued == value) return;
        _fatigued = value;
        OnFatigueChanged?.Invoke(_fatigued);
    }

    private void PublishAll()
    {
        OnEnduranceChanged?.Invoke(Endurance, MaxEndurance);
        OnStaminaChanged?.Invoke(Stamina, Endurance);
        OnFatigueChanged?.Invoke(_fatigued);
    }
}
