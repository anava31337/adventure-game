// =============================================================================
// SegmentedBar.cs   —   Assets/Scripts/UI/
//
// Builds a resource bar out of THREE sprites — empty, half, full — repeated
// along a row, instead of one hand-drawn frame per percentage point.
//
// WHY THIS IS BETTER THAN A 51-FRAME SHEET:
//   • The bar's LENGTH becomes the character's maximum. Raise max HP and the bar
//     physically grows, the way Dark Souls and Zelda do it, instead of the same
//     fixed-width bar silently meaning more per pixel.
//   • Three sprites cover every value at every maximum. A frame-per-percent
//     sheet has to be redrawn if the resolution of the bar ever changes.
//   • Half-segments give twice the granularity of whole segments for free, so a
//     bar reading in 2-point steps needs only ceil(max / 2) tiles.
//
// Each tile represents `pointsPerSegment` (default 2): full when both points
// remain, half when one does, empty when neither.
//
// Works on a HUD Canvas (Image) or in the world as an enemy bar (SpriteRenderer);
// pick with `renderMode`.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SegmentedBar : MonoBehaviour
{
    public enum RenderMode { UIImage, SpriteRenderer }

    [Header("Rendering")]
    [Tooltip("UIImage for a HUD canvas, SpriteRenderer for a world-space bar.")]
    public RenderMode renderMode = RenderMode.UIImage;

    [Tooltip("Parent the segment tiles are created under. Defaults to this object.")]
    public Transform container;

    [Header("Sprites")]
    public Sprite emptySegment;
    public Sprite halfSegment;
    public Sprite fullSegment;

    [Header("Layout")]
    [Tooltip("Resource points each tile represents. 2 gives a half-step of 1 point.")]
    public int pointsPerSegment = 2;

    [Tooltip("Width of one tile in pixels/units, used to space them out.")]
    public float segmentWidth = 2f;

    [Tooltip("Gap between tiles. Usually 0 for a continuous bar.")]
    public float segmentSpacing = 0f;

    [Tooltip("Maximum tiles to build, so an enormous max value can't spawn " +
             "thousands of objects. The bar clamps rather than overflowing.")]
    public int maxSegments = 200;

    [Header("Sorting (SpriteRenderer mode)")]
    public string sortingLayerName = "Default";
    public int    sortingOrder = 100;

    // ── State ────────────────────────────────────────────────────────────────
    private readonly List<GameObject> _segments = new List<GameObject>();
    private int _builtForMax = -1;

    private void Awake()
    {
        if (container == null) container = transform;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Displays `current` out of `max`. Rebuilds the row only when `max` changes,
    /// so ordinary damage just re-sprites existing tiles.
    /// </summary>
    public void SetValue(int current, int max)
    {
        if (max <= 0) { ClearSegments(); return; }

        if (max != _builtForMax) Build(max);

        current = Mathf.Clamp(current, 0, max);

        for (int i = 0; i < _segments.Count; i++)
        {
            // Points this tile covers: tile 0 covers points 1..2, tile 1 covers 3..4.
            int pointsBefore = i * pointsPerSegment;
            int remaining    = current - pointsBefore;

            Sprite s;
            if (remaining >= pointsPerSegment)  s = fullSegment;
            else if (remaining <= 0)            s = emptySegment;
            else                                s = halfSegment;   // partially filled

            Apply(_segments[i], s);
        }
    }

    // =========================================================================
    // Building
    // =========================================================================

    private void Build(int max)
    {
        ClearSegments();

        int count = Mathf.Min(Mathf.CeilToInt((float)max / pointsPerSegment), maxSegments);

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Segment_" + i);
            go.transform.SetParent(container, false);

            float x = i * (segmentWidth + segmentSpacing);

            if (renderMode == RenderMode.UIImage)
            {
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin        = new Vector2(0f, 0.5f);
                rt.anchorMax        = new Vector2(0f, 0.5f);
                rt.pivot            = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0f);

                var img = go.AddComponent<Image>();
                img.sprite         = emptySegment;
                img.raycastTarget  = false;   // a bar should never eat UI clicks
                img.SetNativeSize();
            }
            else
            {
                go.transform.localPosition = new Vector3(x, 0f, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite           = emptySegment;
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder     = sortingOrder;
            }

            _segments.Add(go);
        }

        _builtForMax = max;
    }

    private void Apply(GameObject segment, Sprite sprite)
    {
        if (renderMode == RenderMode.UIImage)
        {
            var img = segment.GetComponent<Image>();
            if (img != null && img.sprite != sprite) img.sprite = sprite;
        }
        else
        {
            var sr = segment.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != sprite) sr.sprite = sprite;
        }
    }

    private void ClearSegments()
    {
        foreach (var s in _segments)
        {
            if (s == null) continue;
            if (Application.isPlaying) Destroy(s);
            else                       DestroyImmediate(s);
        }
        _segments.Clear();
        _builtForMax = -1;
    }

    /// <summary>Total width of the bar as currently built, for laying out the HUD.</summary>
    public float CurrentWidth =>
        _segments.Count * (segmentWidth + segmentSpacing) - segmentSpacing;
}
