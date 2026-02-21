using Dali.UI.ViewModels;
using System.Windows.Controls;

namespace Dali.UI
{
    public partial class DeviceManagerView : UserControl
    {
        public DeviceManagerView()
        {
            InitializeComponent();
        }

        private void EditBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // When a textbox is typed into, notify the ViewModel that edits have occurred
            if (DataContext is DeviceManagerViewModel vm)
            {
                vm.NotifyEditChanged();
            }
        }
    }
}
