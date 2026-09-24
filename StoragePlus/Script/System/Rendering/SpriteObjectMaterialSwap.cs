using Pug.Sprite;
using PugMod;
using UnityEngine;

/// <summary>
/// Resolves the SDK's "UGC SpriteObject *" placeholder materials on SpriteObjects to the game's
/// real materials through the game's MaterialSwapTable.
/// Core Keeper 1.2 did this itself at mod load (SpriteInstancingModAssetProcessor). 1.3 removed
/// that processor, and the mod loader's own swap only covers Renderer and ParticleSystem
/// components, so SpriteObjects kept the placeholder "SpriteObject/Simple" shader and rendered
/// unlit. This is a port of the removed 1.2 behaviour and becomes a no-op if the game restores it.
/// </summary>
internal static class SpriteObjectMaterialSwap
{
    public static int Apply(GameObject root)
    {
        int swapped = 0;
        foreach (SpriteObject spriteObject in root.GetComponentsInChildren<SpriteObject>(includeInactive: true))
        {
            Material placeholder = spriteObject.material;
            if (placeholder == null)
            {
                continue;
            }

            Material gameMaterial = API.Rendering.GetMaterial(placeholder.name);
            if (gameMaterial == null || gameMaterial == placeholder)
            {
                continue;
            }

            spriteObject.material = gameMaterial;
            swapped++;
        }

        if (swapped > 0)
        {
            StoragePlusMod.Log.LogInfo($"Resolved {swapped} SpriteObject material(s) on {root.name}");
        }

        return swapped;
    }
}
