"""Regression tests for request privacy, bounded input and line reconstruction."""

import importlib.util
import io
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / "runtime" / "TransLamp" / "translate.py"
SPEC = importlib.util.spec_from_file_location("translamp_worker", SOURCE)
WORKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(WORKER)


class Words:
    def encode(self, text, out_type=str):
        return ["▁" + word for word in text.split()]

    def decode(self, tokens):
        return " ".join(token.lstrip("▁") for token in tokens)


class WorkerTests(unittest.TestCase):
    def request(self, **values):
        return {"text": "Example text", "sourceLanguage": "en", "targetLanguage": "zh", "modelPath": "C:/Models/en-zh", **values}

    def read(self, request):
        return WORKER.read_request(io.BytesIO(json.dumps(request).encode("utf-8") + b"\n"))

    def test_utf8_and_bom_are_supported(self):
        request = self.request(text="請重新啟動設備", sourceLanguage="zh", targetLanguage="en")
        encoded = b"\xef\xbb\xbf" + json.dumps(request, ensure_ascii=False).encode("utf-8") + b"\n"
        self.assertEqual(WORKER.read_request(io.BytesIO(encoded)), request)

    def test_invalid_requests_have_errors_without_input(self):
        for request in (None, [], self.request(text=3), self.request(text="   "), self.request(sourceLanguage="fr")):
            with self.subTest(request=request), self.assertRaises(WORKER.TranslationError):
                self.read(request)
        with self.assertRaises(WORKER.TranslationError) as caught:
            WORKER.read_request(io.BytesIO(b'{"text":"CONFIDENTIAL-SEED"'))
        self.assertNotIn("CONFIDENTIAL-SEED", str(caught.exception))

    def test_input_limits_reject_instead_of_truncating(self):
        with self.assertRaises(WORKER.TranslationError):
            self.read(self.request(text="a" * (WORKER.MAX_TEXT_CHARACTERS + 1)))
        with self.assertRaises(WORKER.TranslationError):
            WORKER.read_request(io.BytesIO(b"x" * (WORKER.MAX_REQUEST_BYTES + 1)))
        self.assertEqual(len(self.read(self.request(text="a" * WORKER.MAX_TEXT_CHARACTERS))["text"]), WORKER.MAX_TEXT_CHARACTERS)

    def test_invalid_unicode_is_rejected(self):
        for text in ("before\x00after", "before\ud800after"):
            with self.subTest(text=repr(text)), self.assertRaises(WORKER.TranslationError):
                self.read(self.request(text=text))

    def test_token_chunks_cover_long_input_without_loss(self):
        tokens = ["▁first", *["word"] * 7, "▁second", *["piece"] * 17, "▁last"]
        chunks = WORKER.token_chunks(tokens, limit=10)
        self.assertEqual([token for chunk in chunks for token in chunk], tokens)
        self.assertTrue(all(0 < len(chunk) <= 10 for chunk in chunks))
        self.assertEqual(chunks[1][0], "▁second")

    def test_long_chinese_without_spaces_is_not_truncated(self):
        tokens = list("請檢查連線" * 180)
        chunks = WORKER.token_chunks(tokens)
        self.assertGreater(len(chunks), 1)
        self.assertEqual([token for chunk in chunks for token in chunk], tokens)
        self.assertTrue(all(len(chunk) <= WORKER.MAX_SOURCE_TOKENS for chunk in chunks))

    def test_plan_preserves_blank_lines_indentation_and_crlf(self):
        plan = WORKER.make_plan("  First line. Next sentence.\r\n\r\n\tLast line.  \n", Words())
        self.assertEqual([part for part in plan if isinstance(part, str)], ["\r\n", "", "\r\n", "\n", ""])
        self.assertEqual(plan[0][0], "  ")
        self.assertEqual(plan[4][0], "\t")
        self.assertEqual(plan[4][2], "  ")
        self.assertEqual(len(plan[0][1]), 2)

    def test_plan_does_not_split_internal_technical_dots(self):
        plan = WORKER.make_plan("Use 192.168.1.10 and version 1.2.3. Then retry.", Words())
        self.assertEqual(len(plan[0][1]), 2)

    def test_python_network_attempts_are_blocked(self):
        for event in ("socket.connect", "socket.connect_ex", "socket.getaddrinfo", "socket.bind"):
            with self.subTest(event=event), self.assertRaises(WORKER.TranslationError):
                WORKER.deny_network(event, ())
        WORKER.deny_network("open", ())

    def test_model_direction_and_runtime_are_checked(self):
        with tempfile.TemporaryDirectory(prefix="translamp-test-") as directory:
            root = Path(directory)
            manifest = {"schemaVersion": 1, "runtime": WORKER.RUNTIME_ID, "sourceLanguage": "zh", "targetLanguage": "en"}
            (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(WORKER.TranslationError, "方向"):
                WORKER.validate_model(self.request(modelPath=str(root)))
            manifest.update(sourceLanguage="en", targetLanguage="zh", runtime="other")
            (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(WORKER.TranslationError, "引擎"):
                WORKER.validate_model(self.request(modelPath=str(root)))

    def test_incomplete_decoding_is_never_reported_as_success(self):
        fake_result = types.SimpleNamespace(hypotheses=[["▁partial"]])
        fake_translator = types.SimpleNamespace(translate_batch=lambda *args, **kwargs: [fake_result])
        with patch.object(WORKER, "validate_model", return_value=Path("C:/Models/en-zh")), patch.dict(
            "sys.modules", {
                "ctranslate2": types.SimpleNamespace(Translator=lambda *args, **kwargs: fake_translator),
                "sentencepiece": types.SimpleNamespace(SentencePieceProcessor=lambda **kwargs: Words()),
            }
        ):
            with self.assertRaisesRegex(WORKER.TranslationError, "完整翻譯"):
                WORKER.translate(self.request(), progress=lambda value: None)


if __name__ == "__main__":
    unittest.main(verbosity=2)
