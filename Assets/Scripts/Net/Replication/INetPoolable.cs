namespace JellyNet
{
    //풀에서 꺼내고 되돌릴 때 상태를 초기화해야 하는 오브젝트
    //Awake/Start는 재사용 시 다시 돌지 않으므로 여기서 되돌린다
    public interface INetPoolable
    {
        void OnTakenFromPool();
        void OnReturnedToPool();
    }
}
