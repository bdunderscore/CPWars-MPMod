using JetBrains.Annotations;
using MessagePack;
// ReSharper disable MemberCanBePrivate.Global

namespace CPMod_Multiplayer.Serialization
{
    [MessagePack.Union(0, typeof(NetGameInit))]
    [MessagePack.Union(1, typeof(NetUnitPop))]
    [MessagePack.Union(2, typeof(NetCharaState))]
    [MessagePack.Union(3, typeof(NetUnitState))]
    [MessagePack.Union(4, typeof(NetRoomState))]
    [MessagePack.Union(5, typeof(NetFrameStart))]
    [MessagePack.Union(6, typeof(NetFrameComplete))]
    [MessagePack.Union(7, typeof(NetLogCreateMessage))]
    [MessagePack.Union(8, typeof(NetLogCreateGetMessage))]
    [MessagePack.Union(9, typeof(LobbyPacket))]
    [MessagePack.Union(10, typeof(NetUnitOrders))]
    [MessagePack.Union(11, typeof(NetGameResult))]
    [MessagePack.Union(12, typeof(NetUnitDeath))]
    [MessagePack.Union(13, typeof(NetCharaChara))]    
    public interface NetPacket
    {
        
    }

    // Reset - if sent all state is immediately cleared. Play resumes with the next frame.
    [MessagePackObject]
    public class NetGameInit : NetPacket
    {
        // player index -> club name
        // note that this is ONE indexed - team zero is unused
        [Key(0)]
        public string[] clublist;
        [Key(1)]
        public string[] playernames;
        [Key(2)]
        public int yourIndex;

        public override string ToString()
        {
            return $"NetGameInit: {nameof(clublist)}: {clublist}, {nameof(playernames)}: {playernames}, {nameof(yourIndex)}: {yourIndex}";
        }
    }

    [MessagePackObject]
    public class NetUnitPop : NetPacket
    {
        [Key(0)]
        public int playerIndex;
        [Key(1)]
        public int charaIndex;
        [Key(2)]
        public int unitIndex;
        // A subsequent NetUnitSync will bring this unit into sync

        public override string ToString()
        {
            return $"NetUnitPop: {nameof(playerIndex)}: {playerIndex}, {nameof(charaIndex)}: {charaIndex}, {nameof(unitIndex)}: {unitIndex}";
        }
    }

    [MessagePackObject]
    public class NetUnitDeath : NetPacket
    {
        [Key(0)]
        public int unitIndex;

        public override string ToString()
        {
            return $"NetUnitDeath: {nameof(unitIndex)}: {unitIndex}";
        }
    }

    /**
     * Serializes CharacterData.Character
     */
    [MessagePackObject]
    public class NetCharaChara : NetPacket
    {
        [Key(0)]
        public string name;
        [Key(1)]
        public int effort;
        [Key(2)]
        public string[] itemNames;
        [Key(3)]
        public bool isOwn; // true if character is unlocked
    }

    /*
     * Serializes Character (_not_ CharacterData.Character)
     */
    [MessagePackObject]
    public class NetCharaState : NetPacket
    {
        [Key(0)]
        public int charaIndex;
        [Key(1)]
        public string charaName;
        [Key(2)]
        public string displayName;
        [Key(3)]
        public int lv;
        [Key(4)]
        public int effort;
        [Key(5)]
        public int type;
        [Key(6)]
        public int energy;
        [Key(7)]
        public int power;
        [Key(8)]
        public int speed;
        [Key(9)]
        public int intelligence;
        [Key(10)]
        public int energy_max;
        [Key(11)]
        public int ap_energy;
        [Key(12)]
        public int ap_power;
        [Key(13)]
        public int ap_speed;
        [Key(14)]
        public int ap_intelligence;
        [Key(15)]
        public float exp;

        public override string ToString()
        {
            return $"NetCharaState: {nameof(charaIndex)}: {charaIndex}, {nameof(charaName)}: {charaName}, {nameof(displayName)}: {displayName}, {nameof(lv)}: {lv}, {nameof(effort)}: {effort}, {nameof(type)}: {type}, {nameof(energy)}: {energy}, {nameof(power)}: {power}, {nameof(speed)}: {speed}, {nameof(intelligence)}: {intelligence}, {nameof(energy_max)}: {energy_max}, {nameof(ap_energy)}: {ap_energy}, {nameof(ap_power)}: {ap_power}, {nameof(ap_speed)}: {ap_speed}, {nameof(ap_intelligence)}: {ap_intelligence}, {nameof(exp)}: {exp}";
        }
    }

