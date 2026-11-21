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

        /// <summary>
        /// Raised with the suggestion text when the user picks a suggestion.
        /// </summary>
        public event EventHandler<string> SuggestionSelected;

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
            if (suggestions == null)
                return;

            foreach (var suggestion in suggestions)
            {
                if (string.IsNullOrEmpty(suggestion)) continue;

                var suggestionText = new TextBlock
                {
                    Text = suggestion,
                    Tag = suggestion,
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
        /// Hides the overlay.
        /// </summary>
        public new void Hide()
        {
            base.Hide();
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
            string suggestion = source?.Tag as string;
            if (string.IsNullOrEmpty(suggestion) && source is TextBlock text)
                suggestion = text.Text;
            if (string.IsNullOrEmpty(suggestion))
                return; // click on empty panel space, not on a suggestion

            e.Handled = true;
            SuggestionSelected?.Invoke(this, suggestion);
            this.Close();
        }
    }
}