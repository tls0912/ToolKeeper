"""Validate prepared/shipped TransLamp resources and smoke the shipped worker.

Only the standard library is used by this verifier. The publisher verifies the
embedded interpreter's inventory before executing it. Historical quality reports
are not consumed as proof that the current resources work.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
import zipfile

MAX_PACK_BYTES = 2 * 1024 * 1024 * 1024
PAYLOAD_PATHS = {
    "LICENSE", "NOTICE", "sentencepiece.model", "model/model.bin",
    "model/config.json", "model/shared_vocabulary.json", "model/vocabulary.json",
    "model/source_vocabulary.json", "model/target_vocabulary.json",
}
REQUIRED_PATHS = {"LICENSE", "NOTICE", "sentencepiece.model", "model/model.bin", "model/config.json"}
NATIVE_PATHS = {
    "python.exe", "python312.dll", "python312.zip", "python312._pth", "translate.py", "resources.lock.json",
    "Lib/site-packages/ctranslate2/_ext.cp312-win_amd64.pyd",
    "Lib/site-packages/ctranslate2/ctranslate2.dll",
    "Lib/site-packages/sentencepiece/_sentencepiece.cp312-win_amd64.pyd",
}


def sha256(path: Path) -> str:
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def load_json(path: Path) -> dict:
    with path.open(encoding="utf-8-sig") as source:
        result = json.load(source)
    require(isinstance(result, dict), f"Expected JSON object: {path.name}")
    return result


def long_path(path: Path) -> Path:
    """Use Win32 extended paths for deep, self-contained runtime license trees."""
    if os.name != "nt" or str(path).startswith("\\\\?\\"):
        return path
    absolute = str(path.resolve())
    return Path("\\\\?\\UNC\\" + absolute[2:] if absolute.startswith("\\\\") else "\\\\?\\" + absolute)


def safe_relative(value: object) -> str:
    require(isinstance(value, str) and bool(value), "Missing resource path.")
    path = PurePosixPath(value)
    require(not path.is_absolute() and not any(part in {"", ".", ".."} for part in value.split("/"))
            and "\\" not in value and ":" not in value and "\x00" not in value,
            f"Unsafe resource path: {value!r}")
    return value


def check_regular_tree(root: Path) -> list[Path]:
    require(root.is_dir() and not root.is_symlink() and not root.is_junction(), f"Resource directory missing/linked: {root.name}")
    files = []
    def fail_walk(error: OSError) -> None:
        raise error

    for directory, subdirectories, names in os.walk(root, followlinks=False, onerror=fail_walk):
        for name in [*subdirectories, *names]:
            path = Path(directory) / name
            require(not path.is_symlink() and not path.is_junction(), f"Linked resource is not allowed: {path.name}")
        files.extend(Path(directory) / name for name in names)
    return files


def verify_inventory(root: Path, inventory: object, path_key: str = "path") -> list[dict]:
    root = long_path(root)
    require(isinstance(inventory, list) and bool(inventory), "Missing resource inventory; rerun Prepare-TransLampResources.ps1.")
    actual = {path.relative_to(root).as_posix(): path for path in check_regular_tree(root)}
    expected = set()
    names = set()
    reports = []
    for entry in inventory:
        require(isinstance(entry, dict), "Invalid resource inventory entry.")
        relative = safe_relative(entry.get(path_key))
        require(relative.casefold() not in names, "Duplicate resource inventory path.")
        names.add(relative.casefold())
        expected.add(relative)
        path = actual.get(relative)
        require(path is not None and path.is_file(), f"Missing inventoried resource: {relative}")
        require(type(entry.get("size")) is int and entry["size"] >= 0 and path.stat().st_size == entry["size"],
                f"Resource size mismatch: {relative}")
        require(isinstance(entry.get("sha256"), str) and re.fullmatch(r"[0-9a-f]{64}", entry["sha256"]),
                f"Invalid resource SHA256: {relative}")
        require(sha256(path) == entry["sha256"], f"Resource SHA256 mismatch: {relative}")
        reports.append({"path": relative, "size": entry["size"], "sha256": entry["sha256"]})
    require(set(actual) == expected, "Resource inventory differs from directory contents (missing or stale files).")
    return reports


def verify_pack(path: Path, metadata: dict, asset: dict, runtime: str, destination: Path | None = None) -> dict:
    require(path.name == metadata.get("file") and path.stat().st_size == metadata.get("size")
            and sha256(path) == metadata.get("sha256"), f"Language pack inventory mismatch: {path.name}")
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        require(0 < len(entries) <= 128, "Invalid language pack entry count.")
        names = [safe_relative(entry.filename) for entry in entries]
        require(len({name.casefold() for name in names}) == len(names), "Duplicate language pack entry.")
        require(all(not entry.is_dir() and (entry.external_attr >> 16) & 0o170000 != 0o120000 for entry in entries),
                "Language pack contains directory or symbolic link entries.")
        manifest_entry = archive.getinfo("manifest.json")
        require(0 < manifest_entry.file_size <= 128 * 1024, "Invalid language pack manifest size.")
        manifest = json.loads(archive.read(manifest_entry))
        require(isinstance(manifest, dict), "Invalid language pack manifest.")
        direction = (asset["sourceLanguage"], asset["targetLanguage"])
        require(manifest.get("schemaVersion") == 1 and manifest.get("runtime") == runtime,
                "Incompatible language pack runtime.")
        require(manifest.get("id") == asset["id"] == metadata.get("id")
                and (manifest.get("sourceLanguage"), manifest.get("targetLanguage")) == direction,
                "Language pack direction mismatch.")
        require(manifest.get("modelVersion") == asset["version"] == metadata.get("modelVersion")
                and manifest.get("modelSource") == asset["url"] and manifest.get("licenseIdentifier") == asset["license"],
                "Language pack model metadata differs from pinned source.")
        version = manifest.get("packageVersion")
        require(isinstance(version, str) and re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?", version)
                and version == metadata.get("packageVersion")
                and path.name == f"TransLamp.LanguagePack.{asset['id']}.{version}.tlpack", "Language pack version mismatch.")
        for field in ("displayName", "modelName", "modelVersion", "licenseIdentifier"):
            require(isinstance(manifest.get(field), str) and 0 < len(manifest[field].strip()) <= 512,
                    f"Invalid language pack {field}.")
        files = manifest.get("files")
        require(isinstance(files, list) and 5 <= len(files) <= 100, "Invalid language pack file inventory.")
        payload = set()
        installed_size = manifest_entry.file_size
        total = 0
        for entry in files:
            require(isinstance(entry, dict) and entry.get("path") in PAYLOAD_PATHS, "Invalid language pack payload path.")
            relative = entry["path"]
            require(relative not in payload, "Duplicate language pack payload path.")
            payload.add(relative)
            info = archive.getinfo(relative)
            size = entry.get("size")
            require(type(size) is int and 0 < size <= MAX_PACK_BYTES and info.file_size == size, "Language pack file size mismatch.")
            total += size
            require(total <= MAX_PACK_BYTES, "Language pack exceeds size limit.")
            require(isinstance(entry.get("sha256"), str) and re.fullmatch(r"[0-9a-f]{64}", entry["sha256"]),
                    "Invalid language pack file SHA256.")
            with archive.open(info) as source:
                require(hashlib.file_digest(source, "sha256").hexdigest() == entry["sha256"], "Language pack file SHA256 mismatch.")
            installed_size += size
        require(REQUIRED_PATHS <= payload and bool(payload & {
            "model/shared_vocabulary.json", "model/vocabulary.json", "model/source_vocabulary.json"}), "Language pack required payload missing.")
        require(set(names) == payload | {"manifest.json"}, "Language pack contains undeclared entries.")
        require(installed_size == metadata.get("installedSize"), "Language pack installed size mismatch.")
        if destination is not None:
            require(not destination.exists(), "Smoke model extraction target must be new.")
            destination.mkdir(parents=True)
            for relative in names:
                target = destination.joinpath(*PurePosixPath(relative).parts)
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(relative) as source, target.open("xb") as output:
                    shutil.copyfileobj(source, output)
    return {"id": manifest["id"], "file": path.name, "packageVersion": version,
            "modelVersion": manifest["modelVersion"], "sha256": sha256(path), "installedSize": installed_size}


def verify_resources(resources: Path, lock_path: Path, worker_path: Path) -> dict:
    resources = long_path(resources)
    metadata_path = resources / "resource-build.json"
    metadata = load_json(metadata_path)
    lock = load_json(lock_path)
    require(metadata.get("schemaVersion") == 2, "Resource metadata is old; rerun Prepare-TransLampResources.ps1.")
    require(metadata.get("runtime") == lock.get("runtime") == "ctranslate2-sentencepiece-v1"
            and metadata.get("platform") == lock.get("platform") == "windows-x64", "Resource runtime/platform mismatch.")
    require(metadata.get("resourcesLockSha256") == sha256(lock_path)
            and sha256(resources / "Runtime" / "resources.lock.json") == sha256(lock_path)
            and metadata.get("sources") == lock.get("assets"), "Resource source metadata is stale; rerun Prepare-TransLampResources.ps1.")
    require(metadata.get("workerSha256") == sha256(worker_path)
            and sha256(resources / "Runtime" / "translate.py") == sha256(worker_path),
            "Prepared worker differs from current source; rerun Prepare-TransLampResources.ps1.")
    runtime_files = verify_inventory(resources / "Runtime", metadata.get("runtimeFiles"))
    require(NATIVE_PATHS <= {entry["path"] for entry in runtime_files if entry["size"] > 0}, "Required native runtime inventory is incomplete or empty.")
    require(sum(entry["size"] for entry in runtime_files) == metadata.get("runtimeSize"), "Runtime size metadata mismatch.")
    prerequisites = verify_inventory(resources / "Prerequisites", metadata.get("prerequisites"), "file")
    expected_prerequisites = [asset for asset in lock["assets"] if asset["kind"] == "prerequisite"]
    require({entry["path"] for entry in prerequisites} == {asset["fileName"] for asset in expected_prerequisites}, "Prerequisite inventory mismatch.")
    for asset in expected_prerequisites:
        path = resources / "Prerequisites" / asset["fileName"]
        require(path.stat().st_size == asset["size"] and sha256(path) == asset["sha256"], "Prerequisite differs from pinned source.")
    assets = {asset["id"]: asset for asset in lock["assets"] if asset["kind"] == "model"}
    require(set(assets) == {"en-zh", "zh-en"}, "Default offline kit needs both Chinese/English directions.")
    packs = metadata.get("languagePacks")
    require(isinstance(packs, list) and len(packs) == 2 and {entry.get("id") for entry in packs} == set(assets),
            "Missing or duplicate language pack direction.")
    actual_pack_files = check_regular_tree(resources / "LanguagePacks")
    require({path.relative_to(resources / "LanguagePacks").as_posix() for path in actual_pack_files}
            == {safe_relative(entry.get("file")) for entry in packs}, "Language pack directory contains missing or stale files.")
    reports = [verify_pack(resources / "LanguagePacks" / entry["file"], entry, assets[entry["id"]], lock["runtime"]) for entry in packs]
    return {"resourceMetadataSha256": sha256(metadata_path), "resourcesLockSha256": sha256(lock_path),
            "workerSha256": sha256(worker_path), "runtimeFiles": runtime_files,
            "prerequisites": prerequisites, "languagePacks": reports}


def smoke_resources(resources: Path, lock_path: Path, report: dict) -> list[dict]:
    resources = long_path(resources)
    metadata = load_json(resources / "resource-build.json")
    lock = load_json(lock_path)
    assets = {asset["id"]: asset for asset in lock["assets"] if asset["kind"] == "model"}
    checks = []
    # The final kit deliberately does not ship an unpacked Models directory.
    # The isolated, context-managed directory contains only previously verified
    # fixed payload paths, and is removed on success or failure.
    with tempfile.TemporaryDirectory(prefix="translamp-publish-smoke-") as temporary:
        model_root = Path(temporary)
        for entry in metadata["languagePacks"]:
            verify_pack(resources / "LanguagePacks" / entry["file"], entry, assets[entry["id"]], lock["runtime"], model_root / entry["id"])
        for source, target, text in (
            ("en", "zh", "Please restart the device and check the network connection."),
            ("zh", "en", "請重新啟動設備，然後檢查網路連線。"),
        ):
            request = {"sourceLanguage": source, "targetLanguage": target, "text": text,
                       "modelPath": str(model_root / f"{source}-{target}")}
            process = subprocess.run(
                [str(resources / "Runtime" / "python.exe"), "-I", "-B", str(resources / "Runtime" / "translate.py")],
                input=json.dumps(request, ensure_ascii=False) + "\n", capture_output=True, text=True, encoding="utf-8",
                cwd=resources, env=dict(os.environ, PYTHONHOME="invalid-global-python", PYTHONPATH="invalid-global-packages"), timeout=120,
            )
            require(process.returncode == 0 and not process.stderr, f"Final worker smoke failed: {source}-{target}")
            lines = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
            require(bool(lines) and isinstance(lines[-1].get("text"), str), "Final worker returned no translation.")
            translated = lines[-1]["text"]
            require(translated.strip() != text and bool(translated.strip()) and "▁" not in translated,
                    "Final worker translation is empty or leaks internal markers.")
            require(bool(re.search(r"[\u4e00-\u9fff]" if target == "zh" else r"[A-Za-z]", translated)), "Final worker output language smoke failed.")
            progress = [line for line in lines[:-1] if "progress" in line]
            require(bool(progress) and progress[0].get("progress") == 0 and progress[-1].get("progress") == progress[-1].get("total"),
                    "Final worker progress protocol smoke failed.")
            checks.append({"direction": f"{source}-{target}", "passed": True, "input": text, "output": translated})
    return checks


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--resources", required=True, type=Path)
    parser.add_argument("--lock", required=True, type=Path)
    parser.add_argument("--source-worker", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--smoke", action="store_true")
    arguments = parser.parse_args()
    report = verify_resources(arguments.resources, arguments.lock, arguments.source_worker)
    report.update({"schemaVersion": 1, "verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
                   "passed": True, "validationScope": "Current resource integrity and worker operability; no semantic translation-quality acceptance.",
                   "translationQualityAccepted": None, "smokeChecks": []})
    if arguments.smoke:
        report["smokeChecks"] = smoke_resources(arguments.resources, arguments.lock, report)
    arguments.report.parent.mkdir(parents=True, exist_ok=True)
    arguments.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: current resources, {len(report['runtimeFiles'])} runtime files, both language packs, {len(report['smokeChecks'])} smoke checks.")


if __name__ == "__main__":
    main()
