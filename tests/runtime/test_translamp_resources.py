"""Checks that resource metadata cannot silently adopt stale runtime binaries."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile


SPEC = importlib.util.spec_from_file_location(
    "translamp_builder", Path(__file__).resolve().parents[2] / "runtime/TransLamp/build_resources.py")
BUILDER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BUILDER)


class ResourceInventoryTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="TransLamp.InventoryTests.")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def write(self, relative, content=b"fixture"):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return path

    def test_exact_files_have_deterministic_path_size_and_hash(self):
        self.write("Runtime/python.exe", b"interpreter fixture")
        self.write("Runtime/Lib/site-packages/model.pyd", b"native fixture")
        records = BUILDER.inventory_runtime(self.root / "Runtime", {"python.exe", "Lib/site-packages/model.pyd"})
        self.assertEqual(["Lib/site-packages/model.pyd", "python.exe"], [record["path"] for record in records])
        self.assertEqual(len(b"native fixture"), records[0]["size"])
        self.assertEqual(hashlib.sha256(b"native fixture").hexdigest(), records[0]["sha256"])

    def test_stale_dll_is_rejected_instead_of_becoming_trusted_inventory(self):
        self.write("Runtime/python.exe")
        self.write("Runtime/obsolete.dll")
        with self.assertRaisesRegex(ValueError, "unexpected.*obsolete.dll"):
            BUILDER.inventory_runtime(self.root / "Runtime", {"python.exe"})

    def test_missing_pinned_file_is_rejected(self):
        self.write("Runtime/python.exe")
        with self.assertRaisesRegex(ValueError, "Missing.*python312.dll"):
            BUILDER.inventory_runtime(self.root / "Runtime", {"python.exe", "python312.dll"})

    def test_untracked_bytecode_requires_clean_build(self):
        self.write("Runtime/python.exe")
        self.write("Runtime/__pycache__/worker.pyc")
        with self.assertRaisesRegex(ValueError, "untracked bytecode"):
            BUILDER.inventory_runtime(self.root / "Runtime", {"python.exe"})

    def test_cleanup_removes_only_regenerable_bytecode(self):
        runtime_file = self.write("Runtime/python.exe")
        cache = self.write("Runtime/package/__pycache__/module.cpython-312.pyc")
        BUILDER.clean_runtime_bytecode(self.root / "Runtime")
        self.assertTrue(runtime_file.exists())
        self.assertFalse(cache.parent.exists())

    def test_cleanup_preserves_unexpected_cache_content_and_fails(self):
        cache = self.write("Runtime/package/__pycache__/unowned.dll")
        with self.assertRaisesRegex(ValueError, "unexpected content"):
            BUILDER.clean_runtime_bytecode(self.root / "Runtime")
        self.assertTrue(cache.exists())

    def test_expected_set_comes_from_pinned_archives_and_owned_sources(self):
        self.write("sources/licenses/NOTICE.txt")
        self.write("downloads/python.zip", b"")
        with zipfile.ZipFile(self.root / "downloads/python.zip", "w") as archive:
            archive.writestr("python.exe", b"fixture")
        with zipfile.ZipFile(self.root / "downloads/package.whl", "w") as archive:
            archive.writestr("package/", b"")
            archive.writestr("package/native.pyd", b"fixture")
        lock = {"assets": [
            {"kind": "python", "fileName": "python.zip"},
            {"kind": "wheel", "fileName": "package.whl"},
            {"kind": "model", "fileName": "not-opened.argosmodel"},
        ]}
        expected = BUILDER.expected_runtime_files(lock, self.root / "downloads", self.root / "sources")
        self.assertEqual({"python.exe", "Lib/site-packages/package/native.pyd", "Licenses/NOTICE.txt",
                          "translate.py", "resources.lock.json", "python312._pth", "THIRD-PARTY-NOTICES.md"}, expected)


if __name__ == "__main__":
    unittest.main(verbosity=2)
