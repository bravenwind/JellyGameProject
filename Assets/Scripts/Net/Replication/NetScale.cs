using UnityEngine;

namespace JellyNet
{
    public class NetScale : MonoBehaviour
    {
        [SerializeField] private float lerpSpeed = 8f;
        [SerializeField] private float minScale = 0.5f;
        [SerializeField] private float maxScale = 6f;

        private Vector3 baseScale;
        private float visualScale;

        public float Current { get; private set; }

        private void Awake()
        {
            baseScale = transform.localScale;
            Current = 1f;
            visualScale = 1f;
        }

        public void SetImmediate(float scale)
        {
            Current = Mathf.Clamp(scale, minScale, maxScale);
            visualScale = Current;
            Apply();
        }

        public void SetTarget(float scale)
        {
            Current = Mathf.Clamp(scale, minScale, maxScale);
        }

        public void HostGrow(float amount)
        {
            NetManager net = NetManager.Instance;

            if (net == null || !net.IsHost)
                return;

            SetTarget(Current + amount);

            NetIdentity identity = GetComponent<NetIdentity>();

            if (identity != null && NetWorld.Instance != null)
                NetWorld.Instance.BroadcastScale(identity.NetId, Current);
        }

        private void Update()
        {
            if (Mathf.Abs(visualScale - Current) < 0.001f)
                return;

            float t = 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime);

            visualScale = Mathf.Lerp(visualScale, Current, t);
            Apply();
        }

        private void Apply()
        {
            transform.localScale = baseScale * visualScale;
        }
    }
}
