using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GBFileCopier.App.Views;

namespace GBFileCopier.App.Tests;

public sealed class MenuTests
{
    [AvaloniaFact]
    public void HelpMenu_OffersTheDonation()
    {
        var window = new MainWindow();

        var help = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Help").Menu!;

        Assert.Contains(help.Items.OfType<NativeMenuItem>(), i => i.Header == "Donate with PayPal…");
        Assert.Equal(new Uri("https://www.paypal.me/GiulioBanterla"), AboutWindow.DonationPage);
    }

    [AvaloniaFact]
    public void FileMenu_OffersTheChecksumVerification()
    {
        var window = new MainWindow();

        var file = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == "File").Menu!;

        Assert.Contains(file.Items.OfType<NativeMenuItem>(), i => i.Header == "Verify Checksum File…");
    }
}
