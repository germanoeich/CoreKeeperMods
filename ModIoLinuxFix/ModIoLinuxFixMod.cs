using System;
using System.Collections.Generic;
using System.IO;
using PugMod;
using UnityEngine;

public class ModIoLinuxFixMod : IMod
{
    private const string LogPrefix = "[ModIoLinuxFix]";
    private static bool hasRun;
    private static int pendingDialogCopiedCount;

    public void EarlyInit()
    {
        EnsureMonoConfigFiles();
    }

    public void Init()
    {
        EnsureMonoConfigFiles();
    }

    public void Shutdown() { }

    public void ModObjectLoaded(UnityEngine.Object obj) { }

    public void Update()
    {
        TryShowCopiedFilesDialog();
    }

    private static void EnsureMonoConfigFiles()
    {
        if (hasRun)
        {
            return;
        }

        hasRun = true;

        if (!IsLinux())
        {
            return;
        }

        var dataPath = Application.dataPath;
        if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
        {
            Debug.LogWarning($"{LogPrefix} Could not resolve Application.dataPath.");
            return;
        }

        var patcherEtcPath = Path.Combine(dataPath, "StreamingAssets", "Patcher", "Linux", "Core Keeper Patcher_Data", "MonoBleedingEdge", "etc");
        var runtimeEtcPath = Path.Combine(dataPath, "MonoBleedingEdge", "etc");

        var filesToCopy = new (string source, string target)[]
        {
            (
                Path.Combine(patcherEtcPath, "config"),
                Path.Combine(runtimeEtcPath, "config")
            ),
            (
                Path.Combine(patcherEtcPath, "mono", "2.0", "machine.config"),
                Path.Combine(runtimeEtcPath, "mono", "2.0", "machine.config")
            ),
            (
                Path.Combine(patcherEtcPath, "mono", "4.0", "machine.config"),
                Path.Combine(runtimeEtcPath, "mono", "4.0", "machine.config")
            ),
            (
                Path.Combine(patcherEtcPath, "mono", "4.5", "machine.config"),
                Path.Combine(runtimeEtcPath, "mono", "4.5", "machine.config")
            )
        };

        var allTargetsExist = true;
        foreach (var file in filesToCopy)
        {
            if (!File.Exists(file.target))
            {
                allTargetsExist = false;
                break;
            }
        }

        if (allTargetsExist)
        {
            return;
        }

        var copiedCount = 0;
        foreach (var file in filesToCopy)
        {
            if (File.Exists(file.target))
            {
                continue;
            }

            if (!File.Exists(file.source))
            {
                Debug.LogWarning($"{LogPrefix} Missing source file: {file.source}");
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(file.target);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file.source, file.target, overwrite: false);
            copiedCount++;
            Debug.Log($"{LogPrefix} Copied {file.source} -> {file.target}");
        }

        if (copiedCount > 0)
        {
            pendingDialogCopiedCount = copiedCount;
            TryShowCopiedFilesDialog();
        }
    }

    private static bool IsLinux()
    {
        return Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer;
    }

    private static void TryShowCopiedFilesDialog()
    {
        if (pendingDialogCopiedCount <= 0)
        {
            return;
        }

        var menuManager = TryGetMenuManager();
        if (menuManager == null || menuManager.centerPopUpText == null || !IsTitleMainMenuReady(menuManager))
        {
            return;
        }

        var copiedCount = pendingDialogCopiedCount;
        Debug.Log($"{LogPrefix} Showing restart dialog after copying {copiedCount} file(s).");
        
        var message = $"Mod.io fix applied. You can uninstall the mod if you wish.\nA restart is required for the fix to take effect. Restart now?";
        menuManager.centerPopUpText.StartNewDisplaySequence(
            text: message,
            menuInputCooldown: true,
            fadeTime: 0f,
            staticTime: 1.5f,
            useUnscaledTime: true,
            yPosition: 0f,
            textBackgroundAlpha: 1f,
            localize: false,
            fontFace: TextManager.FontFace.boldMedium,
            optionsCallback: response =>
            {
                if (!response.IsConfirm)
                {
                    return;
                }

                Debug.Log($"{LogPrefix} Restart button pressed; restarting game.");
                if (Manager.platform == null)
                {
                    Debug.LogWarning($"{LogPrefix} Could not restart because Manager.platform is null.");
                    return;
                }

                Manager.platform.Restart();
            },
            options: new List<string> { "cancelDialogue", "yes" },
            minWidth: 10f,
            backgroundAlpha: 0.8f,
            priority: 0,
            textMaxWidth: 20f,
            secondOptionPopsAllMenus: false,
            pauseGame: true,
            holdToConfirm: false,
            localizePlaceholders: false,
            accidentalInputBlockDuration: 0f
        );
        pendingDialogCopiedCount = 0;
    }

    private static bool IsTitleMainMenuReady(MenuManager menuManager)
    {
        try
        {
            if (Manager.load == null || Manager.sceneHandler == null)
            {
                return false;
            }

            if (Manager.load.IsSceneTransitionOrLoading() || Manager.load.IsScreenFadingOutOrBlack())
            {
                return false;
            }

            if (!Manager.sceneHandler.isTitle || !Manager.sceneHandler.isSceneHandlerReady || Manager.sceneHandler.isIntro || Manager.sceneHandler.isOutro || Manager.sceneHandler.isGameStartUpLoading)
            {
                return false;
            }

            return menuManager.GetTopMenu() is RadicalMainMenu;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static MenuManager TryGetMenuManager()
    {
        try
        {
            return Manager.menu;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
