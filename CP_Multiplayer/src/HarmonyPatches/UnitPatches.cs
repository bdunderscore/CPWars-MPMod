using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

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
            return !MultiplayerManager.SuppressGameLogic;
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

    internal class PatchTeamOneChecks
    {
        private static MethodInfo m_get_Team = AccessTools.PropertyGetter(typeof(Unit), nameof(Unit.Team));
        private static MethodInfo m_patch = AccessTools.Method(typeof(PatchTeamOneChecks), nameof(PatchTeamValue));

        internal static void PatchClass(Harmony h, Type ty)
        {
            var methods = ty.GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly
            );

            var transpiler = AccessTools.Method(typeof(PatchTeamOneChecks), nameof(Transpiler));

            foreach (var method in methods)
            {
                if (method.HasMethodBody())
                {
                    h.Patch(method, transpiler: new HarmonyMethod(transpiler));
                }
            }
        }
        
        static int PatchTeamValue(int actualTeam)
        {
            if (MultiplayerManager.EveryoneIsPlayer)
            {
                return 1;
            }
            else
            {
                return actualTeam;
            }
        }
        
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var outinsns = new List<CodeInstruction>();

            var branches = new HashSet<OpCode>();
            branches.Add(OpCodes.Beq);
            branches.Add(OpCodes.Beq_S);
            branches.Add(OpCodes.Bne_Un);
            branches.Add(OpCodes.Bne_Un_S);
            branches.Add(OpCodes.Ceq);
            
            foreach (var instruction in instructions)
            {
                if (outinsns.Count < 2 || !branches.Contains(instruction.opcode))
                {
                    outinsns.Add(instruction);
                    continue;
                }
                
                // Look for pattern: get team, const 1, jump
                // Inject after get team our hook

                var l1 = outinsns[outinsns.Count - 2];
                var l2 = outinsns[outinsns.Count - 1];
                
                if (l1.Calls(m_get_Team) && l2.LoadsConstant(1))
                {
                    outinsns.Insert(outinsns.Count - 1, new CodeInstruction(OpCodes.Call, m_patch));
                } else if (l2.Calls(m_get_Team) && l1.LoadsConstant(1))
                {
                    outinsns.Add(new CodeInstruction(OpCodes.Call, m_patch));
                }
                outinsns.Add(instruction);
            }

            return outinsns;
        }
    }
}
