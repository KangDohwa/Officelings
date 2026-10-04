"""Geometry and compiled partition regression; emits all available designs.

python -B tools/test_iso_seated_geometry.py
dotnet run --project tests/Vibedotcom.IsoChecks -- --seated-pack artifacts/seatfix/all-designs
"""
import base64
from io import BytesIO
import json
from pathlib import Path
import unittest

import numpy as np
from PIL import Image

import build_iso_pack as pack
import iso_seated_geometry as geometry
import check_iso_chair_fit as fit
import iso_check

PILOTS=pack.CHARACTERS
OUTPUT=pack.ROOT/"artifacts/seatfix/all-designs"


class SeatedGeometryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest,cls.files=pack.assemble(pack.source_paths(pack.ART,PILOTS))
        # Glasses-bun exports sheets but no catalog record yet; fixture-only binding.
        for identity in PILOTS:
            if not any(c["id"]==f"iso/character/{identity}" for c in cls.manifest["characters"]):
                cls.manifest["characters"].append(dict(id=f"iso/character/{identity}",sheetId=identity+"-normal",name=identity))
        # Use shipped pixels for every design, matching the runtime pack.
        shipped=json.loads((pack.ART/"pack/assets.json").read_text(encoding="utf-8"))
        production={s["id"]:s for s in shipped["sheets"] if s["kind"] in ("character","blink")}
        cls.manifest["sheets"]=[production.get(s["id"],s) for s in cls.manifest["sheets"] if s["id"]!="iso-chair-sandwich"]
        for s in production.values():cls.files[s["file"]]=(pack.ART/"pack"/s["file"]).read_bytes()
        geometry.bake(cls.manifest,cls.files)
        pack.link_blinks({s["id"]:s for s in cls.manifest["sheets"]})
        cls.sheets={s["id"]:s for s in cls.manifest["sheets"]}
        cls.images={s["id"]:Image.open(BytesIO(cls.files[s["file"]])) for s in cls.manifest["sheets"]}
        # Output stays in this worktree, never a profile or another lane.
        OUTPUT.mkdir(parents=True,exist_ok=True)
        for name,data in cls.files.items(): pack.atomic_write(OUTPUT/name,data)
        compact=(json.dumps(cls.manifest,ensure_ascii=False,separators=(",",":"))+"\n").encode("utf-8")
        if len(compact)>pack.MAX_JSON: raise ValueError("All-design fixture exceeds runtime manifest limit")
        pack.atomic_write(OUTPUT/"assets.json",compact)
        pack.atomic_write(OUTPUT/"layout.json",pack.json_bytes(pack.make_layout(cls.manifest)))

    def test_camera_depth_is_constant_on_projected_ray(self):
        p=np.array((1.,2.,12.));q=p+np.array((1.,1.,2.))
        np.testing.assert_array_equal(geometry.project([p])[:,:2],geometry.project([q])[:,:2])
        self.assertGreater(geometry.project([q])[0,2],geometry.project([p])[0,2])

    def test_dimensions_palette_and_empty_reconstruction(self):
        palettes={tuple(bytes.fromhex(c)) for c in (*geometry.TEAL,*geometry.METAL)}
        source=json.loads((pack.ART/"furniture-v3/chair/chair.iso.json").read_text(encoding="utf-8"))
        pixels=Image.open(pack.ART/"furniture-v3/chair/chair-parts.png")
        for facing in geometry.FACINGS:
            art,depth,labels=geometry.chair(facing)
            full=np.zeros_like(art)
            for c in source["sheets"][0]["rows"]:
                if c["direction"]!=facing:continue
                part=geometry.source_cell(source["sheets"][0],pixels,c,0)
                self.assertFalse(np.any((full[:,:,3]>0)&(part[:,:,3]>0)))
                full+=part
            np.testing.assert_array_equal(full,art)
            actual=np.array(Image.open(pack.ART/f"furniture-v3/chair/chair-empty-{facing}.png"))
            np.testing.assert_array_equal(full,actual)
            self.assertTrue({tuple(c) for c in art[art[:,:,3]>0,:3]}.issubset(palettes))
            self.assertTrue(np.isfinite(depth[art[:,:,3]>0]).all())
            self.assertTrue(np.any(labels==3),"Backrest lost")

    def test_all_available_designs_pose_frames_match_fresh_z_buffer(self):
        cases=0;intersections=0;changes=0
        s=self.sheets["iso-chair-sandwich"]
        clips={c["name"]+"/"+c["direction"]:c for c in s["clips"]}
        for design in self.manifest["characters"]:
            sheet=self.sheets[design["sheetId"]]
            for clip in sheet["clips"]:
                if clip["name"] not in geometry.POSES:continue
                facing=clip["direction"]
                for frame,binding in enumerate(clip["chairFrames"]):
                    actor=geometry.source_cell(sheet,self.images[sheet["id"]],clip,frame)
                    source=clip["sourceClip"]["name"]
                    parts,hidden,overlap,distance=geometry.oracle(facing,geometry.POSES[source],frame,clip,actor)
                    union=np.zeros_like(parts[0])
                    for key,expected in zip(binding["clips"],parts):
                        actual=geometry.source_cell(s,self.images[s["id"]],clips[key],0)
                        np.testing.assert_array_equal(actual,expected)
                        self.assertFalse(np.any((union[:,:,3]>0)&(actual[:,:,3]>0)))
                        union+=actual
                    np.testing.assert_array_equal(union,geometry.chair(facing)[0])
                    self.assertEqual(binding["hidden"],geometry.bits(hidden))
                    self.assertEqual(binding["overlap"],geometry.bits(overlap))
                    self.assertTrue(np.all(~hidden|overlap))
                    # The oracle must exercise BOTH visible and covered intersections.
                    intersections+=int(overlap.sum());changes+=int((overlap&~hidden).sum());cases+=1
        expected=sum(c["frames"] for s in self.manifest["sheets"] if s["kind"]=="character" for c in s["clips"] if c["name"] in geometry.POSES)
        self.assertEqual(cases,expected)
        self.assertEqual([c["id"] for c in self.manifest["characters"]],
                         [f"iso/character/{identity}" for identity in PILOTS])
        self.assertGreater(changes,0)
        print(f"\nFresh z-buffer: {cases} frames / {intersections} intersections / {changes} must-show pixels")

    def test_z_buffer_detects_wrong_cushion_and_back_ownership(self):
        chair,cd,label=geometry.chair("nw")
        # Two physically separated surfaces on the same ray; order is independent
        # of submission order, proving the renderer is not painter's sorting.
        near=geometry.box(-1,-1,1,1,12,13,1)
        far=geometry.box(-1,-1,1,1,2,3,2)
        a,_,_=geometry.raster(near+far,"nw");b,_,_=geometry.raster(far+near,"nw")
        np.testing.assert_array_equal(a,b)
        self.assertGreater(int((label==2).sum()),0)
        self.assertGreater(int((label==3).sum()),0)

    def test_seated_cushion_fit_against_authored_silhouette(self):
        # This gate is independent of the baked ownership oracle: authored lap
        # rows must sit above the top and the visible seat must fit narrow hips.
        fit.report(fit.measure(OUTPUT, OUTPUT / "fit"), enforce=True)

    def test_empty_assembly_parts_require_exact_union(self):
        arrays={s["id"]:(np.array(self.images[s["id"]]),s) for s in self.manifest["sheets"]}
        empty=set()
        iso_check.seated_assemblies(self.manifest,arrays,empty)
        self.assertTrue(empty,"Slender cushion should exercise fully hidden front cells")
        # A missing source sample must fail, even though empty parts are legal.
        pixels,sheet=arrays["iso-chair-parts"]
        pixels=pixels.copy(); y,x=np.argwhere(pixels[:,:,3]>0)[0]; pixels[y,x]=0
        arrays["iso-chair-parts"]=(pixels,sheet)
        with self.assertRaisesRegex(AssertionError,"union"):
            iso_check.seated_assemblies(self.manifest,arrays,set())


if __name__=="__main__":unittest.main()
