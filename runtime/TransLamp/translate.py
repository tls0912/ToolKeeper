"""One-request, CPU-only offline translation worker for TransLamp.

Run with the bundled interpreter: Runtime/python.exe -I Runtime/translate.py.
stdin and stdout contain UTF-8 JSON lines. Only the last stdout line is a result;
earlier lines may contain progress. User text is never written to logs or files.
"""

from __future__ import annotations

import json
import os
from pathlib import Path
import re
import sys
from typing import Any

RUNTIME_ID = "ctranslate2-sentencepiece-v1"
# Fixed reviewed direct directions, mirrored by the bundled LanguagePackCatalog.
SUPPORTED_DIRECTIONS = tuple(
    direction
    for language in ("zh", "zt", "fr", "pt", "ja", "ko", "es", "de", "it", "ru", "ar", "hi", "th", "vi")
    for direction in (("en", language), (language, "en"))
    if direction != ("es", "en")  # The current official reverse package needs a different BPE tokenizer.
)
MAX_TEXT_CHARACTERS = 20_000
MAX_REQUEST_BYTES = 1_048_576
MAX_SOURCE_TOKENS = 256
MAX_TARGET_TOKENS = 768
TECHNICAL_SPANS = re.compile(
    r'`[^`\r\n]+`|"(?:[A-Za-z]:[\\/]|\\\\)[^"\r\n]+"'
    r"|https?://[^\s<>\"']+|(?:[A-Za-z]:[\\/]|\\\\)[^\s<>\"']+"
    r"|(?<![A-Za-z0-9_])\d+(?:[.,]\d+)+"
)
ABBREVIATION = re.compile(r"(?:^|\s)(?:Dr|Mr|Mrs|Ms|Prof|Sr|Jr|vs|e\.g|i\.e)\.$", re.IGNORECASE)


class TranslationError(Exception):
    """An error whose message is safe to show without disclosing source text."""


def deny_network(event: str, _arguments: tuple[Any, ...]) -> None:
    # No dependency may turn a translation request into a network operation.
    if event in {"socket.connect", "socket.connect_ex", "socket.getaddrinfo", "socket.bind"}:
        raise TranslationError("離線翻譯程序禁止網路連線。")


def emit(message: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def read_request(stream: Any) -> dict[str, Any]:
    raw = stream.readline(MAX_REQUEST_BYTES + 1)
    if not raw or len(raw) > MAX_REQUEST_BYTES:
        raise TranslationError("翻譯請求為空或超過大小限制。")
    try:
        request = json.loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise TranslationError("翻譯請求不是有效的 UTF-8 JSON。") from error
    if not isinstance(request, dict):
        raise TranslationError("翻譯請求格式不正確。")
    action = request.get("action", "translate")
    if action not in ("translate", "validate"):
        raise TranslationError("不支援這個翻譯引擎操作。")
    if action == "translate":
        text = request.get("text")
        if not isinstance(text, str) or not text.strip():
            raise TranslationError("請先輸入要翻譯的文字。")
        if len(text) > MAX_TEXT_CHARACTERS:
            raise TranslationError("每次最多可翻譯 20,000 個字元，請分次翻譯。")
        if "\x00" in text or any(0xD800 <= ord(char) <= 0xDFFF for char in text):
            raise TranslationError("輸入包含不支援的文字編碼。")
    direction = (request.get("sourceLanguage"), request.get("targetLanguage"))
    if direction not in SUPPORTED_DIRECTIONS:
        raise TranslationError("目前不支援這個翻譯方向，請選擇資料管理中列出的語言包。")
    if not isinstance(request.get("modelPath"), str) or not request["modelPath"]:
        raise TranslationError("尚未選擇可用的語言包。")
    return request


def validate_model(request: dict[str, Any]) -> Path:
    root = Path(request["modelPath"])
    if not root.is_absolute() or not root.is_dir():
        raise TranslationError("找不到已安裝的語言包，請重新匯入。")
    try:
        manifest_path = root / "manifest.json"
        if manifest_path.stat().st_size > 1_048_576:
            raise TranslationError("語言包資訊超過大小限制。")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise TranslationError("語言包資訊損壞，請重新匯入。") from error
    if not isinstance(manifest, dict) or manifest.get("schemaVersion") != 1:
        raise TranslationError("不支援這個語言包格式版本。")
    if manifest.get("runtime") != RUNTIME_ID:
        raise TranslationError("語言包需要不同的翻譯引擎版本。")
    if (manifest.get("sourceLanguage"), manifest.get("targetLanguage")) != (
        request["sourceLanguage"], request["targetLanguage"]
    ):
        raise TranslationError("語言包方向與目前選擇不一致。")
    for relative in ("model/model.bin", "sentencepiece.model", "LICENSE", "NOTICE"):
        path = root / relative
        if not path.is_file() or path.stat().st_size == 0:
            raise TranslationError("語言包不完整，請重新匯入。")
    # CT2's old binary format includes its configuration and uses a text vocabulary.
    # Do not synthesize model configuration or alter original tokenizer/vocabulary bytes.
    def nonempty(relative: str) -> bool:
        path = root / relative
        return path.is_file() and path.stat().st_size > 0

    if not nonempty("model/shared_vocabulary.txt") and not (
        nonempty("model/config.json") and any(nonempty(relative) for relative in (
            "model/shared_vocabulary.json", "model/vocabulary.json", "model/source_vocabulary.json"
        ))
    ):
        raise TranslationError("語言包不完整，請重新匯入。")
    return root


def protected_spans(text: str) -> list[tuple[int, int]]:
    """Locate technical text whose internal punctuation is not a clause break."""
    return [(match.start(), match.start() + len(match.group().rstrip(".,!?;:，。！？；：")))
            for match in TECHNICAL_SPANS.finditer(text)]


def split_sentences(text: str) -> list[str]:
    """Keep common titles, numbered instructions and technical punctuation together."""
    spans = protected_spans(text)
    sentences: list[str] = []
    start = 0
    for match in re.finditer(r"(?<=[。！？])|(?<=[.!?])(?=[ \t])", text):
        end = match.end()
        if any(left < end < right for left, right in spans):
            continue
        current = text[start:end].strip()
        if text[end - 1] == "." and (
            ABBREVIATION.search(current) or re.fullmatch(r"(?:\d{1,4}|[A-Za-z])\.", current)
        ):
            continue
        if current:
            sentences.append(current)
        start = end
    if text[start:].strip():
        sentences.append(text[start:].strip())
    return sentences


def token_chunks(tokens: list[str], limit: int = MAX_SOURCE_TOKENS) -> list[list[str]]:
    """Keep all tokens, preferring clauses in long sentences before word boundaries."""
    if limit < 1:
        raise ValueError("The token limit must be positive.")
    if len(tokens) <= limit:
        return [tokens] if tokens else []

    # SentencePiece's space marker is one character, so these offsets also map
    # to the original token boundaries. Only use this view to choose boundaries;
    # the model receives the exact original tokens, without re-tokenization.
    spans = protected_spans("".join(tokens).replace("▁", " "))
    semicolons: list[int] = []
    commas: list[int] = []
    offset = 0
    for index, token in enumerate(tokens, start=1):
        offset += len(token)
        if not any(left < offset < right for left, right in spans):
            if token.endswith((";", "；")) and index < len(tokens):
                semicolons.append(index)
            elif token.endswith((",", "，")):
                commas.append(index)
    if semicolons:
        # A sentence already exceeds the model budget: use its explicit clauses
        # rather than placing several independent instructions into one long run.
        chunks: list[list[str]] = []
        start = 0
        for end in [*semicolons, len(tokens)]:
            chunks.extend(token_chunks(tokens[start:end], limit))
            start = end
        return chunks

    chunks: list[list[str]] = []
    start = 0
    while start < len(tokens):
        end = min(start + limit, len(tokens))
        if end < len(tokens):
            # Chinese with no spaces is split only at tokenizer boundaries. A
            # pathological single long identifier may also require that fallback.
            boundaries = [index for index in commas if start < index <= end]
            if not boundaries:
                boundaries = [index for index in range(start + 1, end + 1) if tokens[index].startswith("▁")]
            if boundaries:
                end = boundaries[-1]
        chunks.append(tokens[start:end])
        start = end
    return chunks


def make_plan(text: str, tokenizer: Any) -> list[Any]:
    """Keep line breaks and indentation, split sentences then bounded token runs."""
    plan: list[Any] = []
    for line in re.split(r"(\r\n|\r|\n)", text):
        if not line or line.isspace():
            plan.append(line)
            continue
        prefix = line[:len(line) - len(line.lstrip())]
        suffix = line[len(line.rstrip()):]
        segments: list[list[str]] = []
        for sentence in split_sentences(line.strip()):
            segments.extend(token_chunks(tokenizer.encode(sentence, out_type=str)))
        if not segments:
            raise TranslationError("輸入無法轉換為翻譯模型可讀取的文字。")
        plan.append((prefix, segments, suffix))
    return plan


def load_engine(root: Path) -> tuple[Any, Any]:
    """Load both native model components for translation and package preflight."""
    try:
        # Imports happen only after input and package validation. No package
        # manager, model downloader, network client or global Python is used.
        import ctranslate2
        import sentencepiece
    except (ImportError, OSError) as error:
        raise TranslationError("翻譯引擎無法載入。請重新取得完整 Offline Kit 與必要執行元件。") from error

    tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(root / "sentencepiece.model"))
    translator = ctranslate2.Translator(
        str(root / "model"), device="cpu", compute_type="int8",
        inter_threads=1, intra_threads=min(4, max(1, os.cpu_count() or 1)),
    )
    return tokenizer, translator


def translate(request: dict[str, Any], progress: Any = emit) -> str:
    tokenizer, translator = load_engine(validate_model(request))
    plan = make_plan(request["text"], tokenizer)
    total = sum(len(item[1]) for item in plan if isinstance(item, tuple))
    if total == 0:
        raise TranslationError("請先輸入要翻譯的文字。")
    completed = 0
    progress({"progress": completed, "total": total})
    output: list[str] = []
    joiner = "" if request["targetLanguage"] in ("zh", "zt", "ja") else " "
    for item in plan:
        if isinstance(item, str):
            output.append(item)
            continue
        prefix, segments, suffix = item
        translated: list[str] = []
        for tokens in segments:
            result = translator.translate_batch(
                [tokens], beam_size=2, max_input_length=0,
                max_decoding_length=MAX_TARGET_TOKENS, return_end_token=True,
                replace_unknowns=True,
            )[0]
            hypothesis = result.hypotheses[0]
            # Never label a hard-cut model result as a complete translation.
            if not hypothesis or hypothesis[-1] != "</s>":
                raise TranslationError("模型未能完整翻譯其中一段，請縮短該段文字後重試。")
            # Argos' converted OPUS vocabularies can retain a literal space
            # marker after SentencePiece decoding. Preserve ordinary underscores
            # in technical identifiers; normalize only the tokenizer marker.
            value = tokenizer.decode(hypothesis[:-1]).replace("▁", " ").strip()
            if request["targetLanguage"] in ("zh", "zt", "ja"):
                value = re.sub(r" +([，。！？：；、])", r"\1", value)
            if not value:
                raise TranslationError("模型回傳空白結果，請調整文字後重試。")
            translated.append(value)
            completed += 1
            progress({"progress": completed, "total": total})
        output.append(prefix + joiner.join(translated) + suffix)
    return "".join(output)


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="strict", newline="\n")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    sys.dont_write_bytecode = True
    sys.addaudithook(deny_network)
    request: dict[str, Any] | None = None
    try:
        request = read_request(sys.stdin.buffer)
        if request.get("action") == "validate":
            load_engine(validate_model(request))
            emit({"validated": True})
        else:
            emit({"text": translate(request)})
        return 0
    except TranslationError as error:
        emit({"error": str(error)})
        return 1
    except Exception:
        # Native/tokenizer exceptions can contain user text or local paths.
        # Do not send those details to stdout, stderr, telemetry, or a file.
        message = ("語言包無法載入。請確認模型與翻譯引擎相容。"
                   if request is not None and request.get("action") == "validate"
                   else "本機翻譯失敗。請確認語言包完整，或縮短文字後重試。")
        emit({"error": message})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
