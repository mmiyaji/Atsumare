using Microsoft.UI.Xaml.Media.Imaging;

namespace Atsumare;

public sealed class AppItemVm
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required int Count { get; init; }
    public BitmapSource? Icon { get; init; }
}
