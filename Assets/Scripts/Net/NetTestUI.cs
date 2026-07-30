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

        void Awake()
        {
            _net = GetComponent<NetManager>();
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
                    _net.StartHost();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("호스트 IP", GUILayout.Width(60));
                _net.joinIp = GUILayout.TextField(_net.joinIp);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("참가", GUILayout.Height(28)))
                    _net.JoinHost();

                GUILayout.Space(2);
                GUILayout.Label("같은 PC에서 테스트하면 127.0.0.1", MiniLabel());
            }
            else
            {
                if (GUILayout.Button("연결 종료"))
                    _net.Shutdown();

                GUILayout.Space(6);

                // ── 핑 ──
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("핑 보내기", GUILayout.Width(100)))
                    _net.SendPing();

                if (_net.CurrentMode == NetManager.Mode.Client)
                    GUILayout.Label("최근 RTT: " + _net.Client.LastRttMs.ToString("F2") + " ms");
                GUILayout.EndHorizontal();

                // ── 채팅 ──
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                _chatText = GUILayout.TextField(_chatText);
                if (GUILayout.Button("전송", GUILayout.Width(60)) && _chatText.Length > 0)
                {
                    _net.SendChat(_chatText);
                    _chatText = "";
                }
                GUILayout.EndHorizontal();
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
