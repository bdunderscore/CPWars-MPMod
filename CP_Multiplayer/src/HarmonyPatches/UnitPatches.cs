using HarmonyLib;
using System.Reflection;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(Unit), "ActionResult")]
    internal class Unit_ActionResult
    {
        // ReSharper disable once InconsistentNaming
        private static readonly FieldInfo _actionProc = AccessTools.Field(typeof(Unit), "_actionProc");

        private static bool Prefix(Unit __instance)
        {
            if (MultiplayerManager.SuppressGameLogic)
            {
                // lock at fully-completed state until we hear back from the leader
                _actionProc.SetValue(__instance, 1.0f);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Unit), "NPCUpdate")]
    internal class Unit_NPCUpdate
    {
        private static bool Prefix()
        {
            return !MultiplayerManager.EveryoneIsPlayer;
        }
    }

    [HarmonyPatch(typeof(Unit), "Walk")]
    internal class Unit_Walk
    {
        private static bool Prefix()
        {
            return !MultiplayerManager.SuppressGameLogic;
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.Power), MethodType.Getter)]
    internal class Unit_Power
    {
        private static bool Prefix(Unit __instance, ref int __result)
        {
            if (MultiplayerManager.instance != null &&
                MultiplayerManager.instance.StatusOverrides.TryGetValue(__instance.Name, out var value))
            {
                __result = value.Power;
                return false;
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(Unit), nameof(Unit.Speed), MethodType.Getter)]
    internal class Unit_Speed
    {
        private static bool Prefix(Unit __instance, ref int __result)
        {
            if (MultiplayerManager.instance != null &&
                MultiplayerManager.instance.StatusOverrides.TryGetValue(__instance.Name, out var value))
            {
                __result = value.Speed;
                return false;
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(Unit), nameof(Unit.Intelligence), MethodType.Getter)]
    internal class Unit_Intelligence
    {
        private static bool Prefix(Unit __instance, ref int __result)
        {
            if (MultiplayerManager.instance != null &&
                MultiplayerManager.instance.StatusOverrides.TryGetValue(__instance.Name, out var value))
            {
                __result = value.Intelligence;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.SetCommand))]
    internal class Unit_SetCommand
    {
        private static void Prefix(Unit __instance, string Action)
        {
            if (MultiplayerManager.MultiplayerFollower)
            {
                PuppetClient.Instance.UnitSetCommand(__instance, Action);
            }
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.Move))]
    internal class Unit_Move
    {
        private static bool Prefix(Unit __instance, Room room)
        {
            if (MultiplayerManager.MultiplayerFollower)
            {
                PuppetClient.Instance.UnitMoveTo(__instance, room);
                
                return false;
            }

            return true;
        }
    }
    
    [HarmonyPatch(typeof(Unit), "Start")]
    internal class Unit_Start
    {
        private static void Prefix(Unit __instance)
        {
            if (MultiplayerManager.EveryoneIsPlayer)
            {
                __instance.AutoPlay = false;
            }
        }
    }
}
