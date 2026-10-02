"""Translate 40 synthetic, non-user examples for human quality inspection."""

import argparse
import importlib.util
import json
from pathlib import Path
import sys

SPEC = importlib.util.spec_from_file_location("verification", Path(__file__).with_name("verify_translamp_runtime.py"))
VERIFICATION = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFICATION)

ENGLISH = [
    "Please restart the device and check the network connection.",
    "Please check the network cable.",
    "Connection timed out. Please restart the computer.",
    "PLC connection timeout. Device D100 did not respond within 3000 ms.",
    "The server at 192.168.1.10 is not responding on port 502.",
    "Error E102: The motor temperature is too high.",
    "Disconnect the power supply before opening the cover.",
    "Press the emergency stop button if the machine makes an unusual noise.",
    "The pressure must remain below 0.8 MPa.",
    "Firmware version 2.3.1 is required for this device.",
    "Save the log file to C:\\Logs\\alarm_2026.txt.",
    "Visit https://example.com/manual for the installation instructions.",
    "Set retry_count to 3 and restart the service.",
    "Received 0xFF instead of 0x00 from the controller.",
    "The sensor is disconnected, but the motor is still running.",
    "Do not remove the USB drive while the update is in progress.",
    "The connection failed.\n\nTry again after ten seconds.",
    "Where is the nearest train station?",
    "I cannot connect to the Internet. Can you help me?",
    "Thank you for your help. I will arrive tomorrow morning.",
]
CHINESE = [
    "請重新啟動設備，然後檢查網路連線。",
    "请检查网线是否连接正确。",
    "連線逾時，請稍後再試。",
    "設備 D100 在 3000 ms 內沒有回應。",
    "伺服器 192.168.1.10 的通訊埠 502 沒有回應。",
    "錯誤 E102：馬達溫度過高。",
    "打開蓋子之前，請先關閉電源。",
    "如果機器發出異常聲音，請按下緊急停止按鈕。",
    "壓力必須保持在 0.8 MPa 以下。",
    "這個設備需要韌體版本 2.3.1。",
    "請把紀錄檔存到 C:\\Logs\\alarm_2026.txt。",
    "請參考 https://example.com/manual 的安裝說明。",
    "將 retry_count 設為 3，然後重新啟動服務。",
    "控制器回傳的是 0xFF，而不是 0x00。",
    "感測器已斷線，但馬達仍在運轉。",
    "更新期間請勿移除 USB 隨身碟。",
    "連線失敗。\n\n請等十秒後再試一次。",
    "最近的火車站在哪裡？",
    "我無法連上網路，可以請你幫忙嗎？",
    "謝謝你的幫忙，我明天早上會到。",
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--resources", type=Path, required=True)
    resources = parser.parse_args().resources.resolve()
    samples = []
    for source, target, fixtures in (("en", "zh", ENGLISH), ("zh", "en", CHINESE)):
        for index, text in enumerate(fixtures, start=1):
            request = {"text": text, "sourceLanguage": source, "targetLanguage": target, "modelPath": str(resources / "Models" / f"{source}-{target}")}
            process, lines, elapsed = VERIFICATION.invoke(resources, request)
            sample = {"id": f"{source}-{target}-{index:02}", "sourceLanguage": source, "targetLanguage": target, "input": text, "output": lines[-1].get("text"), "error": lines[-1].get("error"), "exitCode": process.returncode, "seconds": round(elapsed, 3)}
            samples.append(sample)
            print(json.dumps(sample, ensure_ascii=False), flush=True)
    report = {"purpose": "Synthetic PoC examples for human review, not a benchmark score or acceptance decision.", "modelVersion": "Argos OPUS-MT 1.9", "sampleCount": len(samples), "successCount": sum(item["exitCode"] == 0 for item in samples), "samples": samples}
    (resources / "quality-samples.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
