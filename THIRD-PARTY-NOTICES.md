# Third-party notices

Scriblism's product license has not been selected. The following notices do not grant a license to Scriblism itself.

| Component | Version used | License / notices |
|---|---|---|
| Markdig, Alexandre Mutel | 1.4.0 | BSD-2-Clause; `third-party/Markdig-LICENSE.txt`. https://github.com/xoofx/markdig |
| Microsoft Windows App SDK and WinUI dependencies | Windows App SDK 2.4.0 | Microsoft package terms and bundled component notices: `third-party/WindowsAppSDK-LICENSE.txt`, `third-party/WindowsAppSDK-NOTICE.txt`. https://github.com/microsoft/WindowsAppSDK |
| .NET self-contained runtime | 10.0.12 | `third-party/DotNet-LICENSE.txt`, `third-party/DotNet-THIRD-PARTY-NOTICES.txt`. https://github.com/dotnet/runtime |

The Windows App SDK may distribute support assemblies for controls that Scriblism does not instantiate, including WebView2-related assemblies. Scriblism's editor is `Microsoft.UI.Xaml.Controls.RichEditBox`; it does not create a WebView2 control or browser process.

xUnit and Microsoft.NET.Test.Sdk are development-only test dependencies and are not included in the published application. Fonts are supplied by Windows, not redistributed by this project.

WinUI project settings, application resources, the Windows manifest pattern, and the window inspection script were adapted from the owner's sibling Peeklism project at the user's request. Peeklism remains unchanged. Its logos, shell hooks, preview services and document web viewers are not included.
