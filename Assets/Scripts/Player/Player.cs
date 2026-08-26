using System;
using System.Collections.Generic;
//using System.Runtime.CompilerServices;
using UnityEngine;

public class Player : AbstractCharacter
{
    public delegate void PlayerAction<T>(T action);
    public static event PlayerAction<Int32> OnPlayerDamage;
    public static event PlayerAction<Int32> OnPlayerHeal;

    #region Variables
    public List<Sprite> hpBar = new List<Sprite>();
    #endregion

    #region Properties
    #endregion

    #region MonoBehaviour
    private new void Start()
    {
        base.Start();
        this.SetLevelData(1);
        this.Hp = this.MaxHp;
        this.Mp = this.MaxMp;
        this.Endurance = this.MaxEndurance;
    }

    private void Update()
    {

    }

    public void FixedUpdate()
    {

    }

    void OnEnable()
    {
        // NOTE: enemy contact damage is no longer routed through a static event.
        // Put a DamageDealer on the enemy's BODY collider (damageOnStay = true,
        // small knockback) and it will damage the player directly on touch.
        Portal.OnPlayerSummon += MovePlayerToPortal;
        //DropItem.OnCharacterTouch += Heal;
    }

    void OnDisable()
    {
        Portal.OnPlayerSummon -= MovePlayerToPortal;
        //DropItem.OnCharacterContact -= Heal;
    }
    #endregion

    #region Methods
    /// <summary>
    /// The Player is the ONLY character that overrides this. Enemies and NPCs
    /// carry their HP bar as a child object, so the inherited broadcast reaches
    /// it; the player's bar lives on the HUD canvas instead, which listens to
    /// OnPlayerDamage. All the actual damage maths stays in AbstractCharacter.
    /// </summary>
    protected override void BroadcastHealth(int hpPercent)
    {
        OnPlayerDamage?.Invoke(hpPercent);
    }

    private void MovePlayerToPortal(Vector3 pos)
    {
        //Debug.Log(pos);
        //this.transform.position = pos;
    }
    #endregion

    #region Coroutines

    #endregion
}