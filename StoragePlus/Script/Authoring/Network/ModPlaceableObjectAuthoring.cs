using System.Collections.Generic;
using UnityEngine;

public class ModPlaceableObjectAuthoring : MonoBehaviour
{
    [Tooltip("Additional objects this placeable can be placed on. Supports base-game ObjectID names, numeric IDs, and mod object names from ObjectAuthoring.objectName.")]
    public List<string> canBePlacedOnObjects = new();

    [Tooltip("Additional objects this placeable cannot be placed on. Supports base-game ObjectID names, numeric IDs, and mod object names from ObjectAuthoring.objectName.")]
    public List<string> canNotBePlacedOnObjects = new();
}
