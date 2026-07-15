using System;
using System.Windows;
using System.Windows.Controls;
namespace V3SClient.ucs { public partial class HeaderControl_v3 : UserControl { public event EventHandler SwitchClientRequested; public event EventHandler LogoutRequested; public HeaderControl_v3(){InitializeComponent();} private void AccountButton_Click(object s,RoutedEventArgs e){AccountPopup.IsOpen=true;} private void SwitchClient_Click(object s,RoutedEventArgs e){SwitchClientRequested?.Invoke(this,EventArgs.Empty);} private void Logout_Click(object s,RoutedEventArgs e){LogoutRequested?.Invoke(this,EventArgs.Empty);} } }
