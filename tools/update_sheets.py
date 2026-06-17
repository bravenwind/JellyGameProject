#!/usr/bin/env python3
"""
Google Sheets 업데이트 스크립트 — 일일 코드 리뷰 루틴용

※ 인자 순서는 실제 시트 컬럼 순서와 일치한다(헤더 기준).
   - 개발계획서: 카테고리 | 작업명 | 세부내용 | 우선순위 | 난이도 | 예상(일) | 상태 | 시작 날짜 | 종료 날짜 | 메모
   - 트러블슈팅: 카테고리 | 심각도 | 날짜 | 이슈명 | 원인 | 해결방법 | 관련파일

사용법:
    python3 tools/update_sheets.py plan "카테고리" "작업명" "세부내용" "우선순위" "난이도" "예상일" "상태" "시작날짜" "종료날짜" "메모"
    python3 tools/update_sheets.py bug "카테고리" "심각도" "날짜" "이슈명" "원인" "해결방법" "관련파일"
    python3 tools/update_sheets.py status          # 현재 시트 상태 출력
"""
import sys
import os
import json
import gspread
from google.oauth2.service_account import Credentials

CREDENTIALS_PATH = os.environ.get(
    "GSHEET_CREDENTIALS_PATH",
    os.path.expanduser("~/.config/gsheet/credentials.json")
)
PLAN_SHEET_ID = "1BsXKrszkx3CnlbnmvsjTaLyOj59_qGhzWE2VKU0xd_4"
TROUBLE_SHEET_ID = "1kl8o2auomNHj-6xpmIPZ_sQTcBRj_1yoELQkvydpCSo"

SCOPES = ["https://www.googleapis.com/auth/spreadsheets"]


def get_client():
    env_creds = os.environ.get("GSHEET_CREDENTIALS_JSON")
    if env_creds:
        info = json.loads(env_creds)
        creds = Credentials.from_service_account_info(info, scopes=SCOPES)
    else:
        creds = Credentials.from_service_account_file(CREDENTIALS_PATH, scopes=SCOPES)
    return gspread.authorize(creds)


def add_plan_entry(args):
    """개발계획서 '일일 코드 리뷰 루틴' 시트에 행 추가
    컬럼: # | 카테고리 | 작업명 | 세부내용 | 우선순위 | 난이도 | 예상(일) | 상태 | 시작 날짜 | 종료 날짜 | 메모
    """
    if len(args) < 9:
        print("사용법: plan 카테고리 작업명 세부내용 우선순위 난이도 예상일 상태 시작날짜 종료날짜 [메모]")
        sys.exit(1)

    gc = get_client()
    sheet = gc.open_by_key(PLAN_SHEET_ID)
    ws = sheet.worksheet("일일 코드 리뷰 루틴")

    all_rows = ws.get_all_values()
    data_rows = [r for r in all_rows if r[0] and r[0].isdigit()]
    next_num = max((int(r[0]) for r in data_rows), default=0) + 1

    # 카테고리~메모까지 최대 10개 필드(메모 생략 가능). 시트 컬럼 순서와 1:1.
    new_row = [str(next_num)] + list(args[:10])

    ws.append_row(new_row, value_input_option="USER_ENTERED")
    print(f"[개발계획서] #{next_num} 추가 완료: {args[1]}")


def add_bug_entry(args):
    """트러블슈팅 시트에 행 추가
    컬럼: # | 카테고리 | 심각도 | 날짜 | 이슈명 | 원인 | 해결방법 | 관련파일
    """
    if len(args) < 7:
        print("사용법: bug 카테고리 심각도 날짜 이슈명 원인 해결방법 관련파일")
        sys.exit(1)

    gc = get_client()
    sheet = gc.open_by_key(TROUBLE_SHEET_ID)
    ws = sheet.worksheet("트러블슈팅")

    all_rows = ws.get_all_values()
    data_rows = [r for r in all_rows if r[0] and r[0].isdigit()]
    next_num = max((int(r[0]) for r in data_rows), default=0) + 1

    new_row = [str(next_num)] + list(args[:7])
    ws.append_row(new_row, value_input_option="USER_ENTERED")
    print(f"[트러블슈팅] #{next_num} 추가 완료: {args[3]}")


def show_status():
    """현재 시트 상태 출력"""
    gc = get_client()

    plan = gc.open_by_key(PLAN_SHEET_ID)
    ws_routine = plan.worksheet("일일 코드 리뷰 루틴")
    routine_rows = [r for r in ws_routine.get_all_values() if r[0] and r[0].isdigit()]

    trouble = gc.open_by_key(TROUBLE_SHEET_ID)
    ws_bug = trouble.worksheet("트러블슈팅")
    bug_rows = [r for r in ws_bug.get_all_values() if r[0] and r[0].isdigit()]

    done = sum(1 for r in routine_rows if r[7] == "완료")
    print(f"[개발계획서 - 루틴] {len(routine_rows)}개 작업 ({done}개 완료)")
    if routine_rows:
        last = routine_rows[-1]
        print(f"  마지막: #{last[0]} {last[2]} ({last[8]})")

    high = sum(1 for r in bug_rows if r[2] == "높음")
    print(f"[트러블슈팅] {len(bug_rows)}개 이슈 (높음: {high}개)")
    if bug_rows:
        last = bug_rows[-1]
        print(f"  마지막: #{last[0]} {last[4]} ({last[1]})")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    cmd = sys.argv[1]
    if cmd == "plan":
        add_plan_entry(sys.argv[2:])
    elif cmd == "bug":
        add_bug_entry(sys.argv[2:])
    elif cmd == "status":
        show_status()
    else:
        print(f"알 수 없는 명령: {cmd}")
        print(__doc__)
        sys.exit(1)
