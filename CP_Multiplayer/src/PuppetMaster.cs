using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CPMod_Multiplayer.HarmonyPatches;
using CPMod_Multiplayer.LobbyManagement;
using CPMod_Multiplayer.Serialization;
using HarmonyLib;
using MessagePack;
using Steamworks;
using UnityEngine;

namespace CPMod_Multiplayer
{
    public class ObjectMapping<T>
    {
        private List<T> idToUnit = new List<T>();
        private Dictionary<T, int> unitToId = new Dictionary<T, int>();

        public int Add(T u)
        {
            //Mod.logger.Log($"Add mapping {u} => {idToUnit.Count}");
            idToUnit.Add(u);
            unitToId.Add(u, idToUnit.Count - 1);
            return idToUnit.Count - 1;
        }

        public int AddOrGet(T u)
        {
            if (unitToId.TryGetValue(u, out var index))
            {
                return index;
            }
            else
            {
                return Add(u);
            }
        }

        public void Set(int index, T u)
        {
            if (index != idToUnit.Count)
            {
                Mod.logger.Error($"Unexpected index {index} for object {u}");

                while (idToUnit.Count < index)
                {
                    idToUnit.Add(default(T));
                }
            }

            if (index < idToUnit.Count)
            {
                idToUnit[index] = u;
            }
            else
            {
                idToUnit.Add(u);
            }
            unitToId[u] = index;
        }

        public int Get(T u)
        {
            if (unitToId.TryGetValue(u, out var index))
            {
                return index;
            }
            
            throw new KeyNotFoundException($"Object {u} not found in mapping.");
        }

        public T Get(int id)
        {
            return idToUnit[id];
        }

        public bool TryGet(T u, out int index)
        {
            return unitToId.TryGetValue(u, out index);
        }

        public bool TryGet(int index, out T u)
        {
            if (idToUnit.Count <= index || idToUnit[index] == null)
            {
                u = default(T);
                return false;
            }

            u = idToUnit[index];
            return true;
        }
        
        public int Destroy(T u)
        {
            int id = unitToId[u];
            idToUnit[id] = default(T);

            return id;
        }
    }

    internal abstract class PuppetBase : MonoBehaviour
    {
        protected readonly FieldInfo f_day = AccessTools.Field(typeof(GameManager), "_day");
        protected readonly FieldInfo f_hour = AccessTools.Field(typeof(GameManager), "_hour");
        protected readonly FieldInfo f_min = AccessTools.Field(typeof(GameManager), "_min");
        protected ObjectMapping<Unit> unitMapping;
        protected ObjectMapping<string> charaMapping;
        protected Dictionary<int, Room> roomMapping;
        protected Dictionary<int, WayPoint> waypointMapping;

        protected PuppetBase()
        {
            unitMapping = new ObjectMapping<Unit>();
            charaMapping = new ObjectMapping<string>();
            roomMapping = new Dictionary<int, Room>();
            waypointMapping = new Dictionary<int, WayPoint>();
        }

        protected void InitIndexes()
        {

            roomMapping.Clear();
            waypointMapping.Clear();
            unitMapping = new ObjectMapping<Unit>();
            charaMapping = new ObjectMapping<string>();
            
            foreach (var room in RoomManager.Instance.Rooms)
            {
                roomMapping[room.Id] = room;
            }

            foreach (var way in WayPointManager.Instance.Ways)
            {
                waypointMapping[way.Id] = way;
            }
        }
    }
    
    /**
     * This singleton exists when we are the leader of an active multiplayer session.
     * It transmits game state to remote players.
     */
    internal class PuppetMaster : PuppetBase
    {
        public static PuppetMaster instance;

        public delegate void OnMessageTransmitDelegate(NetPacket packet);
        public OnMessageTransmitDelegate OnMessageTransmit = (packet) => { };

        private bool _initial = true;
        private bool _requestIncrementalSync = false;
        private bool _connected = true;
        private bool _gameOver = false;
        
        private Dictionary<HSteamNetConnection, LobbyMember> _connections = new Dictionary<HSteamNetConnection, LobbyMember>();

        private Queue<NetPacket> adhocPackets = new Queue<NetPacket>();

        public static void EnqueueAdhocPacket(NetPacket packet)
        {
            if (instance != null) instance.adhocPackets.Enqueue(packet);
        }
        
        private void Awake()
        {
            instance = this;
            
            // Debugging
            //OnMessageTransmit += (packet) => Mod.logger.Log($"PuppetMaster: {packet}");

            GameManager_Tick.AfterTick += AfterTick;

            foreach (var member in LobbyManager.CurrentLobby.Members)
            {
                if (member.Socket != null) _connections.Add(member.Socket.Handle, member);
            }
        }

        private void OnDestroy()
        {
            instance = null;

            GameManager_Tick.AfterTick -= AfterTick;
        }

