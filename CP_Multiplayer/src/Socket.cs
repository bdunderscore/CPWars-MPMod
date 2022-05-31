using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MessagePack;
using Steamworks;
using Unity.Collections.LowLevel.Unsafe;

namespace CPMod_Multiplayer
{
    public class Socket : IDisposable
    {
        private HSteamNetConnection _connection;
        private bool _fault = false;
        private bool _disposed;
        private Queue<byte[]> _sendQueue = new Queue<byte[]>();

        public bool ErrorState => _fault;
        public HSteamNetConnection Handle => _connection;

        public Socket(HSteamNetConnection connection)
        {
            _connection = connection;
        }

        ~Socket()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;

            SteamNetworkingSockets.CloseConnection(_connection, 0, "Disposed", false);
            _disposed = true;
            _fault = true;
        }

        public void Send(byte[] data)
        {
            if (!TrySend(data))
            {
                _sendQueue.Enqueue(data);
            }
        }

        private bool TrySend(byte[] data)
        {
            EResult result;
            
            if (_fault)
            {
                return true;
            }
            
            unsafe
            {
                fixed (byte* p = data)
                {
                    result = SteamNetworkingSockets.SendMessageToConnection(_connection, (IntPtr)p, (uint)data.Length,
                        Steamworks.Constants.k_nSteamNetworkingSend_Reliable, out _);
                }
            }
            
            if (result == EResult.k_EResultOK)
            {
                return true;
            }
            
            if (result == EResult.k_EResultLimitExceeded)
            {
                Mod.logger.Log("Enqueue packet");
                return false;
            }
            
            Mod.logger.Warning($"Socket send error: {result}");
            _fault = true;
            return true;
        }
        
        /**
         * Flushes all untransmitted data out to the steam networking backend. Returns true if no more data needs
         * sending.
         */
        public bool Flush()
        {
            while (_sendQueue.Count > 0)
            {
                if (_fault) return true;

                var toSend = _sendQueue.Peek();

                if (TrySend(toSend))
                {
                    _sendQueue.Dequeue();
                }
                else
                {
                    return false;
                }
            }

            SteamNetworkingSockets.FlushMessagesOnConnection(_connection);

            return true;
        }

        public bool TryReceive(out byte[] pkt)
        {
            unsafe
            {
                IntPtr[] msgs = new IntPtr[1];

                int nMsgs = SteamNetworkingSockets.ReceiveMessagesOnConnection(_connection, msgs, 1);

                if (nMsgs > 0)
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(msgs[0]);

                    try
                    {
                        pkt = new byte[msg.m_cbSize];
                        Marshal.Copy(msg.m_pData, pkt, 0, msg.m_cbSize);
                        return true;
                    }
                    finally
                    {
                        SteamNetworkingMessage_t.Release(msgs[0]);
                    }
                }
            }

            pkt = Array.Empty<byte>();
            return false;
        }
    }
}