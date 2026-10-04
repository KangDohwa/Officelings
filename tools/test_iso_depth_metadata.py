"""No image or network dependencies: validate runtime depth refinement."""
from copy import deepcopy
import json
from pathlib import Path
import unittest
from iso_depth_metadata import refine_depth


class DepthMetadataTests(unittest.TestCase):
    def test_structure_clamps_without_moving_seat_or_props(self):
        parts = [dict(sheetId='desk', animation=name, direction='se', partId=name,
                      depthAnchor=dict(i=1.5, j=2.5), **extra)
                 for name, extra in [('front-00', {}), ('rear-14', dict(seatGroupId='seat', isoZOrder=-20)),
                                     ('monitor-00', {}), ('rear-15', {}), ('rear-16', {})]]
        manifest = dict(sheets=[dict(id='desk', rows=deepcopy(parts), clips=deepcopy(parts))],
                        furniture=[dict(id='iso/desk', variants=[dict(footprintW=1, footprintH=2, height=18,
                            monitorTop=40, parts=parts, screen=dict(sheetId='desk'))])])
        refined = refine_depth(manifest); variant = refined['furniture'][0]['variants'][0]
        self.assertEqual(variant['parts'][0]['depthAnchor'], dict(i=.5, j=1.5))
        for p in variant['parts'][1:]:
            self.assertEqual(p['depthAnchor'], dict(i=1.5, j=2.5))
        self.assertEqual(variant['parts'][1]['isoZOrder'], -20)
        self.assertEqual([p.get('supportRank', 0) for p in parts], [0, 1, 2, 2, 2])
        self.assertEqual(variant['screen']['supportRank'], 3)
        self.assertEqual(variant['heightPx'], 40)

    def test_real_pack_is_idempotent_and_retains_image_hashes(self):
        path = Path(__file__).resolve().parents[1] / 'assets/art/iso/pack/assets.json'
        manifest = json.loads(path.read_text(encoding='utf-8'))
        image_index = deepcopy(manifest['files'])
        refined = refine_depth(deepcopy(manifest))
        self.assertEqual(refined, manifest)
        self.assertEqual(refined['files'], image_index)
        self.assertEqual([s['sha256'] for s in refined['sheets']], [s['sha256'] for s in manifest['sheets']])

    def test_non_desk_anchors_are_preserved(self):
        part = dict(sheetId='plant', animation='rear-00', partId='plant', depthAnchor=dict(i=.75, j=.75))
        manifest = dict(sheets=[dict(id='plant', rows=[deepcopy(part)])], furniture=[dict(id='iso/plant',
            variants=[dict(footprintW=1, footprintH=1, height=34, parts=[part])])])
        self.assertEqual(refine_depth(manifest)['furniture'][0]['variants'][0]['parts'][0]['depthAnchor'], dict(i=.75, j=.75))


if __name__ == '__main__':
    unittest.main()