    [MessagePackObject]
    public class NetUnitState : NetPacket
    {
        [Key(0)]
        public int unitIndex;
        [Key(1)]
        public int energy;
        [Key(2)]
        public int inRoomId;
        [Key(3)]
        public int lastRoomId;
        [Key(4)]
        public int owningPlayer;
        [Key(5)]
        public float actionProc;
        [Key(6)]
        public string actionName;
        [Key(7)]
        public int targetRoomId;
        [Key(8)]
        public int targetWayId;
        [Key(9)]
        public int wayId;
        [Key(10)]
        public bool isLeader;

        [Key(11)]
        public int energy_max;
        [Key(12)]
        public int power;
        [Key(13)]
        public int speed;
        [Key(14)]
        public int intelligence;

        public override string ToString()
        {
            return $"NetUnitState: {nameof(unitIndex)}: {unitIndex}, {nameof(energy)}: {energy}, {nameof(inRoomId)}: {inRoomId}, {nameof(lastRoomId)}: {lastRoomId}, {nameof(owningPlayer)}: {owningPlayer}, {nameof(actionProc)}: {actionProc}, {nameof(actionName)}: {actionName}, {nameof(targetRoomId)}: {targetRoomId}, {nameof(targetWayId)}: {targetWayId}, {nameof(wayId)}: {wayId}, {nameof(isLeader)}: {isLeader}";
        }
    }
    
    [MessagePackObject]
    public class NetRoomState : NetPacket
    {
        [Key(0)]
        public int id;
        [Key(1)]
        public int dominationTeam;
        [Key(2)]
        public float dominance;
        [Key(3)]
        public int trainingPower;

        public override string ToString()
        {
            return $"NetRoomState: {nameof(id)}: {id}, {nameof(dominationTeam)}: {dominationTeam}, {nameof(dominance)}: {dominance}, {nameof(trainingPower)}: {trainingPower}";
        }
    }
    
    /**
     * General global game state - sent every tick
     */
    [MessagePackObject]
    public class NetFrameStart : NetPacket
    {
        [Key(0)]
        public long tick;
        [Key(1)]
        public int day;
        [Key(2)]
        public int hour;
        [Key(3)]
        public float minute;
        [Key(4)]
        public int[] money;

        public override string ToString()
        {
            return $"NetFrameStart: {nameof(tick)}: {tick}, {nameof(day)}: {day}, {nameof(hour)}: {hour}, {nameof(minute)}: {minute}, {nameof(money)}: {money}";
        }
    }

    [MessagePackObject]
    public class NetFrameComplete : NetPacket
    {
        public override string ToString()
        {
            return $"NetFrameComplete";
        }
    }

    [MessagePackObject]
    public struct NetColor
    {
        [Key(0)]
        public float r;
        [Key(1)]
        public float g;
        [Key(2)]
        public float b;
        [Key(3)]
        public float a;
    }

    [MessagePackObject]
    public class NetLogCreateMessage : NetPacket
    {
        [Key(0)]
        public string club;
        [Key(1)]
        public NetColor color;
        [Key(2)]
        public string name;
        [Key(3)]
        public string displayName;
        [Key(4)]
        public string text;
        [Key(5)]
        public int newLv;
        [Key(6)]
        public string name2;
    }

    [MessagePackObject]
    public class NetLogCreateGetMessage : NetPacket
    {
        [Key(0)]
        public string name;
        [Key(1)]
        public string displayName;
        [Key(2)]
        public string text;
    }

    [MessagePackObject]
    public class NetUnitOrders : NetPacket
    {
        [Key(0)] public int? moveTo;
        [Key(1)] [CanBeNull] public string command;
        [Key(2)] public int unitId;

        public override string ToString()
        {
            return $"[NetUnitOrders {nameof(moveTo)}: {moveTo}, {nameof(command)}: {command}, {nameof(unitId)}: {unitId}]";
        }
    }

    [MessagePackObject]
    public class NetGameResult : NetPacket
    {
        [Key(0)] public int winner;
    }
    
    // TODO LogManager intercept
}