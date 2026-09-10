using System.IO;
using DropShelf.Model;

namespace DropShelf.Tests;

/// <summary>
/// Covers what the shelf accepts, rejects, and does with repeats.
/// </summary>
/// <remarks>
/// The shelf is constructed without a thumbnail loader here. That argument is
/// optional precisely so this can happen: loading thumbnails needs a UI thread and
/// a live shell, and none of the behaviour under test depends on it.
/// </remarks>
public sealed class ShelfTests : IDisposable
{
    private readonly string _folder;

    public ShelfTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "DropShelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "contents");
        return path;
    }

    [Fact]
    public void HoldsAFileThatExists()
    {
        var shelf = new Shelf();
        var path = CreateFile("report.pdf");

        shelf.AddPaths([path]);

        var item = Assert.Single(shelf.Items);
        Assert.Equal(path, item.FullPath);
        Assert.Equal("report.pdf", item.DisplayName);
        Assert.False(item.IsDirectory);
    }

    [Fact]
    public void HoldsAFolder()
    {
        var shelf = new Shelf();
        var subfolder = Path.Combine(_folder, "notes");
        Directory.CreateDirectory(subfolder);

        shelf.AddPaths([subfolder]);

        var item = Assert.Single(shelf.Items);
        Assert.True(item.IsDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IgnoresBlankPaths(string path)
    {
        var shelf = new Shelf();

        shelf.AddPaths([path]);

        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void IgnoresPathsThatPointAtNothing()
    {
        var shelf = new Shelf();

        shelf.AddPaths([Path.Combine(_folder, "was-never-here.txt")]);

        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void IgnoresStringsThatAreNotPathsAtAll()
    {
        var shelf = new Shelf();

        // A malformed drop can carry anything. This must not throw.
        shelf.AddPaths(["\0not a path", "|||", new string('x', 500)]);

        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void KeepsTheNewestItemNearestToHand()
    {
        var shelf = new Shelf();
        var first = CreateFile("first.txt");
        var second = CreateFile("second.txt");

        shelf.AddPaths([first]);
        shelf.AddPaths([second]);

        Assert.Equal(second, shelf.Items[0].FullPath);
        Assert.Equal(first, shelf.Items[1].FullPath);
    }

    [Fact]
    public void PromotesARepeatInsteadOfDuplicatingIt()
    {
        var shelf = new Shelf();
        var first = CreateFile("first.txt");
        var second = CreateFile("second.txt");

        // Each path in a batch is inserted at the front in turn, so a two file
        // drop ends up with the last one nearest to hand.
        shelf.AddPaths([first, second]);
        Assert.Equal(second, shelf.Items[0].FullPath);

        shelf.AddPaths([first]);

        Assert.Equal(2, shelf.Items.Count);
        Assert.Equal(first, shelf.Items[0].FullPath);
    }

    [Fact]
    public void TreatsDifferentCasingAsTheSameFile()
    {
        var shelf = new Shelf();
        var path = CreateFile("Report.PDF");

        shelf.AddPaths([path]);
        shelf.AddPaths([path.ToUpperInvariant()]);
        shelf.AddPaths([path.ToLowerInvariant()]);

        // Windows file names are case insensitive, and different applications
        // hand over different casings for the same file.
        Assert.Single(shelf.Items);
    }

    [Fact]
    public void KeepsTheOriginalAddedTimeWhenPromoting()
    {
        var shelf = new Shelf();
        var first = CreateFile("first.txt");
        var second = CreateFile("second.txt");

        shelf.AddPaths([first]);
        var addedAt = shelf.Items[0].AddedAt;

        shelf.AddPaths([second, first]);

        var promoted = shelf.Items.Single(i => i.FullPath == first);
        Assert.Equal(addedAt, promoted.AddedAt);
    }

    [Fact]
    public void RemovesASingleItemAndClearsTheRest()
    {
        var shelf = new Shelf();
        shelf.AddPaths([CreateFile("a.txt"), CreateFile("b.txt"), CreateFile("c.txt")]);

        shelf.Remove(shelf.Items[1]);
        Assert.Equal(2, shelf.Items.Count);

        shelf.Clear();
        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void ReportsWhenAFileHasGoneAway()
    {
        var shelf = new Shelf();
        var path = CreateFile("temporary.txt");
        shelf.AddPaths([path]);

        var item = shelf.Items[0];
        Assert.True(item.StillExists());

        File.Delete(path);

        Assert.False(item.StillExists());
    }
}
