using Microsoft.UI.Xaml;

namespace Atsumare;

public sealed partial class MainWindow : Window
{
    private readonly OverlayManager _overlayManager = new();

    public MainWindow()
    {
        this.InitializeComponent();
    }

    private void ShowOverlay_Click(object sender, RoutedEventArgs e)
    {
        _overlayManager.ToggleAllMonitors();
    }
}
