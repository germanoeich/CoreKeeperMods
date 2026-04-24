using UnityEngine;

public class CornucopiaAuraAuthoring : MonoBehaviour
{
    [Min(0f)]
    public float radius = 8f;

    [Range(0, 100)]
    public int drainLessHungerPercent = 100;

    [Min(0.1f)]
    public float buffRefreshDuration = 0.75f;
}
