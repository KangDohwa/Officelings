"""Runtime-only depth refinement. Source pixels and seated contact/rank data stay authored.

Run against an existing pack to update only assets.json, or call refine_depth from
build_iso_pack. No imaging dependency is needed for this metadata-only operation.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path


def refine_depth(manifest):
    sheets = {s['id']: s for s in manifest['sheets']}
    for family in manifest.get('furniture', []):
        for variant in family['variants']:
            width, height = variant['footprintW'], variant['footprintH']
            variant['heightPx'] = max(variant.get('heightPx', variant.get('height', 0)),
                variant.get('monitorTop', 0), variant.get('tvTop', 0),
                {'iso/chair': 21, 'iso/sofa': 26}.get(family['id'], 0))
            for part in variant['parts']:
                name = part.get('animation', part.get('clip', ''))
                # Desk structural strip anchors extend one tile beyond the
                # declared body. Use the body's nearest separating boundary.
                # Props keep their actual contacts, including tabletop overhang.
                # Seat-group anchors are interaction contacts and remain intact.
                structure = family['id'] == 'iso/desk' and not name.startswith('monitor-') and name not in ('rear-15', 'rear-16')
                if structure and not part.get('seatGroupId') and (anchor := part.get('depthAnchor')):
                    anchor['i'] = max(-.5, min(width - .5, anchor['i']))
                    anchor['j'] = max(-.5, min(height - .5, anchor['j']))
                if family['id'] == 'iso/desk':
                    rank = 2 if name.startswith('monitor-') or name in ('rear-15', 'rear-16') else 1 if name == 'rear-14' else 0
                    if rank:
                        part['supportRank'] = rank
                # Publish the corrected ordinary anchors in every runtime view.
                sheet = sheets[part['sheetId']]
                for collection in ('rows', 'clips', 'parts'):
                    for clip in sheet.get(collection, []):
                        if clip.get('partId') == part.get('partId'):
                            clip['depthAnchor'] = dict(part['depthAnchor'])
                            if part.get('supportRank'):
                                clip['supportRank'] = part['supportRank']
            for field, rank in (('screen', 3), ('glow', 4)):
                if part := variant.get(field):
                    part['supportRank'] = rank
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding='utf-8'))
    args.manifest.write_text(json.dumps(refine_depth(manifest), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
