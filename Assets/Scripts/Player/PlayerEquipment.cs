// =============================================================================
// PlayerEquipment.cs   —   Assets/Scripts/Player/
//
// Manages what the player is holding in each hand.
//
//   Right hand — a Weapon: sword, whip, or bow
//   Left  hand — a Tool:   lantern or shield
//
// Rule: a TWO-HANDED weapon (the bow) occupies both hands, so equipping it
// automatically frees the left hand, and a tool can't be equipped while it's
// held. One-handed weapons (sword, whip) leave the left hand free for a tool.
//
// SETUP:
//   • Create two empty child GameObjects on the player positioned at the hands,
//     e.g. "RightHandSocket" and "LeftHandSocket", and assign them below.
//   • Equipping spawns the item prefab as a child of the matching socket.
// =============================================================================

using UnityEngine;

public class PlayerEquipment : MonoBehaviour
{
    [Header("Hand Sockets")]
    [Tooltip("Empty child transform at the player's right hand (weapons).")]
    public Transform rightHandSocket;
    [Tooltip("Empty child transform at the player's left hand (tools).")]
    public Transform leftHandSocket;

    [Header("Owner")]
    [Tooltip("The character wielding these items. Auto-found on this object if empty.")]
    public AbstractCharacter owner;

    [Header("Starting Gear (optional)")]
    public GameObject startingWeaponPrefab;
    public GameObject startingToolPrefab;

    // Currently held items
    private EquippableItem _right;
    private EquippableItem _left;

    /// <summary>Raised whenever either hand changes, for HUD/UI updates.</summary>
    public event System.Action OnEquipmentChanged;

    public EquippableItem RightHand => _right;
    public EquippableItem LeftHand  => _left;

    // =========================================================================
    // Lifecycle
    // =========================================================================

    private void Awake()
    {
        if (owner == null) owner = GetComponent<AbstractCharacter>();
    }

    private void Start()
    {
        if (startingWeaponPrefab != null) Equip(startingWeaponPrefab);
        if (startingToolPrefab   != null) Equip(startingToolPrefab);
    }

    // =========================================================================
    // Equip / Unequip
    // =========================================================================

    /// <summary>
    /// Spawns and equips an item prefab into the correct hand, enforcing the
    /// two-handed rule. Returns the equipped instance, or null if it couldn't
    /// be equipped.
    /// </summary>
    public EquippableItem Equip(GameObject itemPrefab)
    {
        if (itemPrefab == null) return null;

        var template = itemPrefab.GetComponent<EquippableItem>();
        if (template == null)
        {
            Debug.LogWarning($"[PlayerEquipment] '{itemPrefab.name}' has no EquippableItem component.", this);
            return null;
        }

        EquipSlot slot = template.NaturalSlot;

        // A tool cannot be held while a two-handed weapon is equipped.
        if (slot == EquipSlot.LeftHand && _right != null && _right.IsTwoHanded)
        {
            Debug.Log("[PlayerEquipment] Can't equip a tool while holding a two-handed weapon.");
            return null;
        }

        // Clear whatever currently occupies the target hand.
        Unequip(slot);

        // A two-handed weapon also frees the left hand.
        if (template.IsTwoHanded) Unequip(EquipSlot.LeftHand);

        Transform socket = slot == EquipSlot.RightHand ? rightHandSocket : leftHandSocket;
        if (socket == null)
        {
            Debug.LogWarning($"[PlayerEquipment] No socket assigned for {slot}.", this);
            return null;
        }

        var instance = Instantiate(itemPrefab, socket.position, socket.rotation, socket);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        var item = instance.GetComponent<EquippableItem>();
        item.OnEquip(owner);

        if (slot == EquipSlot.RightHand) _right = item;
        else                             _left  = item;

        OnEquipmentChanged?.Invoke();
        return item;
    }

    /// <summary>Removes and destroys whatever is in the given hand.</summary>
    public void Unequip(EquipSlot slot)
    {
        EquippableItem item = slot == EquipSlot.RightHand ? _right : _left;
        if (item == null) return;

        item.OnUnequip();
        Destroy(item.gameObject);

        if (slot == EquipSlot.RightHand) _right = null;
        else                             _left  = null;

        OnEquipmentChanged?.Invoke();
    }

    /// <summary>True if a tool can currently be held (no two-handed weapon).</summary>
    public bool CanEquipTool => _right == null || !_right.IsTwoHanded;

    /// <summary>
    /// Enchant the held weapon — e.g. a spell that adds fire damage to the sword.
    /// Applies immediately to the next strike.
    /// </summary>
    public void EnchantWeapon(DamageType type, int amount)
    {
        _right?.ApplyEnchantment(type, amount);
    }

    /// <summary>Remove temporary enchantments from the held weapon.</summary>
    public void ClearWeaponEnchantments()
    {
        _right?.ClearEnchantments();
    }
}
