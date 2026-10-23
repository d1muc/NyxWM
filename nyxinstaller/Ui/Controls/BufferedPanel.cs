using System.Windows.Forms;

namespace NyxInstaller.Ui.Controls;

/// <summary>
/// Double-buffered panel used as the page host.
/// </summary>
public sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
