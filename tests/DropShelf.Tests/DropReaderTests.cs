using System.Windows;
using DropShelf.DropHandling;

namespace DropShelf.Tests;

/// <summary>
/// Covers which drops the shelf is willing to accept.
/// </summary>
/// <remarks>
/// The self drag cases are a regression guard. Dragging an item off a shelf parked
/// at the right hand edge of the screen used to trip the edge catcher, which slid
/// in under the pointer and accepted the drop. That was invisible while it was a
/// no-op, and became a way to lose a file the moment the shelf could be set to
/// clear items after a successful drag.
/// </remarks>
public sealed class DropReaderTests
{
    private static DataObject WithText(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        return data;
    }

    [Fact]
    public void AcceptsAnOrdinaryDrop()
    {
        Assert.True(DropReader.CanRead(WithText("hello")));
    }

    [Fact]
    public void RefusesADragThatCameOutOfTheShelf()
    {
        var data = WithText("hello");
        data.SetData(DropReader.SelfDragFormat, true);

        Assert.False(DropReader.CanRead(data));
        Assert.True(DropReader.IsOwnDrag(data));
    }

    [Fact]
    public void ReadsNothingFromItsOwnDragEvenIfAsked()
    {
        var data = WithText("hello");
        data.SetData(DropReader.SelfDragFormat, true);

        var reader = new DropReader(new StagingArea());

        // Checked inside Read as well as in CanRead, so a caller that skips the
        // question still cannot re-add the items being dragged away.
        Assert.Empty(reader.Read(data));
    }

    [Fact]
    public void RefusesADropCarryingNothingItUnderstands()
    {
        Assert.False(DropReader.CanRead(new DataObject()));
    }
}
