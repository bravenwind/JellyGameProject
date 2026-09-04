// ─────────────────────────────────────────────────────────────────────
//  Photon Realtime 전송 — 아직 뼈대만 있다
// ─────────────────────────────────────────────────────────────────────
//
//  이 파일 전체가 PHOTON_UNITY_NETWORKING 안에 들어 있다. SDK가 없는 지금은
//  통째로 없는 파일과 같아서 컴파일에 아무 영향이 없다.
//
//  ★ TODO(사람) — 이 파일을 살리기 전에 해야 하는 일
//    1. Photon Realtime SDK 설치 (PUN2·Fusion 아님)
//    2. Photon 대시보드에서 App ID 발급
//    3. 스크립팅 정의 심볼에 PHOTON_UNITY_NETWORKING 추가.
//       PHOTON_UNITY_NETWORKING 은 원래 PUN2 가 정의하는 심볼이라,
//       Realtime 만 넣으면 자동으로 켜지지 않는다.
//       Player Settings → Other Settings → Scripting Define Symbols 에 손으로 넣거나,
//       심볼 이름을 이 프로젝트 것으로 바꾼다(그 경우 이 파일과 PhotonSession 둘 다).
//    4. 아래 API 이름을 설치한 SDK 버전에 맞춘다. 여기 적힌 건 Realtime 5.x 기준이고
//       검증하지 않았다 — 이 환경에는 SDK가 없어 컴파일해 본 적이 없다.

#if PHOTON_UNITY_NETWORKING

using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Realtime;

namespace JellyNet
{
    /// <summary>
    /// Photon Realtime 릴레이로 INetTransport 를 구현한다.
    ///
    /// LAN 과 다른 점은 셋뿐이다.
    ///   · 프레이밍을 안 한다  — 아래 '메시지 모양' 참고
    ///   · 호스트가 소켓의 주인이 아니라 방의 마스터 클라이언트다
    ///   · 번호가 우리 것이 아니라 Photon 의 ActorNumber 다
    /// 그 위의 라우팅·핸들러는 LanTransport 와 글자 그대로 같다.
    /// </summary>
    public class PhotonTransport : INetTransport
    {
        // ═══════════════════════════════════════════════════════
        //  메시지 모양 — [len4][type1][body] ↔ RaiseEvent(byte, byte[])
        // ═══════════════════════════════════════════════════════
        //
        //  LAN 은 TCP 라 경계가 없어서 우리가 직접 길이를 붙였다.
        //      NetWriter.Buffer = [len4][type1][body...]
        //      NetWriter.Length = 4 + 1 + body 길이
        //  Photon 은 이벤트 하나가 곧 메시지 하나라 경계를 릴레이가 지킨다.
        //  그래서 [len4] 는 빼고 보낸다 — 넣으면 4바이트를 매 메시지 낭비한다.
        //
        //  보낼 때:
        //      eventCode = w.Buffer[4]                  ← type1 이 그대로 이벤트 코드가 된다
        //      content   = w.Buffer[5 .. w.Length-1]    ← body 만
        //
        //  받을 때:
        //      type = (MsgType)eventData.Code
        //      reader.Reset(body, 0, body.Length)       ← body 의 맨 앞부터
        //
        //  ★ 여기서 반드시 주의할 것
        //    LAN 경로는 NetHost.HandleMessage 가 r.ReadMsgType() 으로 타입을 읽어
        //    리더를 한 바이트 밀어놓은 뒤 핸들러에 넘긴다. Photon 은 타입이 이벤트 코드로
        //    따로 오므로 ReadMsgType() 을 부르면 안 된다 — 부르면 body 첫 바이트를
        //    타입으로 먹고 그 뒤가 전부 한 칸씩 밀린다. 예외도 안 나고 값만 이상해진다.
        //    아래 Dispatch 는 그래서 코드로 타입을 정하고 리더는 body 앞에 세운다.

        //TODO(사람): w.Buffer 를 그대로 넘기면 Photon 이 배열 전체를 직렬화한다.
        //           길이에 맞춰 잘라야 한다. 매 메시지 새 배열을 만들면 쓰레기가 쌓이므로
        //           크기별 풀이나 ArraySegment 지원 여부를 SDK 문서에서 확인할 것.
        private static byte[] BodyOf(NetWriter w)
        {
            const int HEADER = 5;               // len4 + type1
            int bodyLen = w.Length - HEADER;

            byte[] body = new byte[bodyLen];
            Buffer.BlockCopy(w.Buffer, HEADER, body, 0, bodyLen);
            return body;
        }

