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
            get
            {
                NetManager net = NetManager.Instance;
                return net == null || net.CurrentMode == NetManager.Mode.None;
            }
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

            net.OnHostMessage += HandleHostMessage;
            net.OnClientMessage += HandleClientMessage;
            net.OnDisconnected += ResetAll;

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
                net.OnHostMessage -= HandleHostMessage;
                net.OnClientMessage -= HandleClientMessage;
                net.OnDisconnected -= ResetAll;
            }

            if (NetWorld.Instance != null)
            {
                NetWorld.Instance.OnSpawned -= HandleSpawned;
                NetWorld.Instance.OnDespawned -= HandleDespawned;
            }

            //씬이 바뀌어도 남아 있으면 다음 판이 죽은 인스턴스를 붙잡는다
            if (Instance == this)
                Instance = null;
        }

        protected virtual void OnModeStart()
        {
        }

        protected virtual void HandleHostMessage(NetHost.Peer from, MsgType type, NetReader r)
        {
        }

        protected virtual void HandleClientMessage(MsgType type, NetReader r)
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
