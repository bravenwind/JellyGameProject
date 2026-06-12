#!/bin/bash
set -uo pipefail

# ─────────────────────────────────────────────────────────────
# JellyGameProject — Claude Code on the web 세션 시작 훅
# 일일 코드 리뷰 루틴의 Google Sheets 기록(tools/update_sheets.py)이
# 매 세션 동작하도록 의존성과 인증 파일을 자동 세팅한다.
#
# 필요한 환경 설정(claude.ai/code 환경 설정에서 1회 등록):
#   GSHEET_CREDENTIALS_JSON = 서비스 계정 credentials.json 전체 내용
# ※ credentials는 절대 Git에 커밋하지 않는다 (CLAUDE.md 규칙)
# ─────────────────────────────────────────────────────────────

# 원격(웹) 컨테이너에서만 실행
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# 1) credentials 주입: 환경 변수 → credentials.json
#    update_sheets.py는 ~(=$HOME) 기준 경로를 읽지만, CLAUDE.md에는 /home/user 경로로
#    문서화되어 있으므로 둘이 다르면 양쪽 모두에 써서 어느 쪽으로 접근해도 동작하게 한다.
if [ -n "${GSHEET_CREDENTIALS_JSON:-}" ]; then
  for dir in "$HOME/.config/gsheet" "/home/user/.config/gsheet"; do
    mkdir -p "$dir" 2>/dev/null || continue
    printf '%s' "$GSHEET_CREDENTIALS_JSON" > "$dir/credentials.json"
    chmod 600 "$dir/credentials.json"
  done
  echo "[session-start] Google Sheets credentials 주입 완료"
else
  echo "[session-start] 경고: GSHEET_CREDENTIALS_JSON 환경 변수가 비어 있음 — 환경 설정에 등록하면 시트 기록이 자동화됩니다"
fi

# 2) Google Sheets 라이브러리 설치 (이미 있으면 스킵 — 컨테이너 캐시 활용)
#    네트워크가 느린 환경 대비 타임아웃/재시도를 넉넉히 주고,
#    실패해도 세션 시작은 막지 않는다 (루틴에서 수동 설치로 복구 가능).
if ! python3 -c "import gspread, google.oauth2.service_account" >/dev/null 2>&1; then
  echo "[session-start] gspread/google-auth 설치 중..."
  if pip install --quiet --timeout 120 --retries 5 gspread google-auth cffi cryptography; then
    echo "[session-start] gspread/google-auth 설치 완료"
  else
    echo "[session-start] 경고: gspread 설치 실패 — 루틴에서 수동 설치(pip install gspread google-auth cffi cryptography) 필요"
  fi
fi

exit 0
