import importlib.util
import pathlib
import unittest


SCRIPT = pathlib.Path(__file__).parents[1] / "embedded-tools" / "rve-segmented-backend.py"
SPEC = importlib.util.spec_from_file_location("rve_segmented_backend", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class SegmentedBackendContractTests(unittest.TestCase):
    def test_contiguous_segments_with_same_output_size_are_valid(self):
        width, height = MODULE.validate_segments([
            {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2,
             "outputWidth": 640, "outputHeight": 360},
            {"start": 4, "end": 8, "backend": "cuda", "model": "b", "scale": 2,
             "outputWidth": 640, "outputHeight": 360},
        ], 8)
        self.assertEqual((width, height), (640, 360))

    def test_gap_or_duplicate_boundary_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "必须从第 4 帧开始"):
            MODULE.validate_segments([
                {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2,
                 "outputWidth": 640, "outputHeight": 360},
                {"start": 3, "end": 8, "backend": "ncnn", "model": "b", "scale": 2,
                 "outputWidth": 640, "outputHeight": 360},
            ], 8)

    def test_output_size_must_match_across_backends(self):
        with self.assertRaisesRegex(ValueError, "与全片 640x360 不一致"):
            MODULE.validate_segments([
                {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2,
                 "outputWidth": 640, "outputHeight": 360},
                {"start": 4, "end": 8, "backend": "cuda", "model": "b", "scale": 2,
                 "outputWidth": 1280, "outputHeight": 720},
            ], 8)

    def test_tile_size_and_rve_ncnn_path_are_available(self):
        segmented_source = SCRIPT.read_text(encoding="utf-8")
        image_source = (SCRIPT.parent / "rve-image-backend.py").read_text(encoding="utf-8")
        self.assertIn('parser.add_argument("--tile-size"', segmented_source)
        self.assertIn("use_rve_ncnn=backend == \"ncnn\"", segmented_source)
        self.assertIn("from src.ncnn.UpscaleNCNN import UpscaleNCNN", image_source)
        self.assertIn("def process_bytes", image_source)


if __name__ == "__main__":
    unittest.main()
