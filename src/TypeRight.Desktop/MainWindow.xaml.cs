using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TypeRight.Configuration;
using TypeRight.Domain.Models;
using TypeRight.Engine.CorrectionDecision;
using TypeRight.TsfBridge;
using MessageBox = System.Windows.MessageBox;

namespace TypeRight.Desktop
{
    /// <summary>
    /// Main application window - settings and correction engine test.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ConfigurationSettings _configuration;
        private readonly ICorrectionDecisionMaker _decisionMaker;
        private const string StartupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "TypeRight";

        public MainWindow(ConfigurationSettings configuration, ICorrectionDecisionMaker decisionMaker)
        {
            _configuration = configuration;
            _decisionMaker = decisionMaker;

            InitializeComponent();

            // Load settings into UI
            LoadSettings();
            RefreshHealth();
        }

        private void RefreshHealth()
        {
            try
            {
                var health = (System.Windows.Application.Current as App)?.GetLocalDiagnostics();
                if (health == null)
                {
                    HealthText.Text = "Health counters unavailable.";
                    return;
                }

                var bridge = health.Bridge;
                HealthText.Text = string.Join(Environment.NewLine,
                    $"Pipe: {bridge.PipeName}",
                    $"Uptime: {FormatUptime(bridge.Uptime)}",
                    $"Evaluations: {bridge.Evaluations}",
                    $"Pings: {bridge.Pings}",
                    $"Apply requests: {bridge.ApplyRequests}",
                    $"Denied: {bridge.Denied}",
                    $"Errors: {bridge.Errors}",
                    $"Engine timeouts: {bridge.EngineTimeouts}",
                    $"Suggestions offered: {bridge.SuggestionsOffered}",
                    $"Suggestions resolved: {bridge.SuggestionsResolved}",
                    $"Active clients: {bridge.ActiveClients}",
                    $"Pending suggestions: {bridge.PendingSuggestions}",
                    $"Active features: {health.ActiveFeatureCount}/{health.TotalFeatureCount}",
                    $"Excluded applications: {health.ExcludedApplicationCount}",
                    $"Personal words: {health.PersonalWordCount}",
                    $"Blocked clients: {health.BlockedClientCount}",
                    $"Blocked domains: {health.BlockedDomainCount}",
                    $"Policy rules: {health.IntegrationRuleCount}",
                    $"Sensitivity rules: {health.SensitivityRuleCount}");
            }
            catch (Exception)
            {
                // Never surface exception text or local paths in diagnostics.
                HealthText.Text = "Health counters unavailable.";
            }
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            return uptime.TotalDays >= 1
                ? uptime.ToString(@"d\.hh\:mm\:ss")
                : uptime.ToString(@"hh\:mm\:ss");
        }

