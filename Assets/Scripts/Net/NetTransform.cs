using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 위치·회전 동기화. Photon의 PhotonTransformView 자리를 대신한다.
    ///
    ///   소유자(IsMine)  : 일정 주기로 자기 위치를 보낸다
    ///   나머지          : 받은 위치를 '부드럽게' 따라간다  ← 보간
    ///
    /// ★ 보간이 왜 필요한가
    ///   위치는 초당 20번만 온다(50ms 간격). 그런데 화면은 초당 60번 그려진다.
    ///   받은 위치를 그대로 꽂으면 캐릭터가 50ms마다 순간이동하듯 뚝뚝 끊긴다.
    ///   그래서 두 위치 사이를 '메워서' 그려야 한다. 그게 보간이다.
    ///
    ///   로컬 테스트(지연 0.1ms)에서는 이 문제가 거의 안 보이지만,
    ///   실제 와이파이(20~60ms)에서는 바로 티가 난다.
    ///   NetSim으로 지연을 걸고 아래 모드를 바꿔가며 비교해 보라.
    /// </summary>
    public class NetTransform : MonoBehaviour
    {
        public enum Mode
        {
            None,       // 보간 없음 — 받은 즉시 순간이동 (문제를 보기 위한 비교용)
            Lerp,       // 목표 위치로 매 프레임 조금씩 접근 (간단하지만 항상 뒤처짐)
            Snapshot    // 스냅샷 보간 — 과거 시점을 재생 (게임에서 쓰는 정석)
        }

        /// <summary>전역 설정. UI에서 실시간으로 바꿔가며 비교한다.</summary>
        public static Mode CurrentMode = Mode.Snapshot;

        /// <summary>
        /// 스냅샷 보간에서 '얼마나 과거를 재생할지'(초).
        /// 전송 간격(50ms)의 2배 정도가 안전하다. 크면 부드럽지만 반응이 늦다.
        /// </summary>
        public static float InterpDelay = 0.1f;

        /// <summary>Lerp 모드의 따라가는 속도(클수록 빠름).</summary>
        public static float LerpSpeed = 12f;

        struct Snap
        {
            public double Time;   // 도착 시각
            public Vector3 Pos;
            public float Yaw;
        }

        NetIdentity _id;
        readonly List<Snap> _snaps = new List<Snap>();   // 최근 위치 기록(시간순)
        float _sendTimer;

        // Lerp / None 모드가 쓰는 목표값
        Vector3 _targetPos;
        float _targetYaw;
        bool _hasTarget;

        void Awake()
        {
            _id = GetComponent<NetIdentity>();
            _targetPos = transform.position;
            _targetYaw = transform.eulerAngles.y;
        }

        void Update()
        {
            if (_id == null) return;

            if (_id.IsMine) SendIfDue();   // 내 것 → 보내기
            else FollowRemote();           // 남의 것 → 따라가기
        }

        // ─────────────────────────────────────────────
        //  소유자: 주기적으로 전송
        // ─────────────────────────────────────────────
        void SendIfDue()
        {
            _sendTimer += Time.deltaTime;
            float interval = 1f / NetConfig.TransformSendRate;   // 20Hz → 0.05초
            if (_sendTimer < interval) return;

            _sendTimer -= interval;

            if (NetWorld.Instance != null)
                NetWorld.Instance.SendMyTransform(_id.NetId, transform.position, transform.eulerAngles.y);
        }

        // ─────────────────────────────────────────────
        //  원격에서 위치가 도착했을 때 (NetWorld가 호출)
        // ─────────────────────────────────────────────
        public void OnRemoteTransform(Vector3 pos, float yaw)
        {
            _targetPos = pos;
            _targetYaw = yaw;
            _hasTarget = true;

            Snap s;
            s.Time = Time.timeAsDouble;
            s.Pos = pos;
            s.Yaw = yaw;
            _snaps.Add(s);

            // 너무 오래된 기록은 버린다(메모리 + 탐색 비용)
            double cutoff = Time.timeAsDouble - (InterpDelay + 1.0);
            while (_snaps.Count > 2 && _snaps[0].Time < cutoff)
                _snaps.RemoveAt(0);
        }

        // ─────────────────────────────────────────────
        //  원격 오브젝트를 따라가는 세 가지 방법
        // ─────────────────────────────────────────────
        void FollowRemote()
        {
            switch (CurrentMode)
            {
                case Mode.None: ApplyInstant(); break;
                case Mode.Lerp: ApplyLerp(); break;
                default: ApplySnapshot(); break;
            }
        }

        /// <summary>① 보간 없음 — 받은 위치를 그대로. 50ms마다 순간이동한다.</summary>
        void ApplyInstant()
        {
            if (!_hasTarget) return;
            transform.position = _targetPos;
            transform.rotation = Quaternion.Euler(0f, _targetYaw, 0f);
        }

        /// <summary>
        /// ② 단순 Lerp — 매 프레임 목표 쪽으로 일정 비율만큼 다가간다.
        /// 부드럽지만 목표에 '완전히' 도달하지 못해 항상 조금 뒤처지고,
        /// 상대가 갑자기 방향을 바꾸면 곡선을 그리며 미끄러진다.
        /// </summary>
        void ApplyLerp()
        {
            if (!_hasTarget) return;

            // 1 - e^(-k·dt) : 프레임률이 달라도 같은 속도로 수렴하게 하는 표준 방식
            float t = 1f - Mathf.Exp(-LerpSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPos, t);

            float yaw = Mathf.LerpAngle(transform.eulerAngles.y, _targetYaw, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// ③ 스냅샷 보간 — 실제 게임이 쓰는 방식.
        ///
        /// 핵심 발상: <b>일부러 조금 과거(InterpDelay)를 재생한다.</b>
        /// 그러면 재생 시점 앞뒤의 위치를 이미 둘 다 받아둔 상태이므로,
        /// 그 사이를 정확히 채워 그릴 수 있다(추측이 아니라 사실 사이를 잇는 것).
        ///
        /// 대가는 InterpDelay만큼 화면이 늦다는 것. 100ms면 게임에서 충분히 감당된다.
        /// </summary>
        void ApplySnapshot()
        {
            if (_snaps.Count == 0) return;

            double renderTime = Time.timeAsDouble - InterpDelay;

            // 재생 시점을 사이에 두는 두 스냅샷을 찾는다
            for (int i = 0; i < _snaps.Count - 1; i++)
            {
                Snap a = _snaps[i];
                Snap b = _snaps[i + 1];

                if (a.Time <= renderTime && renderTime <= b.Time)
                {
                    double span = b.Time - a.Time;
                    float f = span > 0.0001 ? (float)((renderTime - a.Time) / span) : 1f;

                    transform.position = Vector3.Lerp(a.Pos, b.Pos, f);
                    transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(a.Yaw, b.Yaw, f), 0f);
                    return;
                }
            }

            // 여기 오면 두 경우다:
            //  · 아직 스냅샷이 부족(막 접속함) → 가장 오래된 위치로
            //  · 새 데이터가 안 옴(끊김/정지) → 마지막 위치에 머무름
            Snap last = _snaps[_snaps.Count - 1];
            if (renderTime > last.Time)
            {
                transform.position = last.Pos;
                transform.rotation = Quaternion.Euler(0f, last.Yaw, 0f);
            }
            else
            {
                Snap first = _snaps[0];
                transform.position = first.Pos;
                transform.rotation = Quaternion.Euler(0f, first.Yaw, 0f);
            }
        }
    }
}
