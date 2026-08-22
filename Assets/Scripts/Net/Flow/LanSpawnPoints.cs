using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace JellyNet
{
    public class LanSpawnPoints : MonoBehaviour
    {
        public static LanSpawnPoints Instance { get; private set; }

        [Header("스폰 포인트")]
        [Tooltip("직접 지정. 비워두면 태그로 자동 탐색한다.")]
        public Transform[] spawnPoints;

        [Tooltip("자동 탐색에 쓸 태그")]
        public string spawnPointTag = "SpawnPoint";

        [Header("가상 포인트 (슬롯이 부족할 때)")]
        [Tooltip("최소 이만큼의 슬롯을 확보한다")]
        public int minSlots = 8;
        [Tooltip("가상 포인트를 만들 때 시드에서 떨어뜨릴 거리")]
        public float virtualRadius = 8f;

        private readonly List<Vector3> slots = new List<Vector3>();
        private bool prepared;
        private int nextSlot;

        public int SlotCount { get { return slots.Count; } }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            Prepare();
        }

        public void Prepare()
        {
            if (prepared)
                return;
            prepared = true;
            slots.Clear();

            if (spawnPoints != null)
                foreach (Transform t in spawnPoints)
                    if (t != null)
                        slots.Add(t.position);

            if (slots.Count == 0 && !string.IsNullOrEmpty(spawnPointTag))
            {
                GameObject[] tagged = GameObject.FindGameObjectsWithTag(spawnPointTag);
                foreach (GameObject g in tagged)
                    if (g != null)
                        slots.Add(g.transform.position);

                if (slots.Count > 0)
                    Debug.Log("[LanSpawn] 태그 '" + spawnPointTag + "'로 " + slots.Count + "개 발견");
            }

            if (slots.Count == 0)
            {
                NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
                if (tri.vertices != null && tri.vertices.Length > 0)
                {
                    slots.Add(tri.vertices[Random.Range(0, tri.vertices.Length)]);
                    Debug.LogWarning("[LanSpawn] 스폰포인트가 없어 NavMesh 정점을 시드로 씁니다.");
                }
                else
                {
                    slots.Add(Vector3.zero);
                    Debug.LogError("[LanSpawn] 스폰포인트도 NavMesh도 없습니다 — 원점에 스폰합니다.");
                }
            }

            int seedCount = slots.Count;
            while (slots.Count < minSlots)
            {
                Vector3 seed = slots[slots.Count % seedCount];
                float angle = slots.Count * 137.5f * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * virtualRadius;
                slots.Add(Project(seed + offset));
            }

            Debug.Log("[LanSpawn] 슬롯 " + slots.Count + "개 준비 (실제 " + seedCount + " + 가상 "
                      + (slots.Count - seedCount) + ")");
        }

        private static Vector3 Project(Vector3 p)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(p, out hit, 10f, NavMesh.AllAreas))
                return hit.position;
            return p;
        }

        public Vector3 Take()
        {
            Prepare();
            if (slots.Count == 0)
                return Vector3.zero;
            Vector3 p = slots[nextSlot % slots.Count];
            nextSlot++;
            return p;
        }

        public Vector3 Random_()
        {
            Prepare();
            if (slots.Count == 0)
                return Vector3.zero;
            return slots[Random.Range(0, slots.Count)];
        }

        public void ResetAssignment()
        {
            nextSlot = 0;
        }
    }
}
