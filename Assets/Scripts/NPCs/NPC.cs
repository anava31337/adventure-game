// =============================================================================
// NPC.cs
// Place in: Assets/Scripts/NPCs/
//
// The definitive script for all non-player characters. Inherits the full
// stat/inventory system from AbstractCharacter (same as Player and Enemy).
// Can be passive (dialogue only) or hostile (fights like an Enemy).
// Attach NPCDialogue to the same GameObject to give it a dialogue tree.
//
// Damage handling is entirely inherited. There is no weapon-contact listener to
// register or tear down any more: attackers call TakeDamage directly on whatever
// they hit, and CombatRules checks `isHostile` at the moment of the swing. That
// means flipping hostility mid-fight works instantly with no re-subscription.
//
// An NPC that should fight back gets the same treatment as an enemy: a
// DamageDealer on its body collider for contact damage, and/or a child weapon
// hitbox with a DamageDealer enabled by its attack animation.
// =============================================================================

using UnityEngine;

/// <summary>
/// All non-player characters derive from this.  An NPC has the complete
/// AbstractCharacter stat set and can seamlessly flip between passive
/// (dialogue / quest-giver) and hostile (combat) behaviour at runtime.
/// </summary>
public class NPC : AbstractCharacter
{
    // -------------------------------------------------------------------------
    // Identity   (used by NPCDialogue and in-game UI)
    // -------------------------------------------------------------------------

    [Header("Identity")]
    [Tooltip("Display name shown in dialogue windows and above-head labels. " +
             "Can be overridden per dialogue tree or per dialogue node.")]
    public string characterName = "";

    [Tooltip("Portrait sprite used in the dialogue UI and in Dialogue Tree Editor node previews. " +
             "Can be overridden at the dialogue-tree level or per individual node.")]
    public Sprite characterIcon;

    // -------------------------------------------------------------------------
    // Behaviour
    // -------------------------------------------------------------------------

    [Header("Behaviour")]
    [Tooltip("When true this NPC acts as a hostile enemy and will take weapon damage. " +
             "Checked live by CombatRules, so it can be toggled at any time.")]
    public bool isHostile = false;

    // -------------------------------------------------------------------------
    // MonoBehaviour
    // -------------------------------------------------------------------------

    private new void Start()
    {
        base.Start();
        this.SetLevelData(1);
        this.Hp        = this.MaxHp;
        this.Mp        = this.MaxMp;
        this.Endurance = this.MaxEndurance;

        // Publish full health so the HP bar shows the correct sprite from the
        // moment this character exists. Without it a bar only ever updates on
        // the first hit, so spawned creatures start out looking empty.
        RefreshHealthDisplay();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isHostile) return;   // hostile NPCs skip dialogue on contact

        // TODO: plug your interaction / dialogue trigger system in here.
        // Example (once an interaction button press is implemented):
        //   var dialogue = GetComponent<NPCDialogue>();
        //   if (dialogue != null && dialogue.HasDialogue)
        //       dialogue.StartDialogue();
    }

    // -------------------------------------------------------------------------
    // Hostility toggle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Switches an NPC between passive and hostile at runtime — for example
    /// when a quest turns a friendly character against the player.
    /// No event wiring needed: CombatRules reads isHostile at the moment of
    /// each hit, so the change takes effect immediately.
    /// </summary>
    public void SetHostile(bool hostile)
    {
        isHostile = hostile;
    }
}
