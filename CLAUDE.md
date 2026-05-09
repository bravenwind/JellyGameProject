# JellyGameProject — Claude 루틴 지침

## 프로젝트 개요
- Unity C# 멀티플레이어 .io 게임 (Photon PUN2)
- 주요 시스템: 젤리 물리(SoftBody3D/Cloth), FSM 플레이어 컨트롤러, AI 봇, 네트워크 동기화

## 일일 코드 리뷰 루틴

### 진행 조건
- IO 브랜치에서 진행
- 전날 대비 코드 수정이 없으면 진행하지 않음

### 작업 흐름
1. 게임 시퀀스 관련 스크립트를 살펴보고 구조적 개선점 탐색
2. 개선점을 정리해서 도출하고 기억해놓기 (직접 코드 수정 X)
3. 사용자 확인 후 적용 여부 결정 → 승인된 항목만 실제 작업
4. 버그 발견/제보 시 즉시 수정 가능

### Google Sheets 문서 업데이트 (매 루틴 종료 시)
루틴에서 작업을 수행하면 반드시 Google Sheets에 기록한다.

**스크립트 위치:** `tools/update_sheets.py`
**인증 파일:** `/home/user/.config/gsheet/credentials.json` (절대로 Git에 커밋 금지)

```bash
# 개발계획서에 새 작업 추가
python3 tools/update_sheets.py plan "카테고리" "작업명" "세부내용" "우선순위" "난이도" "예상일" "상태" "날짜" "메모"

# 트러블슈팅에 버그 수정 기록 추가
python3 tools/update_sheets.py bug "날짜" "심각도" "카테고리" "이슈명" "원인" "해결방법" "관련파일"

# 현재 상태 확인
python3 tools/update_sheets.py status
```

**시트 ID:**
- 개발계획서: `1BsXKrszkx3CnlbnmvsjTaLyOj59_qGhzWE2VKU0xd_4`
- 트러블슈팅: `1kl8o2auomNHj-6xpmIPZ_sQTcBRj_1yoELQkvydpCSo`

### 푸시
- 원격에서 작업한 게 로컬과 연동될 수 있도록 항상 푸시

## 참고 문서
- `REVIEW_NOTES.md`: 게임 시퀀스 코드 구조 분석 결과 (17개 개선점)
- 개발계획서 시트: 원본 23개 + 루틴 추가 작업
- 트러블슈팅 시트: 버그 수정 이력 (날짜, 원인, 해결방법)
