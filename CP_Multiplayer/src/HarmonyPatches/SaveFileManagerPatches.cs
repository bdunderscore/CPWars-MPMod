using CPMod_Multiplayer.LobbyManagement;
using HarmonyLib;

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(SaveFileManager), nameof(SaveFileManager.SaveCharacter))]
    class SaveFileManager_SaveCharacter
    {
        static bool Prefix()
        {
            if (MultiplayerManager.MultiplayerSession || LobbyManager.CurrentLobby != null) return false;
            return true;
        }
    }
}