"""Resource preflight regressions using non-executable, clearly synthetic files."""

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch
import zipfile

SOURCE = Path(__file__).resolve().parents[2] / "runtime" / "TransLamp" / "verify_publish_resources.py"
SPEC = importlib.util.spec_from_file_location("translamp_publish_verifier", SOURCE)
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


class PublishResourceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="translamp-publish-unit-")
        self.root = Path(self.temporary.name)
        self.resources = self.root / "resources"
        self.worker = self.root / "source-worker.py"
        self.worker.write_text("SYNTHETIC NONEXECUTABLE WORKER", encoding="utf-8")
        self.lock_path = self.root / "source-lock.json"
        self.lock = {
            "runtime": "ctranslate2-sentencepiece-v1", "platform": "windows-x64",
            "assets": [
                {"kind": "model", "id": direction, "sourceLanguage": direction[:2], "targetLanguage": direction[3:],
                 "version": "1.9", "url": f"https://example.invalid/{direction}", "license": "CC-BY-4.0 AND MIT"}
                for direction in ("en-zh", "zh-en")
            ],
        }
        prerequisite = self.resources / "Prerequisites" / "VC_redist.x64.exe"
        prerequisite.parent.mkdir(parents=True)
        prerequisite.write_bytes(b"SYNTHETIC NONEXECUTABLE PREREQUISITE")
        self.lock["assets"].append({"kind": "prerequisite", "id": "microsoft-vc-redist-x64", "version": "14.0",
                                    "fileName": prerequisite.name, "size": prerequisite.stat().st_size,
                                    "sha256": VERIFIER.sha256(prerequisite)})
        self.write_json(self.lock_path, self.lock)
        runtime = self.resources / "Runtime"
        for relative in VERIFIER.NATIVE_PATHS:
            path = runtime / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"SYNTHETIC NONEXECUTABLE RUNTIME")
        (runtime / "translate.py").write_bytes(self.worker.read_bytes())
        (runtime / "resources.lock.json").write_bytes(self.lock_path.read_bytes())
        inventory = [self.inventory_file(path, runtime) for path in sorted(runtime.rglob("*")) if path.is_file()]
        self.metadata = {
            "schemaVersion": 2, "runtime": self.lock["runtime"], "platform": self.lock["platform"],
            "resourcesLockSha256": VERIFIER.sha256(self.lock_path), "workerSha256": VERIFIER.sha256(self.worker),
            "runtimeFiles": inventory, "runtimeSize": sum(entry["size"] for entry in inventory),
            "prerequisites": [{"file": prerequisite.name, "size": prerequisite.stat().st_size, "sha256": VERIFIER.sha256(prerequisite)}],
            "sources": copy.deepcopy(self.lock["assets"]), "languagePacks": [],
        }
        self.manifests = {}
        self.payloads = {}
        (self.resources / "LanguagePacks").mkdir()
        for asset in self.lock["assets"][:2]:
            direction = asset["id"]
            payload = {relative: f"SYNTHETIC {relative}".encode() for relative in VERIFIER.REQUIRED_PATHS | {"model/shared_vocabulary.json"}}
            self.payloads[direction] = payload
            manifest = {"schemaVersion": 1, "id": direction, "sourceLanguage": asset["sourceLanguage"], "targetLanguage": asset["targetLanguage"],
                        "runtime": self.lock["runtime"], "packageVersion": "1.0.0", "modelVersion": asset["version"],
                        "displayName": "Synthetic fixture", "modelName": "Synthetic model", "modelSource": asset["url"],
                        "licenseIdentifier": asset["license"], "files": []}
            import hashlib
            manifest["files"] = [{"path": relative, "size": len(content), "sha256": hashlib.sha256(content).hexdigest()} for relative, content in sorted(payload.items())]
            self.manifests[direction] = manifest
            self.repack(direction)
        self.save_metadata()

    def tearDown(self):
        self.temporary.cleanup()

    @staticmethod
    def write_json(path, value):
        path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")

    @staticmethod
    def inventory_file(path, root):
        return {"path": path.relative_to(root).as_posix(), "size": path.stat().st_size, "sha256": VERIFIER.sha256(path)}

    def repack(self, direction, extra=None):
        path = self.resources / "LanguagePacks" / f"TransLamp.LanguagePack.{direction}.1.0.0.tlpack"
        manifest_bytes = json.dumps(self.manifests[direction]).encode()
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("manifest.json", manifest_bytes)
            for relative, content in self.payloads[direction].items():
                archive.writestr(relative, content)
            if extra:
                archive.writestr(*extra)
        entry = {"id": direction, "file": path.name, "size": path.stat().st_size, "sha256": VERIFIER.sha256(path),
                 "installedSize": len(manifest_bytes) + sum(len(content) for content in self.payloads[direction].values()),
                 "packageVersion": "1.0.0", "modelVersion": "1.9"}
        self.metadata["languagePacks"] = [item for item in self.metadata["languagePacks"] if item["id"] != direction] + [entry]

    def save_metadata(self):
        self.write_json(self.resources / "resource-build.json", self.metadata)

    def verify(self):
        self.save_metadata()
        return VERIFIER.verify_resources(self.resources, self.lock_path, self.worker)

    def test_current_inventory_and_two_packs(self):
        report = self.verify()
        self.assertEqual({entry["id"] for entry in report["languagePacks"]}, {"en-zh", "zh-en"})

    def test_old_build_metadata_requires_prepare(self):
        self.metadata.pop("schemaVersion")
        with self.assertRaisesRegex(ValueError, "old"):
            self.verify()

    def test_missing_native_inventory_rejected_even_when_files_removed(self):
        relative = "Lib/site-packages/ctranslate2/ctranslate2.dll"
        (self.resources / "Runtime" / relative).unlink()
        self.metadata["runtimeFiles"] = [entry for entry in self.metadata["runtimeFiles"] if entry["path"] != relative]
        self.metadata["runtimeSize"] = sum(entry["size"] for entry in self.metadata["runtimeFiles"])
        with self.assertRaisesRegex(ValueError, "native runtime inventory"):
            self.verify()

    def test_corrupt_native_file(self):
        (self.resources / "Runtime" / "python312.dll").write_bytes(b"CORRUPTED")
        with self.assertRaisesRegex(ValueError, "mismatch"):
            self.verify()

    def test_stale_extra_native_file(self):
        (self.resources / "Runtime" / "obsolete.dll").write_bytes(b"UNEXPECTED")
        with self.assertRaisesRegex(ValueError, "stale"):
            self.verify()

    def test_official_empty_python_module_can_be_inventoried(self):
        path = self.resources / "Runtime" / "Lib" / "site-packages" / "package" / "__init__.py"
        path.parent.mkdir()
        path.write_bytes(b"")
        self.metadata["runtimeFiles"].append(self.inventory_file(path, self.resources / "Runtime"))
        self.verify()

    def test_required_native_file_cannot_be_empty(self):
        path = self.resources / "Runtime" / "python312.dll"
        path.write_bytes(b"")
        self.metadata["runtimeFiles"] = [entry for entry in self.metadata["runtimeFiles"] if entry["path"] != path.name]
        self.metadata["runtimeFiles"].append(self.inventory_file(path, self.resources / "Runtime"))
        self.metadata["runtimeSize"] = sum(entry["size"] for entry in self.metadata["runtimeFiles"])
        with self.assertRaisesRegex(ValueError, "native runtime inventory"):
            self.verify()

    def test_stale_worker_source(self):
        self.worker.write_text("UPDATED SOURCE", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "worker differs"):
            self.verify()

    def test_stale_source_versions(self):
        self.metadata["sources"][0]["version"] = "9.9"
        with self.assertRaisesRegex(ValueError, "source metadata"):
            self.verify()

    def test_prerequisite_hash_is_pinned_beyond_inventory(self):
        path = self.resources / "Prerequisites" / "VC_redist.x64.exe"
        path.write_bytes(b"CHANGED SYNTHETIC PREREQUISITE")
        self.metadata["prerequisites"][0].update(size=path.stat().st_size, sha256=VERIFIER.sha256(path))
        with self.assertRaisesRegex(ValueError, "pinned source"):
            self.verify()

    def test_corrupt_archive_hash(self):
        pack = self.resources / "LanguagePacks" / self.metadata["languagePacks"][0]["file"]
        pack.write_bytes(b"NOT A ZIP")
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            self.verify()

    def test_payload_hash_is_checked_with_valid_archive_inventory(self):
        self.payloads["en-zh"]["model/model.bin"] = b"CORRUPT SYNTHETIC MODEL"
        self.repack("en-zh")
        with self.assertRaisesRegex(ValueError, "file size mismatch|SHA256 mismatch"):
            self.verify()

    def test_duplicate_direction(self):
        self.metadata["languagePacks"][1]["id"] = self.metadata["languagePacks"][0]["id"]
        with self.assertRaisesRegex(ValueError, "duplicate language pack direction"):
            self.verify()

    def test_manifest_wrong_direction(self):
        self.manifests["en-zh"]["targetLanguage"] = "en"
        self.repack("en-zh")
        with self.assertRaisesRegex(ValueError, "direction mismatch"):
            self.verify()

    def test_manifest_wrong_model_version(self):
        self.manifests["en-zh"]["modelVersion"] = "9.9"
        self.repack("en-zh")
        with self.assertRaisesRegex(ValueError, "pinned source"):
            self.verify()

    def test_manifest_wrong_package_version(self):
        self.manifests["en-zh"]["packageVersion"] = "2.0.0"
        self.repack("en-zh")
        with self.assertRaisesRegex(ValueError, "version mismatch"):
            self.verify()

    def test_archive_undeclared_path(self):
        self.repack("en-zh", ("unknown.dll", b"SYNTHETIC"))
        with self.assertRaisesRegex(ValueError, "undeclared"):
            self.verify()

    def test_archive_traversal(self):
        self.repack("en-zh", ("../escape.txt", b"SYNTHETIC"))
        with self.assertRaisesRegex(ValueError, "Unsafe resource path"):
            self.verify()
        self.assertFalse((self.resources / "escape.txt").exists())

    def test_archive_symlink(self):
        info = zipfile.ZipInfo("symbolic-link")
        info.external_attr = 0o120777 << 16
        self.repack("en-zh", (info, b"../escape"))
        with self.assertRaisesRegex(ValueError, "symbolic link"):
            self.verify()

    def test_smoke_extracts_from_packs_and_cleans_models(self):
        report = self.verify()
        extracted = []

        def fake_worker(arguments, **kwargs):
            request = json.loads(kwargs["input"])
            model = Path(request["modelPath"])
            self.assertTrue((model / "model" / "model.bin").is_file())
            extracted.append(model)
            translated = "請重新啟動設備。" if request["targetLanguage"] == "zh" else "Please restart the device."
            return types.SimpleNamespace(returncode=0, stderr="", stdout=json.dumps({"progress": 0, "total": 1}) + "\n"
                                         + json.dumps({"progress": 1, "total": 1}) + "\n" + json.dumps({"text": translated}))

        with patch.object(VERIFIER.subprocess, "run", side_effect=fake_worker):
            checks = VERIFIER.smoke_resources(self.resources, self.lock_path, report)
        self.assertEqual(len(checks), 2)
        self.assertTrue(all(not path.exists() for path in extracted))

    def test_failed_smoke_cleans_extracted_models(self):
        report = self.verify()
        extracted = []

        def fake_failure(arguments, **kwargs):
            extracted.append(Path(json.loads(kwargs["input"])["modelPath"]))
            return types.SimpleNamespace(returncode=1, stderr="fixture failure", stdout="")

        with patch.object(VERIFIER.subprocess, "run", side_effect=fake_failure), self.assertRaisesRegex(ValueError, "smoke failed"):
            VERIFIER.smoke_resources(self.resources, self.lock_path, report)
        self.assertTrue(all(not path.exists() for path in extracted))


if __name__ == "__main__":
    unittest.main(verbosity=2)