        private void LateUpdate()
        {
            if (!_connected) return;
            
            if (_initial)
            {
                InitialSync();

                _initial = false;
            }
            
            if (_requestIncrementalSync && _connections.Count != 0)
            {
                DoIncrementalSync();

                _requestIncrementalSync = false;
            }

            CheckGameEnd();

            foreach (var member in _connections.Values)
            {
                member.Socket.Flush();
                PollMessages(member);

                if (member.Socket?.ErrorState == true)
                {
                    try
                    {
                        _connected = false;
                        var errorWindow = ErrorWindow.Show($"{member.DisplayName}が切だんされました");
                        LobbyManager.CurrentLobby = null; // TODO - send game-over message?
                        errorWindow.OnClose = () => { MainSceneManager.Instance.StartTitle(); };
                    }
                    catch (Exception e)
                    {
                        Mod.LogException("[PuppetMaster] Disconnect handling", e);
                    }
                }
            }
        }

        private void CheckGameEnd()
        {
            if (_gameOver) return;
            
            bool[] live = new bool[GameManager.Instance.teamNum + 1];
            int liveTeams = 0;
            int sampleLiveTeam = -1;

            foreach (var unit in UnitManager.Instance.Units.Values)
            {
                if (!live[unit.Team])
                {
                    live[unit.Team] = true;
                    liveTeams++;
                    sampleLiveTeam = unit.Team;
                }
            }

            NetPacket packet = null;
            if (liveTeams == 0)
            {
                // Everyone died?
                packet = new NetGameResult()
                {
                    winner = -1
                };
                GameSetup.DeclareWinner(-1);
            }
            else if (liveTeams == 1)
            {
                packet = new NetGameResult()
                {
                    winner = sampleLiveTeam
                };
                GameSetup.DeclareWinner(sampleLiveTeam);
            }

            if (packet != null)
            {
                _gameOver = true;
                
                foreach (var member in _connections.Values)
                {
                    member.Socket?.Send(packet);
                }
            }
        }

        private void PollMessages(LobbyMember member)
        {
            if (member.Socket == null) return;

            for (int i = 0; i < 10 && member.Socket.TryReceive(out var pkt); i++)
            {
                try
                {
                    var msg = MessagePackSerializer.Deserialize<NetPacket>(pkt);

                    HandleClientMessage(member, msg);
                }
                catch (Exception e)
                {
                    Mod.LogException("[PollMessages] Error handling messages from client", e);
                }
            }
        }

        private void HandleClientMessage(LobbyMember member, NetPacket msg)
        {
            Mod.logger.Log($"PuppetMaster: received {msg}");
            switch (msg)
            {
                case NetUnitOrders orders:
                {
                    if (!unitMapping.TryGet(orders.unitId, out var unit))
                    {
                        Mod.logger.Log($"Ignoring orders for unknown unit {orders.unitId}");
                        return;
                    }

                    if (unit.Team != member.MemberState.teamIndex + 1)
                    {
                        Mod.logger.Log($"Ignoring orders for non-owned unit {orders.unitId}");
                        return;
                    }

                    if (orders.command != null)
                    {
                        unit.SetCommand(orders.command);
                    }

                    if (orders.moveTo.HasValue)
                    {
                        if (roomMapping.TryGetValue(orders.moveTo.Value, out var room))
                        {
                            unit.Move(room);                            
                        }
                        else
                        {
                            Mod.logger.Log($"Ignoring unknown room index {orders.moveTo.Value} in move order");
                        }
                    }

                    break;
                }
                default:
                    Mod.logger.Log($"Ignoring unexpected message type {msg.GetType()}");
                    break;
            }
        }

        private void DoIncrementalSync()
        {
            //Mod.logger.Log("Starting incremental sync");
            try
            {
                List<NetPacket> packets = new List<NetPacket>();

                SendFrameStart(packets.Add);

                foreach (var room in RoomManager.Instance.Rooms)
                {
                    SendRoom(packets.Add, room);
                }

                foreach (var unit in UnitManager.Instance.Units.Values)
                {
                    if (!unitMapping.TryGet(unit, out _))
                    {
                        SendUnitPop(packets.Add, unit);
                    }

                    SendUnitState(packets.Add, unit);
                }

                foreach (var adhocPacket in adhocPackets)
                {
                    packets.Add(adhocPacket);
                }

                adhocPackets.Clear();

                SendFrameComplete(packets.Add);

                foreach (var member in _connections.Values)
                {
                    int byteCount = 0;
                    foreach (var pkt in packets)
                    {
                        var serialized = MessagePackSerializer.Serialize(pkt);
                        //Mod.logger.Log($"Send packet for incremental sync: {pkt}");
                        member.Socket.Send(serialized);
                        byteCount += serialized.Length;
                    }

                    Mod.logger.Log($"Sent {packets.Count} packets for incremental sync, {byteCount} bytes");
                }
            }
            catch (Exception e)
            {
                Mod.LogException("Incremental sync", e);
            }
        }

