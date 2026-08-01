using System.Collections.Generic;
using UnityEngine;

namespace JellyNet
{
    /// <summary>
    /// 스폰 위치 관리. 원본 NetworkManager.PrepareSpawnSlots / GetValidSpawnPoints를 옮긴 것.
    ///
    /// ★ 원본과 같은 규칙을 지킨다
    ///   ① 인스펙터에 직접 지정한 스폰포인트가 있으면 그것
    ///   ② 없으면 "SpawnPoint" 태그로 씬에서 자동 탐색
    ///   ③ 그것도 없으면 NavMesh 정점을 시드로 폴백
    ///   ④ 인원보다 슬롯이 부족하면 시드 주변에 가상 포인트를 만들어 채운다
    ///
    /// ★ 원본과 다른 점
    ///   원본은 각 클라가 자기 플레이어를 Instantiate하며 슬롯을 골랐지만,
    ///   소켓판은 호스트가 전부 스폰하므로 <b>호스트만</b> 슬롯을 분배한다.
    ///   (그래야 두 사람이 같은 슬롯에 겹치는 일이 없다)
    /// </summary>
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

        readonly List<Vector3> _slots = new List<Vector3>();
        bool _prepared;
        int _nextSlot;

        public int SlotCount { get { return _slots.Count; } }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        // 클라도 슬롯을 알아야 할 때가 있다(부활 위치 표시 등). 씬 시작 시 한 번 준비한다.
        void Start()
        {
            Prepare();
        }

        // ─────────────────────────────────────────────
        public void Prepare()
        {
            if (_prepared) return;
            _prepared = true;
            _slots.Clear();

            // ① 인스펙터 지정
            if (spawnPoints != null)
                foreach (Transform t in spawnPoints)
                    if (t != null) _slots.Add(t.position);

            // ② 태그 자동 탐색
            if (_slots.Count == 0 && !string.IsNullOrEmpty(spawnPointTag))
            {
                GameObject[] tagged = GameObject.FindGameObjectsWithTag(spawnPointTag);
                foreach (GameObject g in tagged)
                    if (g != null) _slots.Add(g.transform.position);

                if (_slots.Count > 0)
                    Debug.Log("[LanSpawn] 태그 '" + spawnPointTag + "'로 " + _slots.Count + "개 발견");
            }

            // ③ NavMesh 폴백
            if (_slots.Count == 0)
            {
                UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
                if (tri.vertices != null && tri.vertices.Length > 0)
                {
                    _slots.Add(tri.vertices[Random.Range(0, tri.vertices.Length)]);
                    Debug.LogWarning("[LanSpawn] 스폰포인트가 없어 NavMesh 정점을 시드로 씁니다.");
                }
                else
                {
                    _slots.Add(Vector3.zero);
                    Debug.LogError("[LanSpawn] 스폰포인트도 NavMesh도 없습니다 — 원점에 스폰합니다.");
                }
            }

            // ④ 부족하면 가상 포인트로 채우기
            int seedCount = _slots.Count;
            while (_slots.Count < minSlots)
            {
                Vector3 seed = _slots[_slots.Count % seedCount];
                float angle = _slots.Count * 137.5f * Mathf.Deg2Rad;   // 황금각 — 고르게 흩어진다
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * virtualRadius;
                _slots.Add(Project(seed + offset));
            }

            Debug.Log("[LanSpawn] 슬롯 " + _slots.Count + "개 준비 (실제 " + seedCount + " + 가상 "
                      + (_slots.Count - seedCount) + ")");
        }

        /// <summary>가상 포인트를 NavMesh 위로 끌어당긴다(허공/벽 속 스폰 방지).</summary>
        static Vector3 Project(Vector3 p)
        {
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                return hit.position;
            return p;
        }

        /// <summary>다음 슬롯을 하나 가져간다(호스트 전용).</summary>
        public Vector3 Take()
        {
            Prepare();
            if (_slots.Count == 0) return Vector3.zero;   // % 0 은 예외를 던진다
            Vector3 p = _slots[_nextSlot % _slots.Count];
            _nextSlot++;
            return p;
        }

        /// <summary>부활 등에서 무작위 슬롯이 필요할 때.</summary>
        public Vector3 Random_()
        {
            Prepare();
            if (_slots.Count == 0) return Vector3.zero;
            return _slots[Random.Range(0, _slots.Count)];
        }

        /// <summary>새 판을 시작할 때 슬롯 분배를 처음부터.</summary>
        public void ResetAssignment()
        {
            _nextSlot = 0;
        }
    }
}
