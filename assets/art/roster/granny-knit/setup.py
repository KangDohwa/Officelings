from pathlib import Path
import json
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
refs = root / 'references'
refs.mkdir(exist_ok=True)
sources = [root.parent/'A-contact.png', root.parent/'B-contact.png',
           root.parent/'apron-baker/C-contact.png',
           root.parent/'beanie-scarf/references/pixel-agents-characters.png']
canvas = Image.new('RGB', (1120, 760), (226, 221, 210))
draw = ImageDraw.Draw(canvas)
for path, (x, y, w, h) in zip(sources, [(0,0,560,380),(560,0,560,380),(0,380,560,380),(560,380,560,380)]):
    im = Image.open(path).convert('RGBA')
    im.thumbnail((w-16, h-32), Image.Resampling.NEAREST)
    canvas.paste(im, (x+8, y+24), im)
    draw.text((x+8,y+6), path.parent.name+'/'+path.name, fill=(35,32,39))
canvas.save(refs/'roster-review.png')
Image.open(sources[-1]).save(refs/'pixel-agents-characters.png')

identity = ('Kind elderly woman with a gently plump pear-shaped body and a large round head. '
    'Silver-gray hair swept back with a scalloped side fringe and ONE prominent round bun high at the back, '
    'ONE golden-yellow pencil diagonally through the bun; the bun remains visible above/behind the head in every view. '
    'Large warm brown expressive eyes, each at least 3x4 logical pixels with an ivory square catchlight, '
    'visible THROUGH thin dark-brown ROUND glasses. Two small gold chain strands hang from the glasses to collar level. '
    'Warm peach skin, rosy cheeks, a tiny gentle smile and subtle crow-foot pixels. '
    'Dusty LILAC knitted triangular shawl, broad over both shoulders with two short pointed front ends, '
    'over a cream ankle-length floral dress with sparse terracotta flowers and muted blue-green leaves. '
    'Short comfortable brown rounded shoes. Rounded broad shawl and flared skirt form a strong triangular silhouette; '
    'no trousers, no blazer, no beanie, no apron. Keep the same gray bun/pencil, round glasses/chain, lilac shawl, '
    'cream floral dress and brown shoes in EVERY state and direction. Back views show the bun and full triangular shawl; no face.')
style = ('STRICT 2D PIXEL CHIBI, cozy office game top-down three-quarter orthographic view. '
    'Big head 38-42 percent of body height, minimum one third. Highlighted expressive eyes, NEVER plain dots. '
    'Dark warm plum-brown selective one-pixel outline; warm readable limited palette, 3-4 hard-edged tones per material. '
    'Deliberate chunky square pixel clusters at a logical 32x64 design scale; no vector curves, gradients, antialiasing or dithering. '
    'Target full-body silhouette about 26x46 logical pixels including bun/pencil, preserve eye highlights at final resolution. '
    'Uniform pure MAGENTA #FF00FF background, no magenta in subject. Lilac is muted and grayish, never saturated purple/magenta. '
    'No scenery, floor shadow, furniture, grid, text or detached marks. ')
request = json.loads((root.parents[1]/'worker-request.json').read_text(encoding='utf-8-sig'))
request['style'] = style + 'IDENTITY LOCK: ' + identity
request['chroma_key'] = 'magenta'
for name, spec in request['states'].items():
    action = spec['action'].replace('beneath trousers','beneath the floral dress')
    action = action.replace('Back of hair and teal overshirt clear.', 'Back gray bun with pencil and triangular lilac shawl clear.')
    action = action.replace('wide dot eyes', 'wide expressive brown eyes WITH ivory highlights behind round glasses')
    if name in ('typing_back','typing_side'):
        action += ' Hands stay LOW at waist/lap height below shoulders, elbows down, palms down, not raised fists. Dress drapes over bent knees.'
    if name == 'sleep':
        action += ' Eyelids intentionally closed behind round glasses. Seated skirt folds and bent knees visible.'
    if name.startswith('walk_'):
        action += ' Short brown shoes alternate clearly beneath the dress hem, skirt rocks only subtly. Bun/pencil and shawl do not change shape.'
    spec['action'] = action+' Preserve exact accepted reference identity: '+identity
(root/'request.json').write_text(json.dumps(request,indent=2)+'\n',encoding='utf-8')
(root/'base-prompt.txt').write_text('Generate exactly ONE full-body idle identity reference, facing DOWN toward viewer, top of head slightly visible, feet planted, arms relaxed.\n'+identity+'\n'+style+'\nDraw at clean low resolution then enlarge by integer nearest-neighbor. Generous magenta padding. Attached pixel-agents sheet sets the minimum chunky pixel readability and taste only. Create this ORIGINAL granny, not a reference character. No labels, no other poses. Head about 19 logical pixels tall, body about 27 logical pixels tall. Make glasses fine enough that the expressive eyes and ivory catchlights remain unmistakably visible.\n',encoding='utf-8')
print('Prepared granny-knit request and low-resolution reference composite.')
