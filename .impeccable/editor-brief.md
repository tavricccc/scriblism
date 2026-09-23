# Editor surface

Mode: Operate. Native Windows desktop, keyboard-first editing. The user's pinned WinUI / Peeklism / Notepad++ references override the direction seed (987e28ae, assigned index 7). No alternate identity is introduced and no further confirmation is requested, per the user's instruction to proceed.

## Direction contract

THESIS: One native workspace for source code and readable Markdown; no web editor, dashboard or decorative welcome screen.

OWN-WORLD: Windows Fluent resources, Mica window frame, Segoe UI text, Cascadia Mono source text, system accent, quiet separators. Follow the user's OS theme and high-contrast setting rather than assuming ambient lighting.

STORY: Open a folder, pick a file, edit, search and save. Unsaved state and file encoding remain visible; a canceled close never loses work.

FIRST VIEWPORT: 1200×820 window. Native title bar, compact command/menu row, 248-DIP file tree left, tabs directly above a dominant editing surface, with no dedicated editor toolbar. The user's second follow-up moves language and Markdown mode switches into the compact status strip below; formatting actions live in the Edit menu. Markdown headings use the same foreground as body text, distinguished only by size and weight. The user's follow-up pins a VS Code-like floating find/replace dropdown at the editing area's upper right, with expandable replacement controls. Notifications are bottom-right toasts. Neither surface may change the editor's bounds or scroll position. Empty sidebar offers Open folder. The new document is immediately editable.

FORM: User-pinned Windows editor convention, overriding the roll. Signature interaction: Markdown delimiters recede outside the active line while original text remains editable and saveable. Source view is one toggle away. Motion uses stock WinUI focus, tabs and expansion transitions only.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
