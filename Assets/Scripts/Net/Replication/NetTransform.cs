using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetTransform : MonoBehaviour, INetPoolable
    {
        struct Snap
        {
            public double Time;
            public Vector3 Pos;
            public float Yaw;
        }

        public static float InterpDelay = 0.15f;

        private const double RESYNC_THRESHOLD = 0.5;

        private double timeBase;
        private float senderBase;
        private bool hasBase;

        private NetIdentity id;
        private readonly List<Snap> snaps = new List<Snap>();
        private float sendTimer;

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
            ResetSync();
        }

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
            sendTimer = 0f;
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
            double now = Time.unscaledTimeAsDouble;

            if (!hasBase)
            {
                hasBase = true;
                timeBase = now;
                senderBase = sendTime;
            }

            double t = timeBase + (sendTime - senderBase);

            if (t < now - RESYNC_THRESHOLD || t > now + RESYNC_THRESHOLD)
            {
                timeBase = now;
                senderBase = sendTime;
                t = now;
            }

            if (t > now)
            {
                timeBase -= (t - now);
                t = now;
            }

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
            if (snaps.Count == 0)
                return;

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
