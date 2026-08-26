// =============================================================================
// DamageTypes.cs   —   Assets/Scripts/Combat/
//
// The vocabulary of the damage system: what KINDS of damage exist, a single
// typed amount (DamagePacket), and the full payload of one hit (DamageInfo).
//
// A hit is a LIST of packets, not one number. That's what lets a fire arrow
// deal ordinary physical damage AND fire damage in the same strike, or an
// enchanted sword add magic on top of its steel.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Kinds of damage. Targets can resist or be weak to each one.</summary>
public enum DamageType
{
    Physical,
    Fire,
    Ice,
    Lightning,
    Magic,
    Poison
}

/// <summary>One typed amount of damage — e.g. 3 Fire.</summary>
[Serializable]
public struct DamagePacket
{
    public DamageType type;
    [Min(0)] public int amount;

    public DamagePacket(DamageType type, int amount)
    {
        this.type   = type;
        this.amount = amount;
    }
}

/// <summary>
/// Everything about a single hit: what damage it carries, who dealt it, and
/// where it landed. Passed from the attacker straight to the target — no
/// broadcasting, no guessing who was meant to be hit.
/// </summary>
public class DamageInfo
{
    /// <summary>The typed amounts this hit carries (physical + fire, etc.).</summary>
    public List<DamagePacket> packets = new List<DamagePacket>();

    /// <summary>The GameObject that dealt the damage (weapon, arrow, enemy).</summary>
    public GameObject source;

    /// <summary>The character responsible, if any — useful for XP/quest credit.</summary>
    public AbstractCharacter instigator;

    /// <summary>Where the hit landed, for effects and knockback direction.</summary>
    public Vector2 hitPoint;

    /// <summary>Optional knockback to apply to the target (px/s). Zero = none.</summary>
    public Vector2 knockback;

    /// <summary>
    /// Seconds the target is knocked down (stunned, unable to act). Zero = none.
    /// A knockdown CONSUMES the brief invulnerability that normally follows a
    /// hit, so heavy blows leave the target genuinely exposed rather than safe.
    /// </summary>
    public float knockdownDuration;

    /// <summary>
    /// When true this hit lands even if the target is in its post-hit
    /// invulnerability window. Use sparingly — environmental hazards, boss
    /// attacks, damage-over-time ticks.
    /// </summary>
    public bool ignoresInvulnerability;

    public DamageInfo() { }

    /// <summary>Convenience for a simple single-type hit.</summary>
    public DamageInfo(DamageType type, int amount, GameObject source = null)
    {
        packets.Add(new DamagePacket(type, amount));
        this.source = source;
    }

    /// <summary>Adds another typed amount to this hit (e.g. a fire enchantment).</summary>
    public DamageInfo Add(DamageType type, int amount)
    {
        if (amount > 0) packets.Add(new DamagePacket(type, amount));
        return this;
    }

    /// <summary>Raw total before the target's resistances are applied.</summary>
    public int RawTotal
    {
        get
        {
            int t = 0;
            foreach (var p in packets) t += p.amount;
            return t;
        }
    }

    /// <summary>True if this hit contains any of the given type.</summary>
    public bool Contains(DamageType type)
    {
        foreach (var p in packets) if (p.type == type) return true;
        return false;
    }

    /// <summary>A deep copy — so a weapon's template isn't mutated per hit.</summary>
    public DamageInfo Clone()
    {
        var c = new DamageInfo
        {
            source     = source,
            instigator = instigator,
            hitPoint   = hitPoint,
            knockback  = knockback,
            knockdownDuration      = knockdownDuration,
            ignoresInvulnerability = ignoresInvulnerability
        };
        c.packets.AddRange(packets);
        return c;
    }
}
