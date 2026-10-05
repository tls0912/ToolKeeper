"""Build pinned TransLamp resources from a verified local download cache.

The PowerShell bootstrap downloads resources; this stage has no network access.
It never installs global Python packages or changes system configuration.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import sys
import zipfile

PAYLOAD = (
    "sentencepiece.model",
    "model/model.bin",
    "model/config.json",
    "model/shared_vocabulary.json",
)
PACKAGE_VERSION = "1.0.0"


def sha256(path: Path) -> str:
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def verify_asset(path: Path, asset: dict) -> None:
    if not path.is_file() or path.stat().st_size != asset["size"]:
        raise ValueError(f"Resource missing or size mismatch: {asset['fileName']}")
    if sha256(path) != asset["sha256"]:
        raise ValueError(f"Resource SHA256 mismatch: {asset['fileName']}")


def safe_member_path(root: Path, member: str) -> Path:
    path = PurePosixPath(member)
    if path.is_absolute() or not path.parts or any(part in {".", ".."} for part in path.parts):
        raise ValueError("Unsafe archive path.")
    if "\\" in member or ":" in member:
        raise ValueError("Unsafe archive path.")
    destination = root.joinpath(*path.parts)
    if not destination.resolve().is_relative_to(root.resolve()):
        raise ValueError("Archive member escapes output directory.")
    return destination


def extract_wheel(archive_path: Path, destination: Path) -> None:
    with zipfile.ZipFile(archive_path) as archive:
        for member in archive.infolist():
            if member.is_dir():
                continue
            if (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Symbolic links are not allowed in runtime wheels.")
            target = safe_member_path(destination, member.filename)
            # These pinned wheels contain normal platlib content, not installers.
            if ".data/" in member.filename:
                raise ValueError("An unsupported wheel layout needs explicit review.")
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, target.open("wb") as output:
                shutil.copyfileobj(source, output)


def clean_runtime_bytecode(runtime: Path) -> None:
    """Discard only regenerable Python caches; never adopt them as release input."""
    if runtime.is_symlink() or runtime.is_junction():
        raise ValueError("Runtime cannot be a link or junction.")
    root = runtime.resolve(strict=True)
    for cache in runtime.rglob("__pycache__"):
        if cache.is_symlink() or cache.is_junction() or not cache.resolve().is_relative_to(root):
            raise ValueError("Unsafe runtime bytecode cache path.")
        entries = list(cache.iterdir())
        if any(not entry.is_file() or entry.is_symlink() or entry.suffix != ".pyc" for entry in entries):
            raise ValueError("Runtime bytecode cache contains unexpected content.")
        for entry in entries:
            entry.unlink()
        cache.rmdir()


def inventory_runtime(runtime: Path, expected: set[str]) -> list[dict]:
    """Do not legitimize leftover binaries by adding them to a new inventory."""
    actual: dict[str, Path] = {}
    for path in runtime.rglob("*"):
        if path.is_symlink() or path.is_junction():
            raise ValueError("Runtime inventory cannot contain links or junctions.")
        if path.is_file():
            relative = path.relative_to(runtime).as_posix()
            if "__pycache__" in path.relative_to(runtime).parts:
                raise ValueError("Runtime has untracked bytecode; prepare into a clean resource directory.")
            actual[relative] = path
    if set(actual) != expected:
        missing, unexpected = sorted(expected - set(actual)), sorted(set(actual) - expected)
        raise ValueError(f"Runtime inventory differs from pinned inputs. Missing: {missing}; unexpected: {unexpected}")
    return [{"path": name, "size": actual[name].stat().st_size, "sha256": sha256(actual[name])}
            for name in sorted(actual)]


def expected_runtime_files(lock: dict, cache: Path, source_root: Path) -> set[str]:
    expected = {"translate.py", "resources.lock.json", "python312._pth", "THIRD-PARTY-NOTICES.md"}
    expected.update("Licenses/" + path.relative_to(source_root / "licenses").as_posix()
                    for path in (source_root / "licenses").rglob("*") if path.is_file())
    for asset in lock["assets"]:
        if asset["kind"] not in {"python", "wheel"}:
            continue
        prefix = "Lib/site-packages/" if asset["kind"] == "wheel" else ""
        with zipfile.ZipFile(cache / asset["fileName"]) as archive:
            expected.update(prefix + member.filename for member in archive.infolist() if not member.is_dir())
    return expected


def build_pack(asset: dict, cache: Path, output: Path, source_root: Path) -> dict:
    package_root = output / "Models" / asset["id"]
    package_root.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(cache / asset["fileName"]) as archive:
        source_metadata = json.loads(archive.read(asset["archiveRoot"] + "/metadata.json"))
        if (source_metadata["from_code"], source_metadata["to_code"], source_metadata["package_version"]) != (
            asset["sourceLanguage"], asset["targetLanguage"], asset["version"]
        ):
            raise ValueError("Upstream model direction/version differs from the pinned resource.")
        readme = archive.read(asset["archiveRoot"] + "/README.md").decode("utf-8")
        if "CC-BY 4.0" not in readme:
            raise ValueError("The model license declaration changed; review is required.")
        for relative in PAYLOAD:
            target = safe_member_path(package_root, relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(asset["archiveRoot"] + "/" + relative) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)

    license_root = source_root / "licenses"
    license_text = (
        "OPUS-MT source model: CC-BY-4.0.\n"
        "Argos packaging/contributions: MIT (chosen from MIT OR CC0).\n\n"
        + (license_root / "CC-BY-4.0.txt").read_text(encoding="utf-8")
        + "\n\n--- Argos contributions ---\n\n"
        + (license_root / "Argos-MIT.txt").read_text(encoding="utf-8")
    )
    (package_root / "LICENSE").write_text(license_text, encoding="utf-8", newline="\n")
    notice = (
        f"TransLamp {asset['id']} language pack {PACKAGE_VERSION}\n\n"
        f"Model package: {asset['fileName']}\nSource: {asset['url']}\n"
        f"Source package SHA256: {asset['sha256']}\n"
        "Package index: https://github.com/argosopentech/argospm-index\n"
        "CC-BY-4.0: https://creativecommons.org/licenses/by/4.0/\n"
        "Argos model license clarification: https://github.com/argosopentech/argos-translate/issues/533\n\n"
        "TransLamp changes: retains the original CTranslate2 model, vocabulary and\n"
        "SentencePiece model bytes; omits unused Stanza files; adds this notice,\n"
        "license texts, a manifest and checksums. No model retraining is performed.\n"
        "No upstream author or project endorsement is implied.\n"
        "The en-zh model produces Simplified Chinese. Traditional Chinese input\n"
        "may work in zh-en but is not a separately validated language variant.\n\n"
        "Original upstream model README (preserved verbatim):\n\n" + readme
    )
    (package_root / "NOTICE").write_text(notice, encoding="utf-8", newline="\n")
    payload = sorted((*PAYLOAD, "LICENSE", "NOTICE"))
    files = [
        {"path": relative, "size": (package_root / relative).stat().st_size, "sha256": sha256(package_root / relative)}
        for relative in payload
    ]
    if any(file["size"] <= 0 for file in files):
        raise ValueError("A required model payload file is empty.")
    manifest = {
        "schemaVersion": 1,
        "id": asset["id"],
        "sourceLanguage": asset["sourceLanguage"],
        "targetLanguage": asset["targetLanguage"],
        "displayName": asset["displayName"],
        "packageVersion": PACKAGE_VERSION,
        "modelName": f"OPUS-MT {asset['id']} / Argos Translate",
        "modelVersion": asset["version"],
        "runtime": "ctranslate2-sentencepiece-v1",
        "modelSource": asset["url"],
        "licenseIdentifier": asset["license"],
        "files": files,
    }
    manifest_path = package_root / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    pack_directory = output / "LanguagePacks"
    pack_directory.mkdir(parents=True, exist_ok=True)
    pack = pack_directory / f"TransLamp.LanguagePack.{asset['id']}.{PACKAGE_VERSION}.tlpack"
    temporary = pack.with_suffix(".tlpack.tmp")
    # Fixed timestamps/order make the ZIP reproducible. Only files are emitted,
    # never directory entries or executable code from the model source archive.
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for relative in ["manifest.json", *payload]:
            info = zipfile.ZipInfo(relative, (2026, 10, 2, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            with (package_root / relative).open("rb") as source, archive.open(info, "w") as destination:
                shutil.copyfileobj(source, destination)
    temporary.replace(pack)
    return {"id": asset["id"], "file": pack.name, "packageVersion": PACKAGE_VERSION,
            "modelVersion": asset["version"], "size": pack.stat().st_size, "sha256": sha256(pack),
            "installedSize": sum(file["size"] for file in files) + manifest_path.stat().st_size}


def build(output: Path, source_root: Path) -> dict:
    lock = json.loads((source_root / "resources.lock.json").read_text(encoding="utf-8"))
    cache = output / "downloads"
    for asset in lock["assets"]:
        verify_asset(cache / asset["fileName"], asset)
    runtime = output / "Runtime"
    if not (runtime / "python.exe").is_file():
        raise ValueError("Use Prepare-TransLampResources.ps1 to bootstrap the embedded interpreter first.")
    for asset in lock["assets"]:
        if asset["kind"] == "wheel":
            extract_wheel(cache / asset["fileName"], runtime / "Lib" / "site-packages")
    shutil.copyfile(source_root / "translate.py", runtime / "translate.py")
    shutil.copytree(source_root / "licenses", runtime / "Licenses", dirs_exist_ok=True)
    shutil.copyfile(source_root / "resources.lock.json", runtime / "resources.lock.json")
    (runtime / "python312._pth").write_text("python312.zip\n.\nLib/site-packages\n", encoding="utf-8", newline="\n")
    packs = [build_pack(asset, cache, output, source_root) for asset in lock["assets"] if asset["kind"] == "model"]
    for asset in lock["assets"]:
        if asset["kind"] == "prerequisite":
            prerequisites = output / "Prerequisites"
            prerequisites.mkdir(exist_ok=True)
            shutil.copyfile(cache / asset["fileName"], prerequisites / asset["fileName"])
    notices = (
        "# TransLamp third-party runtime components\n\n"
        "This local CPU runtime is assembled from pinned official Python/PyPI releases.\n"
        "Python's license is in LICENSE.txt; added upstream licenses are in Licenses/.\n"
        "NumPy and PyYAML include their license directories under Lib/site-packages/*.dist-info/.\n"
        "All upstream wheel files, metadata and bundled notices are preserved.\n\n"
        "| Component | Version | Declared upstream license |\n| --- | --- | --- |\n"
        + "".join(f"| {asset['id']} | {asset['version']} | {asset['license']} |\n" for asset in lock["assets"] if asset["kind"] != "model")
        + "\nCTranslate2 uses CPU kernels and may include additional native third-party components\n"
        "in its official wheel (e.g. Intel OpenMP/MKL and the cuDNN loader). The application\n"
        "always selects CPU; no GPU driver or CUDA toolkit is needed for translation.\n"
        "A full transitive binary license audit remains a production-release gate.\n\n"
        "The Microsoft Visual C++ x64 runtime is a native prerequisite. When absent,\n"
        "use the included Microsoft-signed Prerequisites/VC_redist.x64.exe on the target\n"
        "computer. This build does not install it or modify system configuration.\n"
        "Microsoft terms: https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files\n\n"
        "Language model authors, CC-BY-4.0 attribution and Argos MIT notices are carried\n"
        "inside each .tlpack. Keep those LICENSE and NOTICE files with the model.\n"
    )
    (runtime / "THIRD-PARTY-NOTICES.md").write_text(notices, encoding="utf-8", newline="\n")
    clean_runtime_bytecode(runtime)
    runtime_files = inventory_runtime(runtime, expected_runtime_files(lock, cache, source_root))
    result = {
        "schemaVersion": 2,
        "runtime": lock["runtime"], "platform": lock["platform"],
        "resourcesLockSha256": sha256(source_root / "resources.lock.json"),
        "workerSha256": sha256(source_root / "translate.py"),
        "runtimeFiles": runtime_files,
        "runtimeSize": sum(item["size"] for item in runtime_files),
        "prerequisites": [{"file": asset["fileName"], "size": asset["size"], "sha256": asset["sha256"]}
                          for asset in lock["assets"] if asset["kind"] == "prerequisite"],
        "languagePacks": packs,
        "sources": lock["assets"],
    }
    (output / "resource-build.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return result


if __name__ == "__main__":
    sys.dont_write_bytecode = True
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    report = build(arguments.output.resolve(), Path(__file__).resolve().parent)
    print(json.dumps({"runtimeSize": report["runtimeSize"], "languagePacks": report["languagePacks"]}, ensure_ascii=False))
