using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using V3SClient.libs;

namespace V3SClient.viewModels
{
    internal class VMAIEvent:INotifyPropertyChanged
    {
        private ObservableCollection<AIEvent> _pagedEvents;
        private int _currentPage = 1;
        private int _itemsPerPage = 20;
        private string _pageInput = "1";
        private int _totalPages = 1;

        public ObservableCollection<AIEvent> AllEvents { get; set; } =new ObservableCollection<AIEvent>();
        public ObservableCollection<AIEvent> PagedEvents
        {
            get => _pagedEvents;
            set { _pagedEvents = value; OnPropertyChanged(); }
        }

        public ICommand NextPageCommand { get; set; }
        public ICommand PreviousPageCommand { get; set; }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value < 1) value = 1;
                if (value > TotalPages) value = TotalPages;

                _currentPage = value;
                PageInput = value.ToString(); // Đồng bộ với PageInput
                OnPropertyChanged();
                UpdatePagedEvents();
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
        private string _filterType = "both";
        public string FilterType
        {
            get => _filterType;
            set
            {
                _filterType = value;
                OnPropertyChanged();
                FilterAndUpdateEvents();
            }
        }
        private string _detectObjectIdPattern = "";
        public string DetectObjectIdPattern
        {
            get => _detectObjectIdPattern;
            set
            {
                _detectObjectIdPattern = value;
                OnPropertyChanged();
                FilterAndUpdateEvents();
            }
        }
        private void FilterAndUpdateEvents()
        {
            try
            {
                IEnumerable<AIEvent> filtered;

                if (FilterType.ToLower() == "face")
                    filtered = AllEvents.Where(e => e.Type == "face");
                else if (FilterType.ToLower() == "plate")
                    filtered = AllEvents.Where(e => e.Type == "plate");
                else
                    filtered = AllEvents;


                // Bước 2: Lọc theo Detect_Object_id pattern
                if (!string.IsNullOrWhiteSpace(DetectObjectIdPattern))
                {
                    var patterns = DetectObjectIdPattern
                                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(p => p.Trim());

                    filtered = filtered.Where(e =>
                        !string.IsNullOrEmpty(e.Detect_Object_id) &&
                        MatchPatternList(e.Detect_Object_id, patterns));
                }

                int count = filtered.Count();
                TotalPages = (int)Math.Ceiling(count / (double)_itemsPerPage);
                if (TotalPages == 0) TotalPages = 1;

                if (CurrentPage > TotalPages) CurrentPage = TotalPages;
                if (CurrentPage < 1) CurrentPage = 1;

                PagedEvents = new ObservableCollection<AIEvent>(
                    filtered.Skip((CurrentPage - 1) * _itemsPerPage).Take(_itemsPerPage));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error filtering events: " + ex.Message);
            }
        }
        private bool MatchPatternList(string target, IEnumerable<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;

                if (pattern.StartsWith("*") && pattern.EndsWith("*"))
                {
                    string keyword = pattern.Trim('*');
                    if (target.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                else if (pattern.StartsWith("*"))
                {
                    string keyword = pattern.TrimStart('*');
                    if (target.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (pattern.EndsWith("*"))
                {
                    string keyword = pattern.TrimEnd('*');
                    if (target.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (target.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
        public VMAIEvent()
        {
            NextPageCommand = new RelayCommand(_ => CurrentPage++, _ => CanGoNext());
            PreviousPageCommand = new RelayCommand(_ => CurrentPage--, _ => CurrentPage > 1);

        }

        public async void LoadEvents(List<string>CamIds,DateTime searchStart,DateTime searchEnd)
        {
            try
            {
                AllEvents.Clear();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                foreach (string camid in CamIds ?? new List<string>())
                {
                    //var eventHistory =await DatabaseManagerAsync.Instance.GetAIEventsByCamIdAndDate(camid, searchStart, searchEnd, cts.Token);
                    //if (eventHistory != null)
                    //{
                    //    foreach (var ev in eventHistory)
                    //    {
                    //        AllEvents.Add(ev);
                    //    }
                    //}
                }

                CurrentPage = 1; // Reset về trang đầu tiên
                FilterAndUpdateEvents(); // Gọi lọc thay vì UpdatePagedEvents
  
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading events: " + ex.Message);
            }
            
        }

        private void UpdatePagedEvents()
        {
            FilterAndUpdateEvents();
            //PagedEvents = new ObservableCollection<AIEvent>(AllEvents.Skip((CurrentPage - 1) * _itemsPerPage).Take(_itemsPerPage));
        }

        private bool CanGoNext() => (CurrentPage * _itemsPerPage) < AllEvents.Count;

        public void GoToPage(int page)
        {
            if (page < 1) page = 1;
            else if (page > TotalPages) page = TotalPages;

            CurrentPage = page;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}
