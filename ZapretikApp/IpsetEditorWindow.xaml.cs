using System;
using System.Windows;
using System.Windows.Input;

namespace ZapretikApp
{
    public partial class IpsetEditorWindow : Window
    {
        private readonly string _zapretRoot;

        public IpsetEditorWindow(string zapretRoot)
        {
            InitializeComponent();
            _zapretRoot = zapretRoot ?? string.Empty;
            TxtPath.Text = IpsetListManager.GetFilePath(_zapretRoot) ?? string.Empty;
            TxtPath.ToolTip = TxtPath.Text;
            Reload();
            TxtIp.Focus();
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
            DialogResult = true;
            Close();
        }

        private void TxtIp_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddEntry();
                e.Handled = true;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            AddEntry();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            RemoveEntry();
        }

        private void LstEntries_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = LstEntries.SelectedItem as string;
            if (!string.IsNullOrEmpty(item))
                TxtIp.Text = item;
        }

        private void AddEntry()
        {
            string error;
            var entry = IpsetListManager.NormalizeEntry(TxtIp.Text, out error);
            if (entry == null)
            {
                AppDialog.Show(this, error, "Zapretik", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IpsetListManager.Add(_zapretRoot, entry);
                TxtIp.Clear();
                TxtStatus.Text = "Добавлено: " + entry;
                Reload();
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, "Zapretik", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RemoveEntry()
        {
            var raw = TxtIp.Text;
            if (string.IsNullOrWhiteSpace(raw) && LstEntries.SelectedItem is string selected)
                raw = selected;

            string error;
            var entry = IpsetListManager.NormalizeEntry(raw, out error);
            if (entry == null)
            {
                AppDialog.Show(this, error ?? "Укажите IP для удаления.", "Zapretik",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (!IpsetListManager.Remove(_zapretRoot, entry))
                {
                    AppDialog.Show(this, "Запись не найдена:\n" + entry, "Zapretik",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                TxtIp.Clear();
                TxtStatus.Text = "Удалено: " + entry;
                Reload();
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, "Zapretik", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Reload()
        {
            try
            {
                if (!IpsetListManager.FileExists(_zapretRoot))
                {
                    TxtCount.Text = "файл не создан";
                    LstEntries.ItemsSource = null;
                    return;
                }

                var count = IpsetListManager.CountEntries(_zapretRoot);
                TxtCount.Text = count.ToString("N0") + " всего";
                // Show tail so newly added appear at the bottom of the list
                var tail = IpsetListManager.GetTailEntries(_zapretRoot, 80);
                tail.Reverse();
                LstEntries.ItemsSource = tail;
            }
            catch (Exception ex)
            {
                TxtCount.Text = "ошибка";
                TxtStatus.Text = ex.Message;
            }
        }
    }
}
