namespace GBFileCopier.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }
}