        private void RefreshHealthButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshHealth();
        }

        private void LoadSettings()
        {
            // Language variant
            LanguageCombo.SelectedIndex = _configuration.LanguageVariant switch
            {
                LanguageVariant.EnUs => 1,
                LanguageVariant.EnGb => 2,
                _ => 0
            };

            PersonalLearningCheck.IsChecked = _configuration.PersonalLearningEnabled;
            UpdatePersonalWordControls(_configuration.PersonalLearningEnabled);
            ShowSuggestionsCheck.IsChecked = _configuration.ShowSuggestions;
            ConfidenceSlider.Value = _configuration.ConfidenceThreshold;
            ConfidenceValue.Text = _configuration.ConfidenceThreshold.ToString("0.00");

            ExcludedAppsBox.Text = _configuration.ExcludedApplications != null
                ? string.Join(Environment.NewLine, _configuration.ExcludedApplications)
                : string.Empty;

            StartWithWindowsCheck.IsChecked = IsStartupRegistered();
        }

        private void UpdatePersonalWordControls(bool enabled)
        {
            if (PersonalWordBox == null || AddPersonalWordButton == null) return;
            PersonalWordBox.IsEnabled = enabled;
            AddPersonalWordButton.IsEnabled = enabled;
        }

        private void PersonalLearningCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePersonalWordControls(PersonalLearningCheck.IsChecked ?? false);
        }

        private void AddPersonalWordButton_Click(object sender, RoutedEventArgs e)
        {
            var word = PersonalWordBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(word))
            {
                MessageBox.Show("Enter a single word to add.", "TypeRight 2.0",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!App.AddPersonalWord(word))
            {
                MessageBox.Show("Personal learning must be enabled and local storage must be available.",
                    "TypeRight 2.0", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PersonalWordBox.Clear();
            MessageBox.Show("Word added to your local dictionary.", "TypeRight 2.0",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveSettings()
        {
            // Language variant
            _configuration.LanguageVariant = LanguageCombo.SelectedIndex switch
            {
                1 => LanguageVariant.EnUs,
                2 => LanguageVariant.EnGb,
                _ => LanguageVariant.Auto
            };

            _configuration.PersonalLearningEnabled = PersonalLearningCheck.IsChecked ?? false;
            _configuration.ShowSuggestions = ShowSuggestionsCheck.IsChecked ?? false;
            _configuration.ConfidenceThreshold = (float)ConfidenceSlider.Value;

            _configuration.ExcludedApplications = ExcludedAppsBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            _configuration.Save();

            // Apply personal-learning changes immediately without restarting.
            App.ReloadableEngine?.Reload();

            // Startup registration
            SetStartupRegistration(StartWithWindowsCheck.IsChecked ?? false);
        }

        private bool IsStartupRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, false);
                return key?.GetValue(StartupValueName) != null;
            }
            catch
            {
                return false;
            }
        }

        private void SetStartupRegistration(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = GetExecutablePath();
                    key.SetValue(StartupValueName, $"\"{exePath}\"", RegistryValueKind.String);
                    App.ShowNotification("TypeRight 2.0", "TypeRight will now start with Windows.");
                }
                else
                {
                    key.DeleteValue(StartupValueName, false);
                }
            }
            catch (Exception)
            {
                // Exception text can contain paths or other local details.
                MessageBox.Show("The startup setting could not be updated.", "TypeRight 2.0",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string GetExecutablePath()
        {
            return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                   ?? Path.Combine(AppContext.BaseDirectory, "TypeRight.exe");
        }

        private void TestWordBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Clear previous result when input changes
            ResultPanel.Visibility = Visibility.Collapsed;
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            EvaluateWord();
        }

        private void EvaluateWord()
        {
            var word = TestWordBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(word))
            {
                MessageBox.Show("Enter a word to test.", "TypeRight 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Build a correction request using the real engine
                var request = new CorrectionRequest(
                    completedWord: word,
                    adjacentLeft: Array.Empty<string>(),
                    adjacentRight: Array.Empty<string>(),
                    capitalization: CapitalizationInfo.FromOriginal(word),
                    languageVariant: _configuration.LanguageVariant,
                    vocabularyVersion: new VocabularyVersion(2, 0));

                var decision = _decisionMaker.MakeDecision(request);

                // Display result
                ResultPanel.Visibility = Visibility.Visible;

                switch (decision.Action)
                {
                    case CorrectionAction.AutoCorrect:
                        ResultAction.Text = $"Auto-correct: {word} → {decision.CorrectedWord}";
                        ResultAction.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60));
                        break;
                    case CorrectionAction.Suggest:
                        var suggestions = decision.Alternatives != null
                            ? string.Join(", ", decision.Alternatives)
                            : "(none)";
                        ResultAction.Text = $"Suggestion: {suggestions}";
                        ResultAction.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x29, 0x80, 0xB9));
                        break;
                    default:
                        ResultAction.Text = $"Preserve: {word}";
                        ResultAction.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7F, 0x8C, 0x8D));
                        break;
                }

                ResultDetail.Text = decision.Rationale ?? string.Empty;
                ResultConfidence.Text = $"Confidence: {decision.Confidence:P0} | Reason: {decision.Reason}";
            }
            catch (Exception)
            {
                ResultPanel.Visibility = Visibility.Visible;
                ResultAction.Text = "Error";
                ResultDetail.Text = "The correction engine could not evaluate this word.";
            }
        }

        private void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ConfidenceValue != null)
                ConfidenceValue.Text = ConfidenceSlider.Value.ToString("0.00");
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            MessageBox.Show("Settings applied.", "TypeRight 2.0", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            Hide();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            App.ShowNotification("TypeRight 2.0", "TypeRight is running in the system tray.");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            Hide();
            App.ShowNotification("TypeRight 2.0", "TypeRight is running in the system tray. Use Exit in the tray menu to quit.");
            base.OnClosing(e);
        }
    }
}