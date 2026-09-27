using UnityEngine;

namespace JellyNet
{
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
                return;

            net.Events.OnDisconnected += ResetAll;

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
                net.Events.OnDisconnected -= ResetAll;

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

        protected virtual void RegisterRoutes()
        {
        }

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
