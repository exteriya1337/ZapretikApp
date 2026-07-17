using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ZapretikApp
{
    public enum AppDialogKind
    {
        Info,
        Warning,
        Error,
        Question
    }

    public partial class AppDialog : Window
    {
        private bool? _result;

        public AppDialog()
        {
            InitializeComponent();
            Loaded += AppDialog_Loaded;
        }

        private void AppDialog_Loaded(object sender, RoutedEventArgs e)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleX = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleY = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Chrome.BeginAnimation(OpacityProperty, fade);
            ChromeScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ChromeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

            BtnPrimary.Focus();
        }

        public static void Info(Window owner, string message, string title = "Zapretik")
        {
            Show(owner, message, title, AppDialogKind.Info, yesNo: false);
        }

        public static void Warning(Window owner, string message, string title = "Zapretik")
        {
            Show(owner, message, title, AppDialogKind.Warning, yesNo: false);
        }

        public static void Error(Window owner, string message, string title = "Zapretik")
        {
            Show(owner, message, title, AppDialogKind.Error, yesNo: false);
        }

        public static bool Confirm(Window owner, string message, string title = "Zapretik")
        {
            return Show(owner, message, title, AppDialogKind.Question, yesNo: true) == true;
        }

        /// <summary>
        /// Drop-in style helper matching MessageBox.Show signatures used in the app.
        /// </summary>
        public static MessageBoxResult Show(
            Window owner,
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage image)
        {
            // MessageBoxImage has duplicate numeric values (Error==Hand==Stop, Warning==Exclamation).
            AppDialogKind kind;
            if (image == MessageBoxImage.Error)
                kind = AppDialogKind.Error;
            else if (image == MessageBoxImage.Warning)
                kind = AppDialogKind.Warning;
            else if (image == MessageBoxImage.Question)
                kind = AppDialogKind.Question;
            else
                kind = AppDialogKind.Info;

            var yesNo = buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.OKCancel;
            var ok = Show(owner, message, title, kind, yesNo);

            if (!yesNo)
                return MessageBoxResult.OK;

            if (buttons == MessageBoxButton.OKCancel)
                return ok == true ? MessageBoxResult.OK : MessageBoxResult.Cancel;

            return ok == true ? MessageBoxResult.Yes : MessageBoxResult.No;
        }

        private static bool? Show(
            Window owner,
            string message,
            string title,
            AppDialogKind kind,
            bool yesNo)
        {
            var dlg = new AppDialog
            {
                Owner = owner
            };

            dlg.TxtTitle.Text = string.IsNullOrWhiteSpace(title) ? "Zapretik" : title;
            dlg.TxtMessage.Text = message ?? string.Empty;
            dlg.ApplyKind(kind);

            if (yesNo)
            {
                dlg.BtnSecondary.Visibility = Visibility.Visible;
                dlg.BtnSecondary.Content = "Отмена";
                dlg.BtnPrimary.Content = kind == AppDialogKind.Question ? "Да" : "OK";
            }
            else
            {
                dlg.BtnSecondary.Visibility = Visibility.Collapsed;
                dlg.BtnPrimary.Content = "OK";
            }

            if (owner == null)
                dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dlg.ShowDialog();
            return dlg._result;
        }

        private void ApplyKind(AppDialogKind kind)
        {
            Color badge;
            Color iconColor;
            string glyph;

            switch (kind)
            {
                case AppDialogKind.Error:
                    badge = Color.FromArgb(0x33, 0xE8, 0x3B, 0x4A);
                    iconColor = Color.FromRgb(0xFF, 0x6B, 0x7A);
                    glyph = "✕";
                    break;
                case AppDialogKind.Warning:
                    badge = Color.FromArgb(0x33, 0xE8, 0xA8, 0x38);
                    iconColor = Color.FromRgb(0xFF, 0xC8, 0x57);
                    glyph = "!";
                    break;
                case AppDialogKind.Question:
                    badge = Color.FromArgb(0x33, 0x7C, 0x8C, 0xFF);
                    iconColor = Color.FromRgb(0x9A, 0xA6, 0xFF);
                    glyph = "?";
                    break;
                default:
                    badge = Color.FromArgb(0x33, 0x6F, 0xCF, 0x97);
                    iconColor = Color.FromRgb(0x6F, 0xCF, 0x97);
                    glyph = "i";
                    break;
            }

            IconBadge.Background = new SolidColorBrush(badge);
            TxtIcon.Text = glyph;
            TxtIcon.Foreground = new SolidColorBrush(iconColor);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e)
        {
            _result = true;
            AnimateClose();
        }

        private void BtnSecondary_Click(object sender, RoutedEventArgs e)
        {
            _result = false;
            AnimateClose();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _result = false;
            AnimateClose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                _result = false;
                AnimateClose();
                e.Handled = true;
            }
        }

        private void AnimateClose()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleX = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleY = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fade.Completed += (s, e) =>
            {
                try { DialogResult = _result == true; }
                catch { }
                Close();
            };

            Chrome.BeginAnimation(OpacityProperty, fade);
            ChromeScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ChromeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }
    }
}
