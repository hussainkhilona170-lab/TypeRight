namespace TypeRight.WPFOverlay
{
    using System;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Automation;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;

    /// <summary>
    /// Interaction logic for OverlayWindow.xaml
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private readonly List<string> _suggestions = new List<string>();

        /// <summary>
        /// Raised with the suggestion text when the user picks a suggestion.
        /// Kept for simple consumers; indexed selection is preferred because
        /// duplicate strings are otherwise ambiguous.
        /// </summary>
        public event EventHandler<string> SuggestionSelected;

        /// <summary>
        /// Raised with the zero-based alternative index when the user picks a
        /// suggestion. All public overlay methods must be called on the window's
        /// owning Dispatcher.
        /// </summary>
        public event EventHandler<SuggestionSelectionEventArgs> SuggestionSelectedIndexed;

        /// <summary>
        /// Raised when the user requests dismissal with Escape. Correlation state
        /// stays with the caller, so the overlay never resolves the token itself.
        /// </summary>
        public event EventHandler SuggestionDismissRequested;

        /// <summary>
        /// Initializes a new instance of the <see cref="OverlayWindow"/> class.
        /// </summary>
        public OverlayWindow()
        {
            InitializeComponent();

            // Non-activating overlay: never take focus from the host application.
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.WindowStyle = WindowStyle.None;
            this.ShowActivated = false;
            this.Focusable = false;

            AutomationProperties.SetName(this, "Typing suggestion overlay");
            AutomationProperties.SetHelpText(
                this, "Tab to a suggestion, press Enter to apply it, or press Escape to dismiss.");
        }

        /// <summary>
        /// Sets the status text in the overlay.
        /// </summary>
        /// <param name="text">The status message to display.</param>
        public void SetStatus(string text)
        {
            var statusText = this.FindName("StatusText") as TextBlock;
            if (statusText != null)
            {
                statusText.Text = text ?? string.Empty;
            }
        }

        /// <summary>
        /// Sets the suggestions list in the overlay. Buttons provide accessible
        /// names and keyboard activation without any global key capture.
        /// </summary>
        /// <param name="suggestions">The list of suggestion strings.</param>
        public void SetSuggestions(List<string> suggestions)
        {
            var suggestionsPanel = this.SuggestionsPanel ?? this.FindName("SuggestionsPanel") as StackPanel;
            if (suggestionsPanel == null)
                return;

            suggestionsPanel.Children.Clear();
            _suggestions.Clear();
            if (suggestions == null)
                return;

            foreach (string raw in suggestions)
            {
                if (string.IsNullOrEmpty(raw)) continue;

                int index = _suggestions.Count;
                string suggestion = raw;
                _suggestions.Add(suggestion);

                var button = new Button
                {
                    Tag = index,
                    Content = suggestion,
                    Margin = new Thickness(4, 1, 4, 1),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                    IsTabStop = true
                };
                AutomationProperties.SetAutomationId(button, "suggestion_" + index);
                AutomationProperties.SetName(button, "Suggestion " + (index + 1) + ": " + suggestion);
                AutomationProperties.SetHelpText(button, "Press Enter to apply this suggestion.");
                button.Click += (_, __) => RaiseSelection(index, suggestion);
                button.KeyDown += OnSuggestionKeyDown;
                suggestionsPanel.Children.Add(button);
            }
        }

        /// <summary>
        /// Shows the overlay at the specified position, clamped to the work area.
        /// </summary>
        /// <param name="x">X coordinate (screen position).</param>
        /// <param name="y">Y coordinate (screen position).</param>
        public void ShowAt(double x, double y)
        {
            var workArea = SystemParameters.WorkArea;
            double width = Dimension(this.ActualWidth, this.Width);
            double height = Dimension(this.ActualHeight, this.Height);

            if (double.IsNaN(x)) x = workArea.Left;
            if (double.IsNaN(y)) y = workArea.Top;

            double maxLeft = Math.Max(workArea.Left, workArea.Right - width);
            double maxTop = Math.Max(workArea.Top, workArea.Bottom - height);

            this.Left = Math.Min(Math.Max(x, workArea.Left), maxLeft);
            this.Top = Math.Min(Math.Max(y, workArea.Top), maxTop);
            this.Show();
        }

        private static double Dimension(double actual, double specified)
        {
            if (!double.IsNaN(actual) && actual > 0) return actual;
            if (!double.IsNaN(specified) && specified > 0) return specified;
            return 0;
        }

        /// <summary>
        /// Hides the overlay without tearing it down so it can be reused.
        /// </summary>
        public new void Hide()
        {
            base.Hide();
        }

        /// <summary>
        /// Dismisses the current suggestion. Correlation state is owned by the
        /// caller, so this only hides the window.
        /// </summary>
        public void Dismiss()
        {
            Hide();
        }

        /// <summary>
        /// Closes the overlay and releases resources.
        /// </summary>
        public new void Close()
        {
            base.Close();
        }

        private void OnSuggestionKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is int index) ||
                index < 0 || index >= _suggestions.Count)
                return;

            switch (e.Key)
            {
                case Key.Down:
                case Key.Right:
                    FocusSuggestion(SuggestionNavigator.MoveNext(index, _suggestions.Count));
                    e.Handled = true;
                    break;
                case Key.Up:
                case Key.Left:
                    FocusSuggestion(SuggestionNavigator.MovePrevious(index, _suggestions.Count));
                    e.Handled = true;
                    break;
                case Key.Escape:
                    SuggestionDismissRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        private void FocusSuggestion(int index)
        {
            var suggestionsPanel = this.SuggestionsPanel ?? this.FindName("SuggestionsPanel") as StackPanel;
            if (suggestionsPanel == null || index < 0 || index >= suggestionsPanel.Children.Count)
                return;
            (suggestionsPanel.Children[index] as Button)?.Focus();
        }

        private void RaiseSelection(int index, string suggestion)
        {
            if (index < 0 || index >= _suggestions.Count)
                return;

            SuggestionSelectedIndexed?.Invoke(this,
                new SuggestionSelectionEventArgs(index, suggestion));
            SuggestionSelected?.Invoke(this, suggestion);
            Dismiss();
        }
    }
}
