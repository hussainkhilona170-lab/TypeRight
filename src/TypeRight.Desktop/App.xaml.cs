using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using TypeRight.Configuration;
using TypeRight.Engine;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Privacy;
using TypeRight.Suggestions;
using TypeRight.TsfBridge;
using TypeRight.WPFOverlay;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace TypeRight.Desktop
{
    /// <summary>
    /// TypeRight 2.0 WPF application - runs as a Windows system tray utility.
    /// </summary>
    public partial class App : Application
    {
        private NotifyIcon _trayIcon;
        private ConfigurationSettings _configuration;
        private ICorrectionDecisionMaker _decisionMaker;
        private CorrectionPipeServer _pipeServer;
        private MainWindow _mainWindow;
        private EnterprisePolicy _policy;
        private PolicyEvaluator _policyEvaluator;
        private bool _serviceRunning;

        private OverlayWindow _overlay;
        private readonly object _overlayLock = new object();
        private DispatcherTimer _overlayTimer;
        private volatile string _activeSuggestionToken;

        /// <summary>
        /// Gets the application configuration.
        /// </summary>
        public static ConfigurationSettings Settings { get; private set; }

        /// <summary>
        /// Gets the correction decision maker (engine).
        /// </summary>
        public static ICorrectionDecisionMaker Engine { get; private set; }

        /// <summary>
        /// Reloadable engine used by the bridge. Stable instance, refreshed when
        /// the local personal dictionary changes.
        /// </summary>
        public static ReloadableDecisionMaker ReloadableEngine { get; private set; }

        /// <summary>
        /// Local feature gates. Cloud-dependent features are never enabled.
        /// </summary>
        public static LocalFeatureGates Features { get; private set; }

        /// <summary>
        /// Local suggestion feed used by the overlay. Holds identifiers and
        /// ranges only - no user text is persisted.
        /// </summary>
        public static SuggestionFeed Suggestions { get; private set; }

        /// <summary>
        /// Gets whether the correction service is currently running.
        /// </summary>
        public bool ServiceRunning => _serviceRunning;

        /// <summary>
        /// Captures a local-only, content-free health snapshot for the settings UI.
        /// </summary>
        public LocalDiagnosticsSnapshot GetLocalDiagnostics()
        {
            return LocalDiagnosticsSnapshot.Capture(
                _pipeServer?.GetHealthSnapshot(),
                _configuration,
                Features,
                _policyEvaluator);
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // Load configuration
                _configuration = new ConfigurationSettings();
                _configuration.Load();
                Settings = _configuration;

                // Personal learning is opt-in: only create the local vocabulary
                // database when the user has enabled it.
                if (_configuration.PersonalLearningEnabled)
                    _configuration.InitializeVocabulary();

                // Initialize correction engine. The reloadable wrapper lets the
                // bridge keep a stable instance while personal words change.
                ReloadableEngine = new ReloadableDecisionMaker(() => _configuration.LoadPersonalWords());
                _decisionMaker = ReloadableEngine;
                Engine = _decisionMaker;

                // Start the local named-pipe bridge so the native TSF text service
                // can request decisions from the engine without hosting the CLR.
                Features = new LocalFeatureGates();
                Suggestions = new SuggestionFeed();

                _serviceRunning = true;
                _policy = EnterprisePolicy.Load();
                _policyEvaluator = new PolicyEvaluator(_policy, IntegrationRuleset.LoadDefault());
                _pipeServer = new CorrectionPipeServer(_decisionMaker, _configuration, _policyEvaluator, Features,
                    () => _serviceRunning);
                _pipeServer.SuggestionOffered += OnSuggestionOffered;
                _pipeServer.SuggestionResolved += OnSuggestionResolved;
                _pipeServer.Start();

                // Initialize system tray
                InitializeTrayIcon();

                // Create main window (hidden initially - tray app)
                _mainWindow = new MainWindow(_configuration, _decisionMaker);
                this.MainWindow = _mainWindow;

                // Show window on first run or if no config file exists
                var configPath = Path.Combine(AppContext.BaseDirectory, _configuration.ConfigPath);
                if (!File.Exists(configPath) || e.Args.Length > 0 && e.Args[0] == "--show")
                {
                    _mainWindow.Show();
                }
                else
                {
                    _trayIcon.ShowBalloonTip(3000, "TypeRight 2.0",
                        "TypeRight is running in the system tray.", ToolTipIcon.Info);
                }
            }
            catch (Exception)
            {
                // Don't display exception text - it may contain paths or user data.
                MessageBox.Show("TypeRight could not start. Please check the local installation and try again.",
                    "TypeRight 2.0", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "TypeRight 2.0 - Typing Companion",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open TypeRight");
            openItem.Click += (s, e) => ShowMainWindow();

            var statusItem = new ToolStripMenuItem("Service: Running")
            {
                Enabled = false
            };

            int activeFeatures = 0;
            if (Features != null)
                foreach (var pair in Features.Snapshot())
                    if (pair.Value) activeFeatures++;

            var featureItem = new ToolStripMenuItem($"Features: {activeFeatures} active")
            {
                Enabled = false
            };

            var toggleItem = new ToolStripMenuItem("Pause Service");
            toggleItem.Click += (s, e) =>
            {
                _serviceRunning = !_serviceRunning;
                if (!_serviceRunning)
                    DismissActiveSuggestion();
                toggleItem.Text = _serviceRunning ? "Pause Service" : "Resume Service";
                statusItem.Text = _serviceRunning ? "Service: Running" : "Service: Paused";
                _trayIcon.Text = _serviceRunning
                    ? "TypeRight 2.0 - Typing Companion"
                    : "TypeRight 2.0 - Paused";
            };

            var settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += (s, e) => ShowMainWindow();

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => Shutdown();

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(statusItem);
            contextMenu.Items.Add(featureItem);
            contextMenu.Items.Add(toggleItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private Icon LoadIcon()
        {
            // The icon is a WPF resource; prefer the embedded copy so the tray
            // never falls back to the generic system icon just because it was
            // not copied next to the executable.
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/TypeRight.ico", UriKind.Absolute);
                var resource = Application.GetResourceStream(uri);
                if (resource != null)
                {
                    using (resource.Stream)
                    {
                        return new Icon(resource.Stream);
                    }
                }
            }
            catch
            {
                // Try the on-disk copy next.
            }

            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "TypeRight.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath);
            }
            catch
            {
                // Fall through to the system icon.
            }

            return SystemIcons.Application;
        }

        /// <summary>
        /// Bridge events arrive on pipe worker threads; all overlay work is
        /// marshalled to the application Dispatcher.
        /// </summary>
        private void OnSuggestionOffered(object sender, SuggestionOfferedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => ShowOverlayFor(e));
        }

        private void OnSuggestionResolved(object sender, SuggestionResolvedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (string.IsNullOrEmpty(e.Token) || e.Token == _activeSuggestionToken)
                    HideOverlay();
            });
        }

        private void ShowOverlayFor(SuggestionOfferedEventArgs e)
        {
            try
            {
                if (e.Suggestions == null || e.Suggestions.Count == 0)
                    return;

                EnsureOverlay();

                // A single overlay instance: a newer word replaces an older offer.
                string previous = _activeSuggestionToken;
                if (!string.IsNullOrEmpty(previous) && previous != e.Token)
                    _pipeServer?.DismissSuggestion(previous);

                _activeSuggestionToken = e.Token;
                _overlay.SetStatus("Choose a suggestion");
                _overlay.SetSuggestions(e.Suggestions.ToList());

                var anchor = GetAnchorPoint();
                _overlay.ShowAt(anchor.X + 8, anchor.Y + 8);
                StartOverlayTimer();
            }
            catch (Exception)
            {
                // Never surface exception text or word content from the UI path.
                HideOverlay();
            }
        }

        private void EnsureOverlay()
        {
            lock (_overlayLock)
            {
                if (_overlay != null) return;
                _overlay = new OverlayWindow();
                _overlay.SuggestionSelectedIndexed += OnOverlaySuggestionSelected;
                _overlay.SuggestionDismissRequested += OnOverlayDismissRequested;
            }
        }

        private void OnOverlaySuggestionSelected(object sender, SuggestionSelectionEventArgs e)
        {
            string token = _activeSuggestionToken;
            if (token != null)
                _pipeServer?.TryResolveSuggestion(token, e.Index);
            HideOverlay();
        }

        private void OnOverlayDismissRequested(object sender, EventArgs e)
        {
            DismissActiveSuggestion();
        }

        private void StartOverlayTimer()
        {
            _overlayTimer?.Stop();
            if (_overlayTimer != null)
                _overlayTimer.Tick -= OnOverlayTimerTick;

            _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _overlayTimer.Tick += OnOverlayTimerTick;
            _overlayTimer.Start();
        }

        private void OnOverlayTimerTick(object sender, EventArgs e)
        {
            _overlayTimer?.Stop();
            string token = _activeSuggestionToken;
            if (token != null)
                _pipeServer?.DismissSuggestion(token);
            HideOverlay();
        }

        private void DismissActiveSuggestion()
        {
            string token = _activeSuggestionToken;
            _activeSuggestionToken = null;
            if (token != null)
                _pipeServer?.DismissSuggestion(token);
            Dispatcher.InvokeAsync(HideOverlay);
        }

        private void HideOverlay()
        {
            _activeSuggestionToken = null;
            if (_overlayTimer != null)
            {
                _overlayTimer.Stop();
                _overlayTimer.Tick -= OnOverlayTimerTick;
                _overlayTimer = null;
            }
            _overlay?.Dismiss();
        }

        /// <summary>
        /// Anchors the overlay near the focused element, falling back to the
        /// pointer and finally the work-area centre. No caret coordinates cross
        /// the pipe, so the native client cannot provide a more precise anchor.
        /// </summary>
        private System.Windows.Point GetAnchorPoint()
        {
            try
            {
                var focused = AutomationElement.FocusedElement;
                var rect = focused.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                    return DeviceToDip(rect.Right, rect.Bottom);
            }
            catch (Exception)
            {
                // UI Automation is optional; fall through to the pointer.
            }

            var cursor = System.Windows.Forms.Cursor.Position;
            return DeviceToDip(cursor.X, cursor.Y);
        }

        private static System.Windows.Point DeviceToDip(double x, double y)
        {
            try
            {
                using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (graphics.DpiX > 0 && graphics.DpiY > 0)
                        return new System.Windows.Point(
                            x * 96.0 / graphics.DpiX,
                            y * 96.0 / graphics.DpiY);
                }
            }
            catch (Exception)
            {
                // Fall back to one-to-one coordinates.
            }
            return new System.Windows.Point(x, y);
        }

        /// <summary>
        /// Shows the main window and brings it to foreground.
        /// </summary>
        public void ShowMainWindow()
        {
            if (_mainWindow == null)
                return;

            if (!_mainWindow.IsVisible)
                _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
            _mainWindow.Focus();
        }

        /// <summary>
        /// Adds a word to the opt-in local dictionary and refreshes the engine.
        /// Returns false when personal learning is disabled or storage rejects it.
        /// </summary>
        public static bool AddPersonalWord(string word)
        {
            try
            {
                if (Settings == null || !Settings.PersonalLearningEnabled)
                    return false;
                if (!Settings.AddPersonalWord(word, Settings.LanguageVariant))
                    return false;

                ReloadableEngine?.Reload();
                return true;
            }
            catch (Exception)
            {
                // Never expose paths, SQL errors or word content.
                return false;
            }
        }

        /// <summary>
        /// Shows a Windows notification via the system tray.
        /// </summary>
        public static void ShowNotification(string title, string message)
        {
            var app = Current as App;
            app?._trayIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Save configuration on exit. A failed write must not prevent cleanup.
            try { _configuration?.Save(); } catch { }

            // Stop observing before cleanup so no callback reaches a closed overlay.
            if (_pipeServer != null)
            {
                _pipeServer.SuggestionOffered -= OnSuggestionOffered;
                _pipeServer.SuggestionResolved -= OnSuggestionResolved;
            }

            string token = _activeSuggestionToken;
            _activeSuggestionToken = null;
            try
            {
                if (token != null) _pipeServer?.DismissSuggestion(token);
            }
            catch { }
            try
            {
                if (_overlayTimer != null) _overlayTimer.Stop();
                _overlay?.Close();
                _overlay = null;
            }
            catch { }

            // Stop the TSF bridge
            _pipeServer?.Dispose();

            // Clean up tray icon
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}