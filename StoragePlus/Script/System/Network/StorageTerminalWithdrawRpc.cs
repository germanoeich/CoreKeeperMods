using Unity.Entities;
using Unity.NetCode;

public struct StorageTerminalWithdrawRpc : IRpcCommand
{
    public Entity relayEntity;
    public ObjectID objectId;
    public int variation;
    public int itemAmount;
    public int auxDataIndex;
    public ulong entryId;
    public byte flags;
    public int amount;
}
