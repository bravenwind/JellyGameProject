using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetTransform : MonoBehaviour, INetPoolable
    {
        public enum Mode
        {
            None,
            Lerp,
            Snapshot
        }

        public static Mode CurrentMode = Mode.Snapshot;

        //송신 주기(20Hz = 50ms)의 3배. 2배(0.1s)로는 프레임이 한 번만 밀려도
        //보간할 다음 스냅샷이 없어 화면이 멈춘다. 지연이 조금 늘어도 끊기지 않는 쪽이 낫다
        public static float InterpDelay = 0.15f;

        public static float LerpSpeed = 12f;

        struct Snap
        {
            public double Time;
            public Vector3 Pos;
            public float Yaw;
        }

        //보낸 사람의 시계를 내 시계로 옮기는 기준점.
        //  내 시각 = timeBase + (보낸 시각 - senderBase)
        //두 기기의 절대 시각은 다르지만 '흐르는 속도'는 같으므로 차이만 쓰면 된다
        private double timeBase;
        private float senderBase;
        private bool hasBase;

        //기준점이 이만큼 어긋나면 다시 잡는다. 프레임 급락·긴 끊김·클럭 드리프트 대응
        private const double RESYNC_THRESHOLD = 0.5;

        private NetIdentity id;
        private readonly List<Snap> snaps = new List<Snap>();
        private float sendTimer;

        private Vector3 targetPos;
        private float targetYaw;
        private bool hasTarget;

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            ResetSync();
        }

        // ★ 젤리는 풀에서 재사용된다 — Awake가 다시 돌지 않는다
        //   지난 삶의 스냅샷과 시계 기준점이 그대로 남아 있으면, 새 자리에 놓인 젤리가
        //   옛 좌표 사이를 보간하며 이상한 속도로 미끄러지거나 순간이동한다.
        //   보간 시각을 송신 시각 기준으로 바꾸면서 이 잔재가 더 크게 드러났다.
        public void OnTakenFromPool()
        {
            ResetSync();
        }

        public void OnReturnedToPool()
        {
            ResetSync();
        }

        private void ResetSync()
        {
            snaps.Clear();
            hasBase = false;
            hasTarget = false;
            sendTimer = 0f;
            targetPos = transform.position;
            targetYaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (id == null)
                return;

            if (id.IsMine)
                SendIfDue();
            else
                FollowRemote();
        }

        private void SendIfDue()
        {
            sendTimer += Time.deltaTime;
            float interval = 1f / NetConfig.TRANSFORM_SEND_RATE;
            if (sendTimer < interval)
                return;

            sendTimer -= interval;

            if (NetWorld.Instance != null)
                NetWorld.Instance.SendMyTransform(id.NetId, transform.position, transform.eulerAngles.y);
        }

        public void OnRemoteTransform(Vector3 pos, float yaw, float sendTime)
        {
            targetPos = pos;
            targetYaw = yaw;
            hasTarget = true;

            double now = Time.unscaledTimeAsDouble;

            if (!hasBase)
            {
                hasBase = true;
                timeBase = now;
                senderBase = sendTime;
            }

            double t = timeBase + (sendTime - senderBase);

            //보낸 사람 쪽이 오래 멈췄거나 시계가 밀리면 기준을 다시 잡는다
            if (t < now - RESYNC_THRESHOLD || t > now + RESYNC_THRESHOLD)
            {
                timeBase = now;
                senderBase = sendTime;
                t = now;
            }

            //재구성한 시각이 '도착 시각보다 미래'면 기준점이 너무 이른 것이다.
            //  기준점은 첫 패킷 하나로 정해지는데, 하필 그게 유난히 빨리 온 패킷이면
            //  이후 모든 스냅샷이 실제보다 이르게 찍혀 버퍼가 계속 말라 있게 된다.
            //  그러면 renderTime이 최신 스냅샷을 앞질러 마지막 위치에 붙어 멈췄다가,
            //  다음 패킷이 오면 확 튄다 — 느렸다 빨라졌다 하는 정체가 이것이다.
            if (t > now)
            {
                timeBase -= (t - now);
                t = now;
            }

            //ApplySnapshot의 구간 탐색은 시간이 오름차순이라고 가정한다.
            //기준을 다시 잡은 직후 과거로 되돌아가면 그 가정이 깨진다
            if (snaps.Count > 0 && t <= snaps[snaps.Count - 1].Time)
                t = snaps[snaps.Count - 1].Time + 0.0001;

            Snap s;
            s.Time = t;
            s.Pos = pos;
            s.Yaw = yaw;
            snaps.Add(s);

            double cutoff = now - (InterpDelay + 1.0);
            while (snaps.Count > 2 && snaps[0].Time < cutoff)
                snaps.RemoveAt(0);
        }

        private void FollowRemote()
        {
            switch (CurrentMode)
            {
                case Mode.None: ApplyInstant(); break;
                case Mode.Lerp: ApplyLerp(); break;
                default: ApplySnapshot(); break;
            }
        }

        private void ApplyInstant()
        {
            if (!hasTarget)
                return;
            transform.position = targetPos;
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }

        private void ApplyLerp()
        {
            if (!hasTarget)
                return;

            float t = 1f - Mathf.Exp(-LerpSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);

            float yaw = Mathf.LerpAngle(transform.eulerAngles.y, targetYaw, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void ApplySnapshot()
        {
            if (snaps.Count == 0)
                return;

            //스냅샷 시각이 unscaled 기준으로 찍히므로 여기도 unscaled여야 한다
            double renderTime = Time.unscaledTimeAsDouble - InterpDelay;

            for (int i = 0; i < snaps.Count - 1; i++)
            {
                Snap a = snaps[i];
                Snap b = snaps[i + 1];

                if (a.Time <= renderTime && renderTime <= b.Time)
                {
                    double span = b.Time - a.Time;
                    float f = span > 0.0001 ? (float)((renderTime - a.Time) / span) : 1f;

                    transform.position = Vector3.Lerp(a.Pos, b.Pos, f);
                    transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(a.Yaw, b.Yaw, f), 0f);
                    return;
                }
            }   

            Snap last = snaps[snaps.Count - 1];
            if (renderTime > last.Time)
            {
                transform.position = last.Pos;
                transform.rotation = Quaternion.Euler(0f, last.Yaw, 0f);
            }
            else
            {
                Snap first = snaps[0];
                transform.position = first.Pos;
                transform.rotation = Quaternion.Euler(0f, first.Yaw, 0f);
            }
        }
    }
}
