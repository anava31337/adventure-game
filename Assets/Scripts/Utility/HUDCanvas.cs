using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HUDCanvas : MonoBehaviour
{
    public GameObject HpBar;
    public GameObject MpBar;
    public GameObject EnduranceBar;
    public Sprite[] hpSprites;
    public static HUDCanvas Instance { get; private set; }

    void Awake()
    {
        if (!Instance)
        {
            Instance = this;
        }
        else
        {
            Destroy(this.gameObject);
        }
    }

    void OnEnable()
    {
        //Player.OnPlayerDamage += UpdateHpBar;
        //Player.OnHpUpdate += UpdateHpBar;
    }

    void OnDisable()
    {
        //Player.OnPlayerDamage -= UpdateHpBar;
        //Player.OnHpUpdate -= UpdateHpBar;
    }

    void UpdateHpBar(int hpPercentage)
    {
        int animID = (hpSprites.Length - 1) - hpPercentage / 2;
        this.HpBar.GetComponent<Image>().sprite = hpSprites[animID];
    }

        void UpdateCoinHeld(int hpPercentage)
    {
        //Update UI text that represents the total value of held coins here...
    }
}
