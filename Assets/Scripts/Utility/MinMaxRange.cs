// =============================================================================
// MinMaxRange.cs   —   Assets/Scripts/Utility/
//
// A pair of floats representing a range. Renders on ONE Inspector line with the
// fields labelled "Min" and "Max" (see MinMaxRangeDrawer, in the Editor folder),
// instead of the X / Y that a plain Vector2 shows — which reads like coordinates
// rather than a range.
//
// USAGE:
//     public MinMaxRange walkTime = new MinMaxRange(0.6f, 2.6f);
//     float thisLeg = walkTime.Random();
// =============================================================================

using System;
using UnityEngine;

[Serializable]
public struct MinMaxRange
{
    public float min;
    public float max;

    public MinMaxRange(float min, float max)
    {
        this.min = min;
        this.max = max;
    }

    /// <summary>A random value between min and max (inclusive).</summary>
    public float Random() => UnityEngine.Random.Range(min, max);

    /// <summary>The midpoint of the range — handy for a non-random default.</summary>
    public float Average => (min + max) * 0.5f;

    /// <summary>Clamps a value into this range.</summary>
    public float Clamp(float value) => Mathf.Clamp(value, min, max);
}
