// =============================================================================
// EquipmentSpriteSync.cs   —   Assets/Scripts/Player/
//
// Keeps overlay layers (sword, shield, lamp) in step with the character's
// animation. Everything is discovered by convention — there are no sprite arrays
// to fill in and nothing to re-assign when the art changes.
//
// HOW IT FINDS EVERYTHING:
//   • Each child GameObject with a SpriteRenderer becomes a layer.
//   • Its sheet is "<bodySheet>-<childName>", so a child called "sword" uses
//     hero-sword, "lamp" uses hero-lamp, and so on.
//   • Sprites are loaded from Resources at runtime and ordered by the frame
//     number in their name, so adding or removing frames needs no changes here.
//
// WHY CONVENTION RATHER THAN INSPECTOR ARRAYS:
// The body and its overlays are sliced from identically-sized sheets, so they
// always have the same frame count by construction. Listing those frames by hand
// would be re-stating something already guaranteed by the art pipeline — and it
// would silently go stale the moment a frame is added.
//
// EDITOR PREVIEW:
// [ExecuteAlways] means this also runs with the game stopped. Scrub the Animation
// window, or drop a different sprite on the body, and the sword/shield/lamp
// follow immediately — so you can check alignment frame by frame without
// entering Play mode.
//
// REQUIREMENTS:
//   • All sheets sliced Grid By Cell Size, same cell size, same pivot.
//   • Sheets live under a Resources folder (set resourcePath below).
//   • Child object names match the sheet suffix: sword, shield, lamp.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]   // also runs while the editor is stopped, so overlays preview live
public class EquipmentSpriteSync : MonoBehaviour
{
    [Header("Sheets")]
    [Tooltip("Resources folder holding the sheets, e.g. 'Art/Player/'.")]
    public string resourcePath = "Art/Player/";

    [Tooltip("Base sheet name. Overlay sheets are '<this>-<childName>', so a " +
             "child named 'sword' resolves to 'hero-sword'.")]
    public string bodySheet = "hero";

    [Tooltip("The hero's own SpriteRenderer. Found on this object if left empty.")]
    public SpriteRenderer bodyRenderer;

    // ── Runtime layers, built automatically from the children ────────────────
    private class Layer
    {
        public string         id;
        public SpriteRenderer renderer;
        public Sprite[]       frames;
    }

    private readonly List<Layer> _layers = new List<Layer>();
    private readonly Dictionary<Sprite, int> _bodyIndex = new Dictionary<Sprite, int>();

    private Sprite _lastBodySprite;
    private int    _lastFrame = -1;

    // =========================================================================
    // Setup
    // =========================================================================

    private void OnEnable()
    {
        Rebuild();
    }

    /// <summary>
    /// Reloads the sheets and re-discovers the child layers. Safe to call at any
    /// time; it is how the editor picks up a renamed child, a new sheet, or a
    /// re-slice without needing a domain reload.
    /// </summary>
    [ContextMenu("Rebuild From Sheets")]
    public void Rebuild()
    {
        if (bodyRenderer == null) bodyRenderer = GetComponent<SpriteRenderer>();

        _bodyIndex.Clear();
        _layers.Clear();
        _lastBodySprite = null;
        _lastFrame      = -1;

        BuildBodyIndex();
        DiscoverLayers();
    }

