using Avalonia.Controls;

namespace HyperFi;

public partial class StartupWindow : Window
{
    public StartupWindow() => InitializeComponent();

    public string Status
    {
        set => StatusTextBlock.Text = value;
    }
}
