using System;
using System.Collections.Generic;
using CPMod_Multiplayer.HarmonyPatches;
using CPMod_Multiplayer.Serialization;
using MessagePack;
using Steamworks;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    using Random = UnityEngine.Random;

    enum SteamLobbyState
    {
        Init,
        Joining,
        InLobby,
        GamePreparation, // establishing connections
        ConnEstablished, // follower: Connection established, waiting for start
        GameInProgress,
        Error,
    }

    class SteamLobby : Lobby
    {
        private const int START_TIMEOUT = 20;
        
        private bool _isHost;
        public override bool IsHost => _isHost;
        public override LobbyState State { get; protected set; }

        private int _teamIndex;
        public override int MyTeamIndex => _teamIndex;

        public string Token => _matchmaker.Token;
        
        private Lobby _innerLobby;
        private Matchmaker _matchmaker;
        private SteamLobbyState _state;

        private Dictionary<CSteamID, LobbyMember> _idToMember = new Dictionary<CSteamID, LobbyMember>();
        
        private Callback<LobbyChatMsg_t> cb_LobbyChatMessage;
        private Callback<LobbyChatUpdate_t> cb_LobbyChatUpdate;
        private Callback<LobbyDataUpdate_t> cb_LobbyDataUpdate;
        private Callback<SteamNetConnectionStatusChangedCallback_t> cb_ConnStatusChanged;
        
        private float _gameStartTimeout;

        private HSteamListenSocket _listenSocket;
        private Socket _hostSocket;
        
        public override Socket HostSocket => _hostSocket;

        protected void Awake()
        {
            _state = SteamLobbyState.Init;
            _matchmaker = new Matchmaker();
            _innerLobby = null;
            LobbyManager.CurrentLobby = this; // there can be only one

            _matchmaker.OnError += (s) =>
            {
                _state = SteamLobbyState.Error;
                RaiseError(s);
            };

            _matchmaker.OnJoined += OnJoined;
            
            cb_LobbyChatMessage = Callback<LobbyChatMsg_t>.Create(OnLobbyChatMessage);
            cb_LobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            cb_LobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            cb_ConnStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            _listenSocket =
                SteamNetworkingSockets.CreateListenSocketP2P(LOBBY_PORT, 0, Array.Empty<SteamNetworkingConfigValue_t>());
        }

        private void OnDestroy()
        {
            _matchmaker?.Dispose();
            UnityEngine.Object.Destroy(_innerLobby);
            cb_LobbyChatMessage?.Dispose();
            cb_LobbyChatUpdate?.Dispose();
            cb_LobbyDataUpdate?.Dispose();
            cb_ConnStatusChanged?.Dispose();
            _hostSocket?.Dispose();

            if (_listenSocket != default)
            {
                SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            }

            _state = SteamLobbyState.Error;
            
        }

        public static SteamLobby CreateInstance()
        {
            var go = new GameObject("SteamLobby");
            DontDestroyOnLoad(go);
            try
            {
                var lobby = go.AddComponent<SteamLobby>();
                
                return lobby;
            }
            catch (Exception e)
            {
                Mod.LogException("CreateLobby", e);
                Destroy(go);
                return null;
            }
        }
        
        public void CreateLobby()
        {
            if (_state != SteamLobbyState.Init) throw new Exception("Not in Init state");
            _matchmaker.CreateLobby();
            _state = SteamLobbyState.Joining;
            State = LobbyState.JOINING;
            RaiseStateChange();
        }

        public void JoinLobby(string token)
        {
            if (_state != SteamLobbyState.Init) throw new Exception("Not in Init state");
            _matchmaker.JoinLobby(token);
            _state = SteamLobbyState.Joining;
            State = LobbyState.JOINING;
            RaiseStateChange();
        }

        private void OnJoined()
        {
            _state = SteamLobbyState.InLobby;
            LobbyAddress = _matchmaker.Token;
            // Sync all player data

            SyncAllPlayers();

            State = LobbyState.READY;
            RaiseStateChange();
        }

        private void SyncAllPlayers()
        {
            int numPlayers = SteamMatchmaking.GetNumLobbyMembers(_matchmaker.LobbyId);
            for (int i = 0; i < numPlayers; i++)
            {
                SyncPlayer(SteamMatchmaking.GetLobbyMemberByIndex(_matchmaker.LobbyId, i));
            }
        }

        private void SyncPlayer(CSteamID steamId)
        {
            LobbyMember member;
            var isSelf = steamId == SteamUser.GetSteamID();
            if (!_idToMember.TryGetValue(steamId, out member))
            {
                if (!Members.TryJoin(null, out member, isSelf))
                {
                    RaiseError("メンバーが多すぎ！！！");
                    _state = SteamLobbyState.Error;
                    return;
                }

                _idToMember[steamId] = member;
                
                SteamInfoLookup.LookupSteamInfo(steamId, (_id, name) =>
                {
                    member.MemberState.displayName = name;
                    member.RaiseOnChange();
                });
            }
            
            // TODO - sync club members
        }

        private void OnLobbyDataUpdate(LobbyDataUpdate_t param)
        {
            if (param.m_ulSteamIDLobby != param.m_ulSteamIDMember)
            {
                SyncPlayer(new CSteamID(param.m_ulSteamIDMember));
            }
            else
            {
                _isHost = SteamUser.GetSteamID() == SteamMatchmaking.GetLobbyOwner(_matchmaker.LobbyId);
            }
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t param)
        {
            if (0 != (param.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered))
            {
                SyncPlayer(new CSteamID(param.m_ulSteamIDUserChanged));
            } else if (0 != (param.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft))
            {
                LobbyMember member;
                if (_idToMember.TryGetValue(new CSteamID(param.m_ulSteamIDUserChanged), out member))
                {
                    member.Remove();
                }
            }
        }
        
#region Game initiation and connection
        public override void StartGame()
        {
            if (_state != SteamLobbyState.InLobby) throw new Exception("Not in InLobby state");
            if (!IsHost) return;

            _state = SteamLobbyState.GamePreparation;
            SteamMatchmaking.SendLobbyChatMsg(_matchmaker.LobbyId, new byte[] {0}, 1);
            SteamMatchmaking.SetLobbyJoinable(_matchmaker.LobbyId, false);
            _gameStartTimeout = Time.unscaledTime + START_TIMEOUT;
        }
        
        private void OnLobbyChatMessage(LobbyChatMsg_t param)
        {
            var host = SteamMatchmaking.GetLobbyOwner(_matchmaker.LobbyId);

            if (param.m_ulSteamIDUser != host.m_SteamID ||
                param.m_ulSteamIDLobby != _matchmaker.LobbyId.m_SteamID) return;
            if (IsHost) return;

            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(host);
            
            // A chat message indicates we're entering the game preparation phase
            _hostSocket = new Socket(SteamNetworkingSockets.ConnectP2P(
                ref identity, LOBBY_PORT, 0, Array.Empty<SteamNetworkingConfigValue_t>()
            ));
            _state = SteamLobbyState.GamePreparation;
            _gameStartTimeout = Time.unscaledTime + START_TIMEOUT;
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            Mod.logger.Log($"[SteamLobby] Connection status changed: {param.m_hConn} => {param.m_info.m_eState}");
            if (!IsHost)
            {
                if (param.m_hConn != _hostSocket?.Handle)
                {
                    Mod.logger.Warning("[RemoteLobby] Unexpected status change callback for unknown socket handle");
                    return;
                }
                
                switch (param.m_info.m_eState)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                        if (_state == SteamLobbyState.GamePreparation)
                        {
                            _state = SteamLobbyState.ConnEstablished;
                        }
                        else
                        {
                            HostSocket?.Dispose();
                            _hostSocket = null;
                        }
                        break;
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FinWait:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    {
                        _hostSocket.Dispose();
                        RaiseError("Connection error: " + param.m_info.m_szEndDebug);
                        break;
                    }
                }
            }
            else
            {
                // If host...
                switch (param.m_info.m_eState)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    {
                        if (_state == SteamLobbyState.GamePreparation && 
                            _idToMember.TryGetValue(param.m_info.m_identityRemote.GetSteamID(), out var member))
                        {
                            SteamNetworkingSockets.AcceptConnection(param.m_hConn);
                            member.Socket = new Socket(param.m_hConn);
                            CheckReadyToStart();
                        }
                        else
                        {
                            Mod.logger.Warning("[SteamLobby] Rejecting bogon connection from " +
                                               param.m_info.m_identityRemote.GetSteamID() + " in state " + _state);

                            foreach (var k in _idToMember.Keys)
                            {
                                Mod.logger.Log($"[SteamLobby] Known member: {k}");
                            }
                            
                            SteamNetworkingSockets.CloseConnection(param.m_hConn, 0, "", false);
                        }

                        break;
                    }
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FinWait:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    {
                        foreach (var member in _idToMember.Values)
                        {
                            if (member.Socket?.Handle == param.m_hConn)
                            {
                                if (_state == SteamLobbyState.GameInProgress || _state == SteamLobbyState.GamePreparation)
                                {
                                    // TODO - reconnect logic?
                                    RaiseError("プレイヤーが切だんされました…");
                                }
                                
                                member.Socket = null;
                                break;
                            }
                        }

                        break;
                    }
                }
            }
        }

        private void CheckReadyToStart()
        {
            foreach (var member in Members)
            {
                if (Members.Self == member) continue;
                if (member.Socket == null) return;
            }

            try
            {
                _state = SteamLobbyState.GameInProgress;
                Members.Defragment();

                MultiplayerManager.instance.SetupClubs(Members);
                var clubList = MultiplayerManager.instance.clubList.ToArray();

                foreach (var member in Members)
                {
                    if (Members.Self == member) continue;

                    member.Socket.Send(new LobbyStartGame()
                    {
                        teamIndex = member.MemberState.teamIndex,
                        clubList = clubList,
                    }.ToNetPacket());
                    member.Socket.Flush();

                    Mod.logger.Log($"[SteamLobby] Sent start game packet with team index " +
                                   $"{member.MemberState.teamIndex} to {member.MemberState.displayName}");
                }

                _teamIndex = _idToMember[SteamUser.GetSteamID()].MemberState.teamIndex;

                Mod.logger.Log($"[SteamLobby] Starting game with team index {_teamIndex}");

                MainSceneManager.Instance.StartGame();
            }
            catch (Exception e)
            {
                Mod.LogException("[SteamLobby] Error starting game", e);
            }
        }

        protected override void Update()
        {
            base.Update();

            if (_state == SteamLobbyState.GamePreparation || _state == SteamLobbyState.ConnEstablished)
            {
                if (Time.unscaledTime > _gameStartTimeout)
                {
                    RaiseError("なんか上手くいかんかったっぽい");
                    return;
                }
            }

            if (_state == SteamLobbyState.ConnEstablished)
            {
                if (_hostSocket.TryReceive(out var pkt))
                {
                    try
                    {
                        var data = MessagePackSerializer.Deserialize<NetPacket>(pkt);

                        if (data is LobbyPacket lobbyPkt && lobbyPkt.lobbyPacket is LobbyStartGame startPkt)
                        {
                            Mod.logger.Log($"[SteamLobby] Received start game packet with team index " +
                                           $"{startPkt.teamIndex}");
                            _teamIndex = startPkt.teamIndex;
                            MultiplayerManager.instance.clubList = new List<string>(startPkt.clubList);
                            MainSceneManager.Instance.StartGame();
                            _state = SteamLobbyState.GameInProgress;
                        }
                        else
                        {
                            Mod.logger.Log($"[SteamLobby] Received unknown packet {data}");
                        }
                    }
                    catch (Exception e)
                    {
                        Mod.LogException("[SteamLobby] Error while receiving lobby packet", e);
                        RaiseError("なんか変なメッセージが届いた…");
                    }
                }
            }
        }

        public override void OnGameOver()
        {
            _state = SteamLobbyState.InLobby;

            _hostSocket?.Dispose();
            _hostSocket = null;
        }

        #endregion
    }
    
    enum MatchmakerState
    {
        Init,
        Creating,
        Searching,
        Joining,
        Ready,
        Error
    }
    
    internal class Matchmaker : IDisposable
    {
        private MatchmakerState _state;
        private CSteamID _lobbyId;
        private CSteamID _ownerId;

        public string Token { get; private set; } = null;
        public CSteamID LobbyId => _lobbyId;
        
        private Callback<LobbyCreated_t> cb_LobbyCreated;
        private Callback<LobbyMatchList_t> cb_LobbyMatchList;
        private Callback<LobbyEnter_t> cb_LobbyJoin;
        private Callback<LobbyDataUpdate_t> cb_OnDataUpdate;

        public delegate void DelegateOnError(string msg);

        public delegate void DelegateOnJoined();
        

        public event DelegateOnError OnError;
        public event DelegateOnJoined OnJoined;

        static string GenerateToken()
        {
            return $"{Random.Range(0, 9999):D4}-{Random.Range(0, 9999):D4}";
        }

        static List<KeyValuePair<string, string>> GetLobbyFilters(string token)
        {
            var list = new List<KeyValuePair<string, string>>();
            
            list.Add(new KeyValuePair<string, string>("cpwars:netmod:proto", "0"));
            list.Add(new KeyValuePair<string, string>("cpwars:netmod:token", token));

            return list;
        }

        internal Matchmaker()
        {
            _state = MatchmakerState.Init;
        }

        public void CreateLobby()
        {
            if (_state != MatchmakerState.Init) throw new Exception("Not in initializing state");

            cb_LobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _state = MatchmakerState.Creating;
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, MemberSet.MAX_PLAYERS);
        }

        private void OnLobbyCreated(LobbyCreated_t param)
        {
            if (param.m_eResult != EResult.k_EResultOK)
            {
                _state = MatchmakerState.Error;
                OnError?.Invoke("ロビー作成にしっぱいしました・・・");
                return;
            }

            _lobbyId = new CSteamID(param.m_ulSteamIDLobby);
            
            cb_LobbyCreated?.Dispose();
            cb_LobbyCreated = null;

            Token = GenerateToken();
            Mod.logger.Log($"[Matchmaker] Created lobby: {_lobbyId}");
            foreach (var kv in GetLobbyFilters(Token))
            {
                Mod.logger.Log($"[Matchmaker] Setting lobby data {kv.Key}={kv.Value}");
                SteamMatchmaking.SetLobbyData(_lobbyId, kv.Key, kv.Value);
            }

            AfterJoin();
        }

        private void AfterJoin()
        {
            _state = MatchmakerState.Ready;
            
            OnJoined?.Invoke();
        }

        public void JoinLobby(string token)
        {
            if (_state != MatchmakerState.Init) throw new Exception("Not in initializing state");

            Token = token;
            foreach (var kv in GetLobbyFilters(Token))
            {
                Mod.logger.Log($"[Matchmaker] Setting lobby filter {kv.Key}={kv.Value}");
                SteamMatchmaking.AddRequestLobbyListStringFilter(kv.Key, kv.Value, ELobbyComparison.k_ELobbyComparisonEqual);
            }

            cb_LobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
            _state = MatchmakerState.Searching;
            var rv = SteamMatchmaking.RequestLobbyList();
            Mod.logger.Log($"[Matchmaker] RequestLobbyList: {rv}");
        }

        private void OnLobbyMatchList(LobbyMatchList_t param)
        {
            Mod.logger.Log($"[Matchmaker] Lobby search results: {param.m_nLobbiesMatching}");
            
            if (param.m_nLobbiesMatching == 0)
            {
                OnError?.Invoke("ロビーが見つかりませんでした…");
                _state = MatchmakerState.Error;
                return;
            }
            
            _lobbyId = SteamMatchmaking.GetLobbyByIndex(0);
            _state = MatchmakerState.Joining;
            cb_LobbyMatchList?.Dispose();
            cb_LobbyMatchList = null;

            cb_LobbyJoin = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            SteamMatchmaking.JoinLobby(_lobbyId);
        }

        private void OnLobbyEnter(LobbyEnter_t param)
        {
            if (param.m_ulSteamIDLobby != _lobbyId.m_SteamID)
            {
                OnError?.Invoke("なんか変なところに入っちゃった…");
                _state = MatchmakerState.Error;
                return;
            }

            if (param.m_EChatRoomEnterResponse != (UInt32)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                OnError?.Invoke("ロビーに入れなかった…");
                _state = MatchmakerState.Error;
                return;
            }
            
            AfterJoin();
        }

        public void Dispose()
        {
            _state = MatchmakerState.Error;
            cb_LobbyCreated?.Dispose();
            cb_LobbyMatchList?.Dispose();
            cb_LobbyJoin?.Dispose();
            cb_OnDataUpdate?.Dispose();
        }
    }
}