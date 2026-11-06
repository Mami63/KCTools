using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KCTools.Security;

namespace KCTools.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LicenseGate _gate;
        private bool _busy;

        public LoginWindow(LicenseGate gate)
        {
            InitializeComponent();

            _gate = gate;
            Loaded += OnLoaded;
        }

        private void OnDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Oturum kontrol ediliyor...";

            var result = await _gate.RestoreAsync();

            if (result.State == GateState.Authorized || result.State == GateState.Offline)
            {
                DialogResult = true;
                return;
            }

            StatusText.Text = result.State == GateState.Blocked
                ? result.Message
                : "Hesabınızla giriş yapın.";

            UserText.Text = _gate.Email != "—" ? _gate.Email : string.Empty;
        }

        private async void OnSignIn(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            SignInButton.IsEnabled = false;
            StatusText.Text = "Tarayıcı açılıyor... Girişi tamamlayın.";

            try
            {
                var result = await _gate.SignInAsync();

                if (result.State == GateState.Authorized || result.State == GateState.Offline)
                {
                    UserText.Text = _gate.Email + " · lisans bitişi " + _gate.LicenseEndText;
                    DialogResult = true;
                    return;
                }

                StatusText.Text = result.Message;
            }
            catch (TimeoutException)
            {
                StatusText.Text = "Giriş zaman aşımına uğradı. Tekrar deneyin.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Giriş başarısız: " + ex.Message;
            }
            finally
            {
                _busy = false;
                SignInButton.IsEnabled = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
