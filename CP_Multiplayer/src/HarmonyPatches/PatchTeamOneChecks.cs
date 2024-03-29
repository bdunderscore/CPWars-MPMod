﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CPMod_Multiplayer.HarmonyPatches
{
    internal class PatchTeamOneChecks
    {
        private static readonly MethodInfo m_get_Team = AccessTools.PropertyGetter(typeof(Unit), nameof(Unit.Team));
        private static readonly MethodInfo t_UnitTranspiler = AccessTools.Method(typeof(PatchTeamOneChecks), nameof(UnitTranspiler));
        private static readonly MethodInfo t_UiTranspiler = AccessTools.Method(typeof(PatchTeamOneChecks), nameof(UI_Transpiler));

        internal static void PatchClasses(Harmony h)
        {
            PatchClass(h, typeof(Unit), t_UnitTranspiler);
            PatchClass(h, typeof(UI_Unit), t_UiTranspiler);
            PatchClass(h, typeof(UI_Status), t_UiTranspiler);
            h.Patch(AccessTools.Method(typeof(GameManager), nameof(GameManager.CalcAddMoney)),
                transpiler: new HarmonyMethod(t_UiTranspiler));
        }
        internal static void PatchClass(Harmony h, Type ty, MethodInfo methodInfo) {
            var methods = ty.GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly
            );

            var transpiler = methodInfo;

            foreach (var method in methods)
            {
                if (method.HasMethodBody())
                {
                    h.Patch(method, transpiler: new HarmonyMethod(transpiler));
                }
            }
        }
        
        static int UI_Patch(int actualTeam)
        {
            if (actualTeam == MultiplayerManager.MyTeam)
            {
                return 1;
            }
            else
            {
                return 999;
            }
        }

        internal static IEnumerable<CodeInstruction> UI_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return Transpiler(instructions, AccessTools.Method(typeof(PatchTeamOneChecks), nameof(UI_Patch)));
        }
        
        static int UnitPatch(int actualTeam)
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

        internal static IEnumerable<CodeInstruction> UnitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return Transpiler(instructions, AccessTools.Method(typeof(PatchTeamOneChecks), nameof(UnitPatch)));
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodInfo patchMethod)
        {
            var outinsns = new List<CodeInstruction>();

            var branches = new HashSet<OpCode>();
            
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.Name.Length >= 3 && (field.Name.StartsWith("B") || field.Name.StartsWith("C")))
                {
                    var cmp = field.Name.Substring(1, 2);
                    if (cmp == "eq" || cmp == "ne" || cmp == "gt" || cmp == "lt" || cmp == "le" || cmp == "ge")
                    {
                        branches.Add((OpCode) field.GetValue(null));
                    }
                }
            }

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
                    outinsns.Insert(outinsns.Count - 1, new CodeInstruction(OpCodes.Call, patchMethod));
                } else if (l2.Calls(m_get_Team) && l1.LoadsConstant(1))
                {
                    outinsns.Add(new CodeInstruction(OpCodes.Call, patchMethod));
                }
                outinsns.Add(instruction);
            }

            return outinsns;
        }
    }
}