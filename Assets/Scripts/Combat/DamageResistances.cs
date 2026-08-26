// =============================================================================
// DamageResistances.cs   —   Assets/Scripts/Combat/
//
// Resistance is expressed as a PERCENTAGE, which reads the way you'd describe
// it out loud:
//
//    100%  = immune          (rock enemy vs fire — takes nothing)
//     50%  = resistant       (takes half)
//      0%  = normal          (the default for anything unlisted)
//    -50%  = weak            (wooden enemy vs fire — takes 1.5x)
//   -100%  = very weak       (takes double)
//
// The damage multiplier is simply  1 - (percent / 100).
//
// Resistance comes from two places, which stack:
//   • BASE      — set on the character in the Inspector (a rock golem's innate
//                 fire immunity).
//   • MODIFIERS — added at runtime with a duration, so a fire-resistance potion
//                 or an enchanted cloak can grant +40% fire resistance for 30s.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

public class DamageResistances : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public DamageType type;

        [Tooltip("100 = immune, 50 = takes half, 0 = normal, -50 = takes 1.5x, -100 = takes double.")]
        [Range(-200f, 100f)]
        public float percent;
    }

    [Tooltip("Innate resistances. Only list types that differ from normal — " +
             "anything not listed is 0% (takes full damage).")]
    public List<Entry> baseResistances = new List<Entry>();

    // ── Temporary modifiers (potions, buffs, equipment) ──────────────────────
    private class Modifier
    {
        public DamageType type;
        public float      percent;
        public float      remaining;   // seconds; float.PositiveInfinity = permanent
        public object     sourceKey;   // so a specific buff can be removed by name
    }
    private readonly List<Modifier> _modifiers = new List<Modifier>();

    private void Update()
    {
        if (_modifiers.Count == 0) return;
        for (int i = _modifiers.Count - 1; i >= 0; i--)
        {
            if (float.IsPositiveInfinity(_modifiers[i].remaining)) continue;
            _modifiers[i].remaining -= Time.deltaTime;
            if (_modifiers[i].remaining <= 0f) _modifiers.RemoveAt(i);
        }
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Grant temporary resistance. Positive percent resists, negative makes the
    /// target more vulnerable (a curse). Pass duration = 0 for a permanent
    /// modifier (e.g. worn equipment), and remove it later with RemoveModifier.
    /// </summary>
    public void AddModifier(DamageType type, float percent, float duration = 0f, object sourceKey = null)
    {
        _modifiers.Add(new Modifier
        {
            type      = type,
            percent   = percent,
            remaining = duration > 0f ? duration : float.PositiveInfinity,
            sourceKey = sourceKey
        });
    }

    /// <summary>Removes every modifier that was added with the given source key.</summary>
    public void RemoveModifier(object sourceKey)
    {
        _modifiers.RemoveAll(m => Equals(m.sourceKey, sourceKey));
    }

    /// <summary>
    /// Total resistance percent for a type: base plus all active modifiers.
    /// Capped at 100 (immune) — you can't go past immunity into healing.
    /// Weakness is uncapped downward, so stacked curses keep hurting.
    /// </summary>
    public float GetResistancePercent(DamageType type)
    {
        float total = 0f;
        foreach (var e in baseResistances)
            if (e.type == type) total += e.percent;
        foreach (var m in _modifiers)
            if (m.type == type) total += m.percent;
        return Mathf.Min(total, 100f);
    }

    /// <summary>The damage multiplier for a type: 1 - (resistance% / 100).</summary>
    public float GetMultiplier(DamageType type)
    {
        return Mathf.Max(0f, 1f - (GetResistancePercent(type) / 100f));
    }

    /// <summary>
    /// Applies this target's resistances to a hit, returning the final total and
    /// reporting which types actually landed (for reactions like catching fire).
    /// </summary>
    public int ApplyTo(DamageInfo info, out Dictionary<DamageType, int> applied)
    {
        applied = new Dictionary<DamageType, int>();
        int total = 0;

        foreach (var p in info.packets)
        {
            int dealt = Mathf.RoundToInt(p.amount * GetMultiplier(p.type));
            if (dealt <= 0) continue;   // fully resisted

            total += dealt;
            if (applied.ContainsKey(p.type)) applied[p.type] += dealt;
            else                             applied[p.type]  = dealt;
        }
        return total;
    }

    /// <summary>
    /// Static helper so callers don't need to null-check the component.
    /// A target with no DamageResistances simply takes everything at 100%.
    /// </summary>
    public static int Resolve(GameObject target, DamageInfo info,
                              out Dictionary<DamageType, int> applied)
    {
        var res = target != null ? target.GetComponent<DamageResistances>() : null;
        if (res != null) return res.ApplyTo(info, out applied);

        applied = new Dictionary<DamageType, int>();
        int total = 0;
        foreach (var p in info.packets)
        {
            total += p.amount;
            if (applied.ContainsKey(p.type)) applied[p.type] += p.amount;
            else                             applied[p.type]  = p.amount;
        }
        return total;
    }
}