        private static byte CodeOf(NetWriter w)
        {
            return w.Buffer[4];
        }

        // ═══════════════════════════════════════════════════════
        //  상태
        // ═══════════════════════════════════════════════════════

        //TODO(사람): LoadBalancingClient 를 만들고 App ID·지역을 넣는다.
        //           App ID 는 코드에 박지 말고 에셋(ScriptableObject)이나
        //           Photon 이 만들어 주는 설정 에셋에서 읽는다 —
        //           "세팅은 세팅이 있는 곳에".
        private LoadBalancingClient client;

        public Action<string> OnLog;
        public Action<string> OnError;

        public event Action<int> OnPeerJoined;
        public event Action<int> OnPeerLeft;
        public event Action OnHostStarted;
        public event Action OnDisconnected;
        public event Action OnConnectionLost;

        //★ 번호를 반드시 맞춰야 한다
        //  이 게임에서 OwnerId 는 "책임"이고 호스트는 언제나 1이다(NetHost.HOST_ID).
        //  Photon 의 ActorNumber 는 1부터 올라가지만 마스터가 1이라는 보장이 없다
        //  (마스터가 나가면 다음 사람이 마스터가 된다).
        //  TODO(사람): 둘 중 하나를 고를 것.
        //    ① 마스터 이양을 끄고(방 만들 때 옵션) ActorNumber 를 그대로 쓴다 — 간단하다
        //    ② ActorNumber ↔ 우리 번호의 대응표를 여기서 들고 번역한다 — 이양을 견딘다
        //  ①로 가면 마스터가 나간 순간 판이 끝나야 한다. LAN 도 지금 그렇게 동작하므로
        //  ①이 기존 동작과 같다.
        public int MyId
        {
            get { return client != null && client.LocalPlayer != null ? client.LocalPlayer.ActorNumber : 0; }
        }

        public bool IsHost
        {
            get { return client != null && client.LocalPlayer != null && client.LocalPlayer.IsMasterClient; }
        }

        public bool IsConnected
        {
            get { return client != null && client.InRoom; }
        }

        public int PeerCount
        {
            get { return client != null && client.CurrentRoom != null ? client.CurrentRoom.PlayerCount - 1 : 0; }
        }

        //Photon 은 방의 IsOpen 이 이 뜻이다. 방을 닫으면 새 사람이 못 들어온다
        public bool AcceptingNewPeers
        {
            get { return client != null && client.CurrentRoom != null && client.CurrentRoom.IsOpen; }
            set
            {
                if (client != null && client.CurrentRoom != null && IsHost)
                    client.CurrentRoom.IsOpen = value;
            }
        }

        // ═══════════════════════════════════════════════════════
        //  보내기
        // ═══════════════════════════════════════════════════════

        public void Broadcast(NetWriter w)
        {
            //TODO(사람): ReceiverGroup.Others + 호스트 자신은 로컬에서 이미 처리하므로
            //           되돌려 받지 않는다. LAN 의 Broadcast 도 자기 자신에겐 안 보낸다
            Raise(w, ReceiverGroup.Others, null);
        }

        public void BroadcastExcept(int exceptPeerId, NetWriter w)
        {
            //TODO(사람): Photon 에는 "한 명 빼고"가 없다. 방 인원에서 그 사람만 뺀
            //           TargetActors 배열을 만들어 넘긴다. 매번 배열을 만들지 않도록
            //           인원이 바뀔 때만 다시 짓는 것을 권한다
            throw new NotImplementedException("TODO(사람): TargetActors 로 구현");
        }

        public void SendTo(int peerId, NetWriter w)
        {
            Raise(w, ReceiverGroup.Others, new int[] { peerId });
        }

        public void SendToHost(NetWriter w)
        {
            Raise(w, ReceiverGroup.MasterClient, null);
        }

        private void Raise(NetWriter w, ReceiverGroup group, int[] targets)
        {
            if (client == null || !client.InRoom)
                return;

            //TODO(사람): SendOptions 를 메시지 종류별로 나눌 것.
            //           TransformUpdate 처럼 매 프레임 덮어써도 되는 건 SendUnreliable,
            //           스폰·탈락처럼 놓치면 안 되는 건 SendReliable.
            //           LAN 은 전부 TCP 라 신경 쓸 필요가 없었지만 여기선 이게 대역폭을 좌우한다.
            RaiseEventOptions options = new RaiseEventOptions
            {
                Receivers = group,
                TargetActors = targets
            };

            client.OpRaiseEvent(CodeOf(w), BodyOf(w), options, SendOptions.SendReliable);
        }

