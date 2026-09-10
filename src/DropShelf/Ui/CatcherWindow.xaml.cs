using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using DropShelf.DropHandling;
using DropShelf.Model;

namespace DropShelf.Ui;

/// <summary>
/// The panel that slides in at the screen edge while the user is dragging.
/// </summary>
/// <remarks>
/// This is the answer to the problem the whole app exists for. Windows makes you
/// finish a drag in one motion, so if the destination is not already on screen you
/// are stuck. The catcher gives you somewhere to let go, right where your pointer
/// already is.
/// <para>
/// It only exists while a drag gesture is in progress. A permanent strip along the
/// edge would either swallow ordinary clicks or, if made click-through, be unable
/// to receive a drop at all.
/// </para>
/// </remarks>
public partial class CatcherWindow : Window
{
    private static readonly SolidColorBrush IdleBorder = MakeFrozen(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ActiveBorder = MakeFrozen(Color.FromArgb(0xFF, 0x58, 0xA6, 0xFF));
    private static readonly SolidColorBrush ActiveFill = MakeFrozen(Color.FromArgb(0x24, 0x58, 0xA6, 0xFF));

    private readonly Shelf _shelf;
    private readonly DropReader _dropReader;
    private bool _allowClose;

    /// <summary>Raised after a drop lands, so the shelf itself can be shown.</summary>
    public event EventHandler? Caught;

    public CatcherWindow(Shelf shelf, DropReader dropReader)
    {
        _shelf = shelf;
        _dropReader = dropReader;

        InitializeComponent();
    }

    private static SolidColorBrush MakeFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Shows the catcher against the right edge of the screen.
    /// </summary>
    public void ShowCatcher()
    {
        var workArea = SystemParameters.WorkArea;

        // Pushed slightly past the edge so the rounded left side is all that
        // shows, which reads as a drawer rather than a floating box.
        Left = workArea.Right - Width + 14;
        Top = workArea.Top + ((workArea.Height - Height) / 2);

        ResetHighlight();

        Show();

        // Another topmost window shown more recently would otherwise sit above
        // this one, and a catcher the pointer cannot reach is worse than none.
        Topmost = false;
        Topmost = true;
    }

    public void HideCatcher()
    {
        ResetHighlight();
        Hide();
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    private void ResetHighlight()
    {
        Target.BorderBrush = IdleBorder;
        Target.Background = Brushes.Transparent;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (DropReader.CanRead(e.Data))
        {
            Target.BorderBrush = ActiveBorder;
            Target.Background = ActiveFill;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ResetHighlight();
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // Copy, never Move. Move would authorise the source to delete the user's
        // original file, and the shelf is only recording a path.
        e.Effects = DropReader.CanRead(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ResetHighlight();

        try
        {
            _shelf.AddPaths(_dropReader.Read(e.Data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            // Reading a drop means parsing another application's data and writing
            // files. Losing one drop is recoverable; crashing is not.
            return;
        }
        finally
        {
            Hide();
        }

        Caught?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideCatcher();
            return;
        }

        base.OnClosing(e);
    }
}
