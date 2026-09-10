using System.IO;
using DropShelf.DropHandling;

namespace DropShelf.Tests;

/// <summary>
/// Covers the naming rules for content written into the staging folder.
/// </summary>
/// <remarks>
/// These names arrive from whichever application the user dragged from, so they
/// are untrusted input in the ordinary sense. The tests that matter most are the
/// ones asserting that a hostile name cannot escape the folder it was given.
/// </remarks>
public sealed class StagingAreaTests : IDisposable
{
    private readonly string _folder;

    public StagingAreaTests()
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

    [Theory]
    [InlineData(@"..\..\evil.exe")]
    [InlineData(@"..\evil.exe")]
    [InlineData(@"C:\Windows\System32\evil.exe")]
    [InlineData(@"subfolder\evil.exe")]
    [InlineData("/etc/passwd")]
    public void KeepsHostileNamesInsideTheFolder(string hostileName)
    {
        var result = StagingArea.SafePathFor(_folder, hostileName);

        Assert.Equal(_folder, Path.GetDirectoryName(result));
    }

    [Fact]
    public void StripsCharactersWindowsWillNotAccept()
    {
        var result = StagingArea.SafePathFor(_folder, "in<va>lid:na|me?.txt");
        var name = Path.GetFileName(result);

        Assert.DoesNotContain('<', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('|', name);
        Assert.EndsWith(".txt", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    public void FallsBackToAReadableNameWhenNothingUsableIsSupplied(string name)
    {
        var result = StagingArea.SafePathFor(_folder, name);

        Assert.Equal("Dropped item", Path.GetFileName(result));
    }

    [Theory]
    [InlineData("CON.txt")]
    [InlineData("nul.log")]
    [InlineData("Lpt1.dat")]
    public void AvoidsNamesWindowsReservesForDevices(string name)
    {
        var result = StagingArea.SafePathFor(_folder, name);

        // The extension is no defence. Windows rejects these whatever follows.
        Assert.NotEqual(name, Path.GetFileName(result));
        Assert.StartsWith("_", Path.GetFileName(result), StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotOverwriteAFileAlreadyThere()
    {
        var first = StagingArea.SafePathFor(_folder, "report.pdf");
        File.WriteAllText(first, "original");

        var second = StagingArea.SafePathFor(_folder, "report.pdf");

        Assert.NotEqual(first, second);
        Assert.Equal("report (2).pdf", Path.GetFileName(second));
        Assert.Equal("original", File.ReadAllText(first));
    }

    [Fact]
    public void GivesEachDropItsOwnFolder()
    {
        var staging = new StagingArea();

        var first = staging.CreateBatchFolder();
        var second = staging.CreateBatchFolder();

        try
        {
            Assert.NotEqual(first, second);
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }
}
