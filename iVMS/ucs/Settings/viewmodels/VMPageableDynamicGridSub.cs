using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using V3SClient.libs;

namespace V3SClient.ucs.Settings.viewmodels
{
    public class VMPageableDynamicGridSub<T> : VMPageableDynamicGridMain<T>
    {

        public event Action<bool> CloseAction;
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public bool ShowSaveButton { get; set; } = true;
        public bool ShowCancelButton { get; set; } = true;
        public VMPageableDynamicGridSub()
        {
            SaveCommand = new RelayCommand(o => CloseAction?.Invoke(true));
            CancelCommand = new RelayCommand(o => CloseAction?.Invoke(false));
        }
    
    }
}

















