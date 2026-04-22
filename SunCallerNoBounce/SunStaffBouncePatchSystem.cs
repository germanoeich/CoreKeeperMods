using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation, WorldSystemFilterFlags.Default)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class SunStaffBouncePatchSystem : PugSimulationSystemBase
{
    private const int MaxBounceCount = 9999999;

    protected override void OnCreate()
    {
        NeedDatabase();
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        Entity projectilePrefab = PugDatabase.GetPrimaryPrefabEntity(ObjectID.SunStaffProjectile, databaseBank.databaseBankBlob);

        if (projectilePrefab != Entity.Null && EntityManager.HasComponent<BouncingProjectileCD>(projectilePrefab))
        {
            // Use EntityManager.GetComponentData to get the component you need to modify
            // Modify it
            // Set it back on the projectile prefab with Entity Manager
        }

        Enabled = false;
        base.OnUpdate();
    }
}
