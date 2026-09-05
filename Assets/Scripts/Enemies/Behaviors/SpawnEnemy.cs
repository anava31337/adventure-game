// =============================================================================
// SpawnEnemy.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// When this enemy dies, it spawns child enemies that arc outward from the
// death position (like a Metroid splitting). Each spawn point is checked
// against solid ground first, so children never spawn embedded in walls or
// floors where the player can't reach them.
// =============================================================================

using UnityEngine;

public class SpawnEnemy : MonoBehaviour
{
    [Header("Spawn")]
    [Tooltip("The enemy prefab to spawn on death.")]
    public GameObject enemyPrefab;
    [Tooltip("How many children to spawn.")]
    public int spawnCount = 2;

    [Header("Arc Launch")]
    [Tooltip("Horizontal launch speed of spawned children (px/s). The player's " +
             "run speed is 96, so values near that read as a confident leap.")]
    public float launchSpeedX = 80f;
    [Tooltip("Upward launch speed of spawned children (px/s). Defaults match the " +
             "player's jumpHeight (320) so the pop reads like a normal jump arc.")]
    public float launchSpeedY = 320f;   // == the player's jumpHeight
    [Tooltip("Maximum seconds the arc may run before control is handed back, in " +
             "case the creature never registers a landing. Normally it lands first.")]
    public float launchControlDelay = 1.5f;
    [Tooltip("Downward acceleration during the arc (px/s²). Defaults to 981 to match " +
             "the player's gravity, so the arc has the same shape as a player jump. " +
             "Owned by this script, so the pop is identical regardless of prefab setup.")]
    public float launchGravity = 981f;  // == gravityMultiplier (100) x 9.81

    [Tooltip("Extra gravity while falling, matching CharacterController2D's " +
             "fallGravityMultiplier. Your player currently uses 1 (a symmetric " +
             "arc); raise both together if you ever make the player fall faster.")]
    public float launchFallGravityMultiplier = 1f;

    [Tooltip("Terminal fall speed during the arc (px/s). Matches the player's " +
             "maxFallSpeed so a long drop behaves the same.")]
    public float launchMaxFallSpeed = 1200f;
    [Tooltip("Give each spawned creature randomised Patrol timings. OFF by default " +
             "so the pop-out direction, arc, and speed stay completely predictable.")]
    public bool randomizeSpawnedPatrol = false;

    [Header("Ground Avoidance")]
    [Tooltip("Layer(s) considered solid — spawns are nudged out of these.")]
    public LayerMask groundLayer;
    [Tooltip("Radius used to test whether a spawn point overlaps solid ground (px).")]
    public float overlapCheckRadius = 6f;
    [Tooltip("Vertical distance to lift a blocked spawn point looking for free space (px).")]
    public float clearanceStep = 8f;
    [Tooltip("Max attempts to find clear space before giving up on that spawn.")]
    public int maxClearanceTries = 6;

    private void OnEnable()  { Enemy.OnDeath += InstantiateClones; }
    private void OnDisable() { Enemy.OnDeath -= InstantiateClones; }

    private void InstantiateClones(AbstractCharacter sender)
    {
        var _self = GetComponent<AbstractCharacter>();

        if (sender != this.GetComponent<AbstractCharacter>()) return;
        if (enemyPrefab == null) return;

        for (int i = 0; i < spawnCount; i++)
        {
            // Alternate sides: 0 goes left, 1 right, 2 left, and so on.
            float side = (i % 2 == 0) ? -1f : 1f;

            // Spawn at the parent's EXACT position so the children burst out of
            // the creature that just died, rather than appearing already offset
            // up and to the side of it.
            Vector2 spawnPos = FindClearSpawn(transform.position);

            var clone = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);

            // Face and walk the way it was flung.
            var patrol = clone.GetComponent<Patrol>();
            if (patrol != null)
            {
                if (randomizeSpawnedPatrol)
                {
                    patrol.randomizeOnStart = true;
                    patrol.Randomize();
                }
                patrol.SetDirection(side < 0f);
            }

            // Refresh the HP bar. A freshly spawned creature has never been
            // damaged, so nothing would otherwise tell its bar what to display.
            var cloneChar = clone.GetComponent<AbstractCharacter>();
            if (cloneChar != null) cloneChar.RefreshHealthDisplay();

            // If the spawned thing is a projectile (a landlouse egg), launch it
            // through its OWN system rather than SpawnLaunch.
            //
            // Otherwise the two fight: LobbedProjectile suspends the character's
            // gravity and waits to be launched, but SpawnLaunch never calls
            // Launch(), so the projectile's arc physics, swept ground test and
            // "at rest" flag never run. The egg falls through the floor and can
            // never hatch, while an egg from EnemyLobber behaves perfectly —
            // exactly the inconsistency between thrown eggs and death eggs.
            var projectile = clone.GetComponent<LobbedProjectile>();
            if (projectile != null)
            {
                projectile.Launch(new Vector2(side * launchSpeedX, launchSpeedY), _self);
            }
            else
            {
                // The arc runs on the CLONE, not on this dying parent. This object
                // is destroyed immediately after OnDeath fires, so a coroutine
                // started here would be killed on the spot — which is why spawned
                // enemies used to drift down and then freeze permanently.
                var rb = clone.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    var launch = clone.AddComponent<SpawnLaunch>();
                    launch.Begin(new Vector2(side * launchSpeedX, launchSpeedY),
                                 launchGravity, launchControlDelay, groundLayer, patrol,
                                 launchFallGravityMultiplier, launchMaxFallSpeed);
                }
            }
        }
    }

    /// <summary>
    /// Returns a spawn position that is not embedded in solid ground.
    /// If the requested point overlaps ground, it lifts the point upward in
    /// steps until clear, so spawned enemies are always reachable.
    /// </summary>
    private Vector2 FindClearSpawn(Vector2 desired)
    {
        Vector2 test = desired;
        for (int t = 0; t < maxClearanceTries; t++)
        {
            bool blocked = Physics2D.OverlapCircle(test, overlapCheckRadius, groundLayer);
            if (!blocked) return test;
            test += Vector2.up * clearanceStep;   // lift out of the ground
        }
        // Could not find clear space — return the highest point we tried
        return test;
    }
}
