using GBFileCopier.App.Services;

namespace GBFileCopier.App.Tests;

public sealed class SingleInstanceTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";

    private static string Rooted(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void Arguments_AddWithSeveralItems()
    {
        var request = ExternalRequest.FromArguments(["--add", Rooted("a.txt"), Rooted("Photos")], Root);

        Assert.NotNull(request);
        Assert.Equal([Rooted("a.txt"), Rooted("Photos")], request.Sources);
        Assert.False(request.Move);
    }

    [Fact]
    public void Arguments_PlainPathsAreAdded_RelativeOnesFromTheWorkingDirectory()
    {
        var request = ExternalRequest.FromArguments(["notes.txt"], Rooted("Users", "me"));

        Assert.Equal([Rooted("Users", "me", "notes.txt")], request?.Sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arguments_PasteIntoFolder(bool move)
    {
        string[] arguments = move ? ["--paste", Rooted("Backup"), "--move"] : ["--paste", Rooted("Backup")];

        var request = ExternalRequest.FromArguments(arguments, Root);

        Assert.NotNull(request);
        Assert.True(request.PasteFromClipboard);
        Assert.Equal(Rooted("Backup"), request.Destination);
        Assert.Equal(move, request.Move);
    }

    [Fact]
    public void Arguments_DriveRootWithStrayQuote()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows quoting");
        // "%V" on a drive root gives D:\" : the backslash escapes the closing quote
        var request = ExternalRequest.FromArguments(["--paste", "D:\\\""], @"C:\");

        Assert.Equal(@"D:\", request?.Destination);
    }

    [Fact]
    public void Arguments_Nothing()
    {
        Assert.Null(ExternalRequest.FromArguments([], Root));
        Assert.Null(ExternalRequest.FromArguments(["--add"], Root));
    }

    [Fact]
    public void Arguments_Link()
    {
        var request = ExternalRequest.FromArguments([$"gbfilecopier://add?source={Uri.EscapeDataString(Rooted("a.txt"))}"], Root);

        Assert.Equal([Rooted("a.txt")], request?.Sources);
    }

    [Fact]
    public async Task SecondStart_ForwardsItsArgumentsToTheFirst()
    {
        var name = "GBFileCopier-Tests-" + Guid.NewGuid().ToString("N")[..12];
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(name)); // the second start finds the first one

        var received = new TaskCompletionSource<(IReadOnlyList<string> Arguments, string Directory)>();
        first.Listen((arguments, directory) => received.TrySetResult((arguments, directory)));

        var forwarded = await Task.Run(() => SingleInstance.Forward(["--add", "è.txt"], Rooted("work"), TimeSpan.FromSeconds(10), name));

        Assert.True(forwarded);
        var (arguments, directory) = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["--add", "è.txt"], arguments);
        Assert.Equal(Rooted("work"), directory);
    }

    [Fact]
    public void Forward_WithoutARunningInstance_GivesUp()
    {
        var name = "GBFileCopier-Tests-" + Guid.NewGuid().ToString("N")[..12];

        Assert.False(SingleInstance.Forward(["x"], Root, TimeSpan.FromMilliseconds(300), name));
    }
}
