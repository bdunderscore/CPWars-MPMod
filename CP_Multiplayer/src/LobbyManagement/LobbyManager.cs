using IsoTools.Examples.Kenney;

namespace CPMod_Multiplayer.LobbyManagement
{
    /**
     * This singleton tracks the currently active lobby instance, if any.
     */
    public class LobbyManager
    {
        private static Lobby _lobby;
        public static Lobby CurrentLobby
        {
            get => _lobby;
            set
            {
                if (_lobby == value) return;
                if (_lobby != null) UnityEngine.Object.Destroy(_lobby.gameObject);
                _lobby = value;
                if (_lobby != null) _lobby.OnError += OnLobbyError;
            }
        }

        static void OnLobbyError(string msg)
        {
            ErrorWindow.Show(msg);
        }
    }
}