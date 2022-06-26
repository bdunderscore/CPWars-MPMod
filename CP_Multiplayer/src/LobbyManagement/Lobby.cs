using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    public enum LobbyState
    {
        JOINING,
        NOT_READY,
        READY,
        ERROR
    }
    
    public abstract class Lobby : MonoBehaviour
    {
        protected const int LOBBY_PORT = 0;

        public abstract bool IsHost { get; }

        public abstract LobbyState State { get; protected set; }

        public String LobbyAddress { get; protected set; }
        public virtual int MyTeamIndex => Members.SelfIndex + 1;
        public virtual Socket HostSocket => null;

        public delegate void DelegateOnError(string msg);
        public delegate void DelegateOnStateChange();
        
        public event DelegateOnError OnError;
        public event DelegateOnStateChange OnStateChange;

        protected delegate void DeferredEvent();

        protected readonly Queue<DeferredEvent> _eventQueue = new Queue<DeferredEvent>();

        public MemberSet Members { get; internal set; }= new MemberSet();

        protected void Awake()
        {
        }
        
        protected void RaiseError(string msg)
        {
            Mod.logger.Error("[Lobby:RaiseError]" + msg);
            OnError?.Invoke(msg);
            Destroy(gameObject);
        }

        protected void RaiseStateChange()
        {
            Mod.logger.Log("[Lobby:RaiseStateChange]");
            OnStateChange?.Invoke();
        }

        protected virtual void Update()
        {
            while (_eventQueue.Count > 0) _eventQueue.Dequeue()();
        }

        public virtual void StartGame()
        {
        }

        public virtual void OnGameOver()
        {
            
        }
    }
}