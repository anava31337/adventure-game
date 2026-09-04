// =============================================================================
// EnemyLobber.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Periodically lobs a projectile in an arc, alternating sides — the Pregnant
// Landlouse tossing eggs.
//
// This is the arcing counterpart to EnemyProjectile (which fires straight at the
// player). It mirrors SpawnEnemy/SpawnLaunch: the projectile leaves from the
// creature's own position with an upward and sideways impulse, follows the same
// gravity curve as a player jump, and lands as a persistent world object.
//
// Pair the projectile prefab with LobbedProjectile (arc + damage while moving)
// and HatchAfterDelay (turns into a creature).
// =============================================================================

using UnityEngine;

public class EnemyLobber : MonoBehaviour
{
    [Header("Projectile")]
    [Tooltip("Prefab to lob. Needs a LobbedProjectile component.")]
    public GameObject projectilePrefab;

    [Tooltip("Spawn offset from this creature's centre (px). Y raises the throw " +
             "so the egg leaves from the body rather than the ground.")]
    public Vector2 spawnOffset = new Vector2(0f, 8f);

    [Header("Arc")]
    [Tooltip("Sideways speed of the toss (px/s), rolled between min and max so " +
             "successive eggs travel different distances.")]
    public MinMaxRange lobSpeedX = new MinMaxRange(70f, 120f);

    [Tooltip("Upward speed of the toss (px/s), rolled between min and max. The " +
             "player's jumpHeight is 320, so similar values give a comparable arc.")]
    public MinMaxRange lobSpeedY = new MinMaxRange(260f, 340f);
    [Tooltip("Alternate left/right with each toss. Off = always toward the player.")]
    public bool alternateSides = true;

    [Header("Timing")]
    [Tooltip("Seconds between tosses.")]
    public float lobInterval = 3f;
    [Tooltip("Random +/- variation on the interval.")]
    public float intervalJitter = 0.6f;
    [Tooltip("Wait this long after spawning before the first toss.")]
    public float initialDelay = 1f;

    [Header("Targeting")]
    [Tooltip("Only toss when the player is within this distance (px). 0 = always.")]
    public float activationRange = 0f;
    public string targetTag = "Player";

    private float             _timer;
    private bool              _lastWasLeft;
    private Transform         _player;
    private AbstractCharacter _self;

    private void OnEnable()
    {
        _self  = GetComponent<AbstractCharacter>();
        _timer = initialDelay;

        var p = GameObject.FindWithTag(targetTag);
        if (p != null) _player = p.transform;
    }

    private void Update()
    {
        if (projectilePrefab == null) return;

        // Optional proximity gate so a creature off-screen isn't firing forever.
        if (activationRange > 0f)
        {
            if (_player == null) return;
            if (Vector2.Distance(transform.position, _player.position) > activationRange) return;
        }

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        Lob();
        _timer = lobInterval + Random.Range(-intervalJitter, intervalJitter);
    }

    private void Lob()
    {
        // Which way this one goes.
        float side;
        if (alternateSides)
        {
            _lastWasLeft = !_lastWasLeft;
            side = _lastWasLeft ? -1f : 1f;
        }
        else if (_player != null)
        {
            side = Mathf.Sign(_player.position.x - transform.position.x);
            if (side == 0f) side = 1f;
        }
        else side = 1f;

        Vector2 origin = (Vector2)transform.position + new Vector2(spawnOffset.x * side, spawnOffset.y);

        var go   = Instantiate(projectilePrefab, origin, Quaternion.identity);
        var proj = go.GetComponent<LobbedProjectile>();

        if (proj != null)
        {
            // Roll a fresh arc per toss so eggs scatter instead of stacking up
            // in one predictable spot.
            proj.Launch(new Vector2(side * lobSpeedX.Random(), lobSpeedY.Random()), _self);
        }
        else
        {
            // Fallback so a plain rigidbody prefab still gets thrown sensibly.
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null) rb.velocity = new Vector2(side * lobSpeedX.Random(), lobSpeedY.Random());
            Debug.LogWarning("[EnemyLobber] projectilePrefab has no LobbedProjectile " +
                             "component — it will not arc, damage, or settle correctly.", this);
        }
    }
}
