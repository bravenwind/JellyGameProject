using UnityEngine;

namespace JellyNet
{
    [RequireComponent(typeof(NetManager))]
    public class NetTestUI : MonoBehaviour
    {
        private NetManager netManager;
        private string chatText = "";
        private Vector2 scroll;

        enum PendingAction { None, StartHost, Join, Shutdown, Ping, Diagnose }
        private PendingAction pending = PendingAction.None;
        private string pendingChat;

        private void Awake()
        {
            netManager = GetComponent<NetManager>();
        }

        private void Update()
        {
            PendingAction a = pending;
            pending = PendingAction.None;

            switch (a)
            {
                case PendingAction.StartHost: netManager.StartHost(); break;
                case PendingAction.Join: netManager.JoinHost(); break;
                case PendingAction.Shutdown: netManager.Shutdown(); break;
                case PendingAction.Ping: netManager.SendPing(); break;
                case PendingAction.Diagnose: LanDiagnostics.Dump(); break;
            }

            if (pendingChat != null)
            {
                netManager.SendChat(pendingChat);
                pendingChat = null;
            }
        }

        private void OnGUI()
        {
            const int W = 420;
            GUILayout.BeginArea(new Rect(10, 10, W, Screen.height - 20), GUI.skin.box);

            GUILayout.Label("<b>LAN 소켓 테스트 (B-2)</b>", RichLabel());
            GUILayout.Space(4);

            string state;
            if (netManager.CurrentMode == NetManager.Mode.Host)
                state = "호스트 (P" + NetHost.HOST_ID + ") — 접속자 " + netManager.Host.PeerCount + "명";
            else if (netManager.CurrentMode == NetManager.Mode.Client)
                state = netManager.Client.Connected
                    ? "참가자 (P" + netManager.Client.MyId + ") — 연결됨"
                    : "참가자 — 연결 끊김";
            else
                state = "대기 중";

            GUILayout.Label("상태: " + state);
            GUILayout.Space(6);

            if (netManager.CurrentMode == NetManager.Mode.None)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("포트", GUILayout.Width(36));
                string portStr = GUILayout.TextField(netManager.port.ToString(), GUILayout.Width(60));
                int p;
                if (int.TryParse(portStr, out p))
                    netManager.port = p;
                GUILayout.EndHorizontal();

                if (GUILayout.Button("호스트 시작", GUILayout.Height(28)))
                    pending = PendingAction.StartHost;

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("호스트 IP", GUILayout.Width(60));
                netManager.joinIp = GUILayout.TextField(netManager.joinIp);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("참가", GUILayout.Height(28)))
                    pending = PendingAction.Join;

                GUILayout.Space(2);
                GUILayout.Label("같은 PC에서 테스트하면 127.0.0.1", MiniLabel());
            }
            else
            {
                if (GUILayout.Button("연결 종료"))
                    pending = PendingAction.Shutdown;

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("핑 보내기", GUILayout.Width(100)))
                    pending = PendingAction.Ping;

                if (netManager.CurrentMode == NetManager.Mode.Client)
                    GUILayout.Label("최근 RTT: " + netManager.Client.LastRttMs.ToString("F2") + " ms");
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                chatText = GUILayout.TextField(chatText);
                if (GUILayout.Button("전송", GUILayout.Width(60)) && chatText.Length > 0)
                {
                    pendingChat = chatText;
                    chatText = "";
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>원격 캐릭터 보간</b>", RichLabel());
            GUILayout.Label("시뮬레이션 지연을 켜고 바꿔보면 차이가 확실히 보인다.", MiniLabel());

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(NetTransform.CurrentMode == NetTransform.Mode.None, " 없음", GUILayout.Width(70)))
                NetTransform.CurrentMode = NetTransform.Mode.None;
            if (GUILayout.Toggle(NetTransform.CurrentMode == NetTransform.Mode.Lerp, " Lerp", GUILayout.Width(70)))
                NetTransform.CurrentMode = NetTransform.Mode.Lerp;
            if (GUILayout.Toggle(NetTransform.CurrentMode == NetTransform.Mode.Snapshot, " 스냅샷", GUILayout.Width(80)))
                NetTransform.CurrentMode = NetTransform.Mode.Snapshot;
            GUILayout.EndHorizontal();

