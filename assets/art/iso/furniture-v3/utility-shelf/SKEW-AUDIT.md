# SKEW AUDIT — v2 전체 자산

원본은 읽기만 했다. 원시 측정값·좌표는 `skew-edge-measurements.csv/json`, 전체 검사/제외 목록은 `skew-coverage.json`에 있다.

측정: 원본 manifest의 부품을 합성한 native 1× 셀을 사용한다. 구조 소재 마스크의 top/bottom y(x), left/right x(y)를 추출하고, 꺾임을 1.05px로 분할한다. 최소 연속 표본은 가구 8px, 환경 6px, 작은 소품 4px이다. 전체 구간 최소제곱 기울기 m을 계산한다. 대각선 누적오차=|m−sign(m)·0.5|·Δx, 수직 누적오차=|dx/dy|·Δy. 누적오차 **>1px**만 아래 표에 올린다. 계단형 래스터의 작은 잔차를 실제 기울기와 구분한다.

책장은 top/base의 전체 긴 변·짧은 변과 양쪽 수직선을 지정해 별도로 측정했다. 벽·바닥은 선언된 모든 clip/frame과 polish 모듈을 검사했다. 원형/유기체/의도적인 들고 있는 자세는 별도 적용 범위를 표시한다.

검사 셀/프레임 158개, 측정 선분 527개. >1px 구조 윤곽 후보 19개 방향과 곡면/그립 자세 예외 7개를 구분했다. 아래 번호와 주석 PNG 번호가 같다. 빨강=실제 선, 청록 점선=정확한 평행 기준.

| 순위 | 자산 / 방향 / frame | 최악 선분 | 기울기 (기준) | 길이 px | 누적오차 px | 해석 |
|---:|---|---|---:|---:|---:|---|
| 1 | utility/shelf/ne | structure / base-long | +0.37544 (+0.5) | 51 | 6.353 | 구조 윤곽 재검토 대상 |
| 2 | utility/shelf/se | structure / base-long | -0.37994 (-0.5) | 52 | 6.243 | 구조 윤곽 재검토 대상 |
| 3 | lounge/tv-cabinet/se | wood tabletop/cabinet / top-29 | -0.34314 (-0.5) | 16 | 2.510 | 구조 윤곽 재검토 대상 |
| 4 | lounge/tv-cabinet/sw | wood tabletop/cabinet / top-41 | +0.34314 (+0.5) | 16 | 2.510 | 구조 윤곽 재검토 대상 |
| 5 | utility/water-cooler/se | chassis / bottom-0 | +0.35165 (+0.5) | 12 | 1.780 | 구조 윤곽 재검토 대상 |
| 6 | utility/fridge/ne | chassis / bottom-18 | -0.28333 (-0.5) | 8 | 1.733 | 구조 윤곽 재검토 대상 |
| 7 | utility/fridge/se | chassis / bottom-3 | +0.67273 (+0.5) | 9 | 1.555 | 구조 윤곽 재검토 대상 |
| 8 | utility/water-cooler/ne | chassis / bottom-0 | +0.34545 (+0.5) | 10 | 1.545 | 구조 윤곽 재검토 대상 |
| 9 | lounge/tv-cabinet/nw | wood tabletop/cabinet / bottom-76 | -0.66061 (-0.5) | 9 | 1.445 | 구조 윤곽 재검토 대상 |
| 10 | lounge/sofa/nw | sofa frame/cloth / right-12 | -0.08256 (+0.0) | 17 | 1.404 | 구조 윤곽 재검토 대상 |
| 11 | lounge/sofa/se | sofa frame/cloth / right-20 | -0.09265 (+0.0) | 15 | 1.390 | 구조 윤곽 재검토 대상 |
| 12 | lounge/sofa/sw | sofa frame/cloth / left-20 | +0.09265 (+0.0) | 15 | 1.390 | 구조 윤곽 재검토 대상 |
| 13 | utility/coffee-machine/se | chassis / right-8 | -0.06842 (+0.0) | 19 | 1.300 | 구조 윤곽 재검토 대상 |
| 14 | utility/coffee-machine/nw | chassis / bottom-6 | +0.67857 (+0.5) | 7 | 1.250 | 구조 윤곽 재검토 대상 |
| 15 | workstations/desk/sw | monitor housing / bottom-43 | +0.41912 (+0.5) | 15 | 1.213 | 구조 윤곽 재검토 대상 |
| 16 | workstations/desk/se | wood tabletop/cabinet / top-41 | -0.42647 (-0.5) | 16 | 1.176 | 구조 윤곽 재검토 대상 |
| 17 | lounge/sofa/ne | sofa frame/cloth / left-19 | +0.16667 (+0.0) | 7 | 1.167 | 구조 윤곽 재검토 대상 |
| 18 | lounge/tv-cabinet/ne | wood tabletop/cabinet / bottom-0 | +0.66667 (+0.5) | 7 | 1.167 | 구조 윤곽 재검토 대상 |
| 19 | utility/fridge/nw | chassis / top-0 | -0.38788 (-0.5) | 9 | 1.009 | 구조 윤곽 재검토 대상 |

## 곡면·그립 자세 예외 — 구조 결함 수에 포함하지 않음

| 주석 번호 | 자산 / 방향 / frame | 관찰 선분 | 기울기 (기준) | 길이 px | 수치 차이 px | 해석 |
|---:|---|---|---:|---:|---:|---|
| 20 | env/iso-env-wall-decor/clock-l/left/f0 | module boundary / top-1 | -1.00000 (-0.5) | 5 | 2.500 | 의도된 곡면/처진 케이블 |
| 21 | env/iso-env-wall-decor/clock-r/right/f0 | module boundary / bottom-1 | +1.00000 (+0.5) | 5 | 2.500 | 의도된 곡면/처진 케이블 |
| 22 | env/iso-env-wall-decor/string-lights-r/right/f0 | module boundary / top-0 | +0.62281 (+0.5) | 18 | 2.211 | 의도된 곡면/처진 케이블 |
| 23 | env/iso-env-wall-decor/string-lights-l/left/f0 | module boundary / top-13 | -0.62281 (-0.5) | 18 | 2.211 | 의도된 곡면/처진 케이블 |
| 24 | props/report-paper/se | phone/paper outer plane / bottom-0 | +0.08571 (+0.5) | 5 | 2.071 | 그립 자세 예외 |
| 25 | props/report-paper/ne | phone/paper outer plane / bottom-0 | +0.08571 (+0.5) | 5 | 2.071 | 그립 자세 예외 |
| 26 | props/report-paper/nw | phone/paper outer plane / bottom-0 | -0.08571 (-0.5) | 5 | 2.071 | 그립 자세 예외 |

## 주석 이미지

- [skew-annotated-01.png](skew-annotated-01.png)
- [skew-annotated-02.png](skew-annotated-02.png)
- [skew-annotated-03.png](skew-annotated-03.png)
- [skew-annotated-04.png](skew-annotated-04.png)

## 방향별 전체 측정 요약

| 자산 / 방향 | 측정 선분 | 최대 누적오차 px | 판정 / 적용 범위 |
|---|---:|---:|---|
| workstations/desk/se | 17 | 1.176 | >1px 후보 |
| workstations/desk/sw | 22 | 1.213 | >1px 후보 |
| workstations/desk/ne | 18 | 1.000 | 측정 구간 ≤1px |
| workstations/desk/nw | 16 | 1.000 | 측정 구간 ≤1px |
| workstations/chair/se | 3 | 0.222 | 측정 구간 ≤1px |
| workstations/chair/sw | 3 | 0.222 | 측정 구간 ≤1px |
| workstations/chair/ne | 4 | 0.667 | 측정 구간 ≤1px |
| workstations/chair/nw | 4 | 0.667 | 측정 구간 ≤1px |
| lounge/sofa/se | 7 | 1.390 | >1px 후보 |
| lounge/sofa/sw | 9 | 1.390 | >1px 후보 |
| lounge/sofa/ne | 13 | 1.167 | >1px 후보 |
| lounge/sofa/nw | 11 | 1.404 | >1px 후보 |
| lounge/rug/se | 4 | 0.023 | 측정 구간 ≤1px |
| lounge/rug/sw | 4 | 0.023 | 측정 구간 ≤1px |
| lounge/rug/ne | 4 | 0.023 | 측정 구간 ≤1px |
| lounge/rug/nw | 4 | 0.023 | 측정 구간 ≤1px |
| lounge/tv-cabinet/se | 17 | 2.510 | >1px 후보 |
| lounge/tv-cabinet/sw | 16 | 2.510 | >1px 후보 |
| lounge/tv-cabinet/ne | 18 | 1.167 | >1px 후보 |
| lounge/tv-cabinet/nw | 17 | 1.445 | >1px 후보 |
| lounge/side-table/se | 3 | 0.365 | 측정 구간 ≤1px |
| lounge/side-table/sw | 5 | 0.365 | 측정 구간 ≤1px |
| lounge/side-table/ne | 3 | 0.400 | 측정 구간 ≤1px |
| lounge/side-table/nw | 2 | 0.000 | 측정 구간 ≤1px |
| utility/plant/se | 0 | — | 직선 기준 적용 불가 |
| utility/plant/sw | 0 | — | 직선 기준 적용 불가 |
| utility/plant/ne | 0 | — | 직선 기준 적용 불가 |
| utility/plant/nw | 0 | — | 직선 기준 적용 불가 |
| utility/shelf/se | 6 | 6.243 | >1px 후보 |
| utility/shelf/sw | 6 | 0.818 | 측정 구간 ≤1px |
| utility/shelf/ne | 6 | 6.353 | >1px 후보 |
| utility/shelf/nw | 6 | 0.791 | 측정 구간 ≤1px |
| utility/coffee-machine/se | 5 | 1.300 | >1px 후보 |
| utility/coffee-machine/sw | 6 | 0.692 | 측정 구간 ≤1px |
| utility/coffee-machine/ne | 6 | 0.538 | 측정 구간 ≤1px |
| utility/coffee-machine/nw | 5 | 1.250 | >1px 후보 |
| utility/fridge/se | 6 | 1.555 | >1px 후보 |
| utility/fridge/sw | 6 | 0.455 | 측정 구간 ≤1px |
| utility/fridge/ne | 6 | 1.733 | >1px 후보 |
| utility/fridge/nw | 5 | 1.009 | >1px 후보 |
| utility/water-cooler/se | 5 | 1.780 | >1px 후보 |
| utility/water-cooler/sw | 4 | 0.659 | 측정 구간 ≤1px |
| utility/water-cooler/ne | 3 | 1.545 | >1px 후보 |
| utility/water-cooler/nw | 4 | 0.527 | 측정 구간 ≤1px |
| utility/floor-lamp/se | 1 | 0.000 | 측정 구간 ≤1px |
| utility/floor-lamp/sw | 1 | 0.000 | 측정 구간 ≤1px |
| utility/floor-lamp/ne | 1 | 0.000 | 측정 구간 ≤1px |
| utility/floor-lamp/nw | 1 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-floors/wood-i/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/checker-dark/any/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/checker-light/any/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/wood-checker-edge-pos-i/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/wood-checker-edge-pos-j/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/wood-checker-edge-neg-i/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/wood-checker-edge-neg-j/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-floors/wood-checker-corner-0-1/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-floors/wood-checker-corner-1-2/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-floors/wood-checker-corner-2-3/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-floors/wood-checker-corner-3-0/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-walls/plain-l/left/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/window-frame-l/left/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/window-glass-l/left/f0 | 4 | 0.056 | 측정 구간 ≤1px |
| env/iso-env-walls/door-frame-l/left/f0 | 6 | 0.167 | 측정 구간 ≤1px |
| env/iso-env-walls/door-opening-l/left/f0 | 4 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-walls/door-leaf-closed-l/left/f0 | 4 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-walls/door-leaf-open-l/left/f0 | 4 | 0.214 | 측정 구간 ≤1px |
| env/iso-env-walls/door-threshold-l/left/f0 | 2 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-walls/end-cap-l/left/f0 | 1 | 0.118 | 측정 구간 ≤1px |
| env/iso-env-walls/top-trim-l/left/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/base-trim-l/left/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/plain-r/right/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/window-frame-r/right/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/window-glass-r/right/f0 | 4 | 0.056 | 측정 구간 ≤1px |
| env/iso-env-walls/door-frame-r/right/f0 | 6 | 0.167 | 측정 구간 ≤1px |
| env/iso-env-walls/door-opening-r/right/f0 | 4 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-walls/door-leaf-closed-r/right/f0 | 4 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-walls/door-leaf-open-r/right/f0 | 4 | 0.214 | 측정 구간 ≤1px |
| env/iso-env-walls/door-threshold-r/right/f0 | 2 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-walls/end-cap-r/right/f0 | 1 | 0.118 | 측정 구간 ≤1px |
| env/iso-env-walls/top-trim-r/right/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-walls/base-trim-r/right/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-wall-corner/inner-rear-corner/rear-corner/f0 | 2 | 0.000 | 측정 구간 ≤1px |
| env/iso-env-wood-variants/wood-i-v0/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-wood-variants/wood-i-v1/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-wood-variants/wood-i-v2/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-wood-variants/wood-i-v3/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-wood-variants/wood-i-v4/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-wood-variants/wood-i-v5/i/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-dark/any/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-light/any/f0 | 4 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-border-0/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-border-1/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-border-2/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-border-3/any/f0 | 2 | 0.045 | 측정 구간 ≤1px |
| env/iso-env-checker-border/checker-border-4/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-checker-border/checker-border-5/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-checker-border/checker-border-6/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-checker-border/checker-border-7/any/f0 | 0 | — | 직선 기준 적용 불가 |
| env/iso-env-wall-decor/curtain-l/left/f0 | 7 | 0.214 | 측정 구간 ≤1px |
| env/iso-env-wall-decor/picture-l/left/f0 | 4 | 0.079 | 측정 구간 ≤1px |
| env/iso-env-wall-decor/clock-l/left/f0 | 6 | 2.500 | 곡면/자세 예외 |
| env/iso-env-wall-decor/string-lights-l/left/f0 | 6 | 2.211 | 곡면/자세 예외 |
| env/iso-env-wall-decor/curtain-r/right/f0 | 7 | 0.214 | 측정 구간 ≤1px |
| env/iso-env-wall-decor/picture-r/right/f0 | 4 | 0.079 | 측정 구간 ≤1px |
| env/iso-env-wall-decor/clock-r/right/f0 | 6 | 2.500 | 곡면/자세 예외 |
| env/iso-env-wall-decor/string-lights-r/right/f0 | 6 | 2.211 | 곡면/자세 예외 |
| props/cup/se | 0 | — | 곡면/자세 예외 |
| props/cup/sw | 0 | — | 곡면/자세 예외 |
| props/cup/ne | 0 | — | 곡면/자세 예외 |
| props/cup/nw | 0 | — | 곡면/자세 예외 |
| props/water-cup/se | 0 | — | 곡면/자세 예외 |
| props/water-cup/sw | 0 | — | 곡면/자세 예외 |
| props/water-cup/ne | 0 | — | 곡면/자세 예외 |
| props/water-cup/nw | 0 | — | 곡면/자세 예외 |
| props/phone/se | 4 | 0.600 | 곡면/자세 예외 |
| props/phone/sw | 4 | 0.600 | 곡면/자세 예외 |
| props/phone/ne | 4 | 0.600 | 곡면/자세 예외 |
| props/phone/nw | 4 | 0.600 | 곡면/자세 예외 |
| props/report-paper/se | 4 | 2.071 | 곡면/자세 예외 |
| props/report-paper/sw | 4 | 0.600 | 곡면/자세 예외 |
| props/report-paper/ne | 4 | 2.071 | 곡면/자세 예외 |
| props/report-paper/nw | 4 | 2.071 | 곡면/자세 예외 |
| pets/idle/se/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/se/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/se/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/se/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/sw/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/sw/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/sw/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/sw/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/ne/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/ne/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/ne/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/ne/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/nw/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/nw/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/nw/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/idle/nw/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/se/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/se/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/se/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/se/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/sw/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/sw/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/sw/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/sw/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/ne/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/ne/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/ne/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/ne/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/nw/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/nw/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/nw/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/walk/nw/f3 | 0 | — | 직선 기준 적용 불가 |
| pets/sleep/any/f0 | 0 | — | 직선 기준 적용 불가 |
| pets/sleep/any/f1 | 0 | — | 직선 기준 적용 불가 |
| pets/sleep/any/f2 | 0 | — | 직선 기준 적용 불가 |
| pets/sleep/any/f3 | 0 | — | 직선 기준 적용 불가 |

## 적용 범위와 한계

- 식물 잎·원형 화분, 램프의 원뿔 갓/타원형 바닥, 컵의 원형 림/손잡이, 고양이 36프레임은 직선 2:1 기준이 적용되지 않는다. 제외 기록을 누락 없이 coverage에 남겼다.
- 램프는 수직 stem, 정수기는 병을 제외한 chassis, 소파는 구조 frame/cloth, 의자는 cushion/back, 책상과 TV장은 가구와 화면 housing을 각각 측정했다.
- 그림의 소재 경계가 부품을 가리는 곳은 짧은 선분만 남는다. 최소 길이 미만의 1~3px 디테일과 완전히 가려진 구조선은 이 감사에서 측정할 수 없다.
- 앵커의 평행 이동은 선의 기울기를 바꾸지 않는다. 본 감사는 기울기 감사이며 런타임 좌표·벽 부착 오프셋·사람과의 가림을 승인하지 않는다.
- 측정 선분은 보이는 래스터 경계다. 곡면·베벨·소재 무늬의 의도 여부를 자동으로 판별할 수 없으므로 수치 위반을 모두 제공하고, 기능 결함이라고 단정하지 않는다. 다른 lane 자산은 수정하지 않았다.
