using UnityEngine;
using Photon.Pun;

public class PuddingWiggle : MonoBehaviourPun
{
    public Animator puddingAnimator;

    private void OnTriggerEnter(Collider other)
    {
        // 1. 충돌한 오브젝트가 로컬 플레이어인지 확인 (중복 실행 방지)
        // .io 게임 특성상 본인 화면에서 부딪힌 것만 체크하는 게 안전합니다.
        if (other.CompareTag("PlayerMesh"))
        {
            PhotonView playerPv = other.GetComponentInParent<PhotonView>();
            if (playerPv != null && playerPv.IsMine)
            {
                // 2. 모든 클라이언트에게 이 오브젝트의 RPC 함수를 실행하라고 요청
                photonView.RPC(nameof(RPC_PlayWiggle), RpcTarget.AllViaServer);
            }
        }
    }

    [PunRPC]
    private void RPC_PlayWiggle()
    {
        if (puddingAnimator != null)
        {
            puddingAnimator.SetTrigger("Wiggle");
        }
    }
}