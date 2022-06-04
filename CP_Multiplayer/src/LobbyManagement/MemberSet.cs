using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CPMod_Multiplayer.Serialization;

namespace CPMod_Multiplayer.LobbyManagement
{
    public class MemberSet : IEnumerable<LobbyMember>
    {
        const int MAX_PLAYERS = 7;
        
        public delegate void DelOnJoin(LobbyMember member);
        public delegate void DelOnPart(LobbyMember member);
        
        public event DelOnJoin OnJoin;
        public event DelOnPart OnPart;
        public LobbyMember Self => SelfIndex < 0 ? null : _members[SelfIndex];
        
        private readonly LobbyMember[] _members = new LobbyMember[MAX_PLAYERS];
        internal int SelfIndex { get; set; } = -1;

        internal bool TryJoin(Socket socket, out LobbyMember member)
        {
            for (int i = 0; i < _members.Length; i++)
            {
                if (_members[i] == null)
                {
                    member = new LobbyMember(socket, i);
                    _members[i] = member;
                    OnJoin?.Invoke(member);
                    return true;
                }
            }

            member = null;
            return false;
        }

        internal void SetMemberState(int index, LobbyMemberState state)
        {
            if (_members[index] == null)
            {
                _members[index] = new LobbyMember(null, index);
                _members[index].MemberState = state;
                OnJoin?.Invoke(_members[index]);
            }
            else
            {
                _members[index].MemberState = state;
            }
        }

        internal void Part(int index)
        {
            var member = _members[index];
            member.Close();
            OnPart?.Invoke(member);
            _members[index] = null;
        }

        internal void Part(LobbyMember member)
        {
            Part(member.MemberState.teamIndex);
        }

        public IEnumerator<LobbyMember> GetEnumerator()
        {
            return _members.Where(m => m != null).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}