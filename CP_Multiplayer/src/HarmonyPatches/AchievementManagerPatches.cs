using System.Collections.Generic;
using HarmonyLib;

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(AchievementData), nameof(AchievementData.CheckAllAchievements))]
    class AchievementManagerPatches
    {
        static bool Prefix(ref List<string> __result)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                __result = new List<string>();
                return false;
            }

            return true;
        }
    }
}