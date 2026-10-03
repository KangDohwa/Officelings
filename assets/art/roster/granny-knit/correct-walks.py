from pathlib import Path
import subprocess, os, shutil

root=Path(__file__).resolve().parent
run=root/'run'
rejected=run/'qa/rejected'
rejected.mkdir(parents=True,exist_ok=True)
cli=Path(os.environ['SPRITE_GEN_ROOT'])/'.venv/Scripts/sprite-gen.exe'
for state,direction in [('walk_left','LEFT'),('walk_right','RIGHT')]:
    for source in [run/'raw'/f'{state}.png',run/'raw'/f'{state}.png.raw.png',run/'reports/gen-set'/f'{state}.json']:
        if source.exists(): shutil.copy2(source,rejected/('v1-'+source.name))
    original=(run/'prompts'/f'{state}.txt').read_text(encoding='utf-8')
    correction = ('\nMANDATORY GAIT CORRECTION: the rejected version repeated the same shoe spread and arm swing in frames 0 and 2. '
        'Create a NEW four-phase cycle with clearly OPPOSITE near-side arm positions in the two contact frames. '
        f'All four poses face {direction}, top-down three-quarter side view, planted in place. '
        'Use the extra idle strip only to retain the accepted character identity and orientation. '
        'Frame 0 CONTACT A: nearest leg reaches forward, far leg behind; NEAREST ARM swings BACK, far arm forward. '
        'Frame 1 PASS A: both shoes close under body, nearest heel rises slightly, arms near vertical. '
        'Frame 2 CONTACT B: far leg reaches forward, nearest leg behind; NEAREST ARM swings FORWARD visibly, far arm back. '
        'Frame 3 PASS B: both shoes close under body with opposite heel raised, nearest arm slightly forward, far slightly back. '
        'The large dark nearest sleeve cuff and visible peach hand must move from behind body in frame 0 to in FRONT of body in frame 2. '
        'The nearest brown shoe should read larger than the partly hidden far shoe, so swapping legs reads beneath the dress. '
        'Short elderly steps, no stretched legs or skirt redesign. The ankle-length cream floral dress, muted lilac shawl, bun, '
        'pencil, highlighted eyes, round glasses and glasses chain remain IDENTICAL. Skirt hem covers knees and retains the same length; '
        'do not trade the dress for pants. Constant head size and ground line, no travel. Exactly four full bodies, no labels, no divider lines. '
        'Uniform pure #FF00FF background.\n')
    prompt=run/'prompts'/f'{state}-correction.txt'
    prompt.write_text(original+correction,encoding='utf-8')
    args=[str(cli),'gen','--provider','codex','--prompt-file',str(prompt),
          '--ref',str(run/'base-source.png'),'--ref',str(run/'raw/idle.png'),
          '--ref',str(run/'references/layout-guides'/f'{state}.png'),
          '--out',str(run/'raw'/f'{state}.png'),
          '--report',str(run/'reports'/f'{state}-correction.json')]
    with (run/'reports'/f'{state}-correction.log').open('w',encoding='utf-8') as log:
        subprocess.run(args,check=True,stdout=log,stderr=subprocess.STDOUT)
    print(state+' corrected with own accepted idle strip as extra direction reference.',flush=True)

state='typing_back'
for source in [run/'raw'/f'{state}.png',run/'raw'/f'{state}.png.raw.png',run/'reports/gen-set'/f'{state}.json']:
    if source.exists(): shutil.copy2(source,rejected/('v1-'+source.name))
original=(run/'prompts'/f'{state}.txt').read_text(encoding='utf-8')
correction=('\nMANDATORY SEATED POSTURE CORRECTION: the rejected back row raised fists beside her shoulders. '
    'Generate a NEW row viewed from BEHIND, facing UP away from viewer, with the LOW seated posture of the attached typing_side row. '
    'The typing_side strip is ONLY a seated-pose/arm-height reference, not a change of direction. '
    'Show just granny, no chair, table, monitor or keyboard. Her floral dress drapes over bent seated knees. '
    'BOTH elbows stay DOWN beside her waist; upper arms descend from shoulders. Forearms extend FORWARD AWAY FROM THE VIEWER '
    'horizontally at lap/belly-button height. Small hands peek beside the LOWER shawl/dress seam, below the shawl shoulder band. '
    'NO palms or hands alongside the head, ears, shoulders or upper shawl. Do not show raised fists, waving or shrugging. '
    'Frame 0 left fingertips press down slightly, frame 1 both hands level and relaxed, frame 2 right fingertips press down slightly. '
    'Hands are SMALL open palms with fingers facing forward, never fists. Tiny deliberate finger motion is correct. '
    'Back of silver-gray bun and diagonal pencil, triangular dusty-lilac shawl and cream floral dress stay identical. '
    'Exactly three separated full seated bodies on uniform pure #FF00FF, same scale and baseline.\n')
prompt=run/'prompts'/f'{state}-correction.txt'
prompt.write_text(original+correction,encoding='utf-8')
args=[str(cli),'gen','--provider','codex','--prompt-file',str(prompt),
      '--ref',str(run/'base-source.png'),'--ref',str(run/'raw/typing_side.png'),
      '--ref',str(run/'references/layout-guides'/f'{state}.png'),
      '--out',str(run/'raw'/f'{state}.png'),
      '--report',str(run/'reports'/f'{state}-correction.json')]
with (run/'reports'/f'{state}-correction.log').open('w',encoding='utf-8') as log:
    subprocess.run(args,check=True,stdout=log,stderr=subprocess.STDOUT)
print('typing_back corrected with own typing_side LOW arm posture reference.',flush=True)
