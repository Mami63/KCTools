using System.Windows;
using System.Windows.Controls;
using KCTools.ViewModels;

namespace KCTools.Views
{
    public partial class MtkView : UserControl
    {
        public MtkView()
        {
            InitializeComponent();
        }

        private void BrowseScatter_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "MTK scatter dosyası seç",
                Filter = "Scatter dosyası (*.txt;*.xml)|*.txt;*.xml|Tüm dosyalar (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true && DataContext is MtkViewModel viewModel)
            {
                viewModel.SetScatterPath(dialog.FileName);
            }
        }
    }
}
