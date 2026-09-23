---
name: Scriblism
description: Native Windows editing with an unobstructed document surface
colors:
  keyword-light: "#005DB8"
  keyword-dark: "#80BFFF"
  string-light: "#A13D18"
  string-dark: "#E7AE93"
  comment-light: "#467436"
  comment-dark: "#91B982"
  type-light: "#00756B"
  type-dark: "#65D8C3"
  code-ground-light: "#EDF1F5"
  code-ground-dark: "#292D33"
typography:
  ui:
    fontFamily: "Segoe UI Variable Text, Microsoft JhengHei UI"
    fontSize: "14px"
  body:
    fontFamily: "Segoe UI"
    fontSize: "15px"
    fontWeight: 400
  heading-one:
    fontFamily: "Segoe UI"
    fontSize: "28.5px"
    fontWeight: 700
  heading-two:
    fontFamily: "Segoe UI"
    fontSize: "22.5px"
    fontWeight: 700
  code:
    fontFamily: "Cascadia Mono, Consolas"
    fontSize: "15px"
  label:
    fontFamily: "Segoe UI Variable Text, Microsoft JhengHei UI"
    fontSize: "12px"
rounded:
  overlay: "8px"
spacing:
  tight: "4px"
  control: "8px"
  group: "12px"
  panel: "16px"
  editor-top: "20px"
  editor-side: "24px"
  editor-bottom: "48px"
components:
  find-panel:
    rounded: "{rounded.overlay}"
    padding: "{spacing.control}"
    width: "500px"
  status-action:
    typography: "{typography.label}"
    padding: "6px 0"
    height: "24px"
  line-gutter:
    typography: "{typography.code}"
    width: "52px"
---

# Design System: Scriblism

## Overview

Native Windows / Fluent, as requested by the user. The document owns the workspace. Keep Windows controls, system focus behavior and OS materials rather than imitating a web editor.

The follow-up requirements are binding: use a Windows Terminal-style title bar with native WinUI tabs integrated into it, not another tab row inside the workspace. Find/replace floats without resizing the document. Language and Markdown mode switches must not occupy a toolbar above the editor. Markdown headings do not get a separate color.

Values in the frontmatter are Windows DIPs, expressed as px for token portability. They describe the default text zoom, not physical screenshot pixels.

## Colors

Application surfaces, ordinary text, separators, focus, accent and notification severity come from WinUI theme resources. The app can follow Windows or request light/dark mode. Do not replace those resources with fixed light-theme hex colors.

The explicit colors above belong to syntax highlighting and code backgrounds. `EditorSession.TokenColor` contains the remaining number, function, property and diff colors. High contrast skips custom syntax colors.

**Same-color headings.** A Markdown heading inherits the document foreground. Its hierarchy comes from size and weight. Markdown syntax delimiters may use the existing secondary marker color; they are not a heading accent.

## Typography

UI controls use the Windows UI stack. Markdown prose uses Segoe UI with the native text engine's fallback for Chinese. Source text uses Cascadia Mono, falling back to Consolas. Fonts are not bundled.

At default zoom, H1 is 1.9× body size, H2 is 1.5× and H3–H6 are 1.18×. All are bold. User zoom scales the document from 10 to 32 DIPs. TOM character sizes are converted to points at 0.75× the DIP size.

## Layout

A 48-DIP extended native title bar contains the only TabView, with native Windows caption buttons reserved at the right. Its empty footer is the native window drag region; double-click maximizes. Caption-button inset follows display scaling. Below it are the compact menu row and workspace. The left sidebar is 248 DIPs wide, reduced to 196 below 780 DIPs of root width; it can be hidden. The selected document is hosted separately in the workspace; there is no second tab strip or formatting toolbar.

The bottom status strip holds path, line/column, encoding, language and (for Markdown) editing mode. Its text actions are small and unfilled.

Find/replace is a top-right overlay inside the document workspace. It is at most 500 DIPs wide and constrained to the editor width minus 24. Replacement expands inside that overlay. Notifications sit at the bottom right, at most 400 DIPs wide with 16-DIP margins. Both leave editor bounds unchanged.

Source line numbers use RichEdit's measured line positions, not an independently scrolling text column. Hide them for live Markdown and documents above the highlighting limit.

## Elevation & Depth

Mica belongs to the native window frame. Ordinary workspace surfaces are flat WinUI layers with quiet separators. Only the floating find panel and toast use `ThemeShadow` with a Z translation of 24. A closed toast is collapsed, including its shadow.

No custom entrance animation is introduced. Native tab, menu, focus and expansion transitions remain in control.

## Shapes

Floating surfaces have eight-DIP corners. The editor itself has zero border thickness and zero corner radius. Buttons, tabs, menus and dialogs otherwise retain WinUI shapes.

## Components

- **Language switch:** text button in the status strip; opens a native menu of language choices.
- **Markdown mode:** text button in the status strip, also available in View and with Ctrl+E. Formatting actions live under Edit → Markdown format.
- **Find panel:** text query, native case/word/regex toggles, previous/next, close and a replacement expander. Escape closes it and returns focus to the document.
- **Toast:** native InfoBar severity, title, message and close button. Success disappears after four seconds; information after seven. Hover/focus pauses dismissal. Warnings and errors remain until dismissed.
- **Protected decisions:** unsaved-close, external-file conflict and recovery selection remain ContentDialogs. Do not convert them to transient toasts.
- **File tree:** native TreeView, explicit filename content and lazy folder expansion. Root/child operations use real paths; UI labels never expose CLR type names.

## Do's and Don'ts

- Do use the OS theme and native control behavior.
- Do preserve editor geometry when opening find/replace or a toast.
- Do keep language/mode controls in the status strip and formatting in menus.
- Don't reintroduce a separate editor toolbar.
- Don't assign accent colors to Markdown headings.
- Don't hide unsaved or conflict decisions in auto-dismissed notifications.
- Don't build a web surface or plug-in marketplace into this editor.
- Don't redraw Windows caption buttons or duplicate the title-bar tabs inside the workspace.
