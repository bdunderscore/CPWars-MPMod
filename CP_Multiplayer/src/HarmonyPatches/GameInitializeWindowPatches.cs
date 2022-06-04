using CPMod_Multiplayer.LobbyManagement;
using HarmonyLib;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(GameInitializeWindow), nameof(GameInitializeWindow.UpdateSlotIcon))]
    internal class GameInitializeWindow_UpdateSlotIcon
    {
        internal static bool noIntercept = false;
        
        static bool Prefix(GameInitializeWindow __instance)
        {
            var mpLobby = __instance.GetComponent<MultiplayerLobbyWindow>();

            if (mpLobby != null && !noIntercept)
            {
                mpLobby.UpdateMySlotIcons();
                return false;
            }

            return true;
        }
    }
}