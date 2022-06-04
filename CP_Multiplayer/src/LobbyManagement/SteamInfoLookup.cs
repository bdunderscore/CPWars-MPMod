using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    public static class SteamInfoLookup
    {
        public delegate void SteamInfoCallback(CSteamID steamID, string name);
        
        static Dictionary<CSteamID, List<SteamInfoCallback>> _pending = new Dictionary<CSteamID, List<SteamInfoCallback>>();
        static Callback<PersonaStateChange_t> _callback;

        private static void Initialize()
        {
            if (_callback == null)
            {
                _callback = Callback<PersonaStateChange_t>.Create(OnPersonaStateChanged);
            }
        }

        public static void LookupSteamInfo(CSteamID steamID, SteamInfoCallback callback)
        {
            Initialize();
        
            bool ready = false;
            if (!_pending.ContainsKey(steamID))
            {
                _pending.Add(steamID, new List<SteamInfoCallback>());
                ready = !SteamFriends.RequestUserInformation(steamID, true);
                Mod.logger.Log("Requested user info for " + steamID + ": " + ready);
            }
            
            _pending[steamID].Add(callback);

            if (ready)
            {
                OnInfoReady(steamID);
            }
        }

        private static void OnPersonaStateChanged(PersonaStateChange_t param)
        {
            Mod.logger.Log("OnPersonaStateChanged: " + param.m_ulSteamID);
            OnInfoReady(new CSteamID(param.m_ulSteamID));
        }

        static void OnInfoReady(CSteamID steamID)
        {
            string name = SteamFriends.GetFriendPersonaName(steamID);
            Mod.logger.Log("Got user info for " + steamID + ": " + name);
            _pending[steamID].ForEach(cb => cb(steamID, name));
            _pending.Remove(steamID);
        }
    }
}