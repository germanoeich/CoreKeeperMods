using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PugMod;
using UnityEngine;

public static class DebugModReloadPatches
{
    private const string CoreLibModTypeName = "CoreLib.CoreLibMod";

    internal static bool CanBeUnloadedForDebugReload(IMod mod)
    {
        if (mod == null)
        {
            return true;
        }

        return mod.CanBeUnloaded() || IsCoreLibMod(mod);
    }

    private static bool HasReloadBlockers(IEnumerable<LoadedMod> loadedMods)
    {
        foreach (LoadedMod loadedMod in loadedMods)
        {
            if (loadedMod?.Handlers == null)
            {
                continue;
            }

            for (int i = 0; i < loadedMod.Handlers.Count; i++)
            {
                if (!CanBeUnloadedForDebugReload(loadedMod.Handlers[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCoreLibMod(IMod mod)
    {
        return mod.GetType().FullName == CoreLibModTypeName;
    }

    [HarmonyPatch(typeof(ScriptableData), nameof(ScriptableData.AddDataBlocksLoader))]
    private static class ScriptableDataAddDataBlocksLoaderPatch
    {
        private static readonly FieldInfo DataBlockLoadersField =
            AccessTools.Field(typeof(ScriptableData), "s_dataBlockLoaders");

        [HarmonyPrefix]
        private static void Prefix(string name)
        {
            if (DataBlockLoadersField?.GetValue(null) is Dictionary<string, IScriptableDataLoader> loaders &&
                loaders.ContainsKey(name))
            {
                ScriptableData.RemoveDataBlocksLoader(name);
            }
        }
    }

    [HarmonyPatch]
    private static class ModResourceProviderAddAssetPatch
    {
        private static MethodBase TargetMethod()
        {
            System.Type providerType = AccessTools.TypeByName("PugMod.ModResourceProvider");
            return providerType == null ? null : AccessTools.Method(providerType, "AddAsset");
        }

        [HarmonyPrefix]
        private static bool Prefix(string key, Object asset)
        {
            if (IsBrokenAsset(asset, out string reason))
            {
                Debug.LogWarning($"[DebugMod] Skipping invalid mod asset for key {key}: {reason}");
                return false;
            }

            return true;
        }

        private static bool IsBrokenAsset(Object asset, [NotNullWhen(true)] out string reason)
        {
            if (asset == null)
            {
                reason = "asset was null";
                return true;
            }

            try
            {
                if (string.IsNullOrEmpty(asset.name))
                {
                    reason = "asset had no name";
                    return true;
                }

                _ = asset.GetType();
            }
            catch (System.Exception exception)
            {
                reason = exception.GetType().Name + ": " + exception.Message;
                return true;
            }

            reason = null;
            return false;
        }
    }

    [HarmonyPatch]
    private static class LoaderReloadPatch
    {
        private static MethodBase TargetMethod()
        {
            System.Type loaderType = AccessTools.TypeByName("PugMod.Loader");
            return loaderType == null ? null : AccessTools.Method(loaderType, "Reload");
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance)
        {
            if (Integration.Instance?.LoadedMods != null && HasReloadBlockers(Integration.Instance.LoadedMods))
            {
                return;
            }

            FieldInfo modResourceProviderField = __instance.GetType().GetField("_modResourceProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            if (modResourceProviderField == null)
            {
                return;
            }

            if (modResourceProviderField.GetValue(__instance) is System.IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }

            object replacementProvider = System.Activator.CreateInstance(modResourceProviderField.FieldType);
            modResourceProviderField.SetValue(__instance, replacementProvider);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo canBeUnloadedMethod = AccessTools.Method(typeof(IMod), nameof(IMod.CanBeUnloaded));
            MethodInfo replacementMethod = AccessTools.Method(typeof(DebugModReloadPatches), nameof(CanBeUnloadedForDebugReload));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(canBeUnloadedMethod))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacementMethod);
                    continue;
                }

                yield return instruction;
            }
        }
    }

    [HarmonyPatch]
    private static class LoaderResetPatch
    {
        private static MethodBase TargetMethod()
        {
            System.Type loaderType = AccessTools.TypeByName("PugMod.Loader");
            if (loaderType == null)
            {
                return null;
            }

            return loaderType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "Reset" && method.GetParameters().Length == 1);
        }

        [HarmonyPrefix]
        private static void Prefix(object __0)
        {
            string modGuid = TryGetModGuid(__0);
            if (string.IsNullOrEmpty(modGuid))
            {
                return;
            }

            FieldInfo dataBlockLoadersField = AccessTools.Field(typeof(ScriptableData), "s_dataBlockLoaders");
            if (dataBlockLoadersField?.GetValue(null) is not Dictionary<string, IScriptableDataLoader> loaders ||
                !loaders.ContainsKey(modGuid))
            {
                return;
            }

            ScriptableData.RemoveDataBlocksLoader(modGuid);
        }

        private static string TryGetModGuid(object mod)
        {
            if (mod == null)
            {
                return null;
            }

            FieldInfo metadataField = mod.GetType().GetField("Metadata");
            object metadata = metadataField?.GetValue(mod);
            if (metadata == null)
            {
                return null;
            }

            FieldInfo guidField = metadata.GetType().GetField("guid");
            return guidField?.GetValue(metadata) as string;
        }
    }
}
