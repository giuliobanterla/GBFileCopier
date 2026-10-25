using Avalonia.Headless.XUnit;
using GBFileCopier.App.Services;
using GBFileCopier.App.ViewModels;
using GBFileCopier.Platform.Clipboard;
using GBFileCopier.Tests;

namespace GBFileCopier.App.Tests;

public sealed class ExternalRequestTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Root => OperatingSystem.IsWindows() ? @"C:\" : "/";

    private static string Escaped(string path) => Uri.EscapeDataString(Path.Combine(Root, path));

    [Fact]
    public void Add_ReadsTheSourcesAndMove()
    {
        var request = ExternalRequest.Parse(new Uri($"gbfilecopier://add?source={Escaped("Photos")}&source={Escaped("Documents/nome con è.txt")}&move=1"));

        Assert.NotNull(request);
        Assert.Equal([Path.Combine(Root, "Photos"), Path.Combine(Root, "Documents/nome con è.txt")], request.Sources);
        Assert.True(request.Move);
        Assert.False(request.PasteFromClipboard);
    }

    [Fact]
    public void Paste_ReadsTheDestination()
    {
        var request = ExternalRequest.Parse(new Uri($"gbfilecopier://paste?destination={Escaped("Backup")}&move=0"));

        Assert.NotNull(request);
        Assert.Equal(Path.Combine(Root, "Backup"), request.Destination);
        Assert.True(request.PasteFromClipboard);
        Assert.False(request.Move);
    }

    [Theory]
    [InlineData("https://example.com/add?source=/etc")] // another scheme
    [InlineData("gbfilecopier://delete?source=/etc")] // unknown action
    [InlineData("gbfilecopier://add")] // nothing to add
    [InlineData("gbfilecopier://add?source=relative/path&source=../x")] // only absolute paths are accepted
    public void InvalidRequests_AreIgnored(string uri)
    {
        Assert.Null(ExternalRequest.Parse(new Uri(uri)));
    }

    [AvaloniaFact]
    public void Apply_FillsTheWindowWithoutStartingTheCopy()
    {
        var file = _temp.CreateFile("a.txt");
        var viewModel = new MainViewModel(new FakeUiServices(), new FakeSettingsStore());

        viewModel.Apply(new ExternalRequest([file], _temp.Combine("dst"), PasteFromClipboard: false, Move: true));

        Assert.Single(viewModel.Sources);
        Assert.Equal(_temp.Combine("dst"), viewModel.Destination);
        Assert.True(viewModel.PreferMove);
        Assert.Empty(viewModel.Jobs); // nothing copied: the user still has to press Move
        Assert.False(Directory.Exists(_temp.Combine("dst")));
    }

    [AvaloniaFact]
    public void Apply_Paste_TakesTheClipboardFiles()
    {
        var ui = new FakeUiServices { Clipboard = new ClipboardFiles([_temp.CreateFile("a.txt")], IsCut: false) };
        var viewModel = new MainViewModel(ui, new FakeSettingsStore());

        viewModel.Apply(new ExternalRequest([], _temp.Combine("dst"), PasteFromClipboard: true, Move: true));

        Assert.Single(viewModel.Sources);
        Assert.True(viewModel.PreferMove); // "Move Here" in the Finder
        Assert.Equal(_temp.Combine("dst"), viewModel.Destination);
    }
}
