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
        DropReader.MarkAsOwnDrag(data);

        Assert.False(DropReader.CanRead(data));
        Assert.True(DropReader.IsOwnDrag(data));
    }

    [Fact]
    public void MarksItsOwnDragWithAValueThatCanLeaveTheProcess()
    {
        var data = WithText("hello");
        DropReader.MarkAsOwnDrag(data);

        // A data object carries arbitrary objects between processes by
        // serialising them, which .NET 9 refuses to do by default. A marker that
        // is anything other than a string produces a drag that works in process
        // and can fail the moment the application on the other side asks for the
        // value. Reading it back as a string is the check that it stayed simple.
        Assert.IsType<string>(data.GetData(DropReader.SelfDragFormat));
    }

    [Fact]
    public void ReadsNothingFromItsOwnDragEvenIfAsked()
    {
        var data = WithText("hello");
        DropReader.MarkAsOwnDrag(data);

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
