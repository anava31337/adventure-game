// =============================================================================
// ResourceBarsHUD.cs   —   Assets/Scripts/UI/
//
// Drives the HP, MP and Endurance/Stamina bars on the HUD from three-frame art,
// replacing the old one-sprite-per-percentage approach.
//
// WHY THREE FRAMES INSTEAD OF FIFTY:
// Each bar is built by repeating an empty/half/full tile, so the bar's LENGTH is
// the character's maximum. Level up and gain max HP and the bar physically grows
// — the Dark Souls approach — instead of the same fixed-width bar quietly
// meaning more per pixel. It also means the art never has to be redrawn when the
// numbers change.
//
// THE ENDURANCE/STAMINA BAR IS TWO BARS IN ONE:
//   • The ENDURANCE bar is the full-length backing, sized to MAX endurance.
//     Overworld travel shortens the filled portion.
//   • The STAMINA bar is drawn on top, sized to CURRENT endurance. So a long
//     journey visibly shrinks the stamina you'll have in the next dungeon.
// =============================================================================

using UnityEngine;

// Runs AFTER Player (default order 0) and StaminaSystem (50), so by the time our
// Start() reads the character's maxima, SetLevelData has already filled them.
[DefaultExecutionOrder(100)]
public class ResourceBarsHUD : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The Hero. Found by tag if left empty.")]
    public AbstractCharacter character;
    public string characterTag = "Player";

    [Header("Bars")]
    public SegmentedBar hpBar;
    public SegmentedBar mpBar;

    [Tooltip("Backing bar showing total endurance capacity.")]
    public SegmentedBar enduranceBar;

    [Tooltip("Overlay bar showing stamina, drawn on top of the endurance bar. " +
             "Give it a higher sibling index so it renders in front.")]
    public SegmentedBar staminaBar;

    [Header("Fatigue Feedback")]
    [Tooltip("Optional object shown while fatigued — a flashing icon or tint.")]
    public GameObject fatigueIndicator;

    private StaminaSystem _stamina;

    // =========================================================================
    // Lifecycle
    // =========================================================================

    private void Awake()
    {
        // Subscribe in Awake, NOT Start.
        //
        // Unity runs every Awake() before any Start(), but the order among
        // Start() methods is arbitrary. StaminaSystem and Player publish their
        // initial values from Start(), so a listener that subscribes in Start()
        // may miss them entirely — the event fires into nothing and the bars
        // never learn their maxima, so they build zero segments and stay empty.
        // Subscribing here guarantees we are listening before anything publishes.
        if (character == null)
        {
            var go = GameObject.FindWithTag(characterTag);
            if (go != null) character = go.GetComponent<AbstractCharacter>();
        }

        if (character == null)
        {
            Debug.LogWarning("[ResourceBarsHUD] No character found — bars will not update.", this);
            return;
        }

        _stamina = character.GetComponent<StaminaSystem>();

        // Endurance and stamina come from StaminaSystem, which owns both values.
        if (_stamina != null)
        {
            _stamina.OnEnduranceChanged += HandleEndurance;
            _stamina.OnStaminaChanged   += HandleStamina;
            _stamina.OnFatigueChanged   += HandleFatigue;
        }

        // HP and MP updates.
        //
        // The Hero deliberately does NOT raise OnCharacterDamaged — Player
        // overrides BroadcastHealth to raise OnPlayerDamage instead, so enemy HP
        // bars (which listen to OnCharacterDamaged) never react to the Hero.
        // The HUD therefore has to listen to the Hero's own event, or it never
        // hears a single HP change.
        Player.OnPlayerDamage += HandlePlayerDamaged;
        AbstractCharacter.OnCharacterDamaged += HandleCharacterDamaged;
    }

    private void Start()
    {
        // By now every Awake and (at worst) most Starts have run, so the
        // character's maxima are populated. This catches the case where a
        // publisher's Start ran before ours and we were already subscribed but
        // had nothing to draw yet.
        RefreshAll();
    }

    private void OnDestroy()
    {
        if (_stamina != null)
        {
            _stamina.OnEnduranceChanged -= HandleEndurance;
            _stamina.OnStaminaChanged   -= HandleStamina;
            _stamina.OnFatigueChanged   -= HandleFatigue;
        }
        Player.OnPlayerDamage -= HandlePlayerDamaged;
        AbstractCharacter.OnCharacterDamaged -= HandleCharacterDamaged;
    }

    // =========================================================================
    // Updates
    // =========================================================================

    /// <summary>Redraws every bar from the character's current values.</summary>
    public void RefreshAll()
    {
        if (character == null) return;

        if (hpBar != null) hpBar.SetValue(character.Hp, character.MaxHp);
        if (mpBar != null) mpBar.SetValue(character.Mp, character.MaxMp);

        if (_stamina != null)
        {
            HandleEndurance(_stamina.Endurance, _stamina.MaxEndurance);
            HandleStamina(_stamina.Stamina, _stamina.Endurance);
            HandleFatigue(_stamina.IsFatigued);
        }
        else if (enduranceBar != null)
        {
            enduranceBar.SetValue(character.Endurance, character.MaxEndurance);
        }
    }

    // The shared damage event carries a PERCENTAGE, so read the raw values off
    // the character instead — SegmentedBar needs real numbers to size itself.
    private void HandleCharacterDamaged(int hpPercent, AbstractCharacter who)
    {
        if (who != character) return;
        if (hpBar != null) hpBar.SetValue(character.Hp, character.MaxHp);
        if (mpBar != null) mpBar.SetValue(character.Mp, character.MaxMp);
    }

    // The Hero's event carries a percentage; read the raw values instead, since
    // SegmentedBar needs real numbers to size itself.
    private void HandlePlayerDamaged(int hpPercent)
    {
        if (character == null) return;
        if (hpBar != null) hpBar.SetValue(character.Hp, character.MaxHp);
        if (mpBar != null) mpBar.SetValue(character.Mp, character.MaxMp);
    }

    private void HandleEndurance(int current, int max)
    {
        // The backing bar spans MAX endurance and fills to current.
        if (enduranceBar != null) enduranceBar.SetValue(current, max);

        // Stamina's bar is only as long as CURRENT endurance, so it shortens as
        // the journey wears on.
        if (staminaBar != null && _stamina != null)
            staminaBar.SetValue(_stamina.Stamina, Mathf.Max(current, 1));
    }

    private void HandleStamina(int current, int enduranceCap)
    {
        if (staminaBar != null) staminaBar.SetValue(current, Mathf.Max(enduranceCap, 1));
    }

    private void HandleFatigue(bool fatigued)
    {
        if (fatigueIndicator != null) fatigueIndicator.SetActive(fatigued);
    }
}