        // ═══════════════════════════════════════════════════════
        //  받기
        // ═══════════════════════════════════════════════════════

        //TODO(사람): client.EventReceived += OnEventReceived; 로 걸고,
        //           Shutdown 에서 반드시 푼다
        private void OnEventReceived(EventData e)
        {
            //Photon 내부 이벤트(코드 200 이상)는 우리 것이 아니다
            if (e.Code >= 200)
                return;

            byte[] body = e.CustomData as byte[];
            if (body == null)
                return;

            Dispatch((MsgType)e.Code, e.Sender, body);
        }

        private readonly NetReader reader = new NetReader();

        private void Dispatch(MsgType type, int senderId, byte[] body)
        {
            //타입은 이벤트 코드로 왔다. 여기서 ReadMsgType() 을 부르면 안 된다(위 설명 참고)
            reader.Reset(body, 0, body.Length);

            if (IsHost)
            {
                Action<int, NetReader> hostRoute;
                if (hostRoutes.TryGetValue(type, out hostRoute))
                {
                    hostRoute(senderId, reader);
                    return;
                }
            }
            else
            {
                Action<NetReader> clientRoute;
                if (clientRoutes.TryGetValue(type, out clientRoute))
                {
                    clientRoute(reader);
                    return;
                }
            }

            Log("처리되지 않은 메시지: " + type);
        }

        // ═══════════════════════════════════════════════════════
        //  라우팅 — LanTransport 와 같다
        // ═══════════════════════════════════════════════════════
        //
        //TODO(사람): 표를 다루는 코드가 LanTransport 와 글자 그대로 같다.
        //           둘 다 살아난 뒤에 공통 부모(NetRouteTable 같은 것)로 빼는 걸 권한다.
        //           지금 미리 빼두지 않은 이유는, 한쪽만 있는 상태에서 추상화를 하면
        //           실제로 무엇이 같은지 확인하지 못한 채 모양만 맞추게 되기 때문이다.

        private readonly Dictionary<MsgType, Action<int, NetReader>> hostRoutes
            = new Dictionary<MsgType, Action<int, NetReader>>();

        private readonly Dictionary<MsgType, Action<NetReader>> clientRoutes
            = new Dictionary<MsgType, Action<NetReader>>();

        public void RouteHost(MsgType type, Action<int, NetReader> handler)
        {
            if (handler == null)
                return;

            if (hostRoutes.ContainsKey(type))
            {
                LogError("호스트 메시지 " + type + " 의 주인이 이미 있습니다.");
                return;
            }

            hostRoutes[type] = handler;
        }

        public void RouteClient(MsgType type, Action<NetReader> handler)
        {
            if (handler == null)
                return;

            if (clientRoutes.ContainsKey(type))
            {
                LogError("클라 메시지 " + type + " 의 주인이 이미 있습니다.");
                return;
            }

            clientRoutes[type] = handler;
        }

        public void UnrouteHost(MsgType type) { hostRoutes.Remove(type); }

        public void UnrouteClient(MsgType type) { clientRoutes.Remove(type); }

        // ═══════════════════════════════════════════════════════
        //  수명
        // ═══════════════════════════════════════════════════════

        public void Poll()
        {
            //TODO(사람): Photon 은 우리가 직접 돌려야 한다. client.Service() 를 매 프레임.
            //           끊김 감지(OnDisconnected 콜백)도 여기서 OnConnectionLost 로 옮긴다
            if (client != null)
                client.Service();
        }

        public void Shutdown()
        {
            //TODO(사람): 방을 나가고(OpLeaveRoom) 연결을 끊은 뒤 OnDisconnected 를 쏜다.
            //           LanTransport 와 마찬가지로 라우팅 표는 남겨야 한다 —
            //           라우팅은 접속보다 먼저 걸리고 판이 끝나도 살아남아야 한다.
            //           EventReceived 구독도 여기서 푼다
            throw new NotImplementedException("TODO(사람)");
        }

        private void Log(string msg) { OnLog?.Invoke(msg); }

        private void LogError(string msg)
        {
            if (OnError != null)
                OnError(msg);
            else
                OnLog?.Invoke("[오류] " + msg);
        }
    }
}

#endif
