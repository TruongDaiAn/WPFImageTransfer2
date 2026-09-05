using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WPFImageTransfer
{
    public partial class PreviewAllWindow : Window
    {
        public BitmapSource? SelectedImage { get; private set; }

        public PreviewAllWindow(IEnumerable<BitmapSource> images)
        {
            InitializeComponent();
            icPreviews.ItemsSource = images;
        }

        private void Border_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (sender is FrameworkElement fe && fe.DataContext is BitmapSource bmp)
                {
                    SelectedImage = bmp;
                    this.DialogResult = true;
                    this.Close();
                }
            }
        }
    }
}
