using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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
        private static readonly FieldInfo f_popPoint
            = AccessTools.Field(typeof(GameManager), "_popPoint");
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

            if (0 == (int) f_hour.GetValue(GameManager.Instance))
            {
                CheckTimePop(GameManager.Instance);
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
        
        
        private static void CheckTimePop(GameManager gameManager)
        {
            // if (!MainSceneManager.Instance.isEnableAutoRecruit) return;
            
            var charaList = gameManager.GetNonPopCharacterList(false);
            Room[] bestRooms = new Room[gameManager.teamNum + 1];

            foreach (Room r in RoomManager.Instance.Rooms)
            {
                var team = r.DominationTeam;
                if (team < 1) continue;

                if (bestRooms[team] == null || bestRooms[team].TrainingPower < r.TrainingPower)
                {
                    bestRooms[team] = r;
                }
            }

            for (int i = 1; i < bestRooms.Length; i++)
            {
                if (bestRooms[i] != null && charaList.Count > 0)
                {
                    int choice = UnityEngine.Random.Range(0, charaList.Count);
                    var last = charaList[charaList.Count - 1];
                    var chara = charaList[choice];

                    var unit = gameManager.PopCharacter(chara, i);
                    unit.InRoom = null;
                    unit.Way = (WayPoint)f_popPoint.GetValue(gameManager);
                    unit.Move(bestRooms[i]);
                    
                    // Hopefully this gets factored out eventually
                    string str = ConstString.ClubName[gameManager.clubList[i]];
                    Color color1 = ConstColors.Instance.TEAM_COLOR_FRAME[i];
                    ColorUtility.ToHtmlStringRGB(color1);
                    string club = str;
                    Color color2 = color1;
                    string name = unit.name;
                    string nameDisplay = unit.Name_Display;
                    string text = unit.Name_Display + "が " + str + " に入部しました";
                    LogManager.Instance.CreateMessage(club, color2, name, nameDisplay, text);
                    SoundEffectManager.Instance.PlayOneShot("log_join");
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

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.CheckTimePop))]
    static class GameManager_CheckTimePop
    {
        static bool Prefix(GameManager __instance)
        {
            if (MultiplayerManager.MultiplayerSession)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PopCharacter))]
    static class GameManager_PopCharacter
    {
        static void Prefix(string name, int team, Unit popTargetUnit, bool isInitialize)
        {
            MultiplayerManager.SelectCharacterData(name, team);
        }
    }
}
