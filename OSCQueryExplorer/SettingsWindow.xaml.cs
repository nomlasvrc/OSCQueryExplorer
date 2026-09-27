using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OSCQueryExplorer.Core.Settings;
using Wpf.Ui.Controls;

namespace OSCQueryExplorer;

public partial class SettingsWindow : FluentWindow
{
    private readonly Func<Task> _checkUpdatesAsync;

    public AppPreferences Preferences { get; private set; }

    public SettingsWindow(AppPreferences preferences, Func<Task> checkUpdatesAsync)
    {
        Preferences = preferences;
        _checkUpdatesAsync = checkUpdatesAsync;
        InitializeComponent();
        ThemeBox.SelectedValue = preferences.Theme.ToString();
        LogFontSizeBox.Text = preferences.LogFontSize.ToString("G", CultureInfo.CurrentCulture);
        PollingIntervalBox.Text = preferences.PollingIntervalSeconds.ToString(CultureInfo.CurrentCulture);
        PublishToLanBox.IsChecked = preferences.PublishToLan;
        AutomaticUpdateCheckBox.IsChecked = preferences.CheckUpdatesAutomatically;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            await _checkUpdatesAsync();
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(LogFontSizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var fontSize)
            || !double.IsFinite(fontSize) || fontSize is < 9 or > 30)
        {
            ShowValidationError("ログ文字サイズは9～30の数値で入力してください。");
            return;
        }
        if (!int.TryParse(PollingIntervalBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var pollingInterval)
            || pollingInterval is < 1 or > 300)
        {
            ShowValidationError("ツリー更新間隔は1～300秒の整数で入力してください。");
            return;
        }
        if ((ThemeBox.SelectedItem as ComboBoxItem)?.Tag is not string themeText
            || !Enum.TryParse<AppTheme>(themeText, out var theme))
        {
            ShowValidationError("テーマを選択してください。");
            return;
        }

        Preferences = new(theme, fontSize, pollingInterval, PublishToLanBox.IsChecked == true, AutomaticUpdateCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void ShowValidationError(string message) => System.Windows.MessageBox.Show(
        this, message, "設定", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
}
