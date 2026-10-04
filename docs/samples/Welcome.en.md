# Welcome to 汗青

Open Markdown, read comfortably, and press **Ctrl+E** when you need to edit.

In preview mode, the left side lists H1–H6 headings. Click a heading to jump to it; scrolling also highlights the current section. In edit mode, the source is on the left and the live preview is on the right, with the heading list hidden. Drag the divider to adjust the column widths.

> Your documents stay on your computer. Hanqing does not require an account.

## Quick start

| Action | Shortcut |
| --- | --- |
| Open a document | Ctrl+O |
| New document | Ctrl+N |
| Switch preview / edit mode | Ctrl+E |
| Save | Ctrl+S |
| Find text | Ctrl+F |
| Next search result | F3 |
| Close the current tab | Ctrl+W |
| Full-screen reading | F11 |

The left toolbar lets you create documents or windows, save a copy, and open recent documents. It also provides font and theme controls; language and window size are under More. Click the button at the top of the toolbar to expand names, descriptions, and shortcuts, and click again to collapse it. Moving the pointer over the toolbar does not change its state.

The chapter outline setting under More selects H1–H6 levels for both Markdown preview and PDF export. The option to keep open files on exit is off by default. Turn it on to reopen those documents at the next launch; unsaved changes still prompt you.

Font settings are separate for the interface, reading preview, and editor. Each has its own font and size; changes apply immediately and are remembered.

Edit mode shows a formatting toolbar above the document. Choose an editor font (automatic monospace, traditional text, or an installed font), enter a size from 8 to 72, or use **− / +**. These preferences affect the editor display and are remembered without being written into Markdown. The toolbar hides in preview mode or when all documents are closed.

New installations use Bamboo (Light), combining a bamboo interface with a paper background. Both bamboo themes have visible paper fibres. An existing theme preference is preserved. Choose a theme under More → General → Theme; the sidebar button cycles through Light, Dark, Bamboo (Light), and Bamboo (Dark).

In the bamboo themes, automatic interface and preview fonts use traditional text. Traditional Chinese prefers an installed BiauKai font. You can also select traditional text directly. Explicit font choices are preserved, and the editor defaults to monospace.

The default interface font size is 15; preview and editor sizes are 16. The toolbar starts expanded. Autosave is on by default: a document with a saved location is saved three seconds after editing stops. A new document still needs **Ctrl+S** to choose its first save location.

## Export PDF

Click Export PDF below Save in the left toolbar. Hanqing first saves the Markdown, then creates a PDF in the same folder with a local timestamp, for example `Notes.md` → `Notes_20260922_153012.pdf` (`yyyyMMdd_HHmmss`).

A new document first asks for a Markdown save location. Cancelling or failing to save stops the export. You are asked before an existing PDF is overwritten. The PDF uses white A4 pages, the current preview font and size, and includes the full content and local images.

## Task lists

This is a Markdown task-list example. When no document is open, this introduction is read-only. First open or create a document, then try checking and saving items in your own document.

- [x] Open a Markdown document
- [ ] Check an item in your own document and observe the tab's unsaved marker
- [ ] Press Ctrl+S to save the changes

## Code

```csharp
var message = "Read. Edit. Save.";
Console.WriteLine(message);
```

Use the button at the top right of a code block to copy it. You can also select ordinary text and press **Ctrl+Shift+C** to copy Markdown.

## Editing tips

1. Use the formatting toolbar's paragraph menu for body text or **H1–H6** headings.
2. Select text to apply bold, italic, strikethrough, inline code, or a link. **Ctrl+B** and **Ctrl+I** also apply bold and italic. These actions insert standard Markdown and can be undone with **Ctrl+Z**.
3. Paste a URL over selected text to create a Markdown link.
4. Paste an image in edit mode to save it in an `images` folder beside the document.
5. Right-click in preview and choose Edit here to jump to the corresponding paragraph.

Read-only documents remain protected from changes through formatting buttons.

In edit mode, expand the search panel to replace text. **Replace all asks for confirmation first.**

---

[Back to top](#welcome-to-汗青)
