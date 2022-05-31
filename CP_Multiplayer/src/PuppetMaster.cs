using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPMod_Multiplayer.HarmonyPatches;
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
            Mod.logger.Log($"Add mapping {u} => {idToUnit.Count}");
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
        
        private HSteamListenSocket _listenSocket;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCB;
        private Dictionary<HSteamNetConnection, Socket> _connections = new Dictionary<HSteamNetConnection, Socket>();
        private List<Socket> _newConnections = new List<Socket>();

        private Queue<NetPacket> adhocPackets = new Queue<NetPacket>();

        public static void EnqueueAdhocPacket(NetPacket packet)
        {
            if (instance != null) instance.adhocPackets.Enqueue(packet);
        }
        
        private void Awake()
        {
            instance = this;
            
            // Debugging
            OnMessageTransmit += (packet) => Mod.logger.Log($"PuppetMaster: {packet}");

            var ip = new SteamNetworkingIPAddr();
            ip.Clear();
            ip.SetIPv4(0, 9999);

            try
            {
                _listenSocket =
                    SteamNetworkingSockets.CreateListenSocketIP(ref ip, 0, Array.Empty<SteamNetworkingConfigValue_t>());

                _connectionStatusChangedCB =
                    Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSocketStatusChanged);
                Mod.logger.Log($"Opened listening socket: {_listenSocket.m_HSteamListenSocket}");
            }
            catch (Exception e)
            {
                Mod.logger.LogException("Failed to open listen socket", e);
            }

            GameManager_Tick.AfterTick += AfterTick;
        }

        private void OnDestroy()
        {
            instance = null;
            _connectionStatusChangedCB?.Dispose();
            SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            foreach (var socket in _connections.Values)
            {
                socket.Dispose();
            }

            _listenSocket = default;
            _connections.Clear();
            _newConnections.Clear();
            
            GameManager_Tick.AfterTick -= AfterTick;
        }

        private void OnSocketStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            switch (data.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                {
                    Mod.logger.Log($"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Accepted connection");
                    SteamNetworkingSockets.AcceptConnection(data.m_hConn);
                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                {
                    Mod.logger.Log($"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Connected");
                    var socket = new Socket(data.m_hConn);
                    _connections.Add(data.m_hConn, socket);
                    _newConnections.Add(socket);
                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Dead:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                {
                    Mod.logger.Warning(
                        $"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Connection error: {data.m_info.m_eState} {data.m_info.m_szConnectionDescription} {data.m_info.m_szEndDebug}");
                    if (_connections.TryGetValue(data.m_hConn, out var socket))
                    {
                        socket.Dispose();
                        _connections.Remove(data.m_hConn);
                        _newConnections.Remove(socket);
                    }

                    SteamNetworkingSockets.CloseConnection(data.m_hConn, 0, "", false);
                    break;
                }
                default:
                    Mod.logger.Warning($"[{data.m_info.m_addrRemote}/{data.m_info.m_identityRemote}] Transition to {data.m_info.m_eState} {data.m_info.m_szConnectionDescription} {data.m_info.m_szEndDebug}");
                    break;
            }
        }
        
        private void LateUpdate()
        {
            if (_initial)
            {
                InitIndexes();
                _initial = false;
            }
            
            foreach (var socket in _newConnections)
            {
                Mod.logger.Log($"Starting full sync");

                try
                {
                    FullSync((pkt) =>
                    {
                        var serialized = MessagePackSerializer.Serialize(pkt);
                        Mod.logger.Log($"Send packet for full sync: {pkt}");
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

            _newConnections.Clear();

            if (_requestIncrementalSync)
            {
                Mod.logger.Log("Starting incremental sync");
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
                    
                    foreach (var socket in _connections.Values)
                    {
                        int byteCount = 0;
                        foreach (var pkt in packets)
                        {
                            var serialized = MessagePackSerializer.Serialize(pkt);
                            Mod.logger.Log($"Send packet for incremental sync: {pkt}");
                            socket.Send(serialized);
                            byteCount += serialized.Length;
                        }
                        
                        Mod.logger.Log($"Sent {packets.Count} packets for incremental sync, {byteCount} bytes");
                    }
                }
                catch (Exception e)
                {
                    Mod.LogException("Incremental sync", e);
                }

                _requestIncrementalSync = false;
            }

            foreach (var socket in _connections.Values)
            {
                socket.Flush();
            }
        }

        private void AfterTick()
        {
            Mod.logger.Log("AfterTick: Request incremental sync");
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
                money = new long[] { GameManager.Instance.Money,GameManager.Instance.Money, GameManager.Instance.Money },
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

    internal class PuppetStrings : PuppetBase
    {
        private static readonly MethodInfo m_UIUnit_Update = AccessTools.Method(typeof(UI_Unit), "Update");
        
        private Queue<NetPacket> IncomingPackets = new Queue<NetPacket>();

        internal static PuppetStrings Instance;
        
        private readonly FieldInfo f_lastRoom = AccessTools.Field(typeof(Unit), "_lastRoom");
        private readonly FieldInfo f_actionProc = AccessTools.Field(typeof(Unit), "_actionProc");
        private readonly FieldInfo f_targetRoom = AccessTools.Field(typeof(Unit), "_targetRoom");

        private Socket _socket;
        private bool _connected;

        private Callback<SteamNetConnectionStatusChangedCallback_t> _connStatusChanged;

        private HashSet<Unit> _positionUpdateNeeded = new HashSet<Unit>();
        
        void Awake()
        {
            Instance = this;
            
            // Hardcode for now!
            var remoteIp = new SteamNetworkingIPAddr();
            remoteIp.Clear();
            remoteIp.SetIPv4(0x7F000001, 9999);

            Mod.logger.Log("[PuppetStrings] Initiate connection");
            _connStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnStatusChanged);
            var hConn = SteamNetworkingSockets.ConnectByIPAddress(ref remoteIp, 0, Array.Empty<SteamNetworkingConfigValue_t>());
            _socket = new Socket(hConn);
        }

        private void OnDestroy()
        {
            Instance = null;
            _socket.Dispose();
        }

        internal void OnConnStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
        {
            Mod.logger.Log($"[ConnStatus] {info.m_info.m_eState}");

            switch (info.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    Mod.logger.Log($"[ConnStatus] Marked connected");
                    _connected = true;
                    break;
                default:
                    break;
            }
        }

        void FixedUpdate()
        {
            if (!_connected)
            {
                return;
            }

            while (_socket.TryReceive(out var pkt))
            {
                try
                {
                    var msg = MessagePackSerializer.Deserialize<NetPacket>(pkt);
                    IncomingPackets.Enqueue(msg);
                    Mod.logger.Log("Received packet: " + msg);
                }
                catch (Exception e)
                {
                    Mod.logger.LogException("Failed to deserialize message", e);
                    _connected = false;
                    return;
                }
            }
            
            // Only start processing if we have a full packet
            if (IncomingPackets.All(p => !(p is NetFrameComplete)))
            {
                return;
            }

            foreach (var incomingPacket in IncomingPackets)
            {
                try
                {
                    HandlePacket(incomingPacket);
                }
                catch (Exception e)
                {
                    Mod.LogException("Failed to handle incoming packet", e);
                }
            }
            
            IncomingPackets.Clear();
            
            foreach (var unit in _positionUpdateNeeded)
            {
                unit.UpdatePosition();
            }
            _positionUpdateNeeded.Clear();
        }

        void HandlePacket(NetPacket packet)
        {
            switch (packet)
            {
                case NetGameInit gameInit:
                    HandleGameInit(gameInit);
                    break;
                case NetFrameStart frameStart:
                    HandleFrameStart(frameStart);
                    break;
                case NetCharaState charaState:
                    HandleCharaState(charaState);
                    break;
                case NetUnitPop unitPop:
                    HandleUnitPop(unitPop);
                    break;
                case NetUnitState unitState:
                    HandleUnitState(unitState);
                    break;
                case NetRoomState roomState:
                    HandleRoomState(roomState);
                    break;
                case NetFrameComplete _:
                    break;
                case NetLogCreateMessage logCreateMessage:
                    HandleLogCreateMessage(logCreateMessage);
                    break;
                case NetLogCreateGetMessage logCreateGetMessage:
                    HandleLogCreateGetMessage(logCreateGetMessage);
                    break;
                default:
                    Mod.logger.Log($"Unhandled packet: {packet}");
                    break;
            }
        }

        private void HandleLogCreateGetMessage(NetLogCreateGetMessage logCreateGetMessage)
        {
            LogManager.Instance.CreateGetMessage(
                logCreateGetMessage.name,
                logCreateGetMessage.displayName,
                logCreateGetMessage.text
            );
        }

        private void HandleLogCreateMessage(NetLogCreateMessage logCreateMessage)
        {
            LogManager.Instance.CreateMessage(
                logCreateMessage.club,
                new Color(logCreateMessage.color.r, logCreateMessage.color.g, logCreateMessage.color.b, logCreateMessage.color.a),
                logCreateMessage.name,
                logCreateMessage.displayName,
                logCreateMessage.text,
                logCreateMessage.newLv,
                logCreateMessage.name2
            );
        }

        private void HandleRoomState(NetRoomState roomState)
        {
            if (!roomMapping.TryGetValue(roomState.id, out var room))
            {
                Mod.logger.Warning($"Unmapped room {roomState.id}");
                return;
            }

            room.Dominance = roomState.dominance;
            room.DominationTeam = roomState.dominationTeam;
            room.TrainingPower = roomState.trainingPower;
        }

        private void HandleGameInit(NetGameInit gameInit)
        {
            // Clear all units and characters
            InitIndexes();
            
            foreach (var unit in UnitManager.Instance.Units.Values.ToArray())
            {
                GameManager.Instance.DeleteCharacter(unit.Name);
                UnityEngine.Object.Destroy(unit.UIObject.gameObject);
                UnityEngine.Object.Destroy(unit.gameObject);
            }

            foreach (var room in RoomManager.Instance.Rooms)
            {
                room.Units.Clear();
            }
            
            Mod.logger.Log("Clearing character list");
            CharacterManager.Instance.CharacterList.Clear();
            CharacterManager.Instance.NameList.Clear();

            // Set club list
            GameManager.Instance.clubList = new List<string>(gameInit.clublist);
            
            // TODO - do something with playernames/index
        }
        
        private void HandleFrameStart(NetFrameStart frameStart)
        {
            f_day.SetValue(GameManager.Instance, frameStart.day);
            f_hour.SetValue(GameManager.Instance, frameStart.hour);
            f_min.SetValue(GameManager.Instance, frameStart.minute);
        }
        
        private void HandleCharaState(NetCharaState charaState)
        {
            Character c = new Character(charaState.charaName, charaState.displayName, charaState.effort)
            {
                name = charaState.charaName,
                name_display = charaState.displayName,
                lv = charaState.lv,
                effort = charaState.effort,
                type = charaState.type,
                energy = charaState.energy,
                energy_max = charaState.energy_max,
                power = charaState.power,
                speed = charaState.speed,
                intelligence = charaState.intelligence,
                ap_energy = charaState.ap_energy,
                ap_power = charaState.ap_power,
                ap_speed = charaState.ap_speed,
                ap_intelligence = charaState.ap_intelligence,
                exp = charaState.exp,
            };
            
            Mod.logger.Log($"Received character: {c.name} {c.energy}/{c.energy_max} pwr/spd/int {c.power}/{c.speed}/{c.intelligence}");
            CharacterManager.Instance.CharacterList.Add(c.name, c);
            CharacterManager.Instance.NameList.Add(c.name);
            charaMapping.Set(charaState.charaIndex, c.name);
        }

        private void HandleUnitPop(NetUnitPop unitPop)
        {
            var c = CharacterManager.Instance.CharacterList[charaMapping.Get(unitPop.charaIndex)];
            
            Mod.logger.Log($"UnitPop before: {c.name} {c.energy}/{c.energy_max} pwr/spd/int {c.power}/{c.speed}/{c.intelligence}");
            
            var unit = GameManager.Instance.PopCharacter(c.name, unitPop.playerIndex);
            unitMapping.Set(unitPop.unitIndex, unit);
            
            Mod.logger.Log($"UnitPop after: {c.name} {unit.Energy}:{c.energy}/{c.energy_max} pwr/spd/int {c.power}/{c.speed}/{c.intelligence}");
        }

        private void HandleUnitState(NetUnitState unitState)
        {
            var unit = unitMapping.Get(unitState.unitIndex);

            var priorInRoom = unit.InRoom;
            var priorWay = unit.Way;
            
            if (unit.InRoom != null) unit.InRoom.Remove(unit);

            unit.Energy = unitState.energy;
            unit.InRoom = unitState.inRoomId != -1 ? RoomManager.Instance.GetRoom(unitState.inRoomId) : null;
            f_lastRoom.SetValue(unit, GetRoomById(unitState.lastRoomId));
            unit.Team = unitState.owningPlayer;
            unit.ActionName = unitState.actionName;
            // Setting ActionName zeroes actionProc, so set it afterward
            f_actionProc.SetValue(unit, unitState.actionProc);
            f_targetRoom.SetValue(unit,  unitState.targetRoomId != -1 ? RoomManager.Instance.GetRoom(unitState.targetRoomId) : null);
            unit.Way = unitState.wayId != -1 ? waypointMapping[unitState.wayId] : null;
            unit.IsLeader = unitState.isLeader;

            MultiplayerManager.instance.StatusOverrides.Remove(unit.Name);
            MultiplayerManager.instance.StatusOverrides.Add(unit.Name, new StatusOverride()
            {
                Intelligence = unitState.intelligence,
                Power = unitState.power,
                Speed = unitState.speed
            });

            unit.CharacterCurrentData.energy_max = unitState.energy_max;

            if (priorInRoom != unit.InRoom || priorWay != unit.Way)
            {
                if (priorInRoom != null)
                {
                    priorInRoom.Units.ForEach((u) => _positionUpdateNeeded.Add(u));
                }

                if (unit.InRoom != null)
                {
                    unit.InRoom.Units.ForEach((u) => _positionUpdateNeeded.Add(u));
                }

                _positionUpdateNeeded.Add(unit);

                unit.transform.position =
                    unit.InRoom != null ? unit.InRoom.transform.position : unit.Way.transform.position;
            }

            m_UIUnit_Update.Invoke(unit.UIObject, Array.Empty<object>());
        }

        private static Room GetRoomById(int roomId)
        {
            if (roomId < 0) return null;
            var room = RoomManager.Instance.GetRoom(roomId);

            if (room == null)
            {
                Mod.logger.Warning($"Room {roomId} not found, returning null. Known rooms: {RoomManager.Instance.Rooms}");
            }

            return room;
        }
    }
}