using ToolKeeper.UI;

namespace TransLamp;

internal static class Strings
{
    public static IEnumerable<KeyValuePair<string, string>> ForLanguage(string language)
    {
        foreach (var (key, en, zh, ja) in Entries)
            yield return KeyValuePair.Create("Lamp." + key, UiLanguage.Text(language, en, zh, ja));
    }

    private static readonly (string Key, string English, string Chinese, string Japanese)[] Entries =
    [
        ("Description", "Understand a little more, even without the internet.", "沒有網路時，也能先看懂文字的大意。", "ネットにつながらなくても、まずは文章の大意を。"),
        ("Offline", "OFFLINE · Text stays on this device", "完全離線 · 文字留在本機", "完全オフライン · テキストはこの端末内で処理"),
        ("From", "Translate from", "來源語言", "翻訳元"),
        ("To", "Translate to", "目標語言", "翻訳先"),
        ("Swap", "Swap translation direction", "交換翻譯方向", "翻訳方向を交換"),
        ("Translate", "Translate", "翻譯", "翻訳"),
        ("Cancel", "Cancel", "取消", "キャンセル"),
        ("Source", "Source text", "原文", "原文"),
        ("Result", "Translation", "譯文", "翻訳結果"),
        ("Placeholder", "Paste a message, a paragraph, or a short manual excerpt here…", "在此貼上訊息、段落或手冊片段…", "メッセージ、段落、マニュアルの一部などを貼り付けてください…"),
        ("ResultPlaceholder", "Your offline translation will appear here.", "離線翻譯結果會顯示在這裡。", "オフライン翻訳の結果がここに表示されます。"),
        ("Paste", "Paste", "貼上", "貼り付け"),
        ("Clear", "Clear", "清除", "消去"),
        ("Copy", "Copy translation", "複製譯文", "翻訳結果をコピー"),
        ("Resize", "Resize source and translation panes", "調整原文與譯文欄寬", "原文と翻訳結果の幅を調整"),
        ("Packs", "Language packs", "語言包管理", "言語パック"),
        ("Import", "Import .tlpack", "匯入 .tlpack", ".tlpack をインポート"),
        ("PackHint", "Import a language pack from local storage or USB. No download or account is required.", "從本機或 USB 匯入語言包，無須連網或登入帳號。", "ローカルまたは USB から言語パックをインポートします。ネット接続やアカウントは不要です。"),
        ("NoPacks", "No language packs installed. Import a trusted TransLamp .tlpack file to get started.", "尚未安裝語言包。匯入可信來源的 TransLamp .tlpack 檔案即可準備使用。", "言語パックがありません。信頼できる TransLamp .tlpack ファイルをインポートしてください。"),
        ("Remove", "Remove", "移除", "削除"),
        ("LiteralWarning", "Check possible changes to source literals (translation/source counts):", "原文字面值可能缺漏或改變；請核對（譯文／原文次數）：", "原文の数値・識別子の欠落や変更を照合してください（訳文／原文の回数）："),
        ("Quality", "For understanding the gist. Machine translation may miss meaning; verify critical details. Chinese output may use simplified characters.", "以看懂大意為目標。機器翻譯可能有誤，重要資訊請對照原文；中文譯文可能使用簡體字。", "大意をつかむための翻訳です。誤訳の可能性があるため、重要な内容は原文と照合してください。中国語の訳文は簡体字になる場合があります。")
    ];
}
