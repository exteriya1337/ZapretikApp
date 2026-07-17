using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace ZapretikApp
{
    public partial class App : Application
    {
        public const string MutexName = "Local\\ZapretikApp_SingleInstance_v1";
        public const string ShowEventName = "Local\\ZapretikApp_ShowWindow_v1";

        /// <summary>True when started with --tray (autostart / quiet launch).</summary>
        public static bool StartMinimizedToTray { get; private set; }

        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _showEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                // Another instance is already running — ask it to show the window.
                try
                {
                    using (var show = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        show.Set();
                    }
                }
                catch
                {
                    // Ignore if event missing
                }

                Shutdown();
                return;
            }

            try
            {
                bool created;
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out created);
            }
            catch
            {
                _showEvent = null;
            }

            if (e.Args != null)
            {
                StartMinimizedToTray = e.Args.Any(a =>
                    string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-tray", StringComparison.OrdinalIgnoreCase));
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_showEvent != null)
                {
                    _showEvent.Dispose();
                    _showEvent = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_singleInstanceMutex != null)
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                }
            }
            catch
            {
            }

            base.OnExit(e);
        }

        public static EventWaitHandle ShowEvent
        {
            get { return _showEvent; }
        }
    }
}
