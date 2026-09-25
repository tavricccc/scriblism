using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Scriblism.Core;

namespace Scriblism.App;

public sealed partial class MainWindow
{
    private bool _loadingSettingsPage;

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsTab is null)
        {
            SyncSettingsControls();
            _settingsTab = new TabViewItem
            {
                Header = "設定", FontSize = 12, MinWidth = 174, MaxWidth = 208, MinHeight = 35,
                IconSource = new FontIconSource { Glyph = "\uE713" }
            };
            Tabs.TabItems.Add(_settingsTab);
        }
        Tabs.SelectedItem = _settingsTab;
        UpdateDocumentUi();
    }

    private void CloseSettingsTab()
    {
        if (_settingsTab is null) return;
        Tabs.TabItems.Remove(_settingsTab); _settingsTab = null;
        if (Tabs.SelectedItem is null && _documents.Count > 0) Tabs.SelectedItem = _documents[^1].Tab;
        UpdateDocumentUi();
        Current?.Focus();
    }

    private void SyncSettingsControls()
    {
        if (SourceFontsBox is null || MarkdownFontsBox is null) return;
        _loadingSettingsPage = true;
        SourceFontsBox.Text = _settings.SourceFonts;
        MarkdownFontsBox.Text = _settings.MarkdownFonts;
        ThemeSetting.SelectedIndex = _settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        WrapSetting.IsOn = _settings.WordWrap;
        _loadingSettingsPage = false;
    }

    private void OnApplyFonts(object sender, RoutedEventArgs e)
    {
        _settings.SourceFonts = EditorSettings.NormalizeFonts(SourceFontsBox.Text, EditorSettings.DefaultSourceFonts);
        _settings.MarkdownFonts = EditorSettings.NormalizeFonts(MarkdownFontsBox.Text, EditorSettings.DefaultMarkdownFonts);
        SyncSettingsControls();
        foreach (var doc in _documents)
            doc.ApplySettings(_settings.FontSize, _settings.WordWrap, _settings.SourceFonts, _settings.MarkdownFonts);
        _ = PersistSettings();
        ShowNotice("字體已套用", "編輯器字體已更新。", InfoBarSeverity.Success);
    }

    private void OnSettingsThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettingsPage || ThemeSetting is null || ThemeSetting.SelectedIndex < 0) return;
        _settings.Theme = ThemeSetting.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "Default" };
        ApplyTheme(); _ = PersistSettings();
    }

    private void OnSettingsWrapChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettingsPage || WrapSetting is null) return;
        _settings.WordWrap = WrapSetting.IsOn;
        WrapMenu.IsChecked = _settings.WordWrap;
        foreach (var doc in _documents)
            doc.ApplySettings(_settings.FontSize, _settings.WordWrap, _settings.SourceFonts, _settings.MarkdownFonts);
        _ = PersistSettings();
    }
}
