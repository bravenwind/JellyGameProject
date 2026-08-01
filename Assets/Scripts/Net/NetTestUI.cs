using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 2단계 검증용 임시 UI. Canvas 없이 OnGUI로 그리므로 씬 세팅이 필요 없다.
    ///
    /// 사용법:
    ///   1) 빈 GameObject를 만들고 NetManager + NetTestUI 를 붙인다
    ///   2) 한쪽은 [호스트 시작], 다른 쪽은 IP 입력 후 [참가]
    ///   3) 핑/채팅으로 왕복 확인
    ///
    /// 3단계로 가면 이 UI는 실제 로비 UI로 대체된다.
    /// </summary>
    [RequireComponent(typeof(NetManager))]
    public class NetTestUI : MonoBehaviour
    {
        NetManager _net;
        string _chatText = "";
        Vector2 _scroll;

        // ★ OnGUI는 한 프레임에 여러 번 호출된다(Layout / Repaint / 입력 이벤트).
        //   그 안에서 연결을 맺거나 끊으면 UI 구조가 도중에 바뀌어 IMGUI가 꼬이고,
        //   같은 동작이 여러 번 실행될 수 있다.
        //   그래서 버튼은 '요청'만 기록하고, 실제 실행은 Update()에서 한 번만 한다.
        enum PendingAction { None, StartHost, Join, Shutdown, Ping, Diagnose }
        PendingAction _pending = PendingAction.None;
        string _pendingChat;

        void Awake()
        {
            _net = GetComponent<NetManager>();
        }

        void Update()
        {
            PendingAction a = _pending;
            _pending = PendingAction.None;

            switch (a)
            {
                case PendingAction.StartHost: _net.StartHost(); break;
                case PendingAction.Join: _net.JoinHost(); break;
                case PendingAction.Shutdown: _net.Shutdown(); break;
                case PendingAction.Ping: _net.SendPing(); break;
                case PendingAction.Diagnose: LanDiagnostics.Dump(); break;
            }

            if (_pendingChat != null)
            {
                _net.SendChat(_pendingChat);
                _pendingChat = null;
            }
        }

        void OnGUI()
        {
            const int W = 420;
            GUILayout.BeginArea(new Rect(10, 10, W, Screen.height - 20), GUI.skin.box);

            GUILayout.Label("<b>LAN 소켓 테스트 (B-2)</b>", RichLabel());
            GUILayout.Space(4);

            // ── 상태 ──
            string state;
            if (_net.CurrentMode == NetManager.Mode.Host)
                state = "호스트 (P" + NetHost.HostId + ") — 접속자 " + _net.Host.PeerCount + "명";
            else if (_net.CurrentMode == NetManager.Mode.Client)
                state = _net.Client.Connected
                    ? "참가자 (P" + _net.Client.MyId + ") — 연결됨"
                    : "참가자 — 연결 끊김";
            else
                state = "대기 중";

            GUILayout.Label("상태: " + state);
            GUILayout.Space(6);

            // ── 연결 조작 ──
            if (_net.CurrentMode == NetManager.Mode.None)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("포트", GUILayout.Width(36));
                string portStr = GUILayout.TextField(_net.port.ToString(), GUILayout.Width(60));
                int p;
                if (int.TryParse(portStr, out p)) _net.port = p;
                GUILayout.EndHorizontal();

                if (GUILayout.Button("호스트 시작", GUILayout.Height(28)))
                    _pending = PendingAction.StartHost;

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("호스트 IP", GUILayout.Width(60));
                _net.joinIp = GUILayout.TextField(_net.joinIp);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("참가", GUILayout.Height(28)))
                    _pending = PendingAction.Join;

                GUILayout.Space(2);
                GUILayout.Label("같은 PC에서 테스트하면 127.0.0.1", MiniLabel());
            }
            else
            {
                if (GUILayout.Button("연결 종료"))
                    _pending = PendingAction.Shutdown;

                GUILayout.Space(6);

                // ── 핑 ──
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("핑 보내기", GUILayout.Width(100)))
                    _pending = PendingAction.Ping;

                if (_net.CurrentMode == NetManager.Mode.Client)
                    GUILayout.Label("최근 RTT: " + _net.Client.LastRttMs.ToString("F2") + " ms");
                GUILayout.EndHorizontal();

                // ── 채팅 ──
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                _chatText = GUILayout.TextField(_chatText);
                if (GUILayout.Button("전송", GUILayout.Width(60)) && _chatText.Length > 0)
                {
                    _pendingChat = _chatText;   // 실제 전송은 Update()에서
                    _chatText = "";
                }
                GUILayout.EndHorizontal();
            }

            // ── 보간 모드 (B-3) ──
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

            // ── 진단: 내가 알고 있는 네트워크 오브젝트 목록 ──
            if (NetWorld.Instance != null)
            {
                string jellyInfo = AbsorbMode.Instance != null
                    ? "  젤리 " + AbsorbMode.Instance.JellyCount + "개" : "";
                GUILayout.Label("오브젝트 " + NetWorld.Instance.Objects.Count + "개" + jellyInfo
                    + "  (내 번호 P" + _net.MyId + ")", MiniLabel());

                // 플레이어만 나열 (젤리는 수십 개라 목록에서 뺀다)
                foreach (var kv in NetWorld.Instance.Objects)
                {
                    NetIdentity id = kv.Value;
                    if (id == null || id.PrefabId >= NetConfig.JellyPrefabStart) continue;

                    NetScale sc = id.GetComponent<NetScale>();
                    string size = sc != null ? ("  크기 " + sc.Current.ToString("F2")) : "";
                    string mark = id.IsMine ? "★내것" : "  남의것";

                    GUILayout.Label("  net" + id.NetId + "  P" + id.OwnerId + "  " + mark + size, MiniLabel());
                }
            }

            // ── 네트워크 시뮬레이션 ──
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
            if (GUILayout.Button("로컬")) NetSim.PresetLocal();
            if (GUILayout.Button("와이파이")) NetSim.PresetWifi();
            if (GUILayout.Button("원격")) NetSim.PresetRemote();
            if (GUILayout.Button("최악")) NetSim.PresetBad();
            GUILayout.EndHorizontal();

            // ── 디버그 ──
            GUILayout.Space(6);
            if (GUILayout.Button("진단 (콘솔에 상태 전부 출력)  [F1]"))
                _pending = PendingAction.Diagnose;

            FramedConnection.Trace = GUILayout.Toggle(FramedConnection.Trace, " 수신 추적 로그(디버그)");

            // ── 로그 ──
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("로그");
            if (GUILayout.Button("지우기", GUILayout.Width(60))) _net.ClearLog();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUI.skin.box);
            var lines = _net.LogLines;
            for (int i = 0; i < lines.Count; i++)
                GUILayout.Label(lines[i], MiniLabel());
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        // OnGUI에서 매 프레임 new GUIStyle을 만들면 GC가 늘어나므로 캐시해둔다
        GUIStyle _rich, _mini;

        GUIStyle RichLabel()
        {
            if (_rich == null)
            {
                _rich = new GUIStyle(GUI.skin.label);
                _rich.richText = true;
                _rich.fontSize = 14;
            }
            return _rich;
        }

        GUIStyle MiniLabel()
        {
            if (_mini == null)
            {
                _mini = new GUIStyle(GUI.skin.label);
                _mini.fontSize = 11;
                _mini.wordWrap = true;
            }
            return _mini;
        }
    }
}
