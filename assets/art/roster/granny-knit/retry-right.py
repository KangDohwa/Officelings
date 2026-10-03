from pathlib import Path
import subprocess, os, shutil

root=Path(__file__).resolve().parent
run=root/'run'
rejected=run/'qa/rejected'
for source in [run/'raw/walk_right.png',run/'raw/walk_right.png.raw.png',run/'reports/walk_right-correction.json']:
    shutil.copy2(source,rejected/('v2-'+source.name))
prompt=run/'prompts/walk_right-pencil-fix.txt'
prompt.write_text('Edit the attached FOUR-FRAME RIGHT-WALK STRIP (first reference). Preserve every body pose, '
    'opposite arm swing, contact/passing order, head scale, highlighted eye, round glasses and chain, '
    'lilac shawl, cream floral dress and brown shoes. All four bodies face RIGHT.\n'
    'FIX ONE SPECIFIC SILHOUETTE ERROR: currently TWO yellow sticks form a V above each gray bun. '
    'There must be exactly ONE golden pencil through the bun, like the second reference (accepted base). '
    'REMOVE the yellow stick pointing up-left from each bun. Keep only the single pencil pointing diagonally UP-RIGHT, '
    'with its lower end occluded inside the bun. Do not draw two visible ends as a V. '
    'No second pencil, needle, hairpin, horns, antennae or accessory. Preserve the round gray bun. '
    'Keep the FOUR distinct existing gait phases, especially opposite nearest-arm swing in frames 0 and 2. '
    'No redesign of face or clothes, no additional motions, no labels, no furniture, no shadows. '
    'Strict chunky 2D pixel CHIBI, dark selective outline, warm palette, hard-edged 3-4 tones, same density as references. '
    'Exactly four separated full bodies in a single horizontal strip, same placement and baseline, '
    'on uniformly pure magenta #FF00FF. The third reference is slot geometry only.\n',encoding='utf-8')
cli=Path(os.environ['SPRITE_GEN_ROOT'])/'.venv/Scripts/sprite-gen.exe'
args=[str(cli),'gen','--provider','codex','--prompt-file',str(prompt),
      '--ref',str(rejected/'v2-walk_right.png'),'--ref',str(run/'base-source.png'),
      '--ref',str(run/'references/layout-guides/walk_right.png'),
      '--out',str(run/'raw/walk_right.png'),'--report',str(run/'reports/walk_right-pencil-fix.json')]
with (run/'reports/walk_right-pencil-fix.log').open('w',encoding='utf-8') as log:
    subprocess.run(args,check=True,stdout=log,stderr=subprocess.STDOUT)
print('Regenerated walk_right with ONE pencil, preserving corrected four gait phases.',flush=True)
