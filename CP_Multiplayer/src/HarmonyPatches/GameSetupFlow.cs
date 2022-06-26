using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPMod_Multiplayer.LobbyManagement;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CPMod_Multiplayer.HarmonyPatches
{
    [HarmonyPatch(typeof(GameManager), "Awake")]
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
            
    [HarmonyPatch(typeof(GameManager), "Start")]
    static class GameManager_Start
    {
       
        static bool Prefix(GameManager __instance)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                GameSetup.StartGame(__instance);
                return false;
            }

            return true;
        }
    }
    
    static class GameSetup
    {
        private static readonly FieldInfo f_currentBGMName
            = AccessTools.Field(typeof(GameManager), "_currentBGMName");
        private static readonly FieldInfo f_popCharacterList
            = AccessTools.Field(typeof(GameManager), "_popCharacterList");
        private static readonly MethodInfo m_InitializeBase
            = AccessTools.Method(typeof(GameManager), "InitializeBase");
        private static readonly FieldInfo f_MasterData
            = AccessTools.Field(typeof(CharacterData), "MasterData");
        private static readonly FieldInfo f_obj_GameClear
            = AccessTools.Field(typeof(GameManager), "_obj_GameClear");
        private static readonly FieldInfo f_obj_GameOver
            = AccessTools.Field(typeof(GameManager), "_obj_GameOver");
        
        internal static void NextBGM()
        {
            BGMManager.Instance.PlayGameBGM();
            TMP_Text bgmName = f_currentBGMName.GetValue(GameManager.Instance) as TMP_Text;
            if (bgmName != null) bgmName.text = "♪ " + BGMManager.Instance.GetCurrentBGMName() + "(FOIV)";;
        }

        internal static void StartGame(GameManager gameManager)
        {
            try
            {
                NextBGM();
                SetupClubs();

                ForgetOriginalCharacters();
                GameManagerInit();

                if (!MultiplayerManager.MultiplayerFollower)
                {
                    MultiplayerManager.InitMoney(gameManager.teamNum, 4000);
                    // Refresh display as well
                    gameManager.Money = MultiplayerManager.GetMoney(1);
                    PopInitialCharacters();
                }
            }
            catch (Exception e)
            {
                Mod.LogException("[GameSetupFlow] StartGame", e);
            }
        }

        private static void GameManagerInit()
        {
            var popCharacterList = new Dictionary<string, bool>();
            foreach (var key in CharacterManager.Instance.CharacterList.Keys)
            {
                popCharacterList.Add(key, false);
            }
            f_popCharacterList.SetValue(GameManager.Instance, popCharacterList);

            m_InitializeBase.Invoke(GameManager.Instance, Array.Empty<object>());
        }

        private static void PopInitialCharacters()
        {
            if (MultiplayerManager.MultiplayerFollower) return;
            
            // Character ID -> players choosing that character
            Dictionary<string, List<int>> drafts = new Dictionary<string, List<int>>();
            int[] initialCharacterCounts = new int[GameManager.Instance.teamNum + 1];

            foreach (var member in LobbyManager.CurrentLobby.Members)
            {
                foreach (var character in member.MemberState.characters)
                {
                    Mod.logger.Log($"initial draft for {member.MemberState.displayName}: '{character}'");
                    if (string.IsNullOrEmpty(character)) continue;
                    
                    initialCharacterCounts[member.MemberState.teamIndex]++;

                    if (character == "__random__") continue;
                    if (!drafts.ContainsKey(character)) drafts[character] = new List<int>();
                    drafts[character].Add(member.MemberState.teamIndex);
                }
            }
            
            // Pop chosen characters first
            foreach (var chara in drafts.Keys)
            {
                var candidates = drafts[chara];
                var choice = candidates[Random.Range(0, candidates.Count)];
                
                Mod.logger.Log($"Character {chara}: Candidates {candidates.Join(a => a.ToString(), ",")} => {choice}");

                GameManager.Instance.PopCharacter(chara, choice + 1, isInitialize: true);
                initialCharacterCounts[choice]--;
            }
            
            // Add random pops for any missed choices
            List<string> characters = GameManager.Instance.GetNonPopCharacterList();
            for (int i = 0; i < initialCharacterCounts.Length; i++)
            {
                Mod.logger.Log($"Additional pops for {i}: {initialCharacterCounts[i]}");
                while (initialCharacterCounts[i] > 0)
                {
                    int index = Random.Range(0, characters.Count);
                    // Swap-remove
                    string last = characters[characters.Count - 1];
                    string chosen = characters[index];
                    characters[index] = last;
                    characters.RemoveAt(characters.Count - 1);
                    
                    initialCharacterCounts[i]--;
                    var unit = GameManager.Instance.PopCharacter(chosen, i + 1, isInitialize: true);
                    Mod.logger.Log($"Pop unit: {(unit != null ? unit.name : "null")}");
                }
            }
        }

        private static void SetupClubs()
        {
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

        private static void ForgetOriginalCharacters()
        {
            // For now, forget all original characters to avoid issues
            try
            {
                var characters = CharacterData.Instance.GetCharacters();
                var masterData =
                    (Dictionary<String, CharacterData.CharacterMasterData>)
                    f_MasterData.GetValue(CharacterData.Instance);
                var originals = characters.Keys.Where(
                    c => !masterData.ContainsKey(c) || CharacterData.Instance.IsOriginalCharacter(c)
                ).ToArray();
                foreach (var original in originals)
                {
                    characters.Remove(original);
                    CharacterManager.Instance.CharacterList.Remove(original);
                }
            }
            catch (Exception e)
            {
                Mod.logger.LogException("[GameSetupFlow] Failed to remove original characters", e);
            }
        }

        public static void DeclareWinner(int winner)
        {
            Time.timeScale = 0;

            var won = winner == MultiplayerManager.MyTeam;
            var objField = won ? f_obj_GameClear : f_obj_GameOver;
            var objectList = (List<GameObject>) objField.GetValue(GameManager.Instance);
            
            SoundEffectManager.Instance.PlayOneShot(won ? "clear" : "game_over");
            
            foreach (var obj in objectList)
            {
                obj.SetActive(true);
            }
        }
    }
    
    [HarmonyPatch(typeof(ResultData), nameof(ResultData.Initialize))]
    static class ResultData_Initialize
    {
        public static void Prefix()
        {
            // This is a convenient place to override the club list
            
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