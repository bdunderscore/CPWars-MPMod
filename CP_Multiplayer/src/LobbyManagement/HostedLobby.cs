using System;
using System.Collections.Generic;
using System.Linq;
using CPMod_Multiplayer.Serialization;
using MessagePack;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CPMod_Multiplayer.LobbyManagement
{
    public class HostedLobby : Lobby
    {
        public override LobbyState State { get; protected set; } = LobbyState.JOINING;
        public bool LobbyOpen { get; set; } = false;
        public override bool IsHost => true;

        private HSteamListenSocket _listenSocket;

        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;

        private Dictionary<HSteamNetConnection, LobbyMember> _handleToMember =
            new Dictionary<HSteamNetConnection, LobbyMember>();

        private bool GameActive = false;

        HostedLobby()
        {
            var steamId = SteamUser.GetSteamID();
            if (Members.SelfIndex < 0)
            {
                Members.SetMemberState(0, new LobbyMemberState()
                    {
                        displayName = SteamFriends.GetPersonaName()
                    }
                );
                Members.SelfIndex = 0;
                Members.Self.OnChange += OnMemberChange;
                Members.OnRenumber += OnRenumber;
            }
        }

        void OnRenumber(int from, int to)
        {
            var msg = new LobbyRenumber()
            {
                from = from,
                to = to
            }.ToNetPacket();
            
            foreach (var member in Members)
            {
                member.Socket?.Send(msg);
            }
        }
        
        void OnConnect(Socket s, SteamNetworkingIdentity remoteIdentity)
        {
            try
            {
                if (Members.TryJoin(s, out var member))
                {
                    SteamNetworkingSockets.AcceptConnection(s.Handle);
                    _handleToMember[s.Handle] = member;

                    member.Socket?.Send(new LobbyHello()
                        {
                            yourIndex = member.MemberState.teamIndex
                        }.ToNetPacket()
                    );

                    SteamInfoLookup.LookupSteamInfo(remoteIdentity.GetSteamID(), (id, name) =>
                    {
                        member.MemberState.displayName = name;
                        member.RaiseOnChange();
                    });
                    
                    member.OnChange += OnMemberChange;

                    foreach (var preexistingMember in Members)
                    {
                        member.Socket?.Send(new LobbyMemberSync()
                        {
                            syncMember = preexistingMember.MemberState
                        }.ToNetPacket());
                    }
                }
                else
                {
                    s.CloseConnection("No available indexes");
                    s?.Dispose();
                }
            }
            catch (Exception e)
            {
                Mod.LogException("OnConnect", e);
            }
        }

        private void OnMemberChange(LobbyMember member)
        {
            if (member.Disconnected)
            {
                return;
            }

            foreach (var listener in Members)
            {
                listener.Socket?.Send(new LobbyMemberSync()
                    {
                        syncMember = member.MemberState
                    }.ToNetPacket()
                );
            }
        }
        
        protected override void Update()
        {
            try
            {
                if (!GameActive)
                {
                    PollSockets();
                }

                base.Update();
            }
            catch (Exception e)
            {
                Mod.LogException("[HostedLobby] Update", e);
            }
        }

        private void PollSockets()
        {
            foreach (var member in Members)
            {
                if (member.Socket == null) continue;

                member.Socket.Flush();

                if (member.Socket.ErrorState)
                {
                    _eventQueue.Enqueue(() =>
                    {
                        Members.Part(member.MemberState.teamIndex);
                        _handleToMember.Remove(member.Socket.Handle);
                    });
                }

                if (member.Socket.TryReceive(out var pkt))
                {
                    HandlePacket(member, pkt);
                }
            }
        }

        private void HandlePacket(LobbyMember member, byte[] pkt)
        {
            LobbyPacketInner lobbyPacket;

            try
            {
                var msg = MessagePackSerializer.Deserialize<NetPacket>(pkt);

                if (msg is LobbyPacket lp)
                {
                    lobbyPacket = lp.lobbyPacket;
                }
                else
                {
                    Mod.logger.Warning($"Unexpected game packet: {msg}");
                    return;
                }
            }
            catch (Exception e)
            {
                Mod.logger.Warning("Malformed packet received");
                return;
            }
            
            Mod.logger.Log($"Processing incoming packet: {lobbyPacket}");

            switch (lobbyPacket)
            {
                case LobbyMemberSync memberSync:
                {
                    if (memberSync.syncMember.teamIndex != member.MemberState.teamIndex)
                    {
                        Mod.logger.Warning("Player tried to change someone else's state");
                        return;
                    }
                    Members.SetMemberState(memberSync.syncMember.teamIndex, memberSync.syncMember);
                    break;
                }
                default:
                    Mod.logger.Warning($"Unexpected packet: {lobbyPacket}");
                    break;
            }
        }

        private void OnDestroy()
        {
            Mod.logger.Log("[Lobby] Destroyed");
            if (_connectionStatusChangedCallback != null) _connectionStatusChangedCallback.Dispose();
            if (_listenSocket != default) SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            foreach (var member in Members)
            {
                member.Remove();
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
                    if (!LobbyOpen)
                    {
                        SteamNetworkingSockets.CloseConnection(data.m_hConn, 0, "Lobby is closed", false);
                    }
                    else 
                    {
                        Mod.logger.Log(
                            $"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Accepted connection");
                        OnConnect(new Socket(data.m_hConn), data.m_info.m_identityRemote);
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
                    if (_handleToMember.TryGetValue(data.m_hConn, out var member))
                    {
                        Members.Part(member);
                    }
                    break;
                }
            }
        }
        
        public override void StartGame()
        {
            if (GameActive) return;
            
            Members.Defragment();
            
            var pkt = MessagePackSerializer.Serialize<NetPacket>(new LobbyStartGame().ToNetPacket());
            foreach (var member in Members)
            {
                member.Socket?.Send(pkt);
            }

            GameActive = true;
            
            MainSceneManager.Instance.StartGame();
        }

        public override void OnGameOver()
        {
            GameActive = false;
        }
    }
}