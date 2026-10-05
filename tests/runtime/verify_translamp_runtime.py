"""Check real shipped models and JSON worker; emit a reproducible evidence report.

All recorded input/output strings here are fixed public test fixtures. The
application worker never saves a user's source text or translation history.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import zipfile


def check_packs(resources):
    reports = []
    for pack_path in sorted((resources / "LanguagePacks").glob("*.tlpack")):
        with zipfile.ZipFile(pack_path) as archive:
            manifest = json.loads(archive.read("manifest.json"))
            expected = {file["path"] for file in manifest["files"]} | {"manifest.json"}
            assert set(archive.namelist()) == expected, "Pack contains undeclared entries."
            assert len(archive.namelist()) == len(expected), "Pack contains duplicate entries."
            for file in manifest["files"]:
                entry = archive.getinfo(file["path"])
                assert not entry.is_dir() and entry.file_size == file["size"] > 0
                with archive.open(entry) as source:
                    assert hashlib.file_digest(source, "sha256").hexdigest() == file["sha256"]
                with (resources / "Models" / manifest["id"] / file["path"]).open("rb") as installed:
                    assert hashlib.file_digest(installed, "sha256").hexdigest() == file["sha256"]
            assert {"LICENSE", "NOTICE", "sentencepiece.model", "model/model.bin"} <= expected
            reports.append({"id": manifest["id"], "fileCount": len(expected), "size": pack_path.stat().st_size})
    assert {report["id"] for report in reports} == {"en-zh", "zh-en"}
    return reports


def invoke(resources, request):
    environment = dict(os.environ, PYTHONHOME="invalid-global-python", PYTHONPATH="invalid-global-packages")
    started = time.perf_counter()
    process = subprocess.run(
        [str(resources / "Runtime" / "python.exe"), "-I", "-B", str(resources / "Runtime" / "translate.py")],
        input=json.dumps(request, ensure_ascii=False) + "\n", text=True, encoding="utf-8",
        capture_output=True, timeout=120, cwd=str(resources), env=environment,
    )
    elapsed = time.perf_counter() - started
    lines = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
    assert lines, "Worker did not return JSON."
    return process, lines, elapsed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--resources", required=True, type=Path)
    arguments = parser.parse_args()
    resources = arguments.resources.resolve()
    packs = check_packs(resources)
    sentence = "Please restart the device and check the network connection."
    cases = [
        ("en-zh-short", "en", "zh", sentence),
        ("zh-en-traditional", "zh", "en", "請重新啟動設備，然後檢查網路連線。"),
        ("zh-en-simplified", "zh", "en", "请重新启动设备，然后检查网络连接。"),
        ("en-zh-technical", "en", "zh", "PLC connection timeout. Device D100 did not respond within 3000 ms."),
        ("en-zh-marker-regression", "en", "zh", "Connection timed out. Please restart the computer.\r\nThe device did not respond within 3000 ms."),
        ("en-zh-linebreaks", "en", "zh", "  The connection failed.\r\n\r\n\tPlease check the network cable.  \n"),
        ("en-zh-1000", "en", "zh", " ".join([sentence] * 18)),
    ]
    checks = []
    for name, source, target, text in cases:
        request = {"text": text, "sourceLanguage": source, "targetLanguage": target, "modelPath": str(resources / "Models" / f"{source}-{target}")}
        process, lines, elapsed = invoke(resources, request)
        assert process.returncode == 0, f"{name}: {lines[-1]}"
        assert not process.stderr, f"{name}: unexpected stderr output."
        result = lines[-1].get("text")
        assert isinstance(result, str) and result.strip() and result != text, f"{name}: no real translation."
        assert "error" not in lines[-1]
        assert "▁" not in result, f"{name}: internal tokenizer marker leaked into the result."
        if target == "zh":
            assert re.search(r"[\u4e00-\u9fff]", result), f"{name}: no Chinese output."
        else:
            assert re.search(r"[a-zA-Z]", result), f"{name}: no English output."
        progress = [line for line in lines[:-1] if "progress" in line]
        assert progress and progress[0]["progress"] == 0
        assert [line["progress"] for line in progress] == list(range(progress[-1]["total"] + 1))
        assert progress[-1]["progress"] == progress[-1]["total"]
        if name.endswith("linebreaks") or name.endswith("marker-regression"):
            assert re.findall(r"\r\n|\r|\n", result) == re.findall(r"\r\n|\r|\n", text)
        if name.endswith("linebreaks"):
            assert result.startswith("  ") and "\t" in result and result.endswith("  \n")
        checks.append({"name": name, "inputCharacters": len(text), "seconds": round(elapsed, 3), "segments": progress[-1]["total"], "input": text, "output": result})
        print(json.dumps(checks[-1], ensure_ascii=False), flush=True)

    for name, request in (
        ("empty", {"text": "", "sourceLanguage": "en", "targetLanguage": "zh", "modelPath": str(resources / "Models" / "en-zh")}),
        ("wrong-direction", {"text": "CONFIDENTIAL-TEST-FIXTURE", "sourceLanguage": "zh", "targetLanguage": "en", "modelPath": str(resources / "Models" / "en-zh")}),
        ("missing-model", {"text": "CONFIDENTIAL-TEST-FIXTURE", "sourceLanguage": "en", "targetLanguage": "zh", "modelPath": str(resources / "Models" / "missing")}),
    ):
        process, lines, elapsed = invoke(resources, request)
        assert process.returncode != 0 and "error" in lines[-1] and "text" not in lines[-1]
        assert "CONFIDENTIAL-TEST-FIXTURE" not in process.stdout + process.stderr
        checks.append({"name": name, "expectedFailure": True, "seconds": round(elapsed, 3)})

    # Verify the worker's actual audit hook in a fresh process. The hook rejects
    # the name lookup before any connection is attempted.
    offline_probe = (
        "import runpy,sys,socket; "
        "worker=runpy.run_path(sys.argv[1]); "
        "sys.addaudithook(worker['deny_network']); "
        "socket.create_connection(('127.0.0.1',9),timeout=1)"
    )
    probe = subprocess.run([str(resources / "Runtime" / "python.exe"), "-I", "-B", "-c", offline_probe, str(resources / "Runtime" / "translate.py")], capture_output=True, timeout=10)
    assert probe.returncode != 0 and b"TranslationError" in probe.stderr, "Offline network guard failed."
    quality_issues = [
        {"fixture": check["name"], "observation": "The model inserts an irrelevant date-validation fragment into the network-cable translation. This is an unresolved model-quality issue, not a tokenizer error."}
        for check in checks if "date=" in check.get("output", "")
    ]
    report = {"passed": True, "validationScope": "Runtime, resources and process protocol; not human translation-quality acceptance.", "translationQualityAccepted": None, "knownQualityIssues": quality_issues, "packs": packs, "checks": checks, "pythonEnvironmentIsolation": True, "pythonSocketGuard": True, "limitations": ["Measured on one development computer; no clean Windows VM or old-CPU benchmark.", "No operating-system-wide packet capture was performed.", "Human translation quality acceptance and full transitive binary license review remain release gates."]}
    (resources / "verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"PASS: {len(checks)} worker checks, both package hash inventories, Python isolation and socket guard.")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
