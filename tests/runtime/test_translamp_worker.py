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


class Console(io.StringIO):
    def reconfigure(self, **kwargs):
        pass


class WorkerTests(unittest.TestCase):
    def request(self, **values):
        return {"text": "Example text", "sourceLanguage": "en", "targetLanguage": "zh", "modelPath": "C:/Models/en-zh", **values}

    def read(self, request):
        return WORKER.read_request(io.BytesIO(json.dumps(request).encode("utf-8") + b"\n"))

    def invoke_main(self, request):
        source = types.SimpleNamespace(buffer=io.BytesIO(json.dumps(request).encode("utf-8") + b"\n"))
        output, errors = Console(), Console()
        with patch.object(WORKER.sys, "stdin", source), patch.object(WORKER.sys, "stdout", output), \
                patch.object(WORKER.sys, "stderr", errors), patch.object(WORKER.sys, "addaudithook"):
            code = WORKER.main()
        return code, [json.loads(line) for line in output.getvalue().splitlines()], errors.getvalue()

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

    def test_validate_request_needs_model_and_direction_but_not_text(self):
        request = self.request(action="validate")
        del request["text"]
        self.assertEqual(self.read(request), request)
        for changes in ({"action": "download"}, {"action": []}, {"sourceLanguage": "ja"}, {"modelPath": ""}):
            with self.subTest(changes=changes), self.assertRaises(WORKER.TranslationError):
                self.read({**request, **changes})
        with self.assertRaises(WORKER.TranslationError):
            self.read({**request, "action": "translate"})

    def test_reviewed_directions_are_supported_without_pivot_translation(self):
        self.assertEqual(len(WORKER.SUPPORTED_DIRECTIONS), 27)
        for source, target in WORKER.SUPPORTED_DIRECTIONS:
            with self.subTest(source=source, target=target):
                request = self.request(sourceLanguage=source, targetLanguage=target)
                self.assertEqual(self.read(request), request)
        for source, target in (("fr", "pt"), ("ja", "zh"), ("es", "en"), ("en", "id"), ("id", "en"), ("en", "xx"), ("fr", "fr")):
            with self.subTest(source=source, target=target), self.assertRaises(WORKER.TranslationError):
                self.read(self.request(sourceLanguage=source, targetLanguage=target))

    def test_sentence_outputs_use_the_target_languages_spacing(self):
        result = types.SimpleNamespace(hypotheses=[["▁Translated", "</s>"]])
        translator = types.SimpleNamespace(translate_batch=lambda *args, **kwargs: [result])
        for target, expected in (("fr", "Translated Translated"), ("pt", "Translated Translated"),
                                 ("th", "Translated Translated"), ("ko", "Translated Translated"),
                                 ("zh", "TranslatedTranslated"), ("zt", "TranslatedTranslated"), ("ja", "TranslatedTranslated")):
            with self.subTest(target=target), patch.object(WORKER, "validate_model", return_value=Path("C:/Fixture")), \
                    patch.object(WORKER, "load_engine", return_value=(Words(), translator)):
                self.assertEqual(WORKER.translate(self.request(text="First sentence. Second sentence.", targetLanguage=target), progress=lambda _: None), expected)

    def test_chinese_and_japanese_punctuation_has_no_tokenizer_space(self):
        result = types.SimpleNamespace(hypotheses=[["▁譯文", "▁。", "</s>"]])
        translator = types.SimpleNamespace(translate_batch=lambda *args, **kwargs: [result])
        for target in ("zh", "zt", "ja"):
            with self.subTest(target=target), patch.object(WORKER, "validate_model", return_value=Path("C:/Fixture")), \
                    patch.object(WORKER, "load_engine", return_value=(Words(), translator)):
                self.assertEqual(WORKER.translate(self.request(targetLanguage=target), progress=lambda _: None), "譯文。")

    def test_validate_action_loads_engine_and_emits_only_validation_result(self):
        request = self.request(action="validate")
        del request["text"]
        model = Path(request["modelPath"])
        with patch.object(WORKER, "validate_model", return_value=model), \
                patch.object(WORKER, "load_engine") as load_engine, patch.object(WORKER, "translate") as translate:
            code, lines, errors = self.invoke_main(request)
        self.assertEqual((code, lines, errors), (0, [{"validated": True}], ""))
        load_engine.assert_called_once_with(model)
        translate.assert_not_called()

    def test_native_validation_error_never_discloses_exception_or_path(self):
        request = self.request(action="validate", modelPath="C:/CONFIDENTIAL-SEED/model")
        with patch.object(WORKER, "validate_model", return_value=Path(request["modelPath"])), \
                patch.object(WORKER, "load_engine", side_effect=RuntimeError("native details: CONFIDENTIAL-SEED")):
            code, lines, errors = self.invoke_main(request)
        self.assertEqual(code, 1)
        self.assertEqual(len(lines), 1)
        self.assertEqual(set(lines[0]), {"error"})
        self.assertNotIn("CONFIDENTIAL-SEED", json.dumps(lines))
        self.assertEqual(errors, "")

    def test_translation_actions_keep_the_existing_result_protocol(self):
        for action in (None, "translate"):
            request = self.request(**({"action": action} if action else {}))
            with self.subTest(action=action), patch.object(WORKER, "translate", return_value="譯文"):
                self.assertEqual(self.invoke_main(request), (0, [{"text": "譯文"}], ""))

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

    def test_only_long_sentences_are_split_into_semicolon_clauses(self):
        tokens = ["▁first", ";", "▁second", "；", "▁third", ";", "▁last"]
        self.assertEqual(WORKER.token_chunks(tokens, limit=7), [tokens])
        chunks = WORKER.token_chunks(tokens, limit=4)
        self.assertEqual(chunks, [tokens[:2], tokens[2:4], tokens[4:6], tokens[6:]])
        self.assertEqual([token for chunk in chunks for token in chunk], tokens)

    def test_clause_splitting_preserves_technical_punctuation(self):
        for technical in ("https://x/a;b", "C:\\Logs\\a;b.txt", "`key;a,b`", "1,000.25"):
            text = "start; " + technical + " end; finish"
            tokens = list(text.replace(" ", "▁"))
            chunks = WORKER.token_chunks(tokens, limit=len(technical) + 7)
            with self.subTest(technical=technical):
                self.assertEqual([token for chunk in chunks for token in chunk], tokens)
                self.assertTrue(any(technical in "".join(chunk).replace("▁", " ") for chunk in chunks))
                self.assertTrue(all(0 < len(chunk) <= len(technical) + 7 for chunk in chunks))

    def test_long_clause_falls_back_to_commas_before_words_without_losing_tokens(self):
        tokens = ["▁first", "part", ",", "▁second", "part", "more", "▁third", "part"]
        chunks = WORKER.token_chunks(tokens, limit=6)
        self.assertEqual(chunks[0], tokens[:3])
        self.assertEqual([token for chunk in chunks for token in chunk], tokens)
        self.assertTrue(all(0 < len(chunk) <= 6 for chunk in chunks))

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

    def test_titles_abbreviations_and_numbered_steps_stay_with_their_sentence(self):
        for source, expected in (
            ("Dr. Smith changed the limit to 0.8 MPa. Restart the unit.",
             ["Dr. Smith changed the limit to 0.8 MPa.", "Restart the unit."]),
            ("1. Stop the machine. 2. Turn off the power.", ["1. Stop the machine.", "2. Turn off the power."]),
            ("Ask Mr. Smith or Prof. Lee. Use e.g. 0.8 MPa.", ["Ask Mr. Smith or Prof. Lee.", "Use e.g. 0.8 MPa."]),
            ("Open https://example.com/a;b?c=1. Then use C:\\Logs\\a;b.txt.",
             ["Open https://example.com/a;b?c=1.", "Then use C:\\Logs\\a;b.txt."]),
        ):
            with self.subTest(source=source):
                self.assertEqual(WORKER.split_sentences(source), expected)

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

    def test_legacy_and_current_layouts_are_validated_without_fabricating_files(self):
        for legacy in (False, True):
            with self.subTest(legacy=legacy), tempfile.TemporaryDirectory(prefix="translamp-layout-") as directory:
                root = Path(directory)
                manifest = {"schemaVersion": 1, "runtime": WORKER.RUNTIME_ID, "sourceLanguage": "en", "targetLanguage": "ja"}
                (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
                (root / "model").mkdir()
                for relative in ("model/model.bin", "sentencepiece.model", "LICENSE", "NOTICE"):
                    (root / relative).write_text("structural fixture", encoding="utf-8")
                vocabulary = root / "model" / ("shared_vocabulary.txt" if legacy else "shared_vocabulary.json")
                vocabulary.write_text("<unk>\n" if legacy else "[]", encoding="utf-8")
                config = root / "model/config.json"
                if not legacy:
                    config.write_text("{}", encoding="utf-8")
                request = self.request(modelPath=str(root), targetLanguage="ja")
                self.assertEqual(WORKER.validate_model(request), root)
                self.assertEqual(config.exists(), not legacy)
                if not legacy:
                    config.unlink()
                    with self.assertRaisesRegex(WORKER.TranslationError, "不完整"):
                        WORKER.validate_model(request)
                    config.write_text("{}", encoding="utf-8")
                vocabulary.unlink()
                with self.assertRaisesRegex(WORKER.TranslationError, "不完整"):
                    WORKER.validate_model(request)

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
