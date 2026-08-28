using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WpfShop.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _searchText = "";

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
            }
        }

        public ObservableCollection<Product> Products { get; } = new()
        {
            new Product { Name = "机械键盘", Price = 399 },
            new Product { Name = "无线鼠标", Price = 129 },
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class Product
    {
        public string Name { get; set; } = "";
        public double Price { get; set; }
    }
}
