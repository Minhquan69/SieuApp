using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace V3SClient.libs
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object, Task> _executeAsync;
        private readonly Predicate<object> _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => !_isExecuting && (_canExecute == null || _canExecute(parameter));

        public async void Execute(object parameter)
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _executeAsync(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler CanExecuteChanged;

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}















