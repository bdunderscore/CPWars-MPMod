using System.Collections.Generic;
using MessagePack;

namespace CPMod_Multiplayer.Serialization
{
    [MessagePackObject]
    public class LobbyPacket : NetPacket
    {
        [Key(0)]
        public LobbyPacketInner lobbyPacket;

        public override string ToString()
        {
            return $"{nameof(lobbyPacket)}: {lobbyPacket}";
        }
    }
    
    [MessagePack.Union(0, typeof(LobbyMemberState))]
    [MessagePack.Union(1, typeof(LobbyStartGame))]
    [MessagePack.Union(2, typeof(LobbyAckStartGame))]
    [MessagePack.Union(3, typeof(LobbyMemberSync))]
    [MessagePack.Union(4, typeof(LobbyMemberDrop))]
    [MessagePack.Union(5, typeof(LobbyHello))]
    [MessagePack.Union(6, typeof(LobbyRenumber))]
    public interface LobbyPacketInner
    {
    }

    public static class LobbyPacketExt
    {
        public static LobbyPacket ToNetPacket(this LobbyPacketInner packet)
        {
            return new LobbyPacket()
            {
                lobbyPacket = packet
            };
        }
    }

    [MessagePackObject]
    public class LobbyHello : LobbyPacketInner
    {
        [Key(0)] public int yourIndex;

        public override string ToString()
        {
            return $"[LobbyHello {nameof(yourIndex)}: {yourIndex}]";
        }
    }

    /**
     * Sent upon establishment of the management socket, and subsequently whenever state changes.
     */
    [MessagePackObject]
    public class LobbyMemberState : LobbyPacketInner
    {
        public LobbyMemberState()
        {
            displayName = "???";
            selectedClub = "random";
            characters = new string[5]
            {
                "__random__",
                "__random__",
                "__random__",
                "__random__",
                "__random__"
            };
            ready = false;
            teamIndex = 0;
        }
        
        [Key(0)]
        public string displayName;
        [Key(1)]
        public string selectedClub;
        [Key(2)]
        public string[] characters;
        [Key(3)]
        public bool ready;
        [Key(4)]
        public int teamIndex;
        [Key(5)]
        public Dictionary<string, NetCharaChara> characterRoster = new Dictionary<string, NetCharaChara>();

        public override string ToString()
        {
            return $"[LobbyMemberState {nameof(displayName)}: {displayName}, {nameof(selectedClub)}: {selectedClub}, {nameof(characters)}: {characters}, {nameof(ready)}: {ready}, {nameof(teamIndex)}: {teamIndex}]";
        }
    }

    [MessagePackObject]
    public class LobbyMemberSync : LobbyPacketInner
    {
        [Key(0)] public LobbyMemberState syncMember;
    }

    [MessagePackObject]
    public class LobbyMemberDrop : LobbyPacketInner
    {
        [Key(0)] public int index;
    }
    
    /**
     * Sent from host to clients to indicate that the game is about to begin. Contains the team assignment for this
     * player.
     */
    [MessagePackObject]
    public class LobbyStartGame : LobbyPacketInner
    {
        public override string ToString()
        {
            return $"[LobbyStartGame]";
        }
    }

    /**
     * Send from client to host to acknowledge a game start - after the LobbyStartGame/LobbyAckStartGame exchange, all
     * subsequent packets are done using the Net* protocol.
     */
    [MessagePackObject]
    public class LobbyAckStartGame : LobbyPacketInner
    {
        public override string ToString()
        {
            return $"[LobbyAckStartGame]";
        }
    }

    /**
     * Sent from host to clients to indicate that a lobby member has been assigned a new team index.
     * This generally is done to avoid gaps in the team assignments at game start.
     */
    [MessagePackObject]
    public class LobbyRenumber : LobbyPacketInner
    {
        [Key(0)]
        public int from;
        [Key(1)]
        public int to;
    }
}