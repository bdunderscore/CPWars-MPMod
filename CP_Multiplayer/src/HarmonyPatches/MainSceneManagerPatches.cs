using System;
using HarmonyLib;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(MainSceneManager), nameof(MainSceneManager.StartTitle))]
    class MainSceneManager_StartTitle
    {
        static bool Prefix()
        {
            if (Environment.CommandLine.Contains("+client_test"))
            {
                MultiplayerManager.instance.isPuppet = true;
                
                MainSceneManager.Instance.StartGame();
                return false;
            }

            return true;
        }
    }
}