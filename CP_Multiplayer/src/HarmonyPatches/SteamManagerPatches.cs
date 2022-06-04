using System.Text;
using HarmonyLib;
using Steamworks;

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(SteamManager), "SteamAPIDebugTextHook")]
    class SteamManager_SteamAPIDebugTextHook
    {
        static void Prefix(int nSeverity, StringBuilder pchDebugText)
        {
            Mod.logger.Log($"[Steam/{nSeverity}] {pchDebugText}");
        } 
    }
}