// =============================================================================
// HatchAfterDelay.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Turns this object into something else after a delay — the Landlouse Egg
// hatching into a Landlouse.
//
// Because the egg is destroyed by the player like any other character (it has
// an AbstractCharacter and takes damage), killing it before the timer elapses
// prevents the hatch entirely: "A landlouse egg may be destroyed by the player
// before hatching, killing the landlouse inside before it spawns."
//
// Optionally waits until the egg has come to rest, so an egg still sailing
// through the air doesn't pop open mid-flight.
// =============================================================================

using UnityEngine;

public class HatchAfterDelay : MonoBehaviour
{
    [Header("Hatching")]
    [Tooltip("What this becomes. Spawned at this object's position.")]
    public GameObject hatchesInto;

    [Tooltip("Seconds before hatching.")]
    public float hatchDelay = 5f;

    [Tooltip("Random +/- variation on the delay, so a clutch of eggs doesn't all " +
             "hatch on exactly the same frame.")]
    public float delayJitter = 1f;

    [Tooltip("Wait until the egg has settled before the timer starts. Prevents an " +
             "egg hatching while still in mid-air.")]
    public bool requireAtRest = true;

    [Header("Presentation")]
    [Tooltip("Optional effect spawned at the moment of hatching (shell burst, puff).")]
    public GameObject hatchEffect;

    [Tooltip("Seconds of warning shake/flash before hatching. Set 0 for none. " +
             "The ColorBlinker on this object, if present, is enabled for it.")]
    public float tellDuration = 0.8f;

    private float            _timer;
    private bool             _hatched;
    private bool             _tellStarted;
    private LobbedProjectile _projectile;

    private void Awake()
    {
        _projectile = GetComponent<LobbedProjectile>();
        _timer      = hatchDelay + Random.Range(-delayJitter, delayJitter);
    }

    private void Update()
    {
        if (_hatched) return;

        // Hold the timer until the egg lands, if asked to.
        if (requireAtRest && _projectile != null && !_projectile.AtRest) return;

        _timer -= Time.deltaTime;

        // Warning tell so the player has a moment to smash it first.
        if (!_tellStarted && tellDuration > 0f && _timer <= tellDuration)
        {
            _tellStarted = true;
            var blinker = GetComponent<ColorBlinker>();
            if (blinker != null) blinker.enabled = true;
        }

        if (_timer <= 0f) Hatch();
    }

    private void Hatch()
    {
        _hatched = true;

        if (hatchEffect != null)
            Instantiate(hatchEffect, transform.position, Quaternion.identity);

        if (hatchesInto != null)
            Instantiate(hatchesInto, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}
