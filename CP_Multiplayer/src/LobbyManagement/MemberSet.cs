using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CPMod_Multiplayer.Serialization;

namespace CPMod_Multiplayer.LobbyManagement
{
    public class MemberSet : IEnumerable<LobbyMember>
    {
        public const int MAX_PLAYERS = 7;
        
        public delegate void DelOnJoin(LobbyMember member);
        public delegate void DelOnPart(LobbyMember member);

        public delegate void DelRenumber(int from, int to);
        
        public event DelOnJoin OnJoin;
        public event DelOnPart OnPart;
        public event DelRenumber OnRenumber;
        
        public LobbyMember Self => SelfIndex < 0 ? null : _members[SelfIndex];
        
        private readonly LobbyMember[] _members = new LobbyMember[MAX_PLAYERS];
        internal int SelfIndex { get; set; } = -1;

        internal bool TryJoin(Socket socket, out LobbyMember member)
        {
            return TryJoin(socket, out member, false);
        }
        
        internal bool TryJoin(Socket socket, out LobbyMember member, bool isSelf)
        {
            for (int i = _members.Length - 1; i >= 0; i--)
            {
                if (_members[i] == null)
                {
                    if (isSelf) SelfIndex = i;
                    
                    member = new LobbyMember(socket, i);
                    _members[i] = member;
                    OnJoin?.Invoke(member);
                    return true;
                }
            }

            member = null;
            return false;
        }

        internal void Defragment()
        {
            int firstEmpty = 0;
            for (int i = 0; i < _members.Length; i++)
            {
                if (_members[i] != null)
                {
                    if (firstEmpty != i)
                    {
                        Renumber(i, firstEmpty);
                    }
                    firstEmpty++;
                }
            }
        }

        internal void Renumber(int from, int to)
        {
            if (from < _members.Length && to < _members.Length && _members[to] == null)
            {
                _members[to] = _members[from];
                _members[from] = null;

                if (from == SelfIndex) SelfIndex = to;

                if (_members[to] != null)
                {
                    _members[to].MemberState.teamIndex = to;
                    OnRenumber?.Invoke(from, to);
                    _members[to].RaiseOnChange();
                }
            }
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
            member.Remove();
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