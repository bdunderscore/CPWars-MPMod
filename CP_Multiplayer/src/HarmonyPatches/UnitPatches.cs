using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using CPMod_Multiplayer.LobbyManagement;

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

    internal class Unit_Money_Access
    {
        static MethodInfo m_Money_Get 
            = AccessTools.PropertyGetter(typeof(GameManager), nameof(GameManager.Money));
        static MethodInfo m_Money_Set
            = AccessTools.PropertySetter(typeof(GameManager), nameof(GameManager.Money));

        private static MethodInfo patch_GetMoney = AccessTools.Method(typeof(Unit_Money_Access), nameof(GetMoney));
        private static MethodInfo patch_SetMoney = AccessTools.Method(typeof(Unit_Money_Access), nameof(SetMoney));
        
        static int GetMoney(GameManager instance, Unit caller)
        {
            if (!MultiplayerManager.MultiplayerSession)
            {
                return instance.Money;
            }
            else
            {
                return MultiplayerManager.GetMoney(caller.Team);
            }
        }

        static void SetMoney(GameManager instance, int value, Unit caller)
        {
            if (!MultiplayerManager.MultiplayerSession)
            {
                instance.Money = value;
            }
            else
            {
                MultiplayerManager.SetMoney(caller.Team, value);
            }
        }

        public static void PatchClass(Harmony h) {
            var methods = typeof(Unit).GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly
            );

            var transpiler = AccessTools.Method(typeof(Unit_Money_Access), nameof(Transpiler));

            foreach (var method in methods)
            {
                if (method.HasMethodBody())
                {
                    h.Patch(method, transpiler: new HarmonyMethod(transpiler));
                }
            }
        }
        
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insns)
        {
            var output = new List<CodeInstruction>();

            foreach (var insn in insns)
            {
                if (insn.Calls(m_Money_Get))
                {
                    // push(gameManager) call -> push(gameManager) push(this) call
                    output.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    output.Add(new CodeInstruction(OpCodes.Call, patch_GetMoney));
                } else if (insn.Calls(m_Money_Set))
                {
                    // push(gameManager) push(value) call -> push(gameManager) push(value) push(this) call
                    output.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    output.Add(new CodeInstruction(OpCodes.Call, patch_SetMoney));
                }
                else
                {
                    output.Add(insn);
                }
            }

            return output;
        }
    }
}