        private void InitialSync()
        {
            InitIndexes();

            foreach (var member in _connections.Values)
            {
                var socket = member.Socket;
                Mod.logger.Log($"Starting full sync");

                try
                {
                    FullSync((pkt) =>
                    {
                        var serialized = MessagePackSerializer.Serialize(pkt);
                        //Mod.logger.Log($"Send packet for full sync: {pkt}");
                        socket.Send(serialized);
                    });
                }
                catch (Exception e)
                {
                    Mod.logger.LogException("Failed to perform full sync", e);
                    Mod.logger.Log(e.StackTrace);
                    socket.Dispose();
                    _connections.Remove(socket.Handle);

                    Exception inner = e.InnerException;
                    while (inner != null)
                    {
                        Mod.logger.LogException("Caused by", inner);
                        Mod.logger.Log(inner.StackTrace);
                        inner = inner.InnerException;
                    }
                }
            }
        }

        private void AfterTick()
        {
            //Mod.logger.Log("AfterTick: Request incremental sync");
            _requestIncrementalSync = true;
        }

        private void FullSync(OnMessageTransmitDelegate send)
        {
            SendGameInit(send);
            SendFrameStart(send);
            
            foreach (var room in RoomManager.Instance.Rooms)
            {
                SendRoom(send, room);
            }

            foreach (var character in CharacterManager.Instance.CharacterList.Values)
            {
                SendCharaState(send, character);
            }

            foreach (var unit in UnitManager.Instance.Units.Values)
            {
                SendUnitPop(send, unit);
                SendUnitState(send, unit);
            }

            SendFrameComplete(send);
        }

        void SendGameInit(OnMessageTransmitDelegate send)
        {
            // Placeholders for now!
            send(new NetGameInit()
            {
                clublist = GameManager.Instance.clubList.ToArray(),
                playernames = new string[] {"---", "PLAYER", "OBSERVER"},
                yourIndex = 2,
            });
        }

        void SendFrameStart(OnMessageTransmitDelegate send)
        {
            send(new NetFrameStart()
            {
                money = MultiplayerManager.GetMoney(),
                day = (int)f_day.GetValue(GameManager.Instance),
                hour = (int)f_hour.GetValue(GameManager.Instance),
                minute = (float)f_min.GetValue(GameManager.Instance),
                tick = 0, // TODO
            });
        }

        void SendRoom(OnMessageTransmitDelegate send, Room r)
        {
            send(new NetRoomState()
            {
                id = r.Id,
                dominationTeam = r.DominationTeam,
                dominance = r.Dominance,
                trainingPower = r.TrainingPower
            });
        }

        void SendCharaState(OnMessageTransmitDelegate send, Character c)
        {
            int id = charaMapping.AddOrGet(c.name);

            send(new NetCharaState()
            {
                charaIndex = id,
                charaName = c.name,
                displayName = c.name_display,
                lv = c.lv,
                effort = c.effort,
                type = c.type,
                energy = c.energy,
                energy_max = c.energy_max,
                power = c.power,
                speed = c.speed,
                intelligence = c.intelligence,
                ap_energy = c.ap_energy,
                ap_power = c.ap_power,
                ap_speed = c.ap_speed,
                ap_intelligence = c.ap_intelligence,
                exp = c.exp,
            });
        }

        int SendUnitPop(OnMessageTransmitDelegate send, Unit u)
        {
            int id = unitMapping.AddOrGet(u);

            send(new NetUnitPop()
            {
                unitIndex = id,
                charaIndex = charaMapping.Get(u.CharacterCurrentData.name),
                playerIndex = u.Team,
            });

            return id;
        }

        void SendUnitState(OnMessageTransmitDelegate send, Unit u)
        {
            int id = unitMapping.Get(u);

            send(new NetUnitState()
            {
                unitIndex = id,
                energy = u.Energy,
                inRoomId = u.InRoom != null ? u.InRoom.Id : -1,
                lastRoomId = u.LastRoom != null ? u.LastRoom.Id : -1,
                owningPlayer = u.Team,
                actionProc = u.ActionProc,
                actionName = u.ActionName,
                targetRoomId = u.TargetRoom != null ? u.TargetRoom.Id : -1,
                // targetWayId,
                wayId = u.Way != null ? u.Way.Id : -1,
                isLeader = u.IsLeader,
                power = u.Power,
                speed = u.Speed,
                intelligence = u.Intelligence,
                energy_max = u.CharacterCurrentData.energy_max,
            });
        }

        void SendFrameComplete(OnMessageTransmitDelegate send)
        {
            send(new NetFrameComplete());
        }
    }
}