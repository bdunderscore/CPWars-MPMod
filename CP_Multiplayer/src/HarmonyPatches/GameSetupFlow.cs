using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPMod_Multiplayer.LobbyManagement;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CPMod_Multiplayer.HarmonyPatches
{
        
    [HarmonyPatch(typeof(GameManager), "Start")]
    static class GameManager_Start
    {
        internal static bool InStart;
        
        static void Prefix()
        {
            InStart = true;
        }

        static void Postfix()
        {
            InStart = false;
        }
    }

    [HarmonyPatch(typeof(GameManager), "Start")]
    static class GameManager_Awake
    {
        private static FieldInfo f_obj_GameMode = AccessTools.Field(typeof(GameManager), "_obj_GameMode");
        
        static void Prefix()
        {
            if (LobbyManager.CurrentLobby != null)
            {
                MainSceneManager.Instance.SetGameMode(1); // easy mode - no enemy bonuses
            }
        }

        static void Postfix(GameManager __instance)
        {
            var obj_GameMode = f_obj_GameMode.GetValue(__instance) as GameObject;
            if (obj_GameMode != null) obj_GameMode.GetComponent<TextMeshProUGUI>().text = "ネット対戦";
        }
    }
    
        
    [HarmonyPatch(typeof(ResultData), nameof(ResultData.Initialize))]
    static class ResultData_Initialize
    {
        public static void Prefix()
        {
            // This is a convenient place to override the club list
            if (MultiplayerManager.MultiplayerSession && !MultiplayerManager.MultiplayerFollower)
            {
                var clubList = ConstString.ClubName.Keys.Where(k => k != "---")
                    .ToList();

                var selectedClubs = new List<string>();
                selectedClubs.Add("---");
                foreach (var member in LobbyManager.CurrentLobby.Members)
                {
                    if (clubList.Contains(member.MemberState.selectedClub))
                    {
                        selectedClubs.Add(member.MemberState.selectedClub);
                        clubList.Remove(member.MemberState.selectedClub);
                    }
                    else
                    {
                        var randomClub = clubList[Random.Range(0, clubList.Count)];
                        selectedClubs.Add(randomClub);
                        clubList.Remove(randomClub);
                    }
                }

                Mod.logger.Log("[GameSetupFlow] Set clublist=" + selectedClubs.Join(a=>a,","));
                GameManager.Instance.clubList = selectedClubs;
                GameManager.Instance.teamNum = selectedClubs.Count - 1;
            }
        }
    }

    [HarmonyPatch(typeof(MainSceneManager), nameof(MainSceneManager.StartResult))]
    static class MainSceneManager_StartResult
    {
        static bool Prefix(MainSceneManager __instance)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                // MultiplayerManager will return us to the lobby screen
                // TODO: Make results work with multiplayer
                __instance.StartTitle();
                return false;
            }

            return true;
        }
    }
}