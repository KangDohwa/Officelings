# Visual QA ? apron-baker

Resumed the interrupted character run. Reviewed its base, the pixel-agents character benchmark, all 13 existing source rows, every newly completed generation and the final native cells using downscaled batch composites. No large raw images were displayed individually. Duplicate `.raw.png` files are generation evidence, not extra candidates.

Identity: bald scalp, broad ginger beard/moustache, highlighted olive eyes, friendly stocky body, red/cream plaid, cream apron/front pocket and brown boots. Back apron identity is the accepted idle's two parallel cream straps with a plain horizontal waistband. Sleep closes the eyes intentionally; back views hide the face intentionally.

## Reuse and corrections

- Reused original idle (including its already extracted frame cache), typing_side, report, sleep, coffee, phone and surprised: 20 poses. Reused original primary walk_down/left/right indices 0 and 1: another 6 poses.
- Rejected the original talk row, which depicted a different person with hair/headband and a green shirt. Accepted `run/corrections/raw/talk.png`, generated with accepted four-view idle identity + three-slot guide.
- Rejected walk_up's crossed straps/bow/tails. Accepted `run/corrections/raw/walk_up.png`, copying idle back's parallel straps/plain waistband while preserving the back-facing gait.
- Rejected typing_back's added bow, dangling tails and cream lower-back skirt. Accepted `run/corrections/raw/typing_back.png`; back shirt stays plaid, hands remain low, and seated legs are retained. Typing finger motion is small.
- Original walk_down repeated the first contact in its third pose. Corrected full strip supplies the opposite contact/passing half. Selected candidate indices **[0,1,6,7]** from primary + four-frame `opposite-phase` take.
- Left walking needed a more distinct opposite phase. Selected **[0,1,6,7]** from original primary + `run/corrections/raw/walk_left.png` as a four-frame take. The two-frame side-tail candidates changed view/profile and were not chosen.
- Right walking retained the same apparent foreground-leg phase in the first full-row retry. The first two original poses remain; accepted `run/corrections/raw/right-tail-v3.png` supplies a two-frame opposite contact/passing take, selected **[0,1,4,5]**. It uses original right-walk identity/face angle + two-slot guide. The idle-only two-frame candidate turned toward pure profile and was rejected.

Original rejected strips/reports are retained in `run/qa/rejected/` and `run/corrections/`. Initial `run/reports/gen-set/` describes interrupted generation; correction prompts/reports and side-tail run describe subsequent candidates. Canonical takes are `run/raw/<state>.takes/opposite-phase.png`. The first automatically prepared side-tail run selected cyan; generation was interrupted before any image was published, then prepared with explicit **#FF00FF**. No cyan candidate is used. All completed generations were viewed before selection.

Correction attachments: existing target strip for precise edits, accepted idle identity, generated slot guide for geometry. Talk uses idle + guide. Final right-walk tail uses original right walk + two-slot guide. Worker walk strips were inspected only as a motion comparison, never attached as replacement identity. No external character design was introduced.

## Final fit and review

Canonical sprite-gen component extraction removes chroma and separates poses; no fixed-grid raw cuts. Existing idle frames are preserved. Standard curation scale/dy transforms normalize row height and bottom placement while keeping safe margins. Transforms use nearest-neighbor pixel snapping; no pose painting, mirrored replacements, cloned stills, reordered gait phases or changed timing. Export uses the same shared-palette functions and manifest convention as beanie-scarf. Facelock is left for the supervisor.

Static action review: idle selects four directions; down/up walking has alternating foot contacts; side walking has distinct contact/passing shapes but subtle limb-depth and arm alternation. Back/side typing contains seated low hands only. Report is held/extended/offered, mug raised/lowered, phone has small finger changes, sleep has two slumped seated poses, talk raises/lowers an open hand, surprised raises hands beside cheeks. Props remain attached; no furniture, text, floating punctuation or effects are baked into the person.

Geometry/palette/alpha verification is recorded in `run/qa/validation.json`. Row order, counts, fps and loops are compared with the existing worker request; cell rectangles, occupied/empty cells, safe margins, binary alpha, zero RGB under transparency and no visible magenta are checked. Review pages and labeled `C-contact.png` use exact 3? nearest-neighbor.

**Motion verdict:** all four walk loops remain **experimental/unverified**. Computer-use inventory returned no apps/browsers; GIF export does not prove played motion or a seamless loop. Minor eye/beard shading changes and plaid simplification remain, side limb alternation is less clear than front/back, typing fingers are subtle. No app code, tests or build was touched/run for this art-only task.

Extraction reported pixel-pitch disagreements/physical-cap and pre-curation edge warnings; these are retained in the pipeline reports. Final composed cells passed margin/alpha/palette checks and were viewed at 3?. Ten compact images total were viewed, including the labeled contact sheet; no large individual raws. Finish workflow called with curation=skip; save-defaults question answered **no** for this one-off request, with no preferences written.
