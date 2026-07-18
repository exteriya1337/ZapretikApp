using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZapretikApp
{
    public partial class ZapretSettingsWindow : Window
    {
        private readonly string _zapretRoot;
        private readonly Func<bool> _isZapretOnline;
        private readonly Action _openIpEditor;
        private readonly Action<string> _restartZapretIfOnline;

        public ZapretSettingsWindow(
            string zapretRoot,
            Func<bool> isZapretOnline,
            Action openIpEditor,
            Action<string> restartZapretIfOnline)
        {
            InitializeComponent();
            _zapretRoot = zapretRoot ?? string.Empty;
            _isZapretOnline = isZapretOnline;
            _openIpEditor = openIpEditor;
            _restartZapretIfOnline = restartZapretIfOnline;
            RefreshAll();
        }

        public void RefreshAll()
        {
            RefreshStatus();
            RefreshIpSummary();
            RefreshGameFilterUi();
            RefreshIpsetFilterUi();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
        }

        private void BtnOpenIp_Click(object sender, RoutedEventArgs e)
        {
            if (_openIpEditor != null)
                _openIpEditor();
        }

        private void BtnGameFilter_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null)
                return;

            ZapretSettingsHelper.GameFilterMode mode;
            switch (btn.Tag.ToString())
            {
                case "All": mode = ZapretSettingsHelper.GameFilterMode.All; break;
                case "Tcp": mode = ZapretSettingsHelper.GameFilterMode.Tcp; break;
                case "Udp": mode = ZapretSettingsHelper.GameFilterMode.Udp; break;
                default: mode = ZapretSettingsHelper.GameFilterMode.Disabled; break;
            }

            try
            {
                ZapretSettingsHelper.SetGameFilterMode(_zapretRoot, mode);
                RefreshGameFilterUi();

                if (_isZapretOnline != null && _isZapretOnline() && _restartZapretIfOnline != null)
                {
                    if (AppDialog.Confirm(
                        this,
                        "Game Filter изменён.\n\nПерезапустить zapret сейчас, чтобы применить настройку?\n" +
                        "(Нужны права администратора)",
                        "Zapretik"))
                    {
                        _restartZapretIfOnline("Game Filter");
                    }
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Не удалось изменить Game Filter:\n" + ex.Message,
                    "Zapretik", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnIpsetCycle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var next = ZapretSettingsHelper.CycleIpsetFilter(_zapretRoot);
                RefreshIpsetFilterUi();
                RefreshIpSummary();

                if (_isZapretOnline != null && _isZapretOnline() && _restartZapretIfOnline != null)
                {
                    if (AppDialog.Confirm(
                        this,
                        "IPSet Filter: " + ZapretSettingsHelper.FormatIpsetFilterMode(next) +
                        "\n\nПерезапустить zapret сейчас?\n(Нужны права администратора)",
                        "Zapretik"))
                    {
                        _restartZapretIfOnline("IPSet Filter");
                    }
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Не удалось переключить IPSet Filter:\n" + ex.Message,
                    "Zapretik", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshStatus()
        {
            try
            {
                var info = ZapretSettingsHelper.GetStatus(_zapretRoot);
                TxtStatus.Text = info.Summary;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Ошибка статуса: " + ex.Message;
            }
        }

        private void RefreshIpSummary()
        {
            try
            {
                var path = IpsetListManager.GetFilePath(_zapretRoot);
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    TxtIpSummary.Text = "lists\\ipset-all.txt — файл ещё не создан";
                    return;
                }

                var count = IpsetListManager.CountEntries(_zapretRoot);
                TxtIpSummary.Text = "lists\\ipset-all.txt · " + count.ToString("N0") + " записей";
            }
            catch
            {
                TxtIpSummary.Text = "lists\\ipset-all.txt";
            }
        }

        private void RefreshGameFilterUi()
        {
            var mode = ZapretSettingsHelper.GetGameFilterMode(_zapretRoot);
            TxtGameFilter.Text = "Сейчас: " + ZapretSettingsHelper.FormatGameFilterMode(mode);

            ApplyChip(BtnGfOff, mode == ZapretSettingsHelper.GameFilterMode.Disabled);
            ApplyChip(BtnGfAll, mode == ZapretSettingsHelper.GameFilterMode.All);
            ApplyChip(BtnGfTcp, mode == ZapretSettingsHelper.GameFilterMode.Tcp);
            ApplyChip(BtnGfUdp, mode == ZapretSettingsHelper.GameFilterMode.Udp);
        }

        private void RefreshIpsetFilterUi()
        {
            var mode = ZapretSettingsHelper.GetIpsetFilterMode(_zapretRoot);
            TxtIpsetFilter.Text = "Сейчас: " + ZapretSettingsHelper.FormatIpsetFilterMode(mode);
        }

        private void ApplyChip(Button button, bool on)
        {
            if (button == null)
                return;
            button.Style = on
                ? (Style)FindResource("ChipOnButton")
                : (Style)FindResource("ChipButton");
        }
    }
}