            if (NetTransform.CurrentMode == NetTransform.Mode.Snapshot)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("지연재생 " + (NetTransform.InterpDelay * 1000f).ToString("F0") + "ms", GUILayout.Width(110));
                NetTransform.InterpDelay = GUILayout.HorizontalSlider(NetTransform.InterpDelay, 0.02f, 0.4f);
                GUILayout.EndHorizontal();
            }

            if (NetWorld.Instance != null)
            {
                string jellyInfo = AbsorbMode.Instance != null
                    ? "  젤리 " + AbsorbMode.Instance.JellyCount + "개" : "";
                GUILayout.Label("오브젝트 " + NetWorld.Instance.Objects.Count + "개" + jellyInfo
                    + "  (내 번호 P" + netManager.MyId + ")", MiniLabel());

                foreach (var kv in NetWorld.Instance.Objects)
                {
                    NetIdentity id = kv.Value;
                    if (id == null || id.PrefabId >= NetConfig.JELLY_PREFAB_START)
                        continue;

                    NetScale sc = id.GetComponent<NetScale>();
                    string size = sc != null ? ("  크기 " + sc.Current.ToString("F2")) : "";
                    string mark = id.IsMine ? "★내것" : "  남의것";

                    GUILayout.Label("  net" + id.NetId + "  P" + id.OwnerId + "  " + mark + size, MiniLabel());
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>네트워크 시뮬레이션</b>", RichLabel());
            GUILayout.Label("원격 환경을 흉내낸다. 값은 편도 기준 — 양쪽에 켜면 RTT는 약 2배.", MiniLabel());

            NetSim.Enabled = GUILayout.Toggle(NetSim.Enabled, " 켜기  (" + NetSim.Describe() + ")");

            if (NetSim.Enabled)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("지연 " + NetSim.LatencyMs.ToString("F0") + "ms", GUILayout.Width(90));
                NetSim.LatencyMs = GUILayout.HorizontalSlider(NetSim.LatencyMs, 0f, 300f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("지터 ±" + NetSim.JitterMs.ToString("F0") + "ms", GUILayout.Width(90));
                NetSim.JitterMs = GUILayout.HorizontalSlider(NetSim.JitterMs, 0f, 80f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("손실 " + NetSim.LossPercent.ToString("F1") + "%", GUILayout.Width(90));
                NetSim.LossPercent = GUILayout.HorizontalSlider(NetSim.LossPercent, 0f, 10f);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("로컬"))
                NetSim.PresetLocal();
            if (GUILayout.Button("와이파이"))
                NetSim.PresetWifi();
            if (GUILayout.Button("원격"))
                NetSim.PresetRemote();
            if (GUILayout.Button("최악"))
                NetSim.PresetBad();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("진단 (콘솔에 상태 전부 출력)  [F1]"))
                pending = PendingAction.Diagnose;

            FramedConnection.Trace = GUILayout.Toggle(FramedConnection.Trace, " 수신 추적 로그(디버그)");

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("로그");
            if (GUILayout.Button("지우기", GUILayout.Width(60)))
                netManager.ClearLog();
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, GUI.skin.box);
            var lines = netManager.LogLines;
            for (int i = 0; i < lines.Count; i++)
                GUILayout.Label(lines[i], MiniLabel());
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private GUIStyle rich, mini;

        private GUIStyle RichLabel()
        {
            if (rich == null)
            {
                rich = new GUIStyle(GUI.skin.label);
                rich.richText = true;
                rich.fontSize = 14;
            }
            return rich;
        }

        private GUIStyle MiniLabel()
        {
            if (mini == null)
            {
                mini = new GUIStyle(GUI.skin.label);
                mini.fontSize = 11;
                mini.wordWrap = true;
            }
            return mini;
        }
    }
}
