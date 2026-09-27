using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetManager : MonoBehaviour
    {
        public enum Mode { None, Host, Client }

        public static NetManager Instance { get; private set; }

        [SerializeField] private int port = NetConfig.DEFAULT_PORT;
        [SerializeField] private int maxLogLines = 200;

        [SerializeField] private bool persistAcrossScenes = true;

        private NetRouteTable routes;
        public NetEvents Events { get; private set; }

        private SocketTransport localTransport;
        private LocalSession localSession;

#if PHOTON_REALTIME_5_OR_NEWER
        private PhotonTransport photonTransport;
        private PhotonSession photonSession;
#endif

        private INetTransport transport;
        public INetSession Session { get; private set; }

        public bool IsOnline { get; private set; }

        private readonly List<string> log = new List<string>();

        public void UseLocalOrOnline(bool online)
        {
            if (IsOnline == online)
                return;

            if (transport != null && transport.IsConnected)
                return;

#if PHOTON_REALTIME_5_OR_NEWER
            IsOnline = online;
            transport = online ? photonTransport : localTransport;
            Session = online ? photonSession : localSession;
#else
            if (online)
                return;
#endif
        }

        public Mode CurrentMode
        {
            get
            {
                if (transport == null)
                    return Mode.None;

                if (transport.IsHost)
                    return Mode.Host;

                return transport.IsConnected ? Mode.Client : Mode.None;
            }
        }

        public int MyId { get { return transport != null ? transport.MyId : 0; } }

        public bool IsHost { get { return transport != null && transport.IsHost; } }

        public bool PrefersBatchedUpdates
        {
            get { return transport != null && transport.PrefersBatchedUpdates; }
        }

        public static bool Offline
        {
            get
            {
                NetManager net = Instance;
                return net == null || net.CurrentMode == Mode.None;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (persistAcrossScenes)
            {
                if (transform.parent != null)
                    transform.SetParent(null, true);
                DontDestroyOnLoad(gameObject);
            }

            Application.runInBackground = true;

            routes = new NetRouteTable();

            Events = new NetEvents();

            localTransport = new SocketTransport(routes, Events);

            localSession = new LocalSession(localTransport, Events, port);

            transport = localTransport;
            Session = localSession;

#if PHOTON_REALTIME_5_OR_NEWER
            photonTransport = new PhotonTransport(routes, Events);

            photonSession = new PhotonSession(photonTransport, Events);
#endif
        }

        private void Update()
        {
            localTransport?.Poll();
#if PHOTON_REALTIME_5_OR_NEWER
            photonTransport?.Poll();
#endif

            localSession?.Poll();
        }

        private void OnApplicationQuit() { CloseEverything(); }

        private void CloseEverything()
        {
            Shutdown();

#if PHOTON_REALTIME_5_OR_NEWER
            photonTransport?.DisconnectFully();
#endif
        }

        private void OnDestroy()
        {
            CloseEverything();

            localSession?.UnsubscribeFromEvents();
#if PHOTON_REALTIME_5_OR_NEWER
            photonSession?.UnsubscribeFromEvents();
#endif

            if (Instance == this)
                Instance = null;
        }

        public void Shutdown()
        {
            Events.RaiseShutdownRequested();
            transport?.Shutdown();
        }

        public void Broadcast(NetWriter w)
        {
            transport?.Broadcast(w);
        }

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            transport?.BroadcastExcept(exceptPeerId, w);
        }

        public void SendTo(int peerId, NetWriter w)
        {
            transport?.SendTo(peerId, w);
        }

        public void SendToHost(NetWriter w)
        {
            transport?.SendToHost(w);
        }

        public int PeerCount { get { return transport != null ? transport.PeerCount : 0; } }

        public bool AcceptingNewPeers
        {
            get { return transport != null && transport.AcceptingNewPeers; }
            set { if (transport != null) transport.AcceptingNewPeers = value; }
        }

        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            routes.RouteHost(type, handler);
        }

        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            routes.RouteClient(type, handler);
        }

        public void UnrouteHost(MsgType type)
        {
            routes.UnrouteHost(type);
        }

        public void UnrouteClient(MsgType type)
        {
            routes.UnrouteClient(type);
        }

        public void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > maxLogLines)
                log.RemoveAt(0);
            Debug.Log("[Net] " + line);
        }
    }
}
