using System;
using CPMod_Multiplayer.Serialization;
using Steamworks;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    enum SteamLobbyState
    {
        CREATING,
        SEARCHING,
        JOINING,
        CONNECT_TO_HOST,
        ESTABLISHED
    }

    internal class SteamLobbyMemberState
    {
        internal CSteamID SteamID;
        internal long Disambiguator;
        internal int? P2PPort;
        
        internal string SteamName;
        internal Texture2D Avatar;
        
        internal bool IsDataReady;
        internal bool IsPlayerReady;
        internal Socket MemberConnection; // valid only for the host

        public LobbyMemberState MemberState;

        public delegate void OnChangeDelegate();
        public event OnChangeDelegate OnChange;

        internal void SetMemberState(MemberState state)
        {
            OnChange?.Invoke();
        }
    }
    
    /**
     * Lobby protocol:
     *
     * All clients broadcast out their state via metadata keys
     * Clients establish connections to the host. The host posts the port to connect to as a metadata key.
     * 
     * On game start - the host sends out a prepare message to all clients via the socket interface. This starts the
     * UI transition; it then follows up with the initial game state, which will be ingested once the GameScene loads.
     * Note that we must prevent the initial game scene from loading any units to avoid display glitching if the initial
     * data is delayed (TODO)
     */
    public class SteamLobbyHandler : MonoBehaviour
    {
        private const int MAX_MEMBERS = 7;
        private const string K_GAMEID = "karapari_wars:gameid";
        private const string K_GAMETAG = "karapari_wars:gamename";
        private const string V_GAMETAG = "multiplayer_mod_v0";
        
        private long? lobbyTag;
        private bool isHost;
        private bool isConnected;

        private SteamLobbyState _state;
        private CSteamID _lobbySteamId;

        internal delegate void ReportError(string msg);
        internal delegate void ReportReady();

        internal ReportError OnError = (msg) => { };
        internal ReportReady OnReady = () => { };

        private CallResult<LobbyCreated_t> callLobbyCreate;
        private CallResult<LobbyMatchList_t> callLobbyList;
        private CallResult<LobbyEnter_t> callLobbyEnter;
        private Callback<LobbyChatUpdate_t> callbackLobbyChatUpdate;
        private Callback<LobbyDataUpdate_t> callbackLobbyDataUpdate;

        void OnEnable()
        {
            if (lobbyTag == null)
            {
                _state = SteamLobbyState.CREATING;
                var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MAX_MEMBERS);
                callLobbyCreate = CallResult<LobbyCreated_t>.Create();
                callLobbyCreate.Set(call, OnLobbyCreate);
            }
            else
            {
                _state = SteamLobbyState.SEARCHING;
                SteamMatchmaking.AddRequestLobbyListStringFilter(K_GAMEID, lobbyTag.Value.ToString(), 
                    ELobbyComparison.k_ELobbyComparisonEqual);
                SteamMatchmaking.AddRequestLobbyListStringFilter(K_GAMETAG, V_GAMETAG, 
                    ELobbyComparison.k_ELobbyComparisonEqual);
                var call = SteamMatchmaking.RequestLobbyList();
                callLobbyList = CallResult<LobbyMatchList_t>.Create();
                callLobbyList.Set(call, OnLobbyList);
            }
        }

        private void OnLobbyList(LobbyMatchList_t list, bool bioError)
        {
            callLobbyList.Dispose();
            
            if (list.m_nLobbiesMatching < 1)
            {
                OnError("部屋が見つからなかった…");
                return;
            } else if (list.m_nLobbiesMatching > 1)
            {
                OnError("部屋番号が重複しています");
                return;
            }
            
            var lobby = SteamMatchmaking.GetLobbyByIndex(0);
            _state = SteamLobbyState.JOINING;
            callLobbyEnter = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);
            SteamMatchmaking.JoinLobby(lobby);
        }

        private void OnLobbyEnter(LobbyEnter_t enterResult, bool bioError)
        {
            callLobbyEnter.Dispose();
            if (enterResult.m_bLocked)
            {
                OnError("部屋がロックされています");
                return;
            }
            if (enterResult.m_EChatRoomEnterResponse != (uint) EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                OnError("部屋に入れられませんでした");
                Mod.logger.Log("Lobby enter error: " + (EChatRoomEnterResponse)enterResult.m_EChatRoomEnterResponse);
                return;
            }
            
            AfterJoinLobby();
        }

        private void AfterJoinLobby()
        {
            callbackLobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            callbackLobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);

            OnReady();
            
            LoadAllLobbyData();
        }

        private void LoadAllLobbyData()
        {
            
        }
        
        private void OnLobbyDataUpdate(LobbyDataUpdate_t param)
        {
            throw new NotImplementedException();
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t param)
        {
            throw new NotImplementedException();
        }

        void OnDestroy()
        {
            if (_lobbySteamId.IsValid())
            {
                SteamMatchmaking.LeaveLobby(_lobbySteamId);
            }
            callLobbyCreate.Dispose();
            callLobbyList.Dispose();
        }

        private void OnLobbyCreate(LobbyCreated_t param, bool biofailure)
        {
            if (param.m_eResult != EResult.k_EResultOK)
            {
                OnError(param.m_eResult.ToString());
                return;
            }

            if (biofailure)
            {
                OnError("IO failure");
                return;
            }

            _lobbySteamId = new CSteamID(param.m_ulSteamIDLobby);
            _state = SteamLobbyState.ESTABLISHED;
            // TODO - open listen socket
            AfterJoinLobby();
        }
    }
}