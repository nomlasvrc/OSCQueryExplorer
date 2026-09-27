using System.Windows;
using Wpf.Ui.Controls;

namespace OSCQueryExplorer;

public partial class CustomNodeWindow : FluentWindow
{
    public string NodeAddress => AddressBox.Text;
    public string TypeTag => TypeBox.Text;
    public CustomNodeWindow() => InitializeComponent();
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!NodeAddress.StartsWith('/') || string.IsNullOrWhiteSpace(TypeTag)) { System.Media.SystemSounds.Beep.Play(); return; }
        DialogResult = true;
    }
}
