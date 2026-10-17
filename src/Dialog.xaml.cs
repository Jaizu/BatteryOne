using System.Windows;

namespace BatteryOne
{
    /// <summary>Themed confirmation dialog with an optional "Don't show this again" check box.</summary>
    public partial class Dialog : Window
    {
        Dialog()
        {
            InitializeComponent();
            SourceInitialized += (s, e) => Theme.ApplyTitleBar(this);
        }

        /// <returns>True when the user clicked the primary button.</returns>
        public static bool Show(Window owner, string heading, string message, string primaryButton,
            bool danger, bool showDontAskAgain, out bool dontAskAgain)
        {
            var dialog = new Dialog();
            dialog.HeadingText.Text = heading;
            dialog.MessageText.Text = message;
            dialog.PrimaryButton.Content = primaryButton;
            if (danger) dialog.PrimaryButton.Style = (Style)dialog.FindResource("DangerButton");
            if (showDontAskAgain) dialog.DontAskAgainBox.Visibility = Visibility.Visible;

            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized) dialog.Owner = owner;
            else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            bool result = dialog.ShowDialog() == true;
            dontAskAgain = result && dialog.DontAskAgainBox.IsChecked == true;
            return result;
        }

        public static bool Show(Window owner, string heading, string message, string primaryButton, bool danger = false) =>
            Show(owner, heading, message, primaryButton, danger, false, out _);

        void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
