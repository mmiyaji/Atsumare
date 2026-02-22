using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Atsumare
{
    internal sealed class KeepAliveWindow : Window
    {
        public KeepAliveWindow()
        {
            // 表示しないので中身は最小
            Content = new Grid();
        }
    }
}