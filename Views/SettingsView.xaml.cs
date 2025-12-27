using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using PKTWinNode.Services;
using PKTWinNode.ViewModels;

namespace PKTWinNode.Views
{
    public partial class SettingsView : Window
    {
        public SettingsView(ISettingsService settingsService, IWslService wslService, IUpdateService updateService)
        {
            InitializeComponent();
            var viewModel = new SettingsViewModel(settingsService, wslService, updateService, this);
            DataContext = viewModel;

            WslPasswordBox.Password = viewModel.WslPassword;

            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.ShowPassword))
                {

                    if (viewModel.ShowPassword)
                    {

                        WslPasswordTextBox.Text = WslPasswordBox.Password;
                    }
                    else
                    {

                        WslPasswordBox.Password = WslPasswordTextBox.Text;
                    }
                }
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {

            if (DataContext is SettingsViewModel viewModel && viewModel.IsApplyingNetworkConfig)
            {
                e.Cancel = true;
            }

            base.OnClosing(e);
        }

        private void WslPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                viewModel.WslPassword = ((PasswordBox)sender).Password;
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {

            using (Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            }))
            {
                // Process disposed after browser launches
            }
            e.Handled = true;
        }

        private void IpAddressTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {

            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9.]+$");
        }

        private void IpAddressTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!Regex.IsMatch(text, @"^[0-9., ]+$"))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}
