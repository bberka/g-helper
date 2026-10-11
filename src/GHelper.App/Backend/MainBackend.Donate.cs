using GHelper.App.Theme;
using GHelper.Platform;
using GHelper.Properties;

namespace GHelper.App.Backend;

// Port of app/Helpers/DonateControl.cs: a badge on the donate button that grows with the number of starts.
public sealed partial class MainBackend
{
    private int _donateBadgeTarget;

    /// <summary>Badge for the given start count / last click (0 = none).</summary>
    public static int GetDonateBadge(int startCount, int click)
    {
        if (startCount < ((click < 20) ? 20 : click + 50)) return 0;
        return Math.Clamp((startCount - click) / 50, 1, 5);
    }

    private void InitDonate()
    {
        if (AppConfig.Is("donate_dismissed")) return;

        int click = AppConfig.Get("donate_click");
        int startCount = AppConfig.Get("start_count");
        int badge = GetDonateBadge(startCount, click);
        if (badge <= 0) return;

        _vm.Donate.BorderColor = AppColors.Turbo;
        _vm.Donate.Badge = GetDonateBadge(startCount - 1, click);

        // the new badge value "pops" shortly after the window is shown for the first time
        if (_vm.Donate.Badge < badge) _donateBadgeTarget = badge;
    }

    private void PopDonateBadge()
    {
        int target = _donateBadgeTarget;
        if (target <= 0) return;
        _donateBadgeTarget = 0;

        Task.Delay(1500).ContinueWith(_ => OnUi(() =>
        {
            if (!AppConfig.Is("donate_dismissed")) _vm.Donate.Badge = target;
        }));
    }

    private void SetThankYou()
    {
        _vm.Donate.Badge = 0;
        _vm.Donate.Text = Strings.ThankYou;
    }

    private void DonateClick()
    {
        AppConfig.Set("donate_click", AppConfig.Get("start_count"));
        SetThankYou();
        try { PlatformServices.Current.Process.OpenUrl("https://g-helper.com/support"); }
        catch (Exception ex) { Logger.WriteLine("Donate link: " + ex.Message); }
    }

    private void DonateAlreadyDonated()
    {
        AppConfig.Set("donate_dismissed", 1);
        SetThankYou();
    }

    private void DonateNotInterested()
    {
        AppConfig.Set("donate_dismissed", 1);
        _vm.Donate.Badge = 0;
    }
}
