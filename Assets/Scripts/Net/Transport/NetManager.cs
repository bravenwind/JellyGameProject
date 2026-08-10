using System;
using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetManager : MonoBehaviour
    {
        public enum Mode { None, Host, Client }

        public static NetManager Instance { get; private set; }

        [Header("설정")]
        public int port = NetConfig.DEFAULT_PORT;
        public string joinIp = "127.0.0.1";
        public int maxLogLines = 200;

        public Mode CurrentMode { get; private set; }
        public NetHost Host { get; private set; }
        public NetClient Client { get; private set; }

        public int MyId
        {
            get
            {
                if (CurrentMode == Mode.Host)
                    return NetHost.HOST_ID;
                if (CurrentMode == Mode.Client && Client != null)
                    return Client.MyId;
                return 0;
            }
        }

        public bool IsHost { get { return CurrentMode == Mode.Host; } }

        private readonly List<string> log = new List<string>();
        public IReadOnlyList<string> LogLines { get { return log; } }

        public event Action OnHostStarted;
        public event Action<NetHost.Peer> OnPeerJoined;
        public event Action<NetHost.Peer> OnPeerLeft;
        public event Action<NetHost.Peer, MsgType, NetReader> OnHostMessage;
        public event Action<MsgType, NetReader> OnClientMessage;
        public event Action OnDisconnected;

        [Header("씬 전환")]
        [Tooltip("씬이 바뀌어도 연결을 유지한다. Main 씬에서 접속해 게임 씬으로 넘어가려면 켜야 한다.")]
        public bool persistAcrossScenes = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Net] NetManager가 둘입니다. '" + name + "' 쪽을 걷어냅니다. "
                    + "(살아남는 쪽: '" + Instance.name + "')");
                Destroy(this);
                return;
            }
            Instance = this;

            if (persistAcrossScenes)
            {
                if (transform.parent != null)
                {
                    Debug.LogWarning("[Net] NetManager('" + name + "')가 다른 오브젝트의 자식입니다. "
                        + "씬을 넘어 살아남으려면 루트에 있어야 해서 부모에서 떼어냅니다.");
                    transform.SetParent(null, true);
                }
                DontDestroyOnLoad(gameObject);
            }

            Application.runInBackground = true;
        }

        private void Update()
        {
            if (Host != null)
                Host.Poll();
            if (Client != null)
                Client.Poll();
        }

        private void OnApplicationQuit() { Shutdown(); }

        private void OnDestroy()
        {
            Shutdown();
            if (Instance == this)
                Instance = null;
        }

        public void StartHost()
        {
            Shutdown();

            Host = new NetHost();
            Host.OnLog = AddLog;
            Host.OnPeerJoined = RaisePeerJoined;
            Host.OnPeerLeft = RaisePeerLeft;
            Host.OnMessage = RaiseHostMessage;

            if (!Host.Start(port))
            {
                Host = null;
                return;
            }

            CurrentMode = Mode.Host;
            AddLog("== 호스트 모드 ==  내 번호: P" + NetHost.HOST_ID);

            foreach (string ip in NetUtil.GetLocalIPv4List())
                AddLog("  다른 기기에서 접속: " + ip + ":" + port);

            if (OnHostStarted != null)
                OnHostStarted();
        }

        public void JoinHost()
        {
            Shutdown();

            Client = new NetClient();
            Client.OnLog = AddLog;
            Client.OnMessage = RaiseClientMessage;

            if (!Client.Connect(joinIp, port))
            {
                Client = null;
                return;
            }

            CurrentMode = Mode.Client;
            AddLog("== 참가 모드 ==");
        }

        public void Shutdown()
        {
            bool wasConnected = (Host != null || Client != null);

            if (Host != null)
            {
                Host.Stop();
                Host = null;
            }
            if (Client != null)
            {
                Client.Disconnect();
                Client = null;
            }
            CurrentMode = Mode.None;

            if (wasConnected && OnDisconnected != null)
                OnDisconnected();
        }

        private void RaisePeerJoined(NetHost.Peer peer)
        {
            OnPeerJoined?.Invoke(peer);
        }

        private void RaisePeerLeft(NetHost.Peer peer)
        {
            OnPeerLeft?.Invoke(peer);
        }

        private void RaiseHostMessage(NetHost.Peer peer, MsgType type, NetReader reader)
        {
            OnHostMessage?.Invoke(peer, type, reader);
        }

        private void RaiseClientMessage(MsgType type, NetReader reader)
        {
            OnClientMessage?.Invoke(type, reader);
        }

        public void SendPing()
        {
            if (CurrentMode == Mode.Client)
                Client.SendPing();
            else
                AddLog("핑은 참가자 쪽에서 호스트로 보냅니다.");
        }

        public void SendChat(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (CurrentMode == Mode.Client)
            {
                Client.SendChat(text);
            }
            else if (CurrentMode == Mode.Host)
            {
                AddLog("P" + NetHost.HOST_ID + ": " + text);
                Host.SendChat(text);
            }
        }

        public void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > maxLogLines)
                log.RemoveAt(0);
            Debug.Log("[Net] " + line);
        }

        public void ClearLog() { log.Clear(); }
    }
}
