using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace JellyNet
{
    public class NetSpawnPool
    {
        private const int DEFAULT_CAPACITY = 16;
        private const int MAX_SIZE = 128;

        private readonly Transform parent;
        private readonly GameObject[] prefabs;
        private readonly Dictionary<int, ObjectPool<GameObject>> pools = new();

        private readonly Dictionary<GameObject, int> idOfPrefab = new();
        private readonly bool[] poolable;

        private Vector3 spawnPosition;

        public NetSpawnPool(GameObject[] prefabs, Transform parent)
        {
            this.prefabs = prefabs;
            this.parent = parent;

            poolable = new bool[prefabs != null ? prefabs.Length : 0];

            for (int i = 0; i < poolable.Length; i++)
                poolable[i] = IsPoolable(i);
        }

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

            idOfPrefab[go] = prefabId;

            foreach (INetPoolable hook in go.GetComponentsInChildren<INetPoolable>(true))
                hook.OnTakenFromPool();

            return go;
        }

        public void Release(GameObject go)
        {
            if (go == null)
                return;

            if (!idOfPrefab.TryGetValue(go, out int prefabId))
            {
                Object.Destroy(go);
                return;
            }

            idOfPrefab.Remove(go);

            foreach (INetPoolable hook in go.GetComponentsInChildren<INetPoolable>(true))
                hook.OnReturnedToPool();

            GetPool(prefabId).Release(go);
        }

        public void Clear()
        {
            foreach (KeyValuePair<int, ObjectPool<GameObject>> pair in pools)
                pair.Value.Clear();

            pools.Clear();
            idOfPrefab.Clear();
        }

        private ObjectPool<GameObject> GetPool(int prefabId)
        {
            if (pools.TryGetValue(prefabId, out ObjectPool<GameObject> pool))
                return pool;

            pool = new ObjectPool<GameObject>(
                createFunc: () => Object.Instantiate(prefabs[prefabId], spawnPosition, Quaternion.identity, parent),
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
