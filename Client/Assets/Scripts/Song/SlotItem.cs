using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ItemType
{
    none, Rifle, Sniper, Handgun, Shotgun
}

[System.Serializable]
public class SlotItem
{
    public Sprite itemImage;
    public string itemName;
    public ItemType itemType;
}
