using System.Text;
using UnityEngine;

namespace JellyNet
{
    public class LanDiagnostics : MonoBehaviour
    {
        [SerializeField] private KeyCode dumpKey = KeyCode.F1;

        private void Update()
        {
            if (Input.GetKeyDown(dumpKey))
                Dump();
        }

        public static void Dump()
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine("════════ LAN 진단 ════════");

            NetManager net = NetManager.Instance;
            if (net == null)
            {
                s.AppendLine("★ NetManager 없음");
                Debug.Log(s.ToString());
                return;
            }

            s.AppendLine("[연결]");
            s.AppendLine("  모드: " + net.CurrentMode + "   내 번호: P" + net.MyId + "   호스트: " + net.IsHost);
            if (net.IsHost)
                s.AppendLine("  접속자: " + net.PeerCount + "명");

            s.AppendLine();
            s.AppendLine("[게임 흐름]");
            LanGameFlow flow = LanGameFlow.Instance;
            if (flow == null)
                s.AppendLine("  ★ LanGameFlow 없음");
            else
            {
                s.AppendLine("  단계: " + flow.Phase + "   모드: " + LanGameFlow.Mode
                             + "   남은시간: " + flow.Remaining.ToString("F1"));
                s.AppendLine("  최소인원: " + flow.MinPlayersToStart);
            }
            s.AppendLine("  GameState.Phase: " + GameState.Phase);
            s.AppendLine("  PlayerMovement.InputLocked: " + PlayerMovement.InputLocked
                         + (PlayerMovement.InputLocked ? "   ★ 잠겨 있어 움직일 수 없음" : ""));
            s.AppendLine("  PlayerMovement.Local: " + (PlayerMovement.Local != null
                         ? PlayerMovement.Local.gameObject.name : "★ 없음 (MarkAsLocal 미호출)"));

            s.AppendLine();
            s.AppendLine("[스폰 포인트]");
            if (LanSpawnPoints.Instance == null)
                s.AppendLine("  ★ LanSpawnPoints 없음 → 원형 배치 폴백");
            else
                s.AppendLine("  슬롯: " + LanSpawnPoints.Instance.SlotCount + "개");

            s.AppendLine();
            s.AppendLine("[프리팹 등록표]");
            if (NetWorld.Instance == null)
            {
                s.AppendLine("  ★ NetWorld 없음");
                Debug.Log(s.ToString());
                return;
            }

            GameObject[] pf = NetWorld.Instance.prefabs;
            if (pf == null || pf.Length == 0)
                s.AppendLine("  ★ 비어 있음");
            else
            {
                s.AppendLine("  [0] " + (pf[0] != null ? pf[0].name : "★null") + "  (플레이어여야 함)");
                s.AppendLine("  총 " + pf.Length + "개");
            }

            s.AppendLine();
            s.AppendLine("[네트워크 오브젝트] " + NetWorld.Instance.Objects.Count + "개");

            foreach (var kv in NetWorld.Instance.Objects)
            {
                NetIdentity id = kv.Value;
                if (id == null)
                {
                    s.AppendLine("  net" + kv.Key + " ★ 파괴됨");
                    continue;
                }
                if (id.PrefabId >= NetConfig.JELLY_PREFAB_START)
                    continue;

                Renderer[] rends = id.GetComponentsInChildren<Renderer>(true);
                int visible = 0;
                foreach (Renderer r in rends)
                {
                    if (r.enabled && r.gameObject.activeInHierarchy)
                        visible++;
                }

                PlayerMovement pm = id.GetComponentInChildren<PlayerMovement>(true);
                CharacterController cc = id.GetComponent<CharacterController>();
                LanPlayerSetup setup = id.GetComponent<LanPlayerSetup>();

                s.AppendLine("  net" + id.NetId + "  소유P" + id.OwnerId
                             + (id.IsMine ? "  ★내것" : "  남의것"));
                s.AppendLine("      활성: " + id.gameObject.activeInHierarchy
                             + "   위치: " + Fmt(id.transform.position)
                             );
                s.AppendLine("      렌더러: " + visible + "/" + rends.Length + " 보임"
                             + (visible == 0 ? "   ★ 화면에 안 그려짐" : ""));
                s.AppendLine("      PlayerMovement: " + (pm == null ? "★없음"
                             : (pm.enabled ? "켜짐" : "꺼짐"))
                             + "   CharacterController: " + (cc == null ? "없음" : (cc.enabled ? "켜짐" : "꺼짐"))
                             + "   LanPlayerSetup: " + (setup == null ? "★없음" : "있음"));

                LanPlayerVisual vis = id.Visual;
                if (vis == null)
                    s.AppendLine("      ★ LanPlayerVisual 없음 — 크기·색·애니메이션이 동기화되지 않음");
                else
                {
                    PlayerScaleController psc = id.GetComponentInChildren<PlayerScaleController>(true);
                    PlayerColorVisual pcv = id.GetComponentInChildren<PlayerColorVisual>(true);
                    Animator anim = vis.Anim;
                    s.AppendLine("      LanPlayerVisual 있음  ScaleCtrl:" + (psc == null ? "★없음" : psc.CurrentScaleValue.ToString("F2"))
                                 + "  ColorVisual:" + (pcv == null ? "★없음" : "ok")
                                 + "  Animator:" + (anim == null ? "★없음" : "ok"));
                }
            }

            s.AppendLine();
            s.AppendLine("[카메라]");
            if (Camera.main == null)
                s.AppendLine("  ★ MainCamera 태그가 붙은 카메라가 없음");
            else
                s.AppendLine("  Camera.main = " + Camera.main.name + "  위치 " + Fmt(Camera.main.transform.position));

            TopDownCameraFollow f = FindFirstObjectByType<TopDownCameraFollow>(FindObjectsInactive.Include);
            s.AppendLine("  TopDownCameraFollow: " + (f == null ? "★씬에 없음"
                         : f.gameObject.name + " → 타겟 " + (f.Target == null ? "★없음" : f.Target.name)));

            MainCamera_Action ma = FindFirstObjectByType<MainCamera_Action>(FindObjectsInactive.Include);
            s.AppendLine("  MainCamera_Action:   " + (ma == null ? "★씬에 없음"
                         : ma.gameObject.name + " → 타겟 " + (ma.Target == null ? "★없음" : ma.Target.name)));

            AudioListener[] als = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int onCount = 0;
            foreach (AudioListener a in als)
                if (a.enabled && a.gameObject.activeInHierarchy)
                    onCount++;

            s.AppendLine("  AudioListener: 켜짐 " + onCount + " / 전체 " + als.Length
                         + (onCount > 1 ? "   ★ 켜진 게 2개 이상이라 경고가 뜬다" : ""));
            foreach (AudioListener a in als)
                s.AppendLine("      · " + a.gameObject.name + " : " + (a.enabled ? "켜짐" : "꺼짐"));

            s.AppendLine("══════════════════════════");
            Debug.Log(s.ToString());
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")";
        }
    }
}
