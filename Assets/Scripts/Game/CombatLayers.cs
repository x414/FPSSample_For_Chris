using UnityEngine;

public static class CombatLayers
{
    public static readonly int GroundMask =
        1 << LayerMask.NameToLayer("Default") |
        1 << LayerMask.NameToLayer("collision_detail") |
        1 << LayerMask.NameToLayer("Platform");

    public static readonly int CombatRaycastMask =
        1 << LayerMask.NameToLayer("Default") |
        1 << LayerMask.NameToLayer("collision_detail") |
        1 << LayerMask.NameToLayer("Platform") |
        1 << LayerMask.NameToLayer("hitcollision_enabled");

}
