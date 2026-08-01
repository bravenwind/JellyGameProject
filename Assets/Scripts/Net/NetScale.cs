using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 크기(스케일) 동기화. 흡수로 커지는 값을 다룬다.
    ///
    /// ★ 위치와 달리 크기는 Lerp가 적절하다.
    ///   위치는 '어느 경로로 갔는가'가 게임성에 직결되지만,
    ///   크기는 '결국 얼마가 되는가'만 맞으면 되고 과정은 부드럽기만 하면 된다.
    ///   그래서 스냅샷 기록 없이 목표값 하나로 충분하다.
    ///
    /// ★ 권위는 호스트에 있다.
    ///   클라는 스스로 커지지 않고, 호스트가 보내준 값을 따라간다.
    ///   (안 그러면 "내 화면에서만 큰 젤리" 같은 불일치가 생긴다)
    /// </summary>
    public class NetScale : MonoBehaviour
    {
        [Tooltip("커질 때 따라가는 속도. 클수록 빠릿하다.")]
        public float lerpSpeed = 8f;

        [Tooltip("이 배율을 프리팹 원래 크기에 곱한다.")]
        public float minScale = 0.5f;
        public float maxScale = 6f;

        /// <summary>현재 권위 배율(목표값). 호스트가 정하고 전원이 공유한다.</summary>
        public float Current { get; private set; }

        Vector3 _baseScale;     // 프리팹 원래 크기
        float _visual;          // 화면에 실제로 적용 중인 값(부드럽게 따라감)

        void Awake()
        {
            _baseScale = transform.localScale;
            Current = 1f;
            _visual = 1f;
        }

        /// <summary>즉시 반영(스폰 시). 보간 없이 바로 그 크기가 된다.</summary>
        public void SetImmediate(float scale)
        {
            Current = Mathf.Clamp(scale, minScale, maxScale);
            _visual = Current;
            Apply();
        }

        /// <summary>목표만 바꾸고 화면은 부드럽게 따라간다(흡수 시).</summary>
        public void SetTarget(float scale)
        {
            Current = Mathf.Clamp(scale, minScale, maxScale);
        }

        /// <summary>호스트 전용: 배율을 더하고 전원에게 방송한다.</summary>
        public void HostGrow(float amount)
        {
            NetManager net = NetManager.Instance;
            if (net == null || !net.IsHost) return;

            SetTarget(Current + amount);

            NetIdentity id = GetComponent<NetIdentity>();
            if (id != null && NetWorld.Instance != null)
                NetWorld.Instance.BroadcastScale(id.NetId, Current);
        }

        void Update()
        {
            if (Mathf.Abs(_visual - Current) < 0.001f) return;

            // 프레임률과 무관하게 같은 속도로 수렴 (NetTransform의 Lerp와 같은 공식)
            float t = 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime);
            _visual = Mathf.Lerp(_visual, Current, t);
            Apply();
        }

        void Apply()
        {
            transform.localScale = _baseScale * _visual;
        }
    }
}
