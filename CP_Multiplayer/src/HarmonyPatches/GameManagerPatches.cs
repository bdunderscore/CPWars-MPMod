using System.Reflection;
using HarmonyLib;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    // TODO - predictive timer
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.UpdateTimer))]
    internal class GameManager_UpdateTimer
    {
        private static FieldInfo f_hour = AccessTools.Field(typeof(GameManager), "_hour");
        private static FieldInfo f_min = AccessTools.Field(typeof(GameManager), "_min");
        private static float lastHour = 0;
        private static float lastMin = 0;
        private static bool Prefix(GameManager __instance)
        {
            if (MultiplayerManager.SuppressGameLogic)
            {
                var hour = (float)f_hour.GetValue(__instance);
                if (hour < lastHour)
                {
                    GameSetup.NextBGM();
                }

                lastHour = hour;
                return false;
            }
            else
            {
                lastMin = (float) f_min.GetValue(__instance);
                return true;
            }
        }

        private static void Postfix()
        {
            if (MultiplayerManager.MultiplayerFollower) return;
            
            var min = (float) f_min.GetValue(GameManager.Instance);
            if (min >= lastMin)
            {
                return; // No hour rollover - no money increase
            }
            
            // Apply money updates. Note that the original logic in UpdateTimer has no effect, because
            // money tracking is mastered in the MultiplayerManager (so we'll overwrite/ignore the GameManager.Money
            // value later)
            foreach (Room room in RoomManager.Instance.Rooms)
            {
                if (room.DominationTeam > 0)
                {
                    MultiplayerManager.SetMoney(room.DominationTeam, MultiplayerManager.GetMoney(room.DominationTeam) + 10);
                }
            }
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
