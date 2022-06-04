using System;
using System.Collections.Generic;
using System.Linq;
using System.Transactions;
using CPMod_Multiplayer.Serialization;
using MessagePack;
using Steamworks;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    public enum MemberState
    {
        INITIAL_SYNC,
        NOT_READY,
        READY,
        INGAME,
        ERROR
    }
    
    public class LobbyMember
    {
        internal LobbyMember(Socket socket, int index)
        {
            this.Socket = socket;

            this._memberState = new LobbyMemberState()
            {
                characters = new[] {"__random__", "__random__", "__random__", "__random__", "__random__"},
                ready = false,
                displayName = "???",
                selectedClub = "random",
                teamIndex = index
            };
        }
        
        public Socket Socket { get; private set; }

        public string DisplayName => MemberState.displayName;
        public bool IsHost { get; internal set; } = false;
        public bool Ready { get; internal set; } = false;
        public bool Disconnected { get; private set; } = false;

        private LobbyMemberState _memberState;
        public LobbyMemberState MemberState
        {
            get
            {
                return _memberState;
            }
            internal set
            {
                _memberState = value;
                OnChange?.Invoke();
            }
        }

        public delegate void OnChangeDelegate();

        public event OnChangeDelegate OnChange;

        public void Close()
        {
            Disconnected = true;
            Ready = false;
            Socket?.Dispose();
            OnChange?.Invoke();
        }
    }
    
    public enum LobbyState
    {
        JOINING,
        NOT_READY,
        READY,
        ERROR
    }

    public abstract class Lobby : MonoBehaviour
    {
        protected const int LOBBY_PORT = 0;

        public static Lobby CurrentLobby;
        public abstract LobbyState State { get; protected set; }
        public abstract LobbyMember Self { get; }

        public String LobbyAddress { get; protected set; }

        public delegate void DelegateOnError(string msg);
        public delegate void DelegateOnStateChange();
        public delegate void DelegateOnMemberStateChange(LobbyMember[] members);
        
        public event DelegateOnError OnError;
        public event DelegateOnStateChange OnStateChange;
        public event DelegateOnMemberStateChange OnMemberStateChange;

        protected delegate void DeferredEvent();

        protected Queue<DeferredEvent> _eventQueue = new Queue<DeferredEvent>();

        public abstract LobbyMember[] Members { get; }

        protected void Awake()
        {
            CurrentLobby = this;
        }
        
        protected void RaiseError(string msg)
        {
            Mod.logger.Error("[Lobby:RaiseError]" + msg);
            OnError?.Invoke(msg);
            Destroy(gameObject);
        }

        protected void RaiseStateChange()
        {
            Mod.logger.Log("[Lobby:RaiseStateChange]");
            OnStateChange?.Invoke();
        }
        
        protected void RaiseMemberStateChange(LobbyMember[] members)
        {
            Mod.logger.Log("[Lobby:RaiseMemberStateChange]");
            OnMemberStateChange?.Invoke(members);
        }

        protected virtual void Update()
        {
            while (_eventQueue.Count > 0) _eventQueue.Dequeue()();
        }
    }
    
    public class HostedLobby : Lobby
    {
        public override LobbyState State { get; protected set; } = LobbyState.JOINING;
        public override LobbyMember Self => _lobbyMembers[default];
        public bool LobbyOpen { get; set; } = false;
        public int MaxConnections { get; set; } = 7;

        private SteamNetworkingFakeIPResult_t _ipResult;
        private HSteamListenSocket _listenSocket;

        private CallResult<SteamNetworkingFakeIPResult_t> _fakeIpResult;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;

        private Dictionary<HSteamNetConnection, LobbyMember> _lobbyMembers;
        
        public override LobbyMember[] Members
            => _lobbyMembers.Values.OrderBy(n => n.MemberState.teamIndex).ToArray();

        HostedLobby()
        {
            Mod.logger.Log("[HostedLobby] ctor");
            var steamId = SteamUser.GetSteamID();
            _lobbyMembers = new Dictionary<HSteamNetConnection, LobbyMember>();
            _lobbyMembers.Add(default, new LobbyMember(null, 0)
            {
                Ready = false,
                IsHost = true,
                MemberState = new LobbyMemberState()
                {
                    displayName = SteamFriends.GetPersonaName()
                }
            });
            Mod.logger.Log($"[HostedLobby] ctor self={Self}");
        }
        
        void BroadcastLobbyState()
        {
            try
            {
                var members = Members;
                var pkt = MessagePackSerializer.Serialize<NetPacket>(new LobbyMemberSync()
                {
                    members = members.Select(m => m.MemberState).ToArray()
                });

                foreach (var member in members)
                {
                    member.Socket?.Send(pkt);
                }

                RaiseMemberStateChange(members);
            }
            catch (Exception e)
            {
                Mod.logger.LogException("BroadcastLobbyState", e);
            }
        }

        int findOpenIndex()
        {
            bool[] filled = new bool[MaxConnections];
            foreach (var member in _lobbyMembers.Values)
            {
                filled[member.MemberState.teamIndex] = true;
            }

            for (int i = 1; i < filled.Length; i++)
            {
                if (!filled[i]) return i;
            }
            
            return -1;
        }

        protected override void Update()
        {
            try
            {
                foreach (var member in _lobbyMembers.Values)
                {
                    if (member.Socket == null) continue;
                    
                    member.Socket.Flush();
                    if (member.Socket.ErrorState)
                    {
                        _eventQueue.Enqueue(() =>
                        {
                            member.Close();
                            Mod.logger.Log("[HostedLobby:Update] member.Close()");
                            _lobbyMembers.Remove(member.Socket.Handle);
                        });
                    }
                }

                base.Update();
            }
            catch (Exception e)
            {
                Mod.LogException("[HostedLobby] Update", e);
            }
        }
        
        private void OnDestroy()
        {
            Mod.logger.Log("[Lobby] Destroyed");
            if (_fakeIpResult != null) _fakeIpResult.Dispose();
            if (_connectionStatusChangedCallback != null) _connectionStatusChangedCallback.Dispose();
            if (_listenSocket != default) SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            foreach (var member in _lobbyMembers.Values)
            {
                member.Close();
            }

            State = LobbyState.ERROR;
            RaiseStateChange();
        }

        public static HostedLobby CreateLobby()
        {
            var go = new GameObject("LobbyHandler");
            DontDestroyOnLoad(go);
            try
            {
                var handler = go.AddComponent<HostedLobby>();

                handler.LobbyOpen = true;
                handler.OpenSocket();
                return handler;
            }
            catch (Exception e)
            {
                Mod.LogException("CreateLobby", e);
                Destroy(go);
                return null;
            }
        }

        void OpenSocket()
        {
            try
            {
                _connectionStatusChangedCallback =
                    Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSocketStatusChanged);

                if (!SteamNetworkingSockets.GetIdentity(out var identity))
                {
                    throw new Exception("Failed to get steam identity");
                }

                _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(LOBBY_PORT, 0,
                    Array.Empty<SteamNetworkingConfigValue_t>());

                Mod.logger.Log($"Listen socket: {_listenSocket}");
                if (SteamNetworkingSockets.GetListenSocketAddress(_listenSocket, out var addr))
                {
                    addr.ToString(out var addrStr, true);
                    Mod.logger.Log($"Listen address: {addrStr}");
                }
                else
                {
                    Mod.logger.Log("Cannot convert listen address");
                }

                identity.ToString(out var identityStr);
                LobbyAddress = identityStr;

                _eventQueue.Enqueue(() =>
                {
                    State = LobbyState.NOT_READY;
                    RaiseStateChange();
                });
            }
            catch (Exception e)
            {
                Mod.LogException("[HostedLobby] OpenSocket", e);
            }
        }

        private void OnSocketStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            Mod.logger.Log("[Lobby:OnSocketStatusChanged] " + data.m_hConn + ":" + data.m_info.m_eState);
            switch (data.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                {
                    int index = findOpenIndex();
                    if (!LobbyOpen)
                    {
                        SteamNetworkingSockets.CloseConnection(data.m_hConn, 0, "Lobby is closed", false);
                    }
                    else if (index < 0)
                    {
                        SteamNetworkingSockets.CloseConnection(data.m_hConn, 0, "No available indexes", false);
                    }
                    else
                    {
                        Mod.logger.Log(
                            $"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Accepted connection");
                        try
                        {
                            SteamNetworkingSockets.AcceptConnection(data.m_hConn);
                            var socket = new Socket(data.m_hConn);
                            socket.Send(MessagePackSerializer.Serialize<NetPacket>(new LobbyHello()
                            {
                                yourIndex = index
                            }));
                            var member = new LobbyMember(socket, index);
                            var steamId = data.m_info.m_identityRemote.GetSteamID();
                            
                            SteamInfoLookup.LookupSteamInfo(steamId, (_id, name) =>
                            {
                                member.MemberState.displayName = name;
                                BroadcastLobbyState();
                            });
                            
                            _lobbyMembers.Add(data.m_hConn, member);
                            BroadcastLobbyState();
                        }
                        catch (Exception e)
                        {
                            Mod.logger.LogException("Incoming connection setup", e);
                        }
                    }

                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                {
                    Mod.logger.Log($"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Connected");

                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Dead:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                {
                    Mod.logger.Warning(
                        $"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Connection error: {data.m_info.m_eState} {data.m_info.m_szConnectionDescription} {data.m_info.m_szEndDebug}");
                    if (_lobbyMembers.TryGetValue(data.m_hConn, out var member))
                    {
                        member.Close();
                        Mod.logger.Log("[Lobby:OnSocketStatusChanged] member.Close()");
                        _lobbyMembers.Remove(data.m_hConn);
                        BroadcastLobbyState();
                    }

                    SteamNetworkingSockets.CloseConnection(data.m_hConn, 0, "", false);
                    break;
                }
            }
        }

    }

    public class RemoteLobby : Lobby
    {
        public override LobbyState State { get; protected set; } = LobbyState.JOINING;
        private LobbyMember _tmp = new LobbyMember(null, 0);
        public override LobbyMember Self => _tmp;
        public override LobbyMember[] Members => Array.Empty<LobbyMember>();

        private Socket _socket;
        
        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;

        public static Lobby ConnectLobby(String addr)
        {
            var go = new GameObject("LobbyHandler");
            var handler = go.AddComponent<RemoteLobby>();
            
            SteamNetworkingIdentity id = new SteamNetworkingIdentity();
            if (!id.ParseString(addr))
            {
                handler._eventQueue.Enqueue(() => handler.RaiseError("Bad lobby address"));
            }
            else
            {
                id.SetSteamID(id.GetSteamID());
                handler._eventQueue.Enqueue(() => handler.Connect(id));
            }

            return handler;
        }

        void Connect(SteamNetworkingIdentity ip)
        {
            try
            {
                ip.ToString(out var roundTrip);
                _connectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>
                    .Create(OnConnectionStatusChanged);
                var hConn = SteamNetworkingSockets.ConnectP2P(ref ip, LOBBY_PORT, 0,
                    Array.Empty<SteamNetworkingConfigValue_t>());
                _socket = new Socket(hConn);
                Mod.logger.Log($"[RemoteLobby] Connecting, socket ({hConn}) identity {roundTrip}");
            }
            catch (Exception e)
            {
                Mod.logger.LogException("[RemoteLobby] Connect failed", e);
                RaiseError("Internal error");
                return;
            }
        }

        void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t cb)
        {
            if (cb.m_hConn != _socket.Handle)
            {
                Mod.logger.Warning("[RemoteLobby] Unexpected status change callback for unknown socket handle");
                return;
            }
            
            Mod.logger.Log("[RemoteLobby] Conn state: " + cb.m_info.m_eState + " " + cb.m_info.m_szConnectionDescription);
            switch (cb.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    if (State == LobbyState.JOINING)
                    {
                        Mod.logger.Log("[RemoteLobby] Connected");
                        State = LobbyState.NOT_READY;
                        RaiseStateChange();
                    }
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FinWait:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                {
                    _socket.Dispose();
                    RaiseError("Connection error: " + cb.m_info.m_szEndDebug);
                    break;
                }
            }
        }
    }
}