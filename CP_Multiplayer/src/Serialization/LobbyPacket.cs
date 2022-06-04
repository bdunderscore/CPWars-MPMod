using MessagePack;

namespace CPMod_Multiplayer.Serialization
{
    [MessagePack.Union(0, typeof(LobbyMemberState))]
    [MessagePack.Union(1, typeof(LobbyStartGame))]
    [MessagePack.Union(2, typeof(LobbyAckStartGame))]
    [MessagePack.Union(3, typeof(LobbyMemberSync))]
    [MessagePack.Union(4, typeof(LobbyHello))]
    public interface LobbyPacket : NetPacket
    {
        
    }

    [MessagePackObject]
    public class LobbyHello : LobbyPacket
    {
        [Key(0)] public int yourIndex;
    }

    /**
     * Sent upon establishment of the management socket, and subsequently whenever state changes.
     */
    [MessagePackObject]
    public class LobbyMemberState : LobbyPacket
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
    }

    [MessagePackObject]
    public class LobbyMemberSync : LobbyPacket
    {
        [Key(0)]
        public LobbyMemberState[] members;
    }
    
    /**
     * Sent from host to clients to indicate that the game is about to begin. Contains the team assignment for this
     * player.
     */
    [MessagePackObject]
    public class LobbyStartGame : LobbyPacket
    {
        [Key(0)]
        public int teamIndex;
    }

    /**
     * Send from client to host to acknowledge a game start - after the LobbyStartGame/LobbyAckStartGame exchange, all
     * subsequent packets are done using the Net* protocol.
     */
    [MessagePackObject]
    public class LobbyAckStartGame : LobbyPacket
    {
        
    }
}