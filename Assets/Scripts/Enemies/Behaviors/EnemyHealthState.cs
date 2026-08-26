// =============================================================================
// EnemyHealthState.cs   —   Assets/Scripts/Enemies/Behaviors/
//
// Changes the enemy's behaviour when its health drops below a threshold —
// for example, enraging (faster, more aggressive) or fleeing when wounded.
// Listens to AbstractCharacter.OnCharacterDamaged and fires UnityEvents at the threshold.
//
// Wire OnEnraged in the Inspector to: boost EnemyChase.chaseSpeed, shorten
// EnemyProjectile.fireInterval, enable EnemyFlee, swap an animation, etc.
// =============================================================================

using UnityEngine;
using UnityEngine.Events;

public class EnemyHealthState : MonoBehaviour
{
    [Header("Threshold")]
    [Tooltip("Health percent (0-100) at or below which the state changes.")]
    [Range(0f, 100f)]
    public float thresholdPercent = 30f;

    [Header("Events")]
    [Tooltip("Fired once when health first drops to/below the threshold.")]
    public UnityEvent OnEnraged;
    [Tooltip("Fired once if health rises back above the threshold (e.g. healing).")]
    public UnityEvent OnRecovered;

    private AbstractCharacter self;
    private bool triggered;

    private void OnEnable()
    {
        self = GetComponent<AbstractCharacter>();
        AbstractCharacter.OnCharacterDamaged += HandleDamage;
    }

    private void OnDisable()
    {
        AbstractCharacter.OnCharacterDamaged -= HandleDamage;
    }

    // AbstractCharacter.OnCharacterDamaged passes (hpPercent, theDamagedCharacter)
    private void HandleDamage(int hpPercent, AbstractCharacter who)
    {
        if (who != self) return;   // only react to OUR own damage

        if (!triggered && hpPercent <= thresholdPercent)
        {
            triggered = true;
            OnEnraged?.Invoke();
        }
        else if (triggered && hpPercent > thresholdPercent)
        {
            triggered = false;
            OnRecovered?.Invoke();
        }
    }
}
