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
            this.Focusable = true;

            // Initialize suggestions panel once
            if (!_suggestionsInitialized)
            {
                _suggestionsInitialized = true;
                this.SuggestionsPanel.MouseLeftButtonUp += OnSuggestionsMouseLeftButtonUp;
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
                statusText.Text = text;
            }
        }

        /// <summary>
        /// Sets the suggestions list in the overlay.
        /// </summary>
        /// <param name="suggestions">The list of suggestion strings.</param>
        public void SetSuggestions(List<string> suggestions)
        {
            var suggestionsPanel = this.FindName("SuggestionsPanel") as StackPanel;
            if (suggestionsPanel != null)
            {
                suggestionsPanel.Children.Clear();

                foreach (var suggestion in suggestions)
                {
                    var suggestionText = new TextBlock
                    {
                        Text = suggestion,
                        Margin = new Thickness(5),
                        Cursor = Cursors.Hand
                    };
                    suggestionsPanel.Children.Add(suggestionText);
                }
            }
        }

        /// <summary>
        /// Shows the overlay at the specified position.
        /// </summary>
        /// <param name="x">X coordinate (screen position).</param>
        /// <param name="y">Y coordinate (screen position).</param>
        public void ShowAt(double x, double y)
        {
            this.Left = x;
            this.Top = y;
            this.Show();
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
            // TODO: Accept suggestion and close overlay
            this.Close();
        }
    }
}