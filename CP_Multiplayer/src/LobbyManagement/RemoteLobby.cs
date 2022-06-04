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
    enum RemoteLobbyState
    {
        CONNECT_PENDING,
        HELLO_PENDING,
        INITIAL_SYNC_PENDING,
        STANDBY
    }

    public class RemoteLobby : Lobby
    {
        private RemoteLobbyState _internalState = RemoteLobbyState.CONNECT_PENDING;
        public override LobbyState State { get; protected set; } = LobbyState.JOINING;

        private Socket _socket;
        
        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;

        private void OnDestroy()
        {
            _connectionStatusChangedCallback.Dispose();
            _socket?.Dispose();
        }

        protected override void Update()
        {
            base.Update();

            if (_internalState == RemoteLobbyState.CONNECT_PENDING) return;

            _socket?.Flush();

            if (_socket != null && _socket.TryReceive(out var pkt) == true)
            {
                NetPacket netMsg;
                try
                {
                    netMsg = MessagePackSerializer.Deserialize<NetPacket>(pkt);
                    var rendered = netMsg == null ? "null" : netMsg.ToString();
                    Mod.logger.Log($"[RemoteLobby] Receive: {rendered}");
                }
                catch (Exception e)
                {
                    Mod.LogException("[RemoteLobby] Malformed packet", e);
                    return;
                }

                LobbyPacketInner msg;
                if (netMsg is LobbyPacket lp)
                {
                    msg = lp.lobbyPacket;
                }
                else
                {
                    Mod.logger.Warning($"[RemoteLobby] Unexpected gameplay message {netMsg}");
                    return;
                }

                switch (_internalState)
                {
                    case RemoteLobbyState.HELLO_PENDING:
                        if (msg is LobbyHello hello)
                        {
                            Members.SelfIndex = hello.yourIndex;
                            _internalState = RemoteLobbyState.STANDBY;
                            State = LobbyState.NOT_READY;
                            RaiseStateChange();
                        }
                        else
                        {
                            Mod.logger.Warning($"Unexpected message: {msg}");
                        }

                        break;
                    case RemoteLobbyState.STANDBY:
                    {
                        switch (msg)
                        {
                            case LobbyMemberSync memberSync:
                                if (Members.Self == null || memberSync.syncMember.teamIndex != Members.SelfIndex)
                                {
                                    Members.SetMemberState(memberSync.syncMember.teamIndex, memberSync.syncMember);

                                    if (memberSync.syncMember.teamIndex == Members.SelfIndex)
                                    {
                                        Members.Self.OnChange += OnSelfChange;
                                    }
                                }
                                break;
                            default:
                                Mod.logger.Warning($"Unexpected message: {msg}");
                                break;
                        }

                        break;
                    }
                }
            }
        }
        
        private void OnSelfChange(LobbyMember _)
        {
            Mod.logger.Log("OnSelfChange: Propagating change");
            _socket.Send(new LobbyMemberSync() { syncMember = Members.Self.MemberState }.ToNetPacket());
        }

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
                        Mod.logger.Log("[RemoteLobby] Connected socket");
                        _internalState = RemoteLobbyState.HELLO_PENDING;
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