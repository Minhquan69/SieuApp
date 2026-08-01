using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using V3SClient.libs;
using V3SClient.viewModels;

namespace V3SClient.ucs.Settings.viewmodels
{
    public abstract class VMPageableBase<T> : VMBase
    {
        private ObservableCollection<T> _pagedItems;
        private int _currentPage = 1;
        private int _itemsPerPage = 20;
        private int _totalPages = 1;
        private string _pageInput = "1";
        private T _selectedItem;

        public string WindowTitle {  get; set; }    
        public bool ShowAddButton {  get; set; }=true;

        public ObservableCollection<T> AllItems { get; set; } = new ObservableCollection<T>();

        public ObservableCollection<T> PagedItems
        {
            get => _pagedItems;
            set
            {
                _pagedItems = value;
                OnPropertyChanged();
            }
        }
        public T SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value < 1) value = 1;
                if (value > TotalPages) value = TotalPages;

                _currentPage = value;
                PageInput = value.ToString();
                OnPropertyChanged();
                UpdatePagedItems();
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            set
            {
                _totalPages = value > 0 ? value : 1;
                OnPropertyChanged();
            }
        }

        public string PageInput
        {
            get => _pageInput;
            set
            {
                _pageInput = value;
                OnPropertyChanged();
            }
        }
        public ICommand PageInputLostFocusCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }

        public VMPageableBase()
        {
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => CanGoNext());
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => CurrentPage > 1);
            //EditCommand = new RelayCommand(_ => EditSelectedItem(), _ => SelectedItem != null);
            //DeleteCommand = new RelayCommand(_ => DeleteSelectedItem(), _ => SelectedItem != null);
            // Dùng CommandParameter thay vì SelectedItem
            EditCommand = new RelayCommand(
                param => OnEditItem((T)param),
                param => param is T
            );

            DeleteCommand = new RelayCommand(
                param => OnDeleteItem((T)param),
                param => param is T
            );

            AddCommand = new RelayCommand(_ => OnAddItem());
            PageInputLostFocusCommand = new RelayCommand(OnPageInputLostFocus);
        }
        
        private void EditSelectedItem()
        {
            if (SelectedItem != null)
            {
                OnEditItem(SelectedItem);
            }
        }

        private void DeleteSelectedItem()
        {
            if (SelectedItem != null)
            {
                OnDeleteItem(SelectedItem);
            }
        }
        private bool CanGoNext() => (CurrentPage * _itemsPerPage) < FilteredItems().Count();

        public void GoToPage(int page)
        {
            if (page < 1) page = 1;
            else if (page > TotalPages) page = TotalPages;

            CurrentPage = page;
        }

        protected void UpdatePagedItems()
        {
            var filtered = FilteredItems();
            int count = filtered.Count();
            TotalPages = (int)Math.Ceiling(count / (double)_itemsPerPage);
            if (TotalPages == 0) TotalPages = 1;

            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            if (CurrentPage < 1) CurrentPage = 1;

            PagedItems = new ObservableCollection<T>(
                filtered.Skip((CurrentPage - 1) * _itemsPerPage).Take(_itemsPerPage));
        }

        private void OnPageInputLostFocus(object _)
        {
            if (int.TryParse(PageInput, out int page))
            {
                GoToPage(page);
            }
            else
            {
                PageInput = CurrentPage.ToString(); // reset n?u nh?p sai
            }
        }


        /// <summary>
        /// Hàm abstract d? con class override cách l?c d? li?u
        /// </summary>
        protected abstract IEnumerable<T> FilteredItems();
        /// <summary>
        /// Hàm m? c?a s? ch?nh s?a – con class override
        /// </summary>
        protected virtual void OnEditItem(T item) { }

        /// <summary>
        /// Hàm x? lý xóa – con class override
        /// </summary>
        protected virtual void OnDeleteItem(T item) { }
        /// <summary>
        /// Ham load data – con class override
        /// </summary>
        protected virtual void LoadData() { }
        protected virtual void OnAddItem() { }
    }

}

















