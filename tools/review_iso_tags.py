"""Count rendered tag outlines intersecting opaque agent pixels and other tags."""
import argparse
import base64
import json
from pathlib import Path

import numpy as np
from PIL import Image


def review(directory):
    results = []
    for record in json.loads((directory / 'tags.json').read_text(encoding='utf-8')):
        width, height = record['Width'], record['Height']
        body = np.zeros((height, width), bool)
        for item in record['bodies']:
            alpha = np.frombuffer(base64.b64decode(item['alpha']), np.uint8).reshape(item['Height'], item['Width'])
            x, y = item['X'], item['Y']
            left, top, right, bottom = max(0, x), max(0, y), min(width, x+item['Width']), min(height, y+item['Height'])
            if right > left and bottom > top:
                body[top:bottom, left:right] |= alpha[top-y:bottom-y, left-x:right-x] > 0
        plates = np.zeros((height, width), np.uint16)
        outside = 0
        for tag in record['tags']:
            rect = tag['rect']
            if rect['IsEmpty']:
                continue
            x, y, w, h = (int(rect[k]) for k in ('X', 'Y', 'Width', 'Height'))
            left, top, right, bottom = x-1, y-1, x+w+1, y+h+1
            outside += (w+2)*(h+2) - max(0, min(width, right)-max(0, left))*max(0, min(height, bottom)-max(0, top))
            plates[max(0, top):min(height, bottom), max(0, left):min(width, right)] += 1
        with Image.open(directory / (record['name']+'.png')) as png:
            assert png.size == (width, height)
        results.append(dict(scene=record['name'], visible=record['visible'], rendered=record['rendered'],
                            agentPixels=int(np.count_nonzero(body & (plates > 0))),
                            tagPixels=int(np.count_nonzero(plates > 1)), outsidePixels=outside))
    return results


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--require-clear', action='store_true')
    args = parser.parse_args()
    rows = review(args.directory)
    print(json.dumps(rows, indent=2))
    if args.require_clear:
        assert all(r['agentPixels'] == r['tagPixels'] == r['outsidePixels'] == 0 and
                   r['visible'] == r['rendered'] for r in rows), 'Tag collision or missing plate'
