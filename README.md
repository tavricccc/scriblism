# Scriblism

Scriblism 是 Windows 11 的文字、程式碼與 Markdown 編輯器。可以同時打開多個檔案，用分頁切換；側欄顯示目前資料夾，方便在筆記和專案檔案之間移動。寫 Markdown 時，可在原始碼與即時排版之間切換，並用大綱跳到標題。

編輯程式碼時有語法上色、行號、自動縮排和搜尋取代。未儲存的內容會定期備份，意外關閉後可以選擇復原；如果檔案已被其他程式改動，儲存前會提醒。字體、主題和自動換行可在設定頁調整。

## 執行

Windows 11 24H2（build 26100）以上、x64。

從 [GitHub Releases](https://github.com/tavricccc/scriblism/releases) 下載 `Scriblism.Setup.exe`，執行即可安裝或更新。本機建置的安裝檔位於 `artifacts/installer/Scriblism.Setup.exe`。這是單一檔案的圖形化安裝程式，會建立開始功能表捷徑，並在 Windows「已安裝的應用程式」登錄解除安裝項目。安裝僅套用至目前使用者，不要求管理員權限；沒有共用 Windows App 執行環境時，可選擇安裝自帶執行環境的版本。

若要免安裝使用，也可開啟 `artifacts/Scriblism-win-x64/Scriblism.exe`，或解壓縮對應版本的 `artifacts/Scriblism-<版本>-win-x64.zip`。可攜版必須保留整個資料夾，不能只複製 EXE。

```powershell
.\Scriblism.exe
.\Scriblism.exe "C:\notes\plan.md" "C:\code\main.py"
.\Scriblism.exe "C:\code\my-project"
```

可攜版仍附有 `Install.ps1` 與 `Uninstall.ps1`。圖形化安裝程式可更新先前透過 `Install.ps1` 安裝的版本，並保留舊安裝資料夾作為備份。

這版未簽章，Windows 可能顯示未識別發行者。沒有自動更新或遠端服務。

## 已實作

- Windows Terminal 式標題列：WinUI 原生分頁直接整合進標題列，內容區不重複放分頁。支援拖曳排序、未儲存標記及關閉前確認；標題列空白處可拖曳視窗、雙擊最大化，右上角保留 Windows 原生視窗按鈕。
- 開啟檔案／資料夾、拖放、最近開啟；側欄跟隨目前文件所在的資料夾，可拖曳右側分隔線調整寬度，並支援延遲展開、新增檔案與資料夾、顯示隱藏檔、複製路徑及在 Explorer 顯示。
- 33 種語言／格式選項，含 C#、C/C++、JavaScript、TypeScript、Python、Java、Kotlin、Swift、Go、Rust、Ruby、PHP、PowerShell、Shell、SQL、HTML、XML/XAML、CSS、JSON、YAML、TOML、Lua、R、Dart、Scala、F#、Dockerfile、Makefile、Batch、Diff、GraphQL，以及 Markdown 和純文字。
- 原生程式碼高亮、來源模式行號、自動縮排、文字縮放、自動換行。
- Markdown 即時排版與原始碼切換；標題、粗斜體、刪除線、連結、引用、行內／圍欄程式碼。游標所在行顯示語法標記，離開後隱藏支援的行內標記。編輯選單可插入 Markdown 格式；大綱可跳到標題。語言與排版模式切換放在狀態列，不佔上方工具列。
- 右上角浮動搜尋／取代面板，取代欄可收合，不推擠編輯區。支援大小寫、完整單字、正規表示式、移至行。大量結果與高耗時正規表示式有上限。
- 右下角 toast 顯示一般訊息，成功通知會自動消失，錯誤與警告保留到手動關閉。儲存確認仍使用對話框。
- 文字專用復原／重做。語法上色和顯示標記不加入復原歷史；貼上只接受純文字。
- 保留 UTF-8、帶 BOM 的 UTF-16／32、LF／CRLF／CR。先寫同資料夾暫存檔，再替換原檔；儲存前比對磁碟內容雜湊，不直接截斷原檔。
- 未儲存內容閒置兩秒後備份；異常結束後，下次開啟可選擇復原。備份失敗會提示，不冒充已儲存。
- 淺色／深色／跟隨 Windows，原生 Mica 視窗、系統選單、焦點與檔案選擇器。
- 設定頁可從「檢視 → 設定」或 Ctrl+, 開啟。一般／原始碼與 Markdown 即時排版可各自設定多個字體；也可調整主題、自動換行，以及下次啟動時是否重新開啟上次的檔案。

## 邊界

**這版是可用的原生 Markdown 編輯器，不是完整 Typora 替代品。** 表格與圖片保留語法，不呈現可視化表格或內嵌圖片；不執行 HTML、JavaScript、Mermaid，也不下載遠端內容。圍欄、清單和引用符號仍可見。富文本的持久格式是 Markdown，沒有 RTF／DOCX 匯入匯出。

高亮採詞彙分析，不是各語言的完整文法或語意分析；沒有 LSP、自動完成、編譯器、終端機或外掛。超過 262,144 個 UTF-16 字元的文件停用高亮和行號。輸入檔案與儲存內容上限 16 MiB，最多開啟 32 個檔案分頁。這不是虛擬化的 GB 級大型檔案編輯器。

不猜測 ANSI／Big5 等舊式編碼。混合換行的檔案會先警告，編輯儲存後統一使用原檔佔比最高的換行方式。空白行和最後一行的換行不會被修剪。二進位控制字元，以及 RichEdit 會改寫的 U+2028、U+2029、U+FFFC，會明確拒絕；載入和程式化取代也會核對原生控制項讀回的文字。

檔案樹不遞迴展開 junction／symlink，不提供刪除和重新命名；這兩項可從右鍵進入檔案總管操作。單一資料夾超過 5,000 個項目會明確提示截斷。

中文由 Windows 原生 IME 處理，組字期間暫停排版；自動測試涵蓋中文文字與 emoji 的往返，但不等於所有輸入法／候選字視窗都經過人工驗證。NVDA 與 Windows 高對比模式仍需人工檢查。

## 快捷鍵

| 按鍵 | 動作 |
|---|---|
| Ctrl+N／Ctrl+Shift+N | 新增文字／Markdown |
| Ctrl+O／Ctrl+Shift+O | 開啟檔案／資料夾 |
| Ctrl+S／Ctrl+Shift+S | 儲存／另存新檔 |
| Ctrl+W | 關閉分頁 |
| Ctrl+Tab／Ctrl+Shift+Tab | 下／上一個分頁 |
| Ctrl+F／Ctrl+H | 搜尋／取代 |
| F3／Shift+F3 | 下／上一個結果 |
| Ctrl+G | 移至行 |
| Ctrl+E | Markdown 原始碼／即時排版 |
| Ctrl+B | 側邊欄 |
| Ctrl+Shift+B／Ctrl+I | Markdown 粗體／斜體 |
| Alt+Z | 自動換行 |
| Ctrl++／Ctrl+-／Ctrl+0 | 放大／縮小／重設文字 |
| Tab／Shift+Tab | 插入四個空白／離開編輯區 |

## 建置與測試

需要 .NET SDK 10.0.401、Windows 11 x64。腳本先尋找 `%USERPROFILE%\.dotnet\dotnet.exe`，再使用 PATH 中的 dotnet。

```powershell
pwsh -File scripts/Build.ps1 -NativeTests
pwsh -File scripts/Publish.ps1
```

`Build.ps1` 建置 solution、執行核心單元測試；`-NativeTests` 會啟動實際 WinUI 視窗，驗證 RichEdit 讀寫、排版、隱藏標記、復原及儲存。結果在 `artifacts/test-results/`。`Publish.ps1` 預設先執行這些測試，再產出自帶執行環境的資料夾、ZIP、SHA-256 與卸載用的檔案清單。

日常開發也可直接執行：

```powershell
dotnet build Scriblism.slnx
dotnet test tests/Scriblism.Core.Tests/Scriblism.Core.Tests.csproj
dotnet run --project src/Scriblism.App
```

`SCRIBLISM_STATE_ROOT` 可為測試指定獨立的設定／備份資料夾，避免污染個人設定。

## 本機資料

預設在 `%LocalAppData%\Scriblism`：`settings.json`、`Recovery/`、`errors.log`。備份含未儲存的文件原文，沒有額外加密，不會上傳。原始文件不會自動儲存或覆寫。

`src/Scriblism.Core` 放檔案、復原、搜尋和語法分析；`src/Scriblism.App` 放 WinUI 與原生文字控制項。沿用 Peeklism 的 WinUI 專案設定、Fluent 資源、Windows manifest 模式及視窗檢查腳本；沒有複製預覽 hook、系統匣、影音或 WebView2 查看器。正式產品授權與簽章由擁有者決定。
