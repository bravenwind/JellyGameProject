using UnityEngine;

public class JellyGenerator : MonoBehaviour
{
    private float timer = 0.0f;
    public float generateInterval = 2.0f;

    public GameObject[] jellies;

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= generateInterval)
        {
            // 1. 타이머 초기화 (이걸 해줘야 2초마다 계속 생성됩니다)
            timer = 0.0f;

            // 2. 랜덤 인덱스 선택 (0부터 jellies 배열의 길이 사이에서 무작위 숫자)
            int randomIndex = Random.Range(0, jellies.Length);

            // 3. 해당 인덱스의 젤리 소환 (현재 위치와 회전값 기준)
            Instantiate(jellies[randomIndex], Vector3.zero, transform.rotation);
        }
    }
}