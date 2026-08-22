using UnityEngine;

namespace JellyNet
{
    //게임 모드가 공통으로 지는 배선을 한 곳에 모은다
    //구독과 해제가 짝이 맞아야 하는데 모드마다 따로 적어두면 한쪽만 빠뜨리기 쉽다
    public abstract class NetGameMode<T> : MonoBehaviour where T : NetGameMode<T>
    {
        public static T Instance { get; private set; }

        protected abstract GameModeType Mode { get; }

        protected static NetManager Net
        {
            get { return NetManager.Instance; }
        }

        protected static bool IsHost
        {
            get { return NetManager.Instance != null && NetManager.Instance.IsHost; }
        }

        protected static bool IsOffline
        {
            get { return NetManager.Offline; }
        }

        //이 모드의 판이 지금 굴러가는 중인가
        protected bool IsPlaying
        {
            get { return LanGameFlow.IsPlaying(Mode); }
        }

        protected bool IsCurrentMode
        {
            get { return LanGameFlow.IsMode(Mode); }
        }

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = (T)this;
        }

        protected virtual void Start()
        {
            NetManager net = NetManager.Instance;

            if (net == null)
            {
                Debug.LogError("[" + GetType().Name + "] NetManager가 없습니다.");
                return;
            }

            net.OnDisconnected += ResetAll;

            RegisterRoutes();

            if (NetWorld.Instance != null)
            {
                NetWorld.Instance.OnSpawned += HandleSpawned;
                NetWorld.Instance.OnDespawned += HandleDespawned;
            }

            OnModeStart();
        }

        protected virtual void OnDestroy()
        {
            NetManager net = NetManager.Instance;

            if (net != null)
            {
                net.OnDisconnected -= ResetAll;

                UnregisterRoutes();
            }

            if (NetWorld.Instance != null)
            {
                NetWorld.Instance.OnSpawned -= HandleSpawned;
                NetWorld.Instance.OnDespawned -= HandleDespawned;
            }

            if (Instance == this)
                Instance = null;
        }

        protected virtual void OnModeStart()
        {
        }

        /// <summary>이 모드가 담당할 MsgType을 NetManager 라우팅 테이블에 등록한다.</summary>
        protected virtual void RegisterRoutes()
        {
        }

        /// <summary>등록한 만큼 정확히 풀어야 한다. 안 풀면 다음 판에서 중복 등록 에러가 난다.</summary>
        protected virtual void UnregisterRoutes()
        {
        }

        protected virtual void HandleSpawned(NetIdentity id)
        {
        }

        protected virtual void HandleDespawned(int netId)
        {
        }

        protected abstract void ResetAll();
    }
}
