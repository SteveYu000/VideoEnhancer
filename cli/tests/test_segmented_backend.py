import importlib.util
import pathlib
import unittest


SCRIPT = pathlib.Path(__file__).parents[1] / "embedded-tools" / "rve-segmented-backend.py"
SPEC = importlib.util.spec_from_file_location("rve_segmented_backend", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class SegmentedBackendContractTests(unittest.TestCase):
    def test_contiguous_same_backend_and_scale_is_valid(self):
        backend, scale = MODULE.validate_segments([
            {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2},
            {"start": 4, "end": 8, "backend": "ncnn", "model": "b", "scale": 2},
        ], 8)
        self.assertEqual((backend, scale), ("ncnn", 2))

    def test_gap_or_duplicate_boundary_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "必须从第 4 帧开始"):
            MODULE.validate_segments([
                {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2},
                {"start": 3, "end": 8, "backend": "ncnn", "model": "b", "scale": 2},
            ], 8)

    def test_backend_and_scale_are_locked_to_first_segment(self):
        with self.assertRaisesRegex(ValueError, "相同的后端类别"):
            MODULE.validate_segments([
                {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2},
                {"start": 4, "end": 8, "backend": "cuda", "model": "b", "scale": 2},
            ], 8)
        with self.assertRaisesRegex(ValueError, "相同的放大倍率"):
            MODULE.validate_segments([
                {"start": 1, "end": 3, "backend": "ncnn", "model": "a", "scale": 2},
                {"start": 4, "end": 8, "backend": "ncnn", "model": "b", "scale": 4},
            ], 8)


if __name__ == "__main__":
    unittest.main()
