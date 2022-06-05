using HarmonyLib;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(UnitManager), nameof(UnitManager.CheckClear))]
    class UnitManager_CheckClear
    {
        static bool Prefix(ref bool __result)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(UnitManager), nameof(UnitManager.CheckGameOver))]
    class UnitManager_CheckGameOver
    {
        static bool Prefix(ref bool __result)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}