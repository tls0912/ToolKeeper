"""Source-worker/native-model regressions plus a diagnostic 40-sentence comparison.

Set TRANSLAMP_RESOURCES to a prepared resource directory. The fixture snapshot
contains known mistranslations: matching it is not semantic-quality acceptance.
Only synthetic fixtures are printed; the production worker saves no user text.
"""
from collections import Counter
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import subprocess
import sys
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "runtime/TransLamp/translate.py"
RESOURCES = Path(os.environ.get("TRANSLAMP_RESOURCES", ROOT / "artifacts/translamp")).resolve()


class NativeWorkerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.python = RESOURCES / "Runtime/python.exe"
        if not cls.python.is_file() or not (RESOURCES / "Models/en-zh/model/model.bin").is_file():
            raise unittest.SkipTest("Prepared TransLamp runtime/models are required.")
        cls.report = {"qualityAccepted": None, "validation": [], "samples": [],
                      "scope": "Source worker contracts and focused regressions; unchanged outputs may still be wrong."}

    @classmethod
    def tearDownClass(cls):
        print("REPORT_JSON=" + json.dumps(cls.report, ensure_ascii=False), flush=True)

    def invoke(self, request):
        start = time.perf_counter()
        process = subprocess.run([str(self.python), "-I", "-B", str(SOURCE)],
            input=json.dumps(request, ensure_ascii=False) + "\n", capture_output=True,
            text=True, encoding="utf-8", timeout=45,
            env=dict(os.environ, PYTHONHOME="invalid-global-python", PYTHONPATH="invalid-global-packages"))
        lines = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
        self.assertTrue(lines, "Worker did not emit JSON")
        self.assertEqual(process.stderr, "", "Worker disclosed native diagnostics")
        return process, lines, time.perf_counter() - start

    def request(self, direction, **values):
        source, target = direction.split("-")
        return {"sourceLanguage": source, "targetLanguage": target,
                "modelPath": str(RESOURCES / "Models" / direction), **values}

    def assert_translation(self, request):
        process, lines, elapsed = self.invoke(request)
        self.assertEqual(process.returncode, 0, lines[-1])
        self.assertEqual(set(lines[-1]), {"text"})
        self.assertTrue(lines[-1]["text"].strip())
        progress = lines[:-1]
        self.assertTrue(progress)
        self.assertEqual([line["progress"] for line in progress], list(range(progress[-1]["total"] + 1)))
        self.assertTrue(all(line["total"] == progress[-1]["total"] for line in progress))
        return lines[-1]["text"], elapsed, progress[-1]["total"]

    def test_validate_loads_both_real_model_directions_without_text(self):
        for direction in ("en-zh", "zh-en"):
            with self.subTest(direction=direction):
                process, lines, elapsed = self.invoke(self.request(direction, action="validate"))
                self.assertEqual((process.returncode, lines), (0, [{"validated": True}]))
                self.report["validation"].append({"case": direction, "validated": True, "seconds": elapsed})

    def test_validate_rejects_corrupt_native_components_without_disclosure(self):
        healthy = RESOURCES / "Models/en-zh"
        # Copies are isolated in a generated temporary directory and removed only
        # after each child worker exits. The installed models are never changed.
        with tempfile.TemporaryDirectory(prefix="translamp-native-validation-") as temporary:
            base = Path(temporary).resolve()
            for component in ("sentencepiece.model", "model/model.bin", "model/shared_vocabulary.json"):
                with self.subTest(component=component):
                    model = base / ("CONFIDENTIAL-SEED-" + component.replace("/", "-"))
                    self.assertEqual(model.resolve().parent, base)
                    shutil.copytree(healthy, model)
                    (model / component).write_bytes(b"CONFIDENTIAL-SEED invalid native model contents")
                    request = self.request("en-zh", action="validate", modelPath=str(model))
                    process, lines, elapsed = self.invoke(request)
                    self.assertEqual(process.returncode, 1)
                    self.assertEqual(len(lines), 1)
                    self.assertEqual(set(lines[0]), {"error"})
                    self.assertNotIn("CONFIDENTIAL-SEED", process.stdout)
                    self.assertNotIn(str(model), process.stdout)
                    self.report["validation"].append({"case": component, "rejected": True, "seconds": elapsed})

    def test_long_semicolon_sentence_keeps_tokens_and_engineering_numbers(self):
        import sentencepiece
        worker = runpy.run_path(str(SOURCE))
        actions = ["先關閉電源", "檢查線路是否正確", "讀取警報記錄", "確認網路連線", "檢查溫度是否過高",
                   "重新啟動服務", "確認馬達已經停止", "保存最新記錄檔", "確認韌體版本正確", "檢查壓力是否正常"]
        text = "；".join(f"步驟 {i:02}：{action}，確認 D{100+i} 的數值是 {24+i}，等待 {100+i} ms 後再繼續"
                         for i, action in enumerate(actions, 1)) + "。"
        tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(RESOURCES / "Models/zh-en/sentencepiece.model"))
        plan = worker["make_plan"](text, tokenizer)
        segments = [tokens for item in plan if isinstance(item, tuple) for tokens in item[1]]
        self.assertEqual([token for segment in segments for token in segment], tokenizer.encode(text, out_type=str))
        self.assertTrue(all(0 < len(segment) <= worker["MAX_SOURCE_TOKENS"] for segment in segments))
        output, elapsed, count = self.assert_translation(self.request("zh-en", text=text))
        expected = Counter(re.findall(r"\d+(?:\.\d+)*", text))
        actual = Counter(re.findall(r"\d+(?:\.\d+)*", output))
        self.report["longSentence"] = {"input": text, "output": output, "seconds": elapsed,
            "segments": count, "segmentTokens": list(map(len, segments)), "tokensExactlyCovered": True,
            "sourceNumbers": dict(expected), "outputNumbers": dict(actual)}
        self.assertEqual(actual, expected, "Engineering values/step numbers changed; inspect the recorded output")

    def test_synthetic_40_sentence_protocol_and_baseline_comparison(self):
        fixture = json.loads(Path(__file__).with_name("translamp-quality-baseline.json").read_text(encoding="utf-8"))
        for sample in fixture["samples"]:
            with self.subTest(sample=sample["id"]):
                request = self.request(f'{sample["sourceLanguage"]}-{sample["targetLanguage"]}', text=sample["input"])
                output, elapsed, segments = self.assert_translation(request)
                self.report["samples"].append({**sample, "output": output, "seconds": elapsed, "segments": segments,
                    "matchesBaseline": output == sample["baselineOutput"]})
        self.report["changedSampleIds"] = [sample["id"] for sample in self.report["samples"] if not sample["matchesBaseline"]]

    def test_real_translation_retains_crlf_indentation_and_blank_lines(self):
        text = "  Dr. Smith changed the limit to 0.8 MPa.\r\n\r\n\t1. Restart the unit.  \n"
        output, _, _ = self.assert_translation(self.request("en-zh", text=text))
        self.assertEqual(re.findall(r"\r\n|\r|\n", output), re.findall(r"\r\n|\r|\n", text))
        self.assertTrue(output.startswith("  ") and output.endswith("  \n") and "\t" in output)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    unittest.main(verbosity=2)
