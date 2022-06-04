using HarmonyLib;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    // TODO - predictive timer
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.UpdateTimer))]
    internal class GameManager_UpdateTimer
    {
        private static bool Prefix()
        {
            return !(MultiplayerManager.SuppressGameLogic);
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Tick))]
    internal class GameManager_Tick
    {
        internal delegate void Callback();

        internal static Callback AfterTick = () => { };

        private static bool Prefix()
        {
            if (MultiplayerManager.SuppressGameLogic)
            {
                foreach (WayPoint way in WayPointManager.Instance.Ways)
                    way.Tick();
                
                return false;
            }

            return true;
        }

        private static void Postfix()
        {
            AfterTick();
        }
    }

}
