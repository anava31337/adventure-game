using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonetaryDropItem : DropItem
{
    public int monetaryValue;
    public override void Use(AbstractCharacter character)
    {
        base.Use(character);
        if (character != null)
        {
            // AddCoins clamps to the purse cap and raises OnCoinsChanged. The old
            // comparison used "< 9999" then "> 9999", so landing on exactly 9999
            // matched neither branch and the coin was silently lost.
            character.AddCoins(monetaryValue);
            character.CoinUpdate(monetaryValue);   // legacy event, kept for existing listeners
        }
    }
}
