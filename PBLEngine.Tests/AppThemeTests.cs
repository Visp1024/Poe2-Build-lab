using PBLApp.Core;
using PBLApp.ViewModels;
using Xunit;

namespace PBLEngine.Tests;

public class AppThemeTests
{
    [Theory]
    [InlineData("Dark", AppThemeMode.Dark)]
    [InlineData("Light", AppThemeMode.Light)]
    [InlineData("System", AppThemeMode.System)]
    [InlineData(null, AppThemeMode.Dark)]
    [InlineData("", AppThemeMode.Dark)]
    [InlineData("garbage", AppThemeMode.Dark)]
    [InlineData("light", AppThemeMode.Dark)]   // case-sensitive by design (we write nameof)
    public void Parse_MapsStoredValue_WithDarkFallback(string? stored, AppThemeMode expected)
        => Assert.Equal(expected, AppThemeModes.Parse(stored));

    [Theory]
    [InlineData(AppThemeMode.System)]
    [InlineData(AppThemeMode.Dark)]
    [InlineData(AppThemeMode.Light)]
    public void Parse_RoundTripsPersistedFormat(AppThemeMode mode)
        => Assert.Equal(mode, AppThemeModes.Parse(mode.ToString()));

    [Theory]
    [InlineData(AppThemeMode.System, 0)]
    [InlineData(AppThemeMode.Dark, 1)]
    [InlineData(AppThemeMode.Light, 2)]
    public void ViewModel_IndexAndModeMappingRoundTrips(AppThemeMode mode, int index)
    {
        Assert.Equal(index, AppSettingsViewModel.IndexOf(mode));
        Assert.Equal(mode, AppSettingsViewModel.ModeAt(index));
    }

    [Fact]
    public void ViewModel_InitialIndexReflectsCurrentMode_WithoutApplying()
    {
        var switcher = new FakeSwitcher { Current = AppThemeMode.Dark };
        var vm = new AppSettingsViewModel(switcher);

        Assert.Equal(1, vm.SelectedThemeIndex);
        Assert.Empty(switcher.Applied);
    }

    [Fact]
    public void ViewModel_SelectingIndexAppliesMatchingMode()
    {
        var switcher = new FakeSwitcher { Current = AppThemeMode.System };
        var vm = new AppSettingsViewModel(switcher);

        vm.SelectedThemeIndex = 2;
        vm.SelectedThemeIndex = 1;
        vm.SelectedThemeIndex = 0;

        Assert.Equal([AppThemeMode.Light, AppThemeMode.Dark, AppThemeMode.System], switcher.Applied);
    }

    private sealed class FakeSwitcher : IThemeSwitcher
    {
        public AppThemeMode Current { get; set; }
        public List<AppThemeMode> Applied { get; } = [];
        public void Apply(AppThemeMode mode) { Current = mode; Applied.Add(mode); }
    }
}
