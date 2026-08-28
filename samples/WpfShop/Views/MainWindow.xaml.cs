using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfShop.ViewModels;

namespace WpfShop.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += OnLoaded;
            SearchBox.MouseDown += OnSearchBoxMouseDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Title = "WPF 商店";
        }

        private void OnSearch(object sender, RoutedEventArgs e)
        {
            var text = SearchBox.Text;
            Application.Current.Dispatcher.Invoke(() => Title = "搜索：" + text);
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void OnGridMouseDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(ProductsGrid);
            Title = $"点击 {p.X:0} / {p.Y:0}";
        }

        private void OnSearchBoxMouseDown(object sender, MouseButtonEventArgs e)
        {
        }

        private void CloseApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
