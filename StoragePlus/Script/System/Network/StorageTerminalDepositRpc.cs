using Unity.Entities;
using Unity.NetCode;

public struct StorageTerminalDepositRpc : IRpcCommand
{
    public Entity relayEntity;
    public int sourceSlot;
    public int amount;
}
