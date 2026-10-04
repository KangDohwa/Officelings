"""Requested NOTES and measured lane time, after final exports."""
from hanbok_lane import *
def main():
    summary=read(OUT/'validation-summary.json');vis=read(OUT/'seated-visibility.json');proof=read(RUN/'derivation-proof.json')
    with (RUN/'generation-prompts.json').open(encoding='utf-8-sig') as f:gen=json.load(f)
    start=datetime.strptime(gen['startedUtc'],'%Y-%m-%d %H:%M:%S UTC').replace(tzinfo=timezone.utc)
    end=datetime.now(timezone.utc);seconds=(end-start).total_seconds();hours=seconds/3600
    locked=read(FLAT/'locked/hanbok-modern.manifest.json');target=FLAT/'locked'/locked['game_input']
    locked_proof=read(RUN/'locked-source-proof.json')
    source_match=locked_proof['lockedSHA256']==locked_proof['rosterSHA256']
    savejson(RUN/'timing.json',dict(startUtc=start.isoformat(),endUtc=end.isoformat(),seconds=seconds,laneHours=hours,scope='one active lane wall-clock occupancy including reading, serial built-in generation waits, native repair, review, verification and notes; excludes first initial tool read before first clock observation',imageViews=14,generationCalls=3,attemptsPerSheet=[1,1,1],lockedManifestTargetExists=locked_proof['lockedExists'],rosterMatchesLockedTarget=source_match))
    required=[CID+'-iso.png',CID+'.iso.json',CID+'-iso.manifest.json',CID+'-iso-blink.png',CID+'-eyes-se.png',CID+'-eyes-sw.png','iso-contact.png','iso-rows-review.png','iso-seated-review.png','iso-vs-flat.png']+['walk-'+f+'-3x.gif' for f in F]
    assert all((OUT/n).is_file() for n in required)
    rows=[]
    for r in vis['summaries']:
        def value(n):
            z=r[n];return '%d/%d (%.1f%%)'%(z['visible'],z['total'],z['percent'])
        rows.append('| '+r['worker'].upper()+' | '+' | '.join(value(n) for n in ('head','torso','hands'))+' |')
    note=f"""# hanbok-modern C1/C2/C3 제작 기록

38행 일반 아틀라스(128×2432, 152셀), 24행 깜빡임(128×1536, 96셀), SE/SW 눈 마스크, manifest 두 파일, 요청된 네 검수 PNG와 네 방향 3× 걷기 GIF를 내보냈다. 최종 체커는 **PASS {summary['PASS']} / SKIP {summary['SKIP']} / FAIL {summary['FAIL']}**, exit 0이다. [실제 로그](validation.txt), [v3 착석](iso-seated-review.png), [전체 행](iso-rows-review.png), [정체성 비교](iso-vs-flat.png).

내장 image_gen을 순차 3회 사용했다. 네 방향 서기 4개, SE/NE 보행 8개, 네 방향 착석 4개를 세 원화 시트에 그렸고 시트마다 1차를 선택했다. 원본·프롬프트는 runs/의 master PNG와 generation-prompts.json에 있다. 원본 flat README, locked manifest가 지정한 시트, base 얼굴, hero 크롭과 세 파일럿을 참조했다. 잠금 원본과 참조 roster 시트의 크기·알파·60색 팔레트는 같다. 두 PNG의 SHA-256은 다르며 RGB 차이는 얼굴 영역의 811픽셀이다. [출처 비교 근거](runs/locked-source-proof.json)에 파일별 해시와 행별 차이 범위를 기록했다. 이미지 열람은 참조·원화·1×/3× 검수 합계 **14개**다.

연결 성분으로 원화를 추출하고 원화 묶음마다 한 번 정한 등방 축소율, 정확한 원본 정체성/고정 재질 팔레트 20색, alpha 0/255로 정리했다. 원화의 머리·의상·무릎·신발 형태를 사용하고 목·허리 연결, 팔꿈치·손, 눈 하이라이트와 금색 핀을 픽셀로 보정했다. 248셀을 따로 생성한 것은 아니다. 셀 32×64, 발 (16,60), 골반 (16,44), C/H와 전환 일정은 고정값이다. 서기 높이는 SE/SW 42px, NE/NW 41px이며 고정 머리 높이는 22/21px로 몸의 1/3을 넘는다.

네 방향 머리는 독립 서기 원화에서 고정했다. 서기·일반 동작 0, 착석 −1px, 보행 0/+1/0/−1px, 낮추기 0/−1/−1/−1px로 기록했다. 모든 152셀에서 해당 canonical head RGBA가 정확히 일치한다. SW/NW 보행 몸만 x′=32−x로 파생하고 명암을 보정했으며 각 방향 머리를 복원했다. 보행 원화의 공간 편차는 고정된 몸 등록값 −2/−1/+1/+2px로 보정했다. 아틀라스 프레임을 bbox로 다시 피팅하거나 확대·축소하지 않았다.

모션 확정 후 [hanbok_blink_resync.py](hanbok_blink_resync.py)로 일반 셀을 새로 복사하고 작은 눈 영역만 닫힌 눈으로 바꿨다. 96개 non-eye 비교가 모두 정확히 일치하며 눈 마스크는 SE/SW뿐이다. [깜빡임 근거](blink-resync-proof.json). 152개 실루엣의 연결, 네 방향 walk/typing 4단계 구별, sit/stand 정확한 역순과 양 끝 일치, 32개 실제 소품 그립 피부 오차 0px를 확인했다. 마지막→첫 단계 픽셀 변화는 SE 429, SW 422, NE/NW 373으로 각각 내부 변화 최댓값 이하이다. 이 수치가 자연스러운 재생을 증명하지는 않는다. 네 GIF는 저장된 네 프레임이 normal의 정확한 3× NEAREST와 같고 120/130/120/130ms, 루프 500ms다.

v3 workstations와 v3 chair의 실제 manifest/분할 PNG, ordinary ground depth와 seat tie rank로 4방향×4단계를 합성했다. C/H 좌표 오차는 0px, 실제 손 피부는 접점에서 0px이다. 픽셀 ownership과 나중 가림 레이어 alpha 합집합을 교차 검사했다. 입력 해시는 [seated-visibility.json](seated-visibility.json)에 있고 완료 시 같은 해시임을 확인했다.

| worker | 머리, 4단계 합계 | 몸통 ROI | 손 ROI |
| --- | ---: | ---: | ---: |
{chr(10).join(rows)}

머리는 canonical 생성 머리 alpha 전체(목을 포함해 source y40까지)에 선언된 오프셋을 적용했다. 분모를 가려진 픽셀에 맞춰 줄이지 않았다. 몸통 ROI는 opaque에서 머리·손을 제외한 x11..22, y<44 영역이다. 전신/팔 전체 가시율이 아니다. 손 ROI는 두 H의 Chebyshev 반경 2 안에 있는 피부색 C9896A/FBD3A8/FBDDBC이며 머리는 제외했다. 16개 단계 모두 뒤쪽 머리 85%, 앞쪽 90% 기준과 손 ROI 100%를 충족했다.

남은 약점은 거친 머리 명암, 1×에서 작은 손/리본, 일부 길게 보이는 타이핑 전완이다. contact/down/passing/up을 4프레임으로 압축한 보행은 중간 동작이 생략돼 딱딱해 보일 수 있다. sleep은 생성된 착석 하체를 약간 기울인 정적인 졸음 수준이다. SW 보행 리본의 해부학적 방향, 폰 손 선택과 hero 수준의 매력은 독립적인 사람 검수를 받지 않았다. 정체성과 자세의 완성도까지 체커 PASS로 판단하지 않는다.

WPF/앱 실행, 실제 시간 GIF 재생과 이동 속도·발 미끄러짐, DPI/picking, 런타임 깊이 정렬·중단 동작, sofa 수면 접촉, 실제 컵·종이·폰 소품 합성, pack 소비는 검증하지 않았다. 전체 dotnet test, git 쓰기, pack 수정·업로드·배포도 하지 않았다. 직접 파일 쓰기는 이 출력 폴더뿐이다. 내장 image_gen이 자동 저장한 원본 캐시는 사용자 홈 generated_images에 남고 선택본은 이 폴더에 복사했다.

실측 시간: **{hours:.4f} lane-hours ({seconds/60:.2f}분)**, {start.isoformat()} → {end.isoformat()}. 읽기·생성 대기·수정·검수·검증·기록 시간을 포함한 한 lane의 점유 시간이다. 첫 시계 관측 전 최초 파일 읽기와 종료 후 채팅 보고는 제외했다. 병렬 lane 시간을 합산하거나 생성 대기를 이중 계산하지 않았다.

재구성은 sprite-gen venv Python의 -B로 hanbok_production.py normalize → hanbok_export.py → hanbok_blink_resync.py → hanbok_review_verify.py measure verify review → hanbok_finalize.py final_board checks 순서다. 공유 도구의 pycache를 만들지 않는다.
"""
    (OUT/'NOTES-C1-C2-C3.md').write_text(note,encoding='utf-8')
    print('NOTES written; lane-hours',round(hours,4),'minutes',round(seconds/60,2),'locked source matches',source_match)
    print('Required deliverables:',len(required),'present')
if __name__=='__main__':main()


