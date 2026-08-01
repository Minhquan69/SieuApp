using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace V3SClient.libs
{



    public class ObservableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public new void Add(TKey key, TValue value)
        {
            base.Add(key, value);
            OnCollectionChanged(NotifyCollectionChangedAction.Add, new KeyValuePair<TKey, TValue>(key, value));
        }

        public new bool Remove(TKey key)
        {
            if (base.TryGetValue(key, out TValue value))
            {
                base.Remove(key);
                OnCollectionChanged(NotifyCollectionChangedAction.Remove, new KeyValuePair<TKey, TValue>(key, value));
                return true;
            }
            return false;
        }

        public new void Clear()
        {
            base.Clear();
            OnCollectionChanged(NotifyCollectionChangedAction.Reset);
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedAction action, KeyValuePair<TKey, TValue>? item = null)
        {
            CollectionChanged?.Invoke(this, item.HasValue
                ? new NotifyCollectionChangedEventArgs(action, item.Value)
                : new NotifyCollectionChangedEventArgs(action));

            OnPropertyChanged(nameof(Count));
            OnPropertyChanged("Item[]");
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}


















