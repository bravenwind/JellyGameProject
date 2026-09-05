using System;
using System.Collections.Generic;

namespace JellyNet
{
    /// <summary>
    /// "이 MsgType 은 누가 처리하는가" 표. 전송이 무엇이든 하나만 있다.
    ///
    /// ★ 왜 이벤트 브로드캐스트가 아니라 표인가
    ///   멀티캐스트 이벤트는 구독자 전원이 같은 메시지를 순서대로 받는다. 문제가 셋 있었다.
    ///     1. MsgType 하나를 추가하면 NetWorld·AbsorbMode·LanGameFlow 중 어디 switch 에
    ///        넣을지 매번 골라야 하고, 아무 데도 안 넣어도 조용하다.
    ///     2. 두 구독자가 같은 타입을 읽으면 NetReader 를 공유하므로 두 번째는 위치가
    ///        밀린 채 쓰레기를 읽는다. 예외도 안 난다.
    ///     3. 어떤 타입을 누가 담당하는지 코드 어디에도 안 적혀 있다.
    ///   타입당 주인을 하나로 못 박으면 셋 다 사라진다. 중복 등록은 그 자리에서 에러로
    ///   잡히고, 주인 없는 타입은 로그에 남는다.
    ///
    /// ★ 왜 전송 안에 두지 않는가 — 실제로 이것 때문에 온라인이 통째로 안 됐다
    ///   전송마다 표를 하나씩 들고 있었다. 등록은 접속보다 훨씬 먼저 일어나는데
    ///   (로비가 Start 에서 LoadGameScene 을 건다) 그때 활성 전송은 LAN 이라,
    ///   온라인을 골라 방에 들어가면 <b>메시지는 오는데 표가 비어 있었다</b>.
    ///   로그가 "처리되지 않은 메시지: SpawnEntity" 로 도배됐다.
    ///   표는 전송을 갈아끼워도 그대로여야 한다 — 무엇으로 실어 나르든
    ///   "이 타입은 누가 맡는가"는 같은 답이기 때문이다.
    /// </summary>
    public class NetRouteTable
    {
        /// <summary>중복 등록 같은 잘못을 알린다. NetManager 가 꽂아준다.</summary>
        public Action<string> OnError;

        /// <summary>주인 없는 메시지가 왔다는 기록. 조용히 버리면 원인을 못 찾는다.</summary>
        public Action<string> OnLog;

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
                Error("호스트 메시지 " + type + " 의 주인이 이미 있습니다. "
                    + "한 타입은 한 곳에서만 처리해야 합니다.");
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
                Error("클라 메시지 " + type + " 의 주인이 이미 있습니다. "
                    + "한 타입은 한 곳에서만 처리해야 합니다.");
                return;
            }

            clientRoutes[type] = handler;
        }

        //씬을 나갈 때 반드시 풀어야 한다. 안 그러면 파괴된 오브젝트의 메서드가 남아
        //다음 판에서 "주인이 이미 있습니다" 에러가 뜬다
        public void UnrouteHost(MsgType type) { hostRoutes.Remove(type); }

        public void UnrouteClient(MsgType type) { clientRoutes.Remove(type); }

        public void DispatchHost(int peerId, MsgType type, NetReader reader)
        {
            Action<int, NetReader> route;
            if (hostRoutes.TryGetValue(type, out route))
            {
                route(peerId, reader);
                return;
            }

            Log("처리되지 않은 호스트 메시지: " + type);
        }

        public void DispatchClient(MsgType type, NetReader reader)
        {
            Action<NetReader> route;
            if (clientRoutes.TryGetValue(type, out route))
            {
                route(reader);
                return;
            }

            Log("처리되지 않은 클라 메시지: " + type);
        }

        private void Log(string msg) { OnLog?.Invoke(msg); }

        private void Error(string msg) { OnError?.Invoke(msg); }
    }
}
