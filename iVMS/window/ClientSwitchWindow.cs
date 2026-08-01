using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using V3SClient.libs;

namespace V3SClient.window
{
    public sealed class ClientSwitchWindow : Window
    {
        public ApiManager.ClientProfile SelectedProfile { get; private set; }
        private readonly ListBox _list;
        public ClientSwitchWindow(IList<ApiManager.ClientProfile> profiles)
        {
            Title = "Đổi client"; Width = 360; Height = 300; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Chọn client", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,10) });
            _list = new ListBox { ItemsSource = profiles, DisplayMemberPath = "Name", MinHeight = 150 };
            _list.SelectedItem = profiles?.FirstOrDefault(p => p.Id == GlobalUserInfo.Instance.ActiveClientId) ?? profiles?.FirstOrDefault();
            panel.Children.Add(_list);
            var ok = new Button { Content = "Xác nhận", Width = 100, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            ok.Click += (s,e) => { SelectedProfile = _list.SelectedItem as ApiManager.ClientProfile; DialogResult = SelectedProfile != null; };
            panel.Children.Add(ok); Content = panel;
        }
    }
}
