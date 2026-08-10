using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace JellyNet
{
    //프리팹 인덱스별 오브젝트 풀
    //젤리는 초당 여러 번 생성·파괴되므로 재사용한다
    //플레이어·봇은 판당 몇 번뿐이고 상태가 복잡해 풀링하지 않는다
    public class NetSpawnPool
    {
        private const int DEFAULT_CAPACITY = 16;
        private const int MAX_SIZE = 128;

        private readonly Transform parent;
        private readonly GameObject[] prefabs;
        private readonly Dictionary<int, ObjectPool<GameObject>> pools = new();
        private readonly Dictionary<GameObject, int> ownerPrefab = new();
        private readonly bool[] poolable;

        //NavMeshAgent는 켜지는 순간의 자리에서 NavMesh를 찾는다
        //활성화 후에 옮기면 이미 늦어서 스폰 위치를 먼저 넘겨야 한다
        private Vector3 spawnPosition;

        public NetSpawnPool(GameObject[] prefabs, Transform parent)
        {
            this.prefabs = prefabs;
            this.parent = parent;

            poolable = new bool[prefabs != null ? prefabs.Length : 0];

            for (int i = 0; i < poolable.Length; i++)
                poolable[i] = IsPoolable(i);
        }

        //플레이어·봇은 컴포넌트가 많고 재사용 시 되돌릴 상태가 복잡해 제외한다
        private bool IsPoolable(int prefabId)
        {
            if (prefabId < NetConfig.JELLY_PREFAB_START)
                return false;

            GameObject prefab = prefabs[prefabId];

            if (prefab == null)
                return false;

            if (prefab.GetComponentInChildren<PlayerMovement>(true) != null)
                return false;

            if (prefab.GetComponentInChildren<AIPlayerMovement>(true) != null)
                return false;

            return true;
        }

        public bool CanPool(int prefabId)
        {
            return prefabId >= 0 && prefabId < poolable.Length && poolable[prefabId];
        }

        public GameObject Get(int prefabId, Vector3 position)
        {
            if (!CanPool(prefabId))
                return Object.Instantiate(prefabs[prefabId], position, Quaternion.identity);

            spawnPosition = position;

            GameObject go = GetPool(prefabId).Get();

            ownerPrefab[go] = prefabId;

            foreach (INetPoolable hook in go.GetComponentsInChildren<INetPoolable>(true))
                hook.OnTakenFromPool();

            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null)
                return;

            if (!ownerPrefab.TryGetValue(go, out int prefabId))
            {
                Object.Destroy(go);
                return;
            }

            ownerPrefab.Remove(go);

            foreach (INetPoolable hook in go.GetComponentsInChildren<INetPoolable>(true))
                hook.OnReturnedToPool();

            GetPool(prefabId).Release(go);
        }

        public void Clear()
        {
            foreach (KeyValuePair<int, ObjectPool<GameObject>> pair in pools)
                pair.Value.Clear();

            pools.Clear();
            ownerPrefab.Clear();
        }

        private ObjectPool<GameObject> GetPool(int prefabId)
        {
            if (pools.TryGetValue(prefabId, out ObjectPool<GameObject> pool))
                return pool;

            int id = prefabId;

            pool = new ObjectPool<GameObject>(
                createFunc: () => Object.Instantiate(prefabs[id], spawnPosition, Quaternion.identity, parent),
                actionOnGet: go =>
                {
                    go.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
                    go.SetActive(true);
                },
                actionOnRelease: go => go.SetActive(false),
                actionOnDestroy: Object.Destroy,
                collectionCheck: true,
                defaultCapacity: DEFAULT_CAPACITY,
                maxSize: MAX_SIZE);

            pools[prefabId] = pool;
            return pool;
        }
    }
}
