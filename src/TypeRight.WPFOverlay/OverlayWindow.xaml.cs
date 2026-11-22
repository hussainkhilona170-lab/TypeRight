namespace TypeRight.WPFOverlay
{
    using System;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;

    /// <summary>
    /// Interaction logic for OverlayWindow.xaml
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private bool _suggestionsInitialized;
        private int _suggestionCount;

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
        /// Initializes a new instance of the <see cref="OverlayWindow"/> class.
        /// </summary>
        public OverlayWindow()
        {
            InitializeComponent();

            // Set up window properties for non-activating overlay
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.WindowStyle = WindowStyle.None;
            this.ShowActivated = false;
            this.Focusable = false;

            // Initialize suggestions panel once
            if (!_suggestionsInitialized)
            {
                _suggestionsInitialized = true;
                var panel = this.SuggestionsPanel ?? this.FindName("SuggestionsPanel") as StackPanel;
                if (panel != null)
                    panel.MouseLeftButtonUp += OnSuggestionsMouseLeftButtonUp;
            }
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
        /// Sets the suggestions list in the overlay.
        /// </summary>
        /// <param name="suggestions">The list of suggestion strings.</param>
        public void SetSuggestions(List<string> suggestions)
        {
            var suggestionsPanel = this.SuggestionsPanel ?? this.FindName("SuggestionsPanel") as StackPanel;
            if (suggestionsPanel == null)
                return;

            suggestionsPanel.Children.Clear();
            _suggestionCount = 0;
            if (suggestions == null)
                return;

            foreach (var suggestion in suggestions)
            {
                if (string.IsNullOrEmpty(suggestion)) continue;

                int index = _suggestionCount++;
                var suggestionText = new TextBlock
                {
                    Text = suggestion,
                    Tag = index,
                    Margin = new Thickness(5),
                    Cursor = Cursors.Hand
                };
                suggestionsPanel.Children.Add(suggestionText);
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

        private void OnSuggestionsMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as FrameworkElement;
            int? index = source?.Tag as int?;
            string suggestion = source is TextBlock text ? text.Text : source?.Tag as string;
            if ((!index.HasValue || index.Value < 0 || index.Value >= _suggestionCount) &&
                string.IsNullOrEmpty(suggestion))
                return; // click on empty panel space, not on a suggestion

            e.Handled = true;
            if (index.HasValue && index.Value >= 0 && index.Value < _suggestionCount)
                SuggestionSelectedIndexed?.Invoke(this,
                    new SuggestionSelectionEventArgs(index.Value, suggestion));
            else
                SuggestionSelected?.Invoke(this, suggestion);
            this.Close();
        }
    }
}