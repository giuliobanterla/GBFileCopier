using GBFileCopier.Core.Copying;
using GBFileCopier.Core.Resources;
using GBFileCopier.Tests;

namespace GBFileCopier.Core.Tests;

public sealed class MessagesTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Italian_IsComplete() =>
        ResourceAssertions.AssertComplete(typeof(Messages), Messages.ResourceManager, "it");

    [Fact]
    public async Task EngineErrors_FollowTheUiLanguage()
    {
        using var italian = EnglishCulture.Use("it");
        var job = new CopyJob(new CopyRequest { Sources = [_temp.Combine("missing.txt")], Destination = _temp.Combine("dst") });

        var result = await job.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("L'origine non esiste", Assert.Single(result.Files).Error);
    }
}
