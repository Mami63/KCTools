using System.Windows;

namespace KCTools.Views
{
    public partial class UnlockDialog : Window
    {
        public UnlockDialog()
        {
            InitializeComponent();
            PinBox.Focus();
        }

        public string Pin => PinBox.Password;

        public bool Remember => RememberBox.IsChecked == true;

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PinBox.Password))
            {
                ErrorText.Text = "PIN/şifre boş olamaz.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
