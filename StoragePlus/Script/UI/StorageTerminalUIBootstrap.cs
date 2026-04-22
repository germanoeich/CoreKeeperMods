using CoreLib.Submodule.UserInterface.Component;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ModUIAuthoring))]
[RequireComponent(typeof(LinkToPlayerInventory))]
[RequireComponent(typeof(StorageTerminalUI))]
public sealed class StorageTerminalUIBootstrap : MonoBehaviour
{
}
