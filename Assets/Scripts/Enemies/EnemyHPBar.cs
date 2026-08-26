using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHPBar : MonoBehaviour
{
    [SerializeField]
    private Sprite[] hpSprites;

    void Start()
    {
        //Note: Revise this later to set the SortingLayer / SortingLayerOrder on the same layer as UI?
        if (this.GetComponent<SpriteRenderer>() != null && MapManager.Instance.map != null)
        {
            this.GetComponent<SpriteRenderer>().sortingOrder = MapManager.Instance.groundLayerID;
        }

        // Safety net: draw the owner's current health immediately. The character's
        // own Start() also publishes this, but component start order isn't
        // guaranteed, and a bar that never receives a value keeps whatever sprite
        // the prefab was saved with — which looks like an empty bar on a spawn.
        var owner = GetComponentInParent<AbstractCharacter>();
        if (owner != null) SetBar(owner.HealthPercent);
    }

    void OnEnable()
    {
        AbstractCharacter.OnCharacterDamaged += UpdateHpBar;
        //Enemy.OnDeath += DestroyHPBar;
    }

    void OnDisable()
    {
        AbstractCharacter.OnCharacterDamaged -= UpdateHpBar;
    }

    // Subscribing to the shared AbstractCharacter event means this same HP bar
    // works unchanged on Enemies AND NPCs — it simply filters for its own parent.
    void UpdateHpBar(int hpPercentage, AbstractCharacter character)
    {
        if (character == this.GetComponentInParent<AbstractCharacter>())
            SetBar(hpPercentage);
    }

    /// <summary>Selects the sprite for a 0–100 health percentage.</summary>
    private void SetBar(int hpPercentage)
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null || hpSprites == null || hpSprites.Length == 0) return;

        int animID = hpSprites.Length - (hpPercentage / 4);
        animID = Mathf.Clamp(animID, 0, hpSprites.Length - 1);   // guard the ends
        sr.sprite = hpSprites[animID];
    }
    void DestroyHPBar(AbstractCharacter character)
    {
        Destroy(this.gameObject);
    }
}