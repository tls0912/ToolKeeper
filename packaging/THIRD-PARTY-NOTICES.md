# MarkPad third-party notices

MarkPad uses the following components. Their original licenses and notices are preserved in the `licenses/` folder. Package names, versions, license declarations, and repository revisions below were verified against the restored NuGet `.nuspec` files on 2026-09-22.

| Component | Version | Use | License source |
| --- | --- | --- | --- |
| AvalonEdit | 6.3.1.120 | WPF text editing | MIT; [upstream LICENSE at package commit](https://raw.githubusercontent.com/icsharpcode/AvalonEdit/862415d51eddc9eac93f462dbc522ffbf929cd52/LICENSE), copied as `AvalonEdit-LICENSE.txt` |
| Markdig | 1.4.0 | Markdown parsing and HTML rendering | BSD-2-Clause; [upstream license.txt at package commit](https://raw.githubusercontent.com/xoofx/markdig/56e9c238584a44a169f174c881855c049768634c/license.txt), copied as `Markdig-LICENSE.txt` |
| HtmlSanitizer | 9.2.1039 | Remove executable HTML before preview | MIT; [upstream LICENSE.md at package commit](https://raw.githubusercontent.com/mganss/HtmlSanitizer/0417018d81765e3e15126a65f5a49d71e55f0920/LICENSE.md), copied as `HtmlSanitizer-LICENSE.txt` |
| AngleSharp | 1.7.2 | HTML DOM used by HtmlSanitizer | MIT; [upstream LICENSE at package commit](https://raw.githubusercontent.com/AngleSharp/AngleSharp/3718d6804dd3a3781efcd1ccd7bdcb10a4352edd/LICENSE), copied as `AngleSharp-LICENSE.txt` |
| AngleSharp.Css | 1.0.2 | CSS parsing used by HtmlSanitizer | MIT; [upstream LICENSE at package commit](https://raw.githubusercontent.com/AngleSharp/AngleSharp.Css/d158478f58882dad1d595dc7bb2286f3da4bf6ea/LICENSE), copied as `AngleSharp.Css-LICENSE.txt` |
| Microsoft.Web.WebView2 | 1.0.4191.47 | Embedded preview control and loader | Original `LICENSE.txt` and `NOTICE.txt` included in the NuGet package, copied as `WebView2-LICENSE.txt` and `WebView2-NOTICE.txt` |
| Microsoft.Windows.SDK.NET.Ref | 10.0.17763.57 | Windows APIs used by the WPF WebView2 composition control | [Official license URL declared by its NuGet metadata](https://aka.ms/WinSDKLicenseURL); original downloaded RTF preserved as `Windows.SDK-LICENSE.rtf` |

AvalonEdit package metadata additionally records copyright 2000–2025 AlphaSierraPapa for the SharpDevelop Team. HtmlSanitizer package metadata records copyright 2013–2026 Michael Ganss. Original upstream copyright notices remain unchanged in the accompanying license files.

The separately installed Microsoft Edge WebView2 Runtime is not bundled in this folder. Its installation and license are provided by Microsoft.

## .NET runtime packs in this build

For self-contained builds, the publish script appends the actual runtime pack versions below and copies the original license and third-party notices available in those restored packs. Framework-dependent builds do not bundle these packs.

