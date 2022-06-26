using CPMod_Multiplayer.Serialization;

namespace CPMod_Multiplayer.LobbyManagement
{
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

        private Socket _socket;

        public Socket Socket
        {
            get { return _socket;  }
            set
            {
                if (_socket == value) return;
                _socket?.Dispose();
                _socket = value;
            }
        }

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
                RaiseOnChange();
            }
        }

        public delegate void OnChangeDelegate(LobbyMember lobbyMember);

        public event OnChangeDelegate OnChange;

        public void Remove()
        {
            Disconnected = true;
            Ready = false;
            Socket?.Dispose();
        }

        public void RaiseOnChange()
        {
            OnChange?.Invoke(this);
        }
    }
}