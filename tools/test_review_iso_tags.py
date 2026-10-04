import base64
import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image
from review_iso_tags import review


class TagRenderReviewTests(unittest.TestCase):
    def result(self, positions):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            tags = [dict(rect=dict(IsEmpty=False, X=x, Y=y, Width=1, Height=1)) for x, y in positions]
            record = dict(name='fixture', Width=20, Height=20, visible=len(tags), rendered=len(tags), tags=tags,
                          bodies=[dict(X=4, Y=4, Width=1, Height=1, alpha=base64.b64encode(b'\xff').decode())])
            (root / 'tags.json').write_text(json.dumps([record]), encoding='utf-8')
            Image.new('RGBA', (20, 20), 'black').save(root / 'fixture.png')
            return review(root)[0]

    def test_counts_opaque_body_and_intersecting_plate_pixels(self):
        result = self.result([(4, 4), (4, 4)])
        self.assertEqual(1, result['agentPixels'])
        self.assertEqual(9, result['tagPixels'])

    def test_adjacent_outlines_do_not_overlap(self):
        result = self.result([(4, 4), (7, 4)])
        self.assertEqual(0, result['tagPixels'])
        self.assertEqual(0, result['outsidePixels'])

    def test_includes_the_border_outside_the_view(self):
        self.assertEqual(5, self.result([(0, 0)])['outsidePixels'])


if __name__ == '__main__':
    unittest.main()
