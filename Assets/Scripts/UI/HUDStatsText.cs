// =============================================================================
// HUDStatsText.cs   —   Assets/Scripts/UI/
//
// Displays the Hero's experience and coin count on the HUD.
//
// The numbers LIVE ON THE HERO; this only displays them. The old
// ScoreCalculator stored the score in the UI text itself — reading the number
// back out of the label, incrementing it, and writing it in again — so the
// displayed string WAS the data. That breaks the moment anything else needs the
// value (saving, a shop, a level-up check), and any change to the display
// format could corrupt it.
//
// Fully event-driven: text changes only when experience or coins actually
// change. Nothing is checked per frame.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;

// After Player (default 0), so the Hero's starting values exist when we first draw.
[DefaultExecutionOrder(100)]
public class HUDStatsText : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The Hero. Found by tag if left empty.")]
    public AbstractCharacter character;
    public string characterTag = "Player";

    [Header("Text")]
    public Text expValueText;
    public Text coinValueText;

    [Header("Formatting")]
    [Tooltip("Zero-padded width for both counters. 6 gives '000042'.")]
    public int digits = 6;

    private void Awake()
    {
        // Subscribe in Awake, before any Start() can publish.
        AbstractCharacter.OnExpChanged   += HandleExpChanged;
        AbstractCharacter.OnCoinsChanged += HandleCoinsChanged;
        AbstractCharacter.OnLeveledUp    += HandleExpChanged;
    }

    private void OnDestroy()
    {
        AbstractCharacter.OnExpChanged   -= HandleExpChanged;
        AbstractCharacter.OnCoinsChanged -= HandleCoinsChanged;
        AbstractCharacter.OnLeveledUp    -= HandleExpChanged;
    }

    private void Start()
    {
        if (character == null)
        {
            var go = GameObject.FindWithTag(characterTag);
            if (go != null) character = go.GetComponent<AbstractCharacter>();
        }

        // Draw the starting values once. After this, only events update the text.
        DrawExp();
        DrawCoins();
    }

    // These events are raised by EVERY character, so ignore any that aren't ours.
    private void HandleExpChanged(AbstractCharacter who)
    {
        if (who == character) DrawExp();
    }

    private void HandleCoinsChanged(AbstractCharacter who)
    {
        if (who == character) DrawCoins();
    }

    private void DrawExp()
    {
        if (expValueText == null || character == null) return;

        expValueText.text = Pad(character.Exp);
    }

    /// <summary>Formats a counter as a fixed-width, zero-padded number.</summary>
    private string Pad(int value)
    {
        return Mathf.Max(0, value).ToString(new string('0', Mathf.Max(1, digits)));
    }

    private void DrawCoins()
    {
        if (coinValueText == null || character == null) return;
        coinValueText.text = Pad(character.CoinPurse);
    }
}