    private void OnValidate()
    {
        // Path or sheet name edited in the Inspector — reload against the new value.
        // Deferred, because Unity forbids Resources calls during OnValidate itself.
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) Rebuild();
        };
        #endif
    }

    /// <summary>
    /// Maps every body sprite to its frame number once, so the per-frame lookup
    /// is a dictionary hit rather than a string parse.
    /// </summary>
    private void BuildBodyIndex()
    {
        var sprites = LoadSheet(bodySheet);
        for (int i = 0; i < sprites.Length; i++)
            if (sprites[i] != null) _bodyIndex[sprites[i]] = i;

        if (sprites.Length == 0)
            Debug.LogWarning($"[EquipmentSpriteSync] No sprites found at " +
                             $"Resources/{resourcePath}{bodySheet}. Check the path and " +
                             "that the sheet is sliced.", this);
    }

    /// <summary>Every child with a SpriteRenderer becomes an overlay layer.</summary>
    private void DiscoverLayers()
    {
        foreach (Transform child in transform)
        {
            var sr = child.GetComponent<SpriteRenderer>();
            if (sr == null || sr == bodyRenderer) continue;

            string id     = child.name.ToLowerInvariant();
            var    frames = LoadSheet(bodySheet + "-" + id);

            if (frames.Length == 0)
            {
                Debug.LogWarning($"[EquipmentSpriteSync] Child '{child.name}' found, but no " +
                                 $"sheet at Resources/{resourcePath}{bodySheet}-{id}. " +
                                 "Rename the child to match its sheet suffix.", this);
                continue;
            }

            if (frames.Length != _bodyIndex.Count)
                Debug.LogWarning($"[EquipmentSpriteSync] '{id}' has {frames.Length} frames but " +
                                 $"the body has {_bodyIndex.Count}. The sheets were sliced " +
                                 "differently — every sheet needs Grid By Cell Size with the " +
                                 "same cell size, not Automatic.", this);

            _layers.Add(new Layer { id = id, renderer = sr, frames = frames });
        }
    }

    /// <summary>
    /// Loads a sliced sheet and orders it by the frame number in each sprite's
    /// name. Resources.LoadAll makes no ordering guarantee, so sorting is what
    /// makes index N mean the same frame on every sheet.
    /// </summary>
    private Sprite[] LoadSheet(string sheetName)
    {
        var loaded = Resources.LoadAll<Sprite>(resourcePath + sheetName);
        if (loaded == null || loaded.Length == 0) return new Sprite[0];

        System.Array.Sort(loaded, (a, b) => FrameNumber(a).CompareTo(FrameNumber(b)));
        return loaded;
    }

    private static int FrameNumber(Sprite s)
    {
        int u = s.name.LastIndexOf('_');
        if (u < 0 || u == s.name.Length - 1) return 0;
        int.TryParse(s.name.Substring(u + 1), out int n);
        return n;
    }

    // =========================================================================
    // Per-frame sync
    // =========================================================================

    private void LateUpdate()
    {
        // LateUpdate so this runs AFTER the Animator has set the body's frame.
        // In Update the equipment would render one frame behind the swing.
        if (bodyRenderer == null || bodyRenderer.sprite == null) return;

        // While stopped, children can be added, removed or renamed at any moment.
        // A count mismatch is a cheap way to notice and re-discover.
        if (!Application.isPlaying && LayersLookStale()) Rebuild();

        if (bodyRenderer.sprite != _lastBodySprite)
        {
            _lastBodySprite = bodyRenderer.sprite;
            _lastFrame = _bodyIndex.TryGetValue(_lastBodySprite, out int i) ? i : -1;
        }
        if (_lastFrame < 0) return;

        foreach (var layer in _layers)
        {
            if (layer.renderer == null || !layer.renderer.gameObject.activeSelf) continue;

            // A null frame is legitimate — the sword is simply not visible on
            // that frame, e.g. passing behind the body mid-swing.
            layer.renderer.sprite = _lastFrame < layer.frames.Length
                                  ? layer.frames[_lastFrame]
                                  : null;
            layer.renderer.flipX = bodyRenderer.flipX;
        }
    }

    /// <summary>
    /// True when the discovered layers no longer match the children — something
    /// was added, removed or renamed since the last rebuild.
    /// </summary>
    private bool LayersLookStale()
    {
        int rendererChildren = 0;
        foreach (Transform child in transform)
        {
            var sr = child.GetComponent<SpriteRenderer>();
            if (sr != null && sr != bodyRenderer) rendererChildren++;
        }
        // Fewer layers than children is normal when a child has no matching sheet,
        // so only a genuine increase, or losing layers entirely, forces a rebuild.
        return rendererChildren < _layers.Count || (_layers.Count == 0 && rendererChildren > 0);
    }

    // =========================================================================
    // Equipping
    // =========================================================================

    /// <summary>Shows or hides a layer by its child object name — "sword", "lamp".</summary>
    public void SetEquipped(string layerId, bool equipped)
    {
        foreach (var l in _layers)
            if (l.id == layerId) l.renderer.gameObject.SetActive(equipped);
    }

    public bool IsEquipped(string layerId)
    {
        foreach (var l in _layers)
            if (l.id == layerId) return l.renderer.gameObject.activeSelf;
        return false;
    }

    /// <summary>The transform of a layer, for parenting effects like the lamp's light.</summary>
    public Transform GetLayerTransform(string layerId)
    {
        foreach (var l in _layers)
            if (l.id == layerId) return l.renderer.transform;
        return null;
    }
}
