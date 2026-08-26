// =============================================================================
// EquippableItem.cs   —   Assets/Scripts/Items/Equipment/
//
// Put this on any prefab the player can hold: sword, whip, bow, lantern, shield.
// It describes WHAT the item is (weapon vs tool, one- vs two-handed) and carries
// the damage it contributes if it's a weapon.
//
// The prefab is spawned into a hand socket by PlayerEquipment. Because each
// equippable is its own GameObject with its own collider and DamageDealer, the
// damage pipeline doesn't need to know or care which item is being held — the
// sword, whip, and bow all flow through the exact same path.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

/// <summary>Which hand an item occupies.</summary>
public enum EquipSlot
{
    RightHand,   // weapons: sword, whip, bow
    LeftHand     // tools: lantern, shield
}

/// <summary>Broad purpose of the item, which decides its default hand.</summary>
public enum EquipCategory
{
    Weapon,   // deals damage — goes in the right hand
    Tool      // utility/defence — goes in the left hand
}

/// <summary>How many hands the item needs.</summary>
public enum Handedness
{
    OneHanded,   // sword, whip, lantern, shield — leaves the other hand free
    TwoHanded    // bow — occupies both hands, so no tool can be held
}

public class EquippableItem : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Stable name used for saving and inventory lookups.")]
    public string itemName = "";

    [Header("How it's held")]
    public EquipCategory category   = EquipCategory.Weapon;
    public Handedness    handedness = Handedness.OneHanded;

    [Header("Damage (weapons only)")]
    [Tooltip("Damage this item contributes. Leave empty for tools like a lantern. " +
             "Multiple entries make it inherently multi-type (e.g. a flaming sword).")]
    public List<DamagePacket> damage = new List<DamagePacket>();

    [Tooltip("The DamageDealer on this item's hit collider. Auto-found if left empty.")]
    public DamageDealer dealer;

    /// <summary>The slot this item naturally occupies.</summary>
    public EquipSlot NaturalSlot =>
        category == EquipCategory.Weapon ? EquipSlot.RightHand : EquipSlot.LeftHand;

    public bool IsTwoHanded => handedness == Handedness.TwoHanded;

    private void Awake()
    {
        if (dealer == null) dealer = GetComponentInChildren<DamageDealer>(true);
    }

    /// <summary>
    /// Called by PlayerEquipment when this item is put in a hand. Pushes the
    /// item's damage into its dealer and tags the wielder so the player can't
    /// hurt themselves with their own weapon.
    /// </summary>
    public void OnEquip(AbstractCharacter wielder)
    {
        if (dealer == null) return;

        dealer.owner = wielder;
        if (damage != null && damage.Count > 0)
        {
            dealer.damage = new List<DamagePacket>(damage);
        }
        dealer.ClearBonusDamage();   // start clean; enchantments re-apply after
    }

    /// <summary>Called when the item leaves the hand.</summary>
    public void OnUnequip()
    {
        if (dealer != null) dealer.ClearBonusDamage();
    }

    /// <summary>
    /// Enchant this weapon with extra typed damage (fire oil, magic buff).
    /// Applies to the live dealer so it takes effect on the very next swing.
    /// </summary>
    public void ApplyEnchantment(DamageType type, int amount)
    {
        if (dealer != null) dealer.AddBonusDamage(type, amount);
    }

    /// <summary>Strip all temporary enchantments from this weapon.</summary>
    public void ClearEnchantments()
    {
        if (dealer != null) dealer.ClearBonusDamage();
    }
}
