using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using V3SClient.ucs;

namespace V3SClient.libs
{
    public static class ToastManager
    {
        private static readonly List<ToastMessageWindow> ActiveToasts = new List<ToastMessageWindow>();

        public static void ShowToast(string title, string message, ToastType type)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new ToastMessageWindow(title, message, type);
                PositionToast(toast);
                ActiveToasts.Add(toast);

                toast.Closed += (s, e) =>
                {
                    ActiveToasts.Remove(toast);
                    RepositionToasts();
                };

                toast.Show();
            });
        }

        private static void PositionToast(ToastMessageWindow toast)
        {
            double screenRight = SystemParameters.WorkArea.Right;
            double screenBottom = SystemParameters.WorkArea.Bottom;

            const int margin = 10;
            double topOffset = screenBottom - toast.Height - margin;

            foreach (var t in ActiveToasts)
            {
                topOffset -= (t.Height + margin);
            }

            toast.Left = screenRight - toast.Width - margin;
            toast.Top = topOffset;
        }

        private static void RepositionToasts()
        {
            double screenRight = SystemParameters.WorkArea.Right;
            double screenBottom = SystemParameters.WorkArea.Bottom;

            const int margin = 10;
            double topOffset = screenBottom - margin;

            foreach (var toast in ActiveToasts)
            {
                topOffset -= (toast.Height + margin);
                toast.Left = screenRight - toast.Width - margin;
                toast.Top = topOffset;
            }
        }
    }
}
















