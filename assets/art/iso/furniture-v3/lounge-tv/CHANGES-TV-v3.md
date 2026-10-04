# TV v3: S0 예외 — TV 상단 38 → 48

**이 TV lane은 TV 상단 높이만 38에서 48로 변경한다.** 넓은 65인치 OLED형 패널을 상단 38 안에 넣은 첫 시안은 화면이 지나치게 낮아 울트라와이드 모니터처럼 보였다. 최종 패널의 상단을 10px 올려 화면을 54×28px로 만들었다. 일반적인 16:9 화면에 가까운 인상을 주되, 화면의 네 모서리와 본체 모서리는 정확한 2:1 투영을 따른다.

캐비닛 높이 16, 1×2 풋프린트(회전 시 2×1), blockedCells, 네 회전, 128×128 셀, SE/NW 원점 (76,72), SW/NE 원점 (52,72), TV 접근 지점은 v2와 같다. 데스크·의자·배우의 A/C/H 및 seated pelvis (16,44)는 이 lane의 대상이 아니며 변경하지 않았다. 재질 램프, 상단 왼쪽 조명, 그림자 (6,3)/alpha 0·48·96, partId 명명 및 지면 기반 깊이 정렬도 유지한다.

다음은 **lane에서 적용한 정확한 텍스트 diff**다. 원본 문서 파일을 수정하지 않았다. 패키징 담당자는 이 예외를 먼저 확인해야 한다.

`docs/iso-slice-s0.md`:

```diff
-| A4 sofa/rug/TV/side table / 4–7 | A3 part convention | Four named variants for each family. Sofa 1×3 at rotation 0, cushion12/back24, explicit rotated nap approach/contact; split rear/front strips. Rug 2×3, nonblocking, 192×128 cells, 180° pairs reusable if nondirectional. TV cabinet 1×2, cabinet16/top38; side table 1×1,height16. Check empty/occupied sofa reconstruction and walkers at both ends. |
+| A4 sofa/rug/TV/side table / 4–7 | A3 part convention | Four named variants for each family. Sofa 1×3 at rotation 0, cushion12/back24, explicit rotated nap approach/contact; split rear/front strips. Rug 2×3, nonblocking, 192×128 cells, 180° pairs reusable if nondirectional. TV cabinet 1×2, cabinet16/top48; side table 1×1,height16. Check empty/occupied sofa reconstruction and walkers at both ends. |
```

`assets/art/iso/furniture/furniture-part-convention.md`:

```diff
-| tv-cabinet | 128×128 | (76,72) | (52,72) | 1×2 | cabinet16, TV38 |
+| tv-cabinet | 128×128 | (76,72) | (52,72) | 1×2 | cabinet16, TV48 |
```

manifest의 variant마다 `height:16`, `tvTop:48`을 명시했다. runtime ID는 `iso/tv-cabinet`을 유지하고, sheet ID는 `iso-tv-cabinet-v3-*`로 구분했다. 화면 상태에 따라 partId를 바꾸지 않는다.
