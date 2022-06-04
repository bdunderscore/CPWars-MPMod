using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPMod_Multiplayer.LobbyManagement;
using CPMod_Multiplayer.Serialization;
using HarmonyLib;
using MessagePack;
using MessagePack.Unity;
using Steamworks;
using UnityEngine;

namespace CPMod_Multiplayer
{
    internal class PuppetClient : PuppetBase
    {
        private static readonly MethodInfo m_UIUnit_Update = AccessTools.Method(typeof(UI_Unit), "Update");
        
        private Queue<NetPacket> IncomingPackets = new Queue<NetPacket>();

        internal static PuppetClient Instance;
        
        private readonly FieldInfo f_lastRoom = AccessTools.Field(typeof(Unit), "_lastRoom");
        private readonly FieldInfo f_actionProc = AccessTools.Field(typeof(Unit), "_actionProc");
        private readonly FieldInfo f_targetRoom = AccessTools.Field(typeof(Unit), "_targetRoom");

        private Socket _socket;
        private bool _connected;
        private bool _inReceive;

        private Callback<SteamNetConnectionStatusChangedCallback_t> _connStatusChanged;

        private HashSet<Unit> _positionUpdateNeeded = new HashSet<Unit>();
        
        void Awake()
        {
            Instance = this;

            _socket = ((RemoteLobby) LobbyManager.CurrentLobby).Socket;

            _connected = true;
        }

        private void OnDestroy()
        {
            Instance = null;
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
                    //Mod.logger.Log("Received packet: " + msg);
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
                    _inReceive = true;
                    HandlePacket(incomingPacket);
                }
                catch (Exception e)
                {
                    Mod.LogException("Failed to handle incoming packet", e);
                }
                finally
                {
                    _inReceive = false;
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
                    GameManager.Instance.AddMoney = RoomManager.Instance.Rooms
                        .Count(r => r.DominationTeam == MultiplayerManager.MyTeam) * 10;
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
            
            GameManager.Instance.Money = frameStart.money[MultiplayerManager.MyTeam];
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
            
            //Mod.logger.Log($"Received character: {c.name} {c.energy}/{c.energy_max} pwr/spd/int {c.power}/{c.speed}/{c.intelligence}");
            CharacterManager.Instance.CharacterList.Add(c.name, c);
            CharacterManager.Instance.NameList.Add(c.name);
            charaMapping.Set(charaState.charaIndex, c.name);
        }

        private void HandleUnitPop(NetUnitPop unitPop)
        {
            var c = CharacterManager.Instance.CharacterList[charaMapping.Get(unitPop.charaIndex)];
            
            Mod.logger.Log($"UnitPop: c null? {c == null}");
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
            if (unit.ActionName != "capture" && unit.ActionName != "walk")
            {
                // Unit will try to reset its action in FixedUpdate if command is inconsistent
                unit.SetCommand(unit.ActionName);
            }
            else
            {
                unit.ActionName = unitState.actionName;                
            }
            // Setting ActionName zeroes actionProc, so set it afterward
            f_actionProc.SetValue(unit, unitState.actionProc);
            f_targetRoom.SetValue(unit,  unitState.targetRoomId != -1 ? RoomManager.Instance.GetRoom(unitState.targetRoomId) : null);
            if (unitState.wayId == -1 || !waypointMapping.TryGetValue(unitState.wayId, out var waypoint))
            {
                unit.Way = null;
            }
            else
            {
                unit.Way = waypoint;
            }
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

                if (unit.InRoom != null) unit.transform.position = unit.InRoom.transform.position;
                else if (unit.Way != null) unit.transform.position = unit.Way.transform.position;
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

        public void UnitSetCommand(Unit unit, string action)
        {
            if (_inReceive) return;
            
            int index;
            try
            {
                index = unitMapping.Get(unit);
            }
            catch (KeyNotFoundException e)
            {
                return;
            }
            
            _socket.Send(new NetUnitOrders()
            {
                unitId = index,
                command = action
            });
        }

        public void UnitMoveTo(Unit unit, Room destination)
        {
            if (_inReceive) return;
            
            int index;
            try
            {
                index = unitMapping.Get(unit);
            }
            catch (KeyNotFoundException e)
            {
                return;
            }
            
            _socket.Send(new NetUnitOrders()
            {
                unitId = index,
                moveTo = destination.Id
            });
        }
    }
}