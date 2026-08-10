using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    public class NetTransform : MonoBehaviour
    {
        public enum Mode
        {
            None,
            Lerp,
            Snapshot
        }

        public static Mode CurrentMode = Mode.Snapshot;

        public static float InterpDelay = 0.1f;

        public static float LerpSpeed = 12f;

        struct Snap
        {
            public double Time;
            public Vector3 Pos;
            public float Yaw;
        }

        private NetIdentity id;
        private readonly List<Snap> snaps = new List<Snap>();
        private float sendTimer;

        private Vector3 targetPos;
        private float targetYaw;
        private bool hasTarget;

        private void Awake()
        {
            id = GetComponent<NetIdentity>();
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

        public void OnRemoteTransform(Vector3 pos, float yaw)
        {
            targetPos = pos;
            targetYaw = yaw;
            hasTarget = true;

            Snap s;
            s.Time = Time.timeAsDouble;
            s.Pos = pos;
            s.Yaw = yaw;
            snaps.Add(s);

            double cutoff = Time.timeAsDouble - (InterpDelay + 1.0);
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

            double renderTime = Time.timeAsDouble - InterpDelay;

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
