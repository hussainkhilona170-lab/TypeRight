using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using TypeRight.Configuration;
using TypeRight.Engine;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.Privacy;
using TypeRight.Suggestions;
using TypeRight.TsfBridge;
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
        private bool _serviceRunning;

        /// <summary>
        /// Gets the application configuration.
        /// </summary>
        public static ConfigurationSettings Settings { get; private set; }

        /// <summary>
        /// Gets the correction decision maker (engine).
        /// </summary>
        public static ICorrectionDecisionMaker Engine { get; private set; }

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

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // Load configuration
                _configuration = new ConfigurationSettings();
                _configuration.Load();
                Settings = _configuration;

                // Initialize vocabulary database
                _configuration.InitializeVocabulary();

                // Initialize correction engine
                _decisionMaker = EngineComposition.CreateDecisionMaker();
                Engine = _decisionMaker;

                // Start the local named-pipe bridge so the native TSF text service
                // can request decisions from the engine without hosting the CLR.
                Features = new LocalFeatureGates();
                Suggestions = new SuggestionFeed();

                var policy = new PolicyEvaluator(EnterprisePolicy.Load(), IntegrationRuleset.LoadDefault());
                _pipeServer = new CorrectionPipeServer(_decisionMaker, _configuration, policy, Features);
                _pipeServer.Start();

                // Initialize system tray
                InitializeTrayIcon();

                // Create main window (hidden initially - tray app)
                _mainWindow = new MainWindow(_configuration, _decisionMaker);
                this.MainWindow = _mainWindow;

                // Start service
                _serviceRunning = true;

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
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start TypeRight: {ex.Message}", "TypeRight 2.0",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "TypeRight.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath);

                // Fallback: extract from assembly resource or use system icon
                return SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
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
        /// Shows a Windows notification via the system tray.
        /// </summary>
        public static void ShowNotification(string title, string message)
        {
            var app = Current as App;
            app?._trayIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Save configuration on exit
            _configuration?.Save();

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