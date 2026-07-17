using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZapretikApp.Properties;
using Forms = System.Windows.Forms;
// Process is still used for engine discovery via Process.GetProcessesByName

namespace ZapretikApp
{
    public partial class MainWindow : Window
    {
        private const int MaxBatScanDepth = 2;

        /// <summary>
        /// Zapret engine process names (winws is the real one; winvs kept as a typo/compat alias).
        /// </summary>
        private static readonly string[] ZapretEngineProcessNames = { "winws", "winvs" };

        private static readonly Brush DotOnlineBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xCF, 0x97));
        private static readonly Brush DotOfflineBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));

        private bool _isClosingAnimated;
        private bool _exitRequested;
        private bool _suppressBatSelectionSave;
        private readonly RectangleGeometry _chromeClip = new RectangleGeometry();
        private string _zapretRootPath = string.Empty;
        private readonly List<BatFileItem> _batFiles = new List<BatFileItem>();

        private BatFileItem _activeBat;
        private DateTime? _launchedAtUtc;
        private ActiveScriptInfo _detectedScript;
        private string _lastActiveDisplay = string.Empty;
        private DateTime? _sessionStartedUtc;
        private bool _isOnline;
        private int _monitorTick;
        private bool _statusCheckBusy;
        private readonly DispatcherTimer _uptimeTimer;
        private readonly DispatcherTimer _statusTimer;
        private Storyboard _pulseStoryboard;
        private TrayIconService _tray;
        private RegisteredWaitHandle _showWaitHandle;

        public MainWindow()
        {
            InitializeComponent();
            _chromeClip.RadiusX = 14;
            _chromeClip.RadiusY = 14;
            WindowChrome.Clip = _chromeClip;
            TxtMachineName.Text = Environment.MachineName;
            TxtAppVersion.Text = AppVersion.Display;
            TxtAppVersion.ToolTip = "Zapretik " + AppVersion.Current;

            DotOnlineBrush.Freeze();
            DotOfflineBrush.Freeze();

            // Light: only refresh uptime text (no animations, no process scans).
            _uptimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uptimeTimer.Tick += UptimeTimer_Tick;

            // Heavy: process/service check less often, on background thread.
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _statusTimer.Tick += StatusTimer_Tick;

            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateChromeClip();
            LoadLastLaunchInfo();
            LoadZapretPath();

            _pulseStoryboard = (Storyboard)FindResource("OnlinePulseStoryboard");
            RefreshOnlineStatus(forceUi: true, resolveScript: true);
            _uptimeTimer.Start();
            _statusTimer.Start();

            InitTray();
            RefreshAutostartButton();
            StartSingleInstanceListener();

            // Autostart: open directly in tray without flashing the main window.
            if (App.StartMinimizedToTray)
            {
                // Defer hide until after first layout/render.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    HideToTray();
                }), DispatcherPriority.ApplicationIdle);
            }

            var enter = (Storyboard)FindResource("WindowEnterStoryboard");
            if (!App.StartMinimizedToTray)
            {
                enter.Begin(this);
                // Cold start only (not --tray): check for updates after UI is ready
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CheckForUpdatesOnColdStart();
                }), DispatcherPriority.ApplicationIdle);
            }
            else
            {
                // Tray start — no update popup
            }
        }

        /// <summary>
        /// Shows update prompt only when user opens the app window (not when started into tray).
        /// </summary>
        private void CheckForUpdatesOnColdStart()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                UpdateInfo info;
                string error;
                var ok = UpdateChecker.TryGetLatest(out info, out error);
                if (!ok || info == null)
                {
                    // Silent on network errors at startup
                    return;
                }

                if (!UpdateChecker.IsNewerThanCurrent(info.Version))
                    return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    PromptAndApplyUpdate(info);
                }));
            });
        }

        private void PromptAndApplyUpdate(UpdateInfo info)
        {
            if (info == null)
                return;

            var notes = string.IsNullOrWhiteSpace(info.Notes) ? string.Empty : "\n\n" + info.Notes;
            var msg =
                "Доступна новая версия Zapretik.\n\n" +
                "Сейчас: " + AppVersion.Current + "\n" +
                "Новая: " + info.Version + notes + "\n\n" +
                "Обновить сейчас? Приложение перезапустится.";

            if (!AppDialog.Confirm(this, msg, "Обновление Zapretik"))
                return;

            try
            {
                UiAnimation.SetText(TxtStatusDescription, "Скачивание обновления " + info.Version + "…", slide: false);
                SetServiceButtonsEnabled(false);

                System.Threading.ThreadPool.QueueUserWorkItem(__ =>
                {
                    try
                    {
                        UpdateInstaller.DownloadAndApply(info, progress =>
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                UiAnimation.SetText(TxtStatusDescription, progress, slide: false);
                            }));
                        });

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Exit fully so updater can replace exe (not hide to tray)
                            ExitApplication();
                        }));
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            SetServiceButtonsEnabled(true);
                            AppDialog.Show(
                                this,
                                "Не удалось обновить приложение:\n" + ex.Message,
                                "Обновление Zapretik",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }));
                    }
                });
            }
            catch (Exception ex)
            {
                SetServiceButtonsEnabled(true);
                AppDialog.Show(
                    this,
                    "Не удалось обновить приложение:\n" + ex.Message,
                    "Обновление Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// When a second .exe is launched, it signals this event — we bring the existing window up.
        /// </summary>
        private void StartSingleInstanceListener()
        {
            var showEvent = App.ShowEvent;
            if (showEvent == null)
                return;

            _showWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                showEvent,
                (state, timedOut) =>
                {
                    Dispatcher.BeginInvoke(new Action(ShowFromTray));
                },
                null,
                Timeout.Infinite,
                false);
        }

        private void InitTray()
        {
            if (_tray != null)
                return;

            _tray = new TrayIconService();
            _tray.OpenRequested += () => Dispatcher.BeginInvoke(new Action(ShowFromTray));
            _tray.ExitRequested += () => Dispatcher.BeginInvoke(new Action(ExitApplication));
            _tray.InstallRequested += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowFromTray();
                InstallSelectedStrategy();
            }));
            _tray.StopRequested += () => Dispatcher.BeginInvoke(new Action(StopZapretService));
            _tray.StrategyRequested += bat => Dispatcher.BeginInvoke(new Action(() => InstallStrategyFromTray(bat)));

            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            if (_tray == null)
                return;

            var active = _detectedScript != null ? _detectedScript.DisplayName : null;
            if (string.IsNullOrWhiteSpace(active) && _isOnline && _activeBat != null)
                active = _activeBat.Name;

            var selected = LstBatFiles.SelectedItem as BatFileItem;
            var selectedName = selected != null ? selected.Name : null;

            // Tooltip + menu always show current bypass when online.
            _tray.UpdateTooltip(_isOnline, active);
            _tray.RebuildMenu(_batFiles, _isOnline, active, selectedName);
        }

        private bool IsInTray
        {
            get { return !IsVisible || WindowState == WindowState.Minimized; }
        }

        /// <summary>
        /// When hidden in tray — slow down polling and stop UI animations to save CPU.
        /// </summary>
        private void ApplyTrayPerformanceMode(bool inTray)
        {
            if (inTray)
            {
                // No need to refresh uptime text while window is hidden.
                _uptimeTimer.Stop();
                // Rare background checks only.
                _statusTimer.Interval = TimeSpan.FromSeconds(8);
                StopPulse();
            }
            else
            {
                if (!_uptimeTimer.IsEnabled)
                    _uptimeTimer.Start();
                _statusTimer.Interval = TimeSpan.FromSeconds(2);
                if (_isOnline)
                    StartPulse();
                UpdateUptimeText();
            }
        }

        private void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            ApplyTrayPerformanceMode(false);
            // Quick refresh when user opens the window again.
            RefreshOnlineStatus(forceUi: true, resolveScript: true);
        }

        private void HideToTray()
        {
            Hide();
            ApplyTrayPerformanceMode(true);
            RefreshTrayMenu();
        }

        private void ExitApplication()
        {
            _exitRequested = true;
            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }
            _uptimeTimer.Stop();
            _statusTimer.Stop();
            Application.Current.Shutdown();
        }

        private void InstallStrategyFromTray(BatFileItem bat)
        {
            if (bat == null)
                return;

            // Select in list so InstallSelectedStrategy uses it
            if (_batFiles.Contains(bat))
                LstBatFiles.SelectedItem = bat;

            InstallSelectedStrategy();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_exitRequested)
                return;

            // X / Alt+F4 → hide to tray, keep running
            e.Cancel = true;
            HideToTray();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            _uptimeTimer.Stop();
            _statusTimer.Stop();

            try
            {
                if (_showWaitHandle != null)
                {
                    _showWaitHandle.Unregister(null);
                    _showWaitHandle = null;
                }
            }
            catch
            {
            }

            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                HideToTray();
        }

        private void WindowChrome_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateChromeClip();
        }

        private void UpdateChromeClip()
        {
            if (WindowChrome.ActualWidth <= 0 || WindowChrome.ActualHeight <= 0)
                return;

            _chromeClip.Rect = new Rect(
                0,
                0,
                WindowChrome.ActualWidth,
                WindowChrome.ActualHeight);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            if (e.OriginalSource is DependencyObject source)
            {
                var button = FindAncestor<Button>(source);
                if (button != null)
                    return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            HideToTray();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Close button hides to tray (exit only from tray menu).
            HideToTray();
        }

        private void BtnBrowseZapret_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите корневую папку Zapret";
                dialog.ShowNewFolderButton = false;

                if (!string.IsNullOrWhiteSpace(_zapretRootPath) && Directory.Exists(_zapretRootPath))
                    dialog.SelectedPath = _zapretRootPath;

                var result = dialog.ShowDialog();
                if (result != Forms.DialogResult.OK)
                    return;

                SetZapretPath(dialog.SelectedPath, save: true);
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Always install/replace selected strategy (service.bat → 1 already removes old service).
            InstallSelectedStrategy();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopZapretService();
        }

        /// <summary>
        /// service.bat menu 1: install selected strategy as Windows service "zapret".
        /// If something is already running, it is removed first (same as service.bat install).
        /// </summary>
        private void InstallSelectedStrategy()
        {
            if (string.IsNullOrWhiteSpace(_zapretRootPath) || !Directory.Exists(_zapretRootPath))
            {
                AppDialog.Show(
                    this,
                    "Сначала укажите корневую папку Zapret.",
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var serviceBat = Path.Combine(_zapretRootPath, "service.bat");
            if (!File.Exists(serviceBat))
            {
                AppDialog.Show(
                    this,
                    "В выбранной папке нет service.bat.\nНужен дистрибутив zapret-discord-youtube (Flowseal).",
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var selected = LstBatFiles.SelectedItem as BatFileItem;
            if (selected == null)
            {
                AppDialog.Show(
                    this,
                    "Выберите стратегию (.bat) из списка справа.",
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (ZapretServiceManager.IsServiceBat(selected.Name))
            {
                AppDialog.Show(
                    this,
                    "service.bat — менеджер служб, его нельзя ставить как стратегию.",
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!File.Exists(selected.FullPath))
            {
                AppDialog.Show(
                    this,
                    "Файл больше не существует:\n" + selected.FullPath,
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ReloadBatFiles();
                return;
            }

            var isSwitch = IsZapretEngineRunning() || ZapretProcessInspector.IsZapretServiceRunning();

            try
            {
                SetServiceButtonsEnabled(false);
                UiAnimation.SetText(
                    TxtStatusDescription,
                    isSwitch
                        ? "Смена стратегии (снятие + установка)…"
                        : "Установка службы (service.bat → 1)…");

                // Install always replaces existing service (like service.bat option 1).
                ZapretServiceManager.InstallStrategy(_zapretRootPath, selected.FullPath);

                _activeBat = selected;
                _launchedAtUtc = DateTime.UtcNow;
                SaveLastLaunch(selected);

                UiAnimation.SetText(
                    TxtStatusDescription,
                    isSwitch ? "Стратегия сменена, ожидаем winws…" : "Служба установлена, ожидаем winws…");
                RefreshOnlineStatus(forceUi: true, resolveScript: true);
            }
            catch (OperationCanceledException)
            {
                UiAnimation.SetText(TxtStatusDescription, "Установка отменена (UAC).");
            }
            catch (Exception ex)
            {
                AppDialog.Show(
                    this,
                    "Не удалось установить стратегию через службу:\n" + ex.Message,
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                RefreshOnlineStatus(forceUi: true, resolveScript: true);
            }
            finally
            {
                SetServiceButtonsEnabled(true);
            }
        }

        /// <summary>service.bat menu 2: remove services + kill winws.</summary>
        private void StopZapretService()
        {
            try
            {
                SetServiceButtonsEnabled(false);
                UiAnimation.SetText(TxtStatusDescription, "Снятие службы (service.bat → 2)…");

                ZapretServiceManager.RemoveServices(_zapretRootPath);

                _activeBat = null;
                _launchedAtUtc = null;
                RefreshOnlineStatus(forceUi: true, resolveScript: true);
            }
            catch (OperationCanceledException)
            {
                UiAnimation.SetText(TxtStatusDescription, "Снятие службы отменено (UAC).");
            }
            catch (Exception ex)
            {
                AppDialog.Show(
                    this,
                    "Не удалось снять службу zapret:\n" + ex.Message,
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RefreshOnlineStatus(forceUi: true, resolveScript: true);
            }
            finally
            {
                SetServiceButtonsEnabled(true);
            }
        }

        private void SetServiceButtonsEnabled(bool enabled)
        {
            BtnStart.IsEnabled = enabled;
            BtnStop.IsEnabled = enabled;
        }

        private void UptimeTimer_Tick(object sender, EventArgs e)
        {
            // Cheap path only — plain text, no process scans, no storyboards.
            if (_isOnline)
                UpdateUptimeText();
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (_statusCheckBusy)
                return;

            _statusCheckBusy = true;
            _monitorTick++;

            // Resolve script rarely — WMI process tree is expensive.
            // In tray mode timer is already slower (8s), still skip some WMI ticks.
            var resolveScript = IsInTray
                ? (_monitorTick % 2) == 0   // ~every 16s in tray
                : (_monitorTick % 3) == 0;  // ~every 6s when window is open
            var forceUi = false;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    DateTime? engineStartUtc;
                    string engineName;
                    var online = TryGetZapretEngineInfo(out engineStartUtc, out engineName);

                    ActiveScriptInfo resolved = null;
                    if (online && resolveScript)
                        resolved = ResolveActiveScript();

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ApplyOnlineSnapshot(online, engineStartUtc, resolved, resolveScript, forceUi);
                        }
                        finally
                        {
                            _statusCheckBusy = false;
                        }
                    }));
                }
                catch
                {
                    Dispatcher.BeginInvoke(new Action(() => { _statusCheckBusy = false; }));
                }
            });
        }

        /// <summary>
        /// Online = Zapret engine process (winws) is running.
        /// </summary>
        private void RefreshOnlineStatus(bool forceUi, bool resolveScript = true)
        {
            DateTime? engineStartUtc;
            string engineName;
            var online = TryGetZapretEngineInfo(out engineStartUtc, out engineName);

            ActiveScriptInfo resolved = null;
            if (online && resolveScript)
                resolved = ResolveActiveScript();

            ApplyOnlineSnapshot(online, engineStartUtc, resolved, resolveScript, forceUi);
        }

        private void ApplyOnlineSnapshot(
            bool online,
            DateTime? engineStartUtc,
            ActiveScriptInfo resolved,
            bool hadScriptResolve,
            bool forceUi)
        {
            if (online)
            {
                if (engineStartUtc.HasValue)
                    _sessionStartedUtc = engineStartUtc;
                else if (_sessionStartedUtc == null)
                    _sessionStartedUtc = DateTime.UtcNow;

                if (hadScriptResolve && resolved != null)
                {
                    var displayChanged = !string.Equals(
                        _lastActiveDisplay,
                        resolved.DisplayName,
                        StringComparison.OrdinalIgnoreCase);

                    if (!_isOnline || forceUi || displayChanged)
                    {
                        _isOnline = true;
                        _detectedScript = resolved;
                        _lastActiveDisplay = resolved.DisplayName ?? string.Empty;
                        UpdateConnectionUi(isOnline: true, script: resolved);
                        return;
                    }
                }

                if (!_isOnline || forceUi)
                {
                    _isOnline = true;
                    if (resolved != null)
                    {
                        _detectedScript = resolved;
                        _lastActiveDisplay = resolved.DisplayName ?? string.Empty;
                    }
                    UpdateConnectionUi(isOnline: true, script: resolved ?? _detectedScript);
                }
                // else: stay online, uptime timer handles the clock
            }
            else
            {
                _sessionStartedUtc = null;
                _detectedScript = null;
                _lastActiveDisplay = string.Empty;

                if (_isOnline || forceUi)
                {
                    _isOnline = false;
                    UpdateConnectionUi(isOnline: false, script: null);
                }
            }
        }

        private ActiveScriptInfo ResolveActiveScript()
        {
            // Cheap first: registry strategy (same as service.bat) — no WMI.
            ActiveScriptInfo fromService = null;
            try
            {
                fromService = ZapretProcessInspector.TryGetServiceStrategy(_batFiles, _zapretRootPath);
            }
            catch
            {
            }

            var serviceRunning = false;
            try
            {
                serviceRunning = ZapretProcessInspector.IsZapretServiceRunning();
            }
            catch
            {
            }

            if (serviceRunning && fromService != null)
                return fromService;

            // Expensive: only if not running as service (standalone bat parent tree).
            if (!serviceRunning)
            {
                try
                {
                    var fromTree = ZapretProcessInspector.TryResolveFromProcessTree(_batFiles, _zapretRootPath);
                    if (fromTree != null)
                        return fromTree;
                }
                catch
                {
                }
            }

            if (fromService != null)
                return fromService;

            if (_activeBat != null && _launchedAtUtc.HasValue)
            {
                var age = DateTime.UtcNow - _launchedAtUtc.Value;
                if (age <= TimeSpan.FromMinutes(3))
                    return new ActiveScriptInfo(_activeBat.Name, _activeBat.FullPath, "launched");

                _activeBat = null;
                _launchedAtUtc = null;
            }

            return null;
        }

        /// <summary>
        /// Lightweight engine check. Avoid Process.StartTime unless we need a new session start.
        /// </summary>
        private bool TryGetZapretEngineInfo(out DateTime? earliestStartUtc, out string processName)
        {
            earliestStartUtc = null;
            processName = null;

            foreach (var name in ZapretEngineProcessNames)
            {
                Process[] processes = null;
                try
                {
                    processes = Process.GetProcessesByName(name);
                    if (processes.Length == 0)
                        continue;

                    processName = name;

                    // Only query StartTime when we don't already have a session start (avoids slow security checks every tick).
                    if (_sessionStartedUtc == null || !_isOnline)
                    {
                        foreach (var process in processes)
                        {
                            try
                            {
                                var start = process.StartTime.ToUniversalTime();
                                if (earliestStartUtc == null || start < earliestStartUtc.Value)
                                    earliestStartUtc = start;
                            }
                            catch
                            {
                                if (earliestStartUtc == null)
                                    earliestStartUtc = DateTime.UtcNow;
                            }
                        }
                    }
                    else
                    {
                        earliestStartUtc = _sessionStartedUtc;
                    }

                    return true;
                }
                catch
                {
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (var process in processes)
                        {
                            try { process.Dispose(); } catch { }
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsZapretEngineRunning()
        {
            foreach (var name in ZapretEngineProcessNames)
            {
                Process[] processes = null;
                try
                {
                    processes = Process.GetProcessesByName(name);
                    if (processes.Length > 0)
                        return true;
                }
                catch
                {
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (var process in processes)
                        {
                            try { process.Dispose(); } catch { }
                        }
                    }
                }
            }

            return false;
        }

        private void UpdateConnectionUi(bool isOnline, ActiveScriptInfo script)
        {
            if (isOnline)
            {
                UiAnimation.SetText(TxtConnectionStatus, "Онлайн");
                UiAnimation.SetText(TxtStatusCardConnection, "Онлайн");
                OnlineDot.Fill = DotOnlineBrush;

                string activeName;
                string activeTip;
                string statusLine;

                if (script != null && !string.IsNullOrWhiteSpace(script.DisplayName))
                {
                    activeName = script.DisplayName;
                    activeTip = string.IsNullOrWhiteSpace(script.Detail) ? script.DisplayName : script.Detail;
                    statusLine = "Информация об активном скрипте";
                }
                else
                {
                    activeName = "Не определён";
                    activeTip = "winws запущен, но bat/cmd в дереве процессов не найден";
                    statusLine = "Zapret онлайн — скрипт не удалось определить по процессам";
                }

                UiAnimation.SetText(TxtActiveBat, activeName);
                TxtActiveBat.ToolTip = activeTip;
                UiAnimation.SetText(TxtStatusDescription, statusLine);
                SetStartButtonContent("Сменить стратегию");
                BtnStop.Visibility = Visibility.Visible;
                UpdateUptimeText();
                StartPulse();
                // No PulseElement here — scale animation every status flip is expensive.
            }
            else
            {
                UiAnimation.SetText(TxtConnectionStatus, "Отключен");
                UiAnimation.SetText(TxtStatusCardConnection, "Отключен");
                OnlineDot.Fill = DotOfflineBrush;
                OnlineDot.Opacity = 1;
                OnlineDotScale.ScaleX = 1;
                OnlineDotScale.ScaleY = 1;
                UiAnimation.SetText(TxtActiveBat, "—");
                TxtActiveBat.ToolTip = "Процесс winws не найден";
                TxtUptime.Text = "—";
                SetStartButtonContent("Установить службу");
                BtnStop.Visibility = Visibility.Collapsed;
                StopPulse();

                // Clear in-session launch memory when offline.
                _activeBat = null;
                _launchedAtUtc = null;

                if (string.IsNullOrWhiteSpace(_zapretRootPath))
                    UiAnimation.SetText(TxtStatusDescription, "Укажите корневую папку Zapret, чтобы приложение могло с ним работать.");
                else if (!Directory.Exists(_zapretRootPath))
                    UiAnimation.SetText(TxtStatusDescription, "Сохранённый путь больше не существует. Выберите папку заново.");
                else
                {
                    var selected = LstBatFiles.SelectedItem as BatFileItem;
                    UiAnimation.SetText(
                        TxtStatusDescription,
                        selected != null
                            ? "Готов к запуску: " + selected.Name
                            : "Папка Zapret указана. Выберите bat-файл справа.");
                }
            }

            RefreshLastLaunchUi();
            RefreshTrayMenu();
        }

        private void UpdateUptimeText()
        {
            if (_sessionStartedUtc == null)
            {
                if (TxtUptime.Text != "—")
                    TxtUptime.Text = "—";
                return;
            }

            var elapsed = DateTime.UtcNow - _sessionStartedUtc.Value;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            string text;
            if (elapsed.TotalHours >= 1)
                text = string.Format("{0:D2}:{1:D2}:{2:D2}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
            else
                text = string.Format("{0:D2}:{1:D2}", elapsed.Minutes, elapsed.Seconds);

            // Direct assignment — no opacity storyboards every second (that caused micro-stutters).
            if (!string.Equals(TxtUptime.Text, text, StringComparison.Ordinal))
                TxtUptime.Text = text;
        }

        private void SetStartButtonContent(string content)
        {
            var current = BtnStart.Content as string;
            if (string.Equals(current, content, StringComparison.Ordinal))
                return;

            BtnStart.BeginAnimation(UIElement.OpacityProperty, null);
            var fadeOut = new DoubleAnimation
            {
                To = 0.25,
                Duration = TimeSpan.FromMilliseconds(100),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                BtnStart.Content = content;
                var fadeIn = new DoubleAnimation
                {
                    From = 0.25,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BtnStart.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            };
            BtnStart.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void StartPulse()
        {
            if (_pulseStoryboard == null)
                return;

            _pulseStoryboard.Stop(this);
            _pulseStoryboard.Begin(this, true);
        }

        private void StopPulse()
        {
            if (_pulseStoryboard == null)
                return;

            _pulseStoryboard.Stop(this);
            OnlineDot.Opacity = 1;
            OnlineDotScale.ScaleX = 1;
            OnlineDotScale.ScaleY = 1;
        }

        private void SaveLastLaunch(BatFileItem bat)
        {
            var now = DateTime.Now;
            var prevName = Settings.Default.LastLaunchedBatName;
            var prevRelative = Settings.Default.LastLaunchedBatRelativePath;
            var prevAt = Settings.Default.LastLaunchedAt;

            // "Последний запуск" = strategy before the current one.
            // Shift Last → Previous only when switching to a different bat.
            var sameAsLast =
                !string.IsNullOrWhiteSpace(prevRelative) &&
                string.Equals(prevRelative, bat.RelativePath, StringComparison.OrdinalIgnoreCase);

            if (!sameAsLast &&
                (!string.IsNullOrWhiteSpace(prevName) || !string.IsNullOrWhiteSpace(prevRelative)))
            {
                Settings.Default.PreviousLaunchedBatName = prevName ?? string.Empty;
                Settings.Default.PreviousLaunchedBatRelativePath = prevRelative ?? string.Empty;
                Settings.Default.PreviousLaunchedAt = prevAt ?? string.Empty;
            }

            Settings.Default.LastLaunchedBatName = bat.Name;
            Settings.Default.LastLaunchedBatRelativePath = bat.RelativePath;
            Settings.Default.LastLaunchedAt = now.ToString("o", CultureInfo.InvariantCulture);
            Settings.Default.Save();
            RefreshLastLaunchUi();
        }

        private void LoadLastLaunchInfo()
        {
            RefreshLastLaunchUi();
        }

        private void RefreshLastLaunchUi()
        {
            // Show the previous strategy (before current install), not the one just launched.
            var name = Settings.Default.PreviousLaunchedBatName;
            var relative = Settings.Default.PreviousLaunchedBatRelativePath;
            var atRaw = Settings.Default.PreviousLaunchedAt;

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(relative))
            {
                UiAnimation.SetText(TxtLastBat, "Ещё не запускался");
                TxtLastBat.ToolTip = null;
                UiAnimation.SetText(TxtLastBatTime, string.Empty, slide: false);
                return;
            }

            var display = !string.IsNullOrWhiteSpace(name) ? name : Path.GetFileName(relative);
            UiAnimation.SetText(TxtLastBat, display);
            TxtLastBat.ToolTip = string.IsNullOrWhiteSpace(relative) ? display : relative;

            DateTime at;
            if (!string.IsNullOrWhiteSpace(atRaw) &&
                DateTime.TryParse(atRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at))
            {
                UiAnimation.SetText(
                    TxtLastBatTime,
                    at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture),
                    slide: false);
            }
            else
            {
                UiAnimation.SetText(TxtLastBatTime, string.Empty, slide: false);
            }
        }

        private void BtnGitHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppVersion.GitHubRepoUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppDialog.Show(
                    this,
                    "Не удалось открыть GitHub:\n" + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void LstBatFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressBatSelectionSave)
                return;

            var selected = LstBatFiles.SelectedItem as BatFileItem;
            Settings.Default.SelectedBatRelativePath = selected != null ? selected.RelativePath : string.Empty;
            Settings.Default.Save();

            if (!IsZapretEngineRunning())
                UpdateStatusForSelection(selected);

            if (selected != null)
                UiAnimation.PulseElement(CardRight, 1.02);

            RefreshTrayMenu();
        }

        private void LoadZapretPath()
        {
            var saved = Settings.Default.ZapretRootPath;
            if (!string.IsNullOrWhiteSpace(saved))
                SetZapretPath(saved, save: false);
            else
            {
                UpdateZapretUi();
                ClearBatList("Выберите папку Zapret — здесь появятся bat-файлы.");
            }
        }

        private void SetZapretPath(string path, bool save)
        {
            _zapretRootPath = (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (save)
            {
                Settings.Default.ZapretRootPath = _zapretRootPath;
                Settings.Default.Save();
            }

            UpdateZapretUi();
            ReloadBatFiles();
            RefreshIpsetUi();
        }

        private void UpdateZapretUi()
        {
            if (string.IsNullOrWhiteSpace(_zapretRootPath))
            {
                UiAnimation.SetText(TxtZapretPath, "Путь не выбран");
                TxtZapretPath.ToolTip = "Укажите корневую папку дистрибутива Zapret";
                if (!IsZapretEngineRunning())
                    UiAnimation.SetText(TxtStatusDescription, "Укажите корневую папку Zapret, чтобы приложение могло с ним работать.");
                RefreshIpsetUi();
                return;
            }

            UiAnimation.SetText(TxtZapretPath, _zapretRootPath);
            TxtZapretPath.ToolTip = _zapretRootPath;

            if (!Directory.Exists(_zapretRootPath))
            {
                if (!IsZapretEngineRunning())
                    UiAnimation.SetText(TxtStatusDescription, "Сохранённый путь больше не существует. Выберите папку заново.");
            }

            RefreshIpsetUi();
        }

        private void RefreshIpsetUi()
        {
            var ready = !string.IsNullOrWhiteSpace(_zapretRootPath) && Directory.Exists(_zapretRootPath);
            BtnOpenIpset.IsEnabled = ready;

            if (!ready)
            {
                BtnOpenIpset.ToolTip = "Сначала укажите папку Zapret";
                return;
            }

            try
            {
                var path = IpsetListManager.GetFilePath(_zapretRootPath);
                if (!File.Exists(path))
                {
                    BtnOpenIpset.Content = "IP-адреса…";
                    BtnOpenIpset.ToolTip = "lists\\ipset-all.txt — файл ещё не создан";
                    return;
                }

                var count = IpsetListManager.CountEntries(_zapretRootPath);
                BtnOpenIpset.Content = "IP · " + count.ToString("N0");
                BtnOpenIpset.ToolTip = path + Environment.NewLine + "Добавить / удалить записи";
            }
            catch
            {
                BtnOpenIpset.Content = "IP-адреса…";
                BtnOpenIpset.ToolTip = "Ошибка чтения ipset-all.txt";
            }
        }

        private void BtnOpenIpset_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_zapretRootPath) || !Directory.Exists(_zapretRootPath))
            {
                AppDialog.Show(this, "Сначала укажите папку Zapret.", "Zapretik",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new IpsetEditorWindow(_zapretRootPath)
            {
                Owner = this
            };
            dialog.ShowDialog();
            RefreshIpsetUi();
        }

        private void BtnAutostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var enable = !AutostartHelper.IsEnabled();
                AutostartHelper.SetEnabled(enable);
                RefreshAutostartButton();

                AppDialog.Show(
                    this,
                    enable
                        ? "Автозапуск включён.\nZapretik будет стартовать вместе с Windows и сразу уходить в трей."
                        : "Автозапуск выключен.",
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(
                    this,
                    "Не удалось изменить автозапуск:\n" + ex.Message,
                    "Zapretik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                RefreshAutostartButton();
            }
        }

        private void RefreshAutostartButton()
        {
            var on = AutostartHelper.IsEnabled();
            BtnAutostart.Content = on ? "Автозапуск: вкл" : "Автозапуск: выкл";
            BtnAutostart.ToolTip = on
                ? "Zapretik запускается с Windows (в трей).\nНажмите, чтобы выключить."
                : "Нажмите, чтобы запускать Zapretik вместе с Windows.";

            // Highlight when enabled
            BtnAutostart.Style = on
                ? (Style)FindResource("AutostartOnButton")
                : (Style)FindResource("BrowseButton");
        }

        private void ReloadBatFiles()
        {
            _batFiles.Clear();

            if (string.IsNullOrWhiteSpace(_zapretRootPath) || !Directory.Exists(_zapretRootPath))
            {
                ClearBatList(
                    string.IsNullOrWhiteSpace(_zapretRootPath)
                        ? "Выберите папку Zapret — здесь появятся bat-файлы."
                        : "Папка недоступна. Выберите путь заново.");
                return;
            }

            try
            {
                _batFiles.AddRange(ScanBatFiles(_zapretRootPath));
            }
            catch (UnauthorizedAccessException)
            {
                ClearBatList("Нет доступа к папке Zapret.");
                return;
            }
            catch (IOException)
            {
                ClearBatList("Не удалось прочитать файлы в папке Zapret.");
                return;
            }

            _suppressBatSelectionSave = true;
            try
            {
                LstBatFiles.ItemsSource = null;
                LstBatFiles.ItemsSource = _batFiles;
                TxtBatCount.Text = _batFiles.Count.ToString();

                if (_batFiles.Count == 0)
                {
                    LstBatFiles.Visibility = Visibility.Collapsed;
                    TxtBatEmpty.Visibility = Visibility.Visible;
                    TxtBatEmpty.Text = "Файлы .bat / .cmd не найдены";
                    TxtBatHint.Text = "В выбранной папке нет bat/cmd. Проверьте, что это корень Zapret.";
                    if (!IsZapretEngineRunning())
                        UiAnimation.SetText(TxtStatusDescription, "Папка указана, но скрипты .bat/.cmd не найдены.");
                    return;
                }

                LstBatFiles.Visibility = Visibility.Visible;
                TxtBatEmpty.Visibility = Visibility.Collapsed;
                TxtBatHint.Text = "Выберите стратегию и нажмите «Установить» / «Сменить» — старая служба снимется сама.";

                var savedRelative = Settings.Default.SelectedBatRelativePath;
                BatFileItem toSelect = null;

                if (!string.IsNullOrWhiteSpace(savedRelative))
                {
                    toSelect = _batFiles.FirstOrDefault(b =>
                        string.Equals(b.RelativePath, savedRelative, StringComparison.OrdinalIgnoreCase));
                }

                if (toSelect == null)
                    toSelect = _batFiles[0];

                LstBatFiles.SelectedItem = toSelect;
                LstBatFiles.ScrollIntoView(toSelect);

                Settings.Default.SelectedBatRelativePath = toSelect.RelativePath;
                Settings.Default.Save();

                if (!IsZapretEngineRunning())
                    UpdateStatusForSelection(toSelect);
            }
            finally
            {
                _suppressBatSelectionSave = false;
                RefreshTrayMenu();
            }
        }

        private void ClearBatList(string hint)
        {
            _suppressBatSelectionSave = true;
            try
            {
                _batFiles.Clear();
                LstBatFiles.ItemsSource = null;
                LstBatFiles.SelectedItem = null;
                LstBatFiles.Visibility = Visibility.Collapsed;
                TxtBatEmpty.Visibility = Visibility.Visible;
                TxtBatEmpty.Text = "Список пуст";
                TxtBatCount.Text = "0";
                TxtBatHint.Text = hint;
            }
            finally
            {
                _suppressBatSelectionSave = false;
                RefreshTrayMenu();
            }
        }

        private void UpdateStatusForSelection(BatFileItem selected)
        {
            if (IsZapretEngineRunning())
                return;

            if (selected == null)
            {
                if (!string.IsNullOrWhiteSpace(_zapretRootPath) && Directory.Exists(_zapretRootPath))
                    UiAnimation.SetText(TxtStatusDescription, "Папка Zapret указана. Выберите bat-файл справа.");
                return;
            }

            UiAnimation.SetText(TxtStatusDescription, "Готов к запуску: " + selected.Name);
            UiAnimation.PulseElement(CardLeft, 1.015);
        }

        private static List<BatFileItem> ScanBatFiles(string root)
        {
            var results = new List<BatFileItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFile(string fullPath)
            {
                if (!seen.Add(fullPath))
                    return;

                var relative = GetRelativePath(root, fullPath);
                results.Add(new BatFileItem(fullPath, relative));
            }

            void ScanDirectory(string directory, int depth)
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Same as service.bat: skip service*.bat (manager, not a strategy)
                    if (ZapretServiceManager.IsServiceBat(Path.GetFileName(file)))
                        continue;

                    AddFile(file);
                }

                if (depth >= MaxBatScanDepth)
                    return;

                string[] dirs;
                try
                {
                    dirs = Directory.GetDirectories(directory);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }

                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    if (IsSkippedDirectory(name))
                        continue;

                    ScanDirectory(dir, depth + 1);
                }
            }

            ScanDirectory(root, 0);

            return results
                .OrderBy(b => b.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSkippedDirectory(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;

            return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("node_modules", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string root, string fullPath)
        {
            try
            {
                var rootFull = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var fileFull = Path.GetFullPath(fullPath);

                if (fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return fileFull.Substring(rootFull.Length);

                return Path.GetFileName(fullPath);
            }
            catch
            {
                return Path.GetFileName(fullPath);
            }
        }

        private void AnimateClose()
        {
            if (_isClosingAnimated)
                return;

            _isClosingAnimated = true;

            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleX = new DoubleAnimation
            {
                To = 0.96,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleY = new DoubleAnimation
            {
                To = 0.96,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fade.Completed += (s, e) => Close();

            WindowChrome.BeginAnimation(OpacityProperty, fade);
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                    return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
