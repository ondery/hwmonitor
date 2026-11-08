// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Resources;

namespace HWMonitor.Core.Text;

public static class UiText
{
    private static readonly ResourceManager Resources = new("HWMonitor.Core.Resources.Strings", typeof(UiText).Assembly);

    public static string AppName => Get(nameof(AppName));

    public static string AppSubtitle => Get(nameof(AppSubtitle));

    public static string PageTitle => Get(nameof(PageTitle));

    public static string PageName => Get(nameof(PageName));

    public static string SearchPlaceholder => Get(nameof(SearchPlaceholder));

    public static string SectionCooling => Get(nameof(SectionCooling));

    public static string SectionProcessors => Get(nameof(SectionProcessors));

    public static string SectionGraphics => Get(nameof(SectionGraphics));

    public static string SectionStatus => Get(nameof(SectionStatus));

    public static string EmptyTitle => Get(nameof(EmptyTitle));

    public static string EmptySubtitle => Get(nameof(EmptySubtitle));

    public static string ReadingFormat => Get(nameof(ReadingFormat));

    public static string Unavailable => Get(nameof(Unavailable));

    public static string RolePackage => Get(nameof(RolePackage));

    public static string RoleHottestCore => Get(nameof(RoleHottestCore));

    public static string RoleAdapter => Get(nameof(RoleAdapter));

    public static string RoleCpuGraphics => Get(nameof(RoleCpuGraphics));

    public static string RoleSharedPackage => Get(nameof(RoleSharedPackage));

    public static string TagBlue => Get(nameof(TagBlue));

    public static string TagYellow => Get(nameof(TagYellow));

    public static string TagOrange => Get(nameof(TagOrange));

    public static string TagRed => Get(nameof(TagRed));

    public static string StatusTempsTitle => Get(nameof(StatusTempsTitle));

    public static string StatusTempsSubtitle => Get(nameof(StatusTempsSubtitle));

    public static string StatusServiceTitle => Get(nameof(StatusServiceTitle));

    public static string StatusServiceSubtitle => Get(nameof(StatusServiceSubtitle));

    public static string ReadingSensors => Get(nameof(ReadingSensors));

    public static string CoolBadgeAuto => Get(nameof(CoolBadgeAuto));

    public static string CoolBadgeOn => Get(nameof(CoolBadgeOn));

    public static string CoolBadgeOff => Get(nameof(CoolBadgeOff));

    public static string CoolDockAuto => Get(nameof(CoolDockAuto));

    public static string CoolDockEngaged => Get(nameof(CoolDockEngaged));

    public static string CoolAutoIdleSubtitle => Get(nameof(CoolAutoIdleSubtitle));

    public static string SettingCoolModeShort => Get(nameof(SettingCoolModeShort));

    public static string CoolOn => Get(nameof(CoolOn));

    public static string CoolOff => Get(nameof(CoolOff));

    public static string CoolDockOn => Get(nameof(CoolDockOn));

    public static string CoolDockOff => Get(nameof(CoolDockOff));

    public static string CoolDockLabel => Get(nameof(CoolDockLabel));

    public static string CoolTitle => Get(nameof(CoolTitle));

    public static string CoolOnSubtitle => Get(nameof(CoolOnSubtitle));

    public static string CoolOffSubtitle => Get(nameof(CoolOffSubtitle));

    public static string CoolAutoSubtitle => Get(nameof(CoolAutoSubtitle));

    public static string CoolFailed => Get(nameof(CoolFailed));

    public static string CoolReadFailed => Get(nameof(CoolReadFailed));

    public static string SettingRefreshLabel => Get(nameof(SettingRefreshLabel));

    public static string SettingRefreshDescription => Get(nameof(SettingRefreshDescription));

    public static string SettingCompactLabel => Get(nameof(SettingCompactLabel));

    public static string SettingCompactDescription => Get(nameof(SettingCompactDescription));

    public static string SettingBlueBelowLabel => Get(nameof(SettingBlueBelowLabel));

    public static string SettingYellowLabel => Get(nameof(SettingYellowLabel));

    public static string SettingOrangeLabel => Get(nameof(SettingOrangeLabel));

    public static string SettingRedLabel => Get(nameof(SettingRedLabel));

    public static string SettingThresholdDescription => Get(nameof(SettingThresholdDescription));

    public static string SettingBlueColorLabel => Get(nameof(SettingBlueColorLabel));

    public static string SettingYellowColorLabel => Get(nameof(SettingYellowColorLabel));

    public static string SettingOrangeColorLabel => Get(nameof(SettingOrangeColorLabel));

    public static string SettingRedColorLabel => Get(nameof(SettingRedColorLabel));

    public static string SettingColorDescription => Get(nameof(SettingColorDescription));

    public static string SettingCoolMaxLabel => Get(nameof(SettingCoolMaxLabel));

    public static string SettingCoolMaxDescription => Get(nameof(SettingCoolMaxDescription));

    public static string SettingCoolBoostLabel => Get(nameof(SettingCoolBoostLabel));

    public static string SettingCoolBoostDescription => Get(nameof(SettingCoolBoostDescription));

    public static string SettingBoostChoiceLabel => Get(nameof(SettingBoostChoiceLabel));

    public static string SettingBoostChoiceDescription => Get(nameof(SettingBoostChoiceDescription));

    public static string SettingBoostDisabled => Get(nameof(SettingBoostDisabled));

    public static string SettingBoostEfficient => Get(nameof(SettingBoostEfficient));

    public static string SettingAutoLabel => Get(nameof(SettingAutoLabel));

    public static string SettingAutoDescription => Get(nameof(SettingAutoDescription));

    public static string SettingAutoOnLabel => Get(nameof(SettingAutoOnLabel));

    public static string SettingAutoOnSecondsLabel => Get(nameof(SettingAutoOnSecondsLabel));

    public static string SettingAutoOffLabel => Get(nameof(SettingAutoOffLabel));

    public static string SettingAutoOffSecondsLabel => Get(nameof(SettingAutoOffSecondsLabel));

    public static string SettingDeviceVisibilityDescription => Get(nameof(SettingDeviceVisibilityDescription));

    public static string VisibilityDefault => Get(nameof(VisibilityDefault));

    public static string VisibilityShow => Get(nameof(VisibilityShow));

    public static string VisibilityHide => Get(nameof(VisibilityHide));

    public static string RoleCore(int number) => string.Format(CultureInfo.CurrentCulture, Get("RoleCoreFormat"), number);

    public static string DeviceShortLabel(string name) => string.Format(CultureInfo.CurrentCulture, Get("SettingDeviceShortLabel"), name);

    public static string DeviceShortDescription(string id) => string.Format(CultureInfo.CurrentCulture, Get("SettingDeviceShortDescription"), id);

    public static string DeviceFullLabel(string name) => string.Format(CultureInfo.CurrentCulture, Get("SettingDeviceFullLabel"), name);

    public static string DeviceFullDescription => Get("SettingDeviceFullDescription");

    public static string DeviceListLabel(string name) => string.Format(CultureInfo.CurrentCulture, Get("SettingDeviceListLabel"), name);

    public static string DeviceDockLabel(string name) => string.Format(CultureInfo.CurrentCulture, Get("SettingDeviceDockLabel"), name);

    public static string SettingsPageName => Get(nameof(SettingsPageName));

    public static string SettingsPageTitle => Get(nameof(SettingsPageTitle));

    public static string SettingSave => Get(nameof(SettingSave));

    public static string SettingGroupGeneral => Get(nameof(SettingGroupGeneral));

    public static string SettingGroupGeneralHelp => Get(nameof(SettingGroupGeneralHelp));

    public static string SettingGroupColors => Get(nameof(SettingGroupColors));

    public static string SettingGroupColorsHelp => Get(nameof(SettingGroupColorsHelp));

    public static string SettingGroupCool => Get(nameof(SettingGroupCool));

    public static string SettingGroupCoolHelp => Get(nameof(SettingGroupCoolHelp));

    public static string SettingGroupDevices => Get(nameof(SettingGroupDevices));

    public static string SettingGroupDevicesHelp => Get(nameof(SettingGroupDevicesHelp));

    public static string SettingGroupDevicesEmpty => Get(nameof(SettingGroupDevicesEmpty));

    public static string SettingDockColorLabel => Get(nameof(SettingDockColorLabel));

    public static string SettingDockColorDescription => Get(nameof(SettingDockColorDescription));

    public static string SettingCustomHexDescription => Get(nameof(SettingCustomHexDescription));

    public static string SettingCustomHexPlaceholder => Get(nameof(SettingCustomHexPlaceholder));

    public static string SettingAutoOnDescription => Get(nameof(SettingAutoOnDescription));

    public static string SettingAutoOnSecondsDescription => Get(nameof(SettingAutoOnSecondsDescription));

    public static string SettingAutoOffDescription => Get(nameof(SettingAutoOffDescription));

    public static string SettingAutoOffSecondsDescription => Get(nameof(SettingAutoOffSecondsDescription));

    public static string SettingDeviceListDescription => Get(nameof(SettingDeviceListDescription));

    public static string SettingDeviceDockDescription => Get(nameof(SettingDeviceDockDescription));

    public static string ColorPreset(string id) => Get("ColorPreset_" + id);

    public static string CustomHexLabel(string band) => string.Format(CultureInfo.CurrentCulture, Get("SettingCustomHexLabel"), band);

    public static string DeviceHardwareId(string id) => string.Format(CultureInfo.CurrentCulture, Get("SettingDeviceHardwareId"), id);

    public static string SettingScopeListDock => Get(nameof(SettingScopeListDock));

    public static string SettingScopeList => Get(nameof(SettingScopeList));

    public static string SettingScopeDock => Get(nameof(SettingScopeDock));

    public static string SettingScopeCool => Get(nameof(SettingScopeCool));

    public static string SettingScopeAuto => Get(nameof(SettingScopeAuto));

    public static string SettingRefreshShort => Get(nameof(SettingRefreshShort));

    public static string SettingCompactShort => Get(nameof(SettingCompactShort));

    public static string SettingDockColorShort => Get(nameof(SettingDockColorShort));

    public static string SettingCoolMaxShort => Get(nameof(SettingCoolMaxShort));

    public static string SettingCoolBoostShort => Get(nameof(SettingCoolBoostShort));

    public static string SettingBoostShort => Get(nameof(SettingBoostShort));

    public static string SettingAutoShort => Get(nameof(SettingAutoShort));

    public static string SettingAutoOnShort => Get(nameof(SettingAutoOnShort));

    public static string SettingAutoOnSecondsShort => Get(nameof(SettingAutoOnSecondsShort));

    public static string SettingAutoOffShort => Get(nameof(SettingAutoOffShort));

    public static string SettingAutoOffSecondsShort => Get(nameof(SettingAutoOffSecondsShort));

    public static string SettingDeviceShortShort => Get(nameof(SettingDeviceShortShort));

    public static string SettingDeviceFullShort => Get(nameof(SettingDeviceFullShort));

    public static string SettingDeviceListShort => Get(nameof(SettingDeviceListShort));

    public static string SettingDeviceDockShort => Get(nameof(SettingDeviceDockShort));

    public static string SettingAdvanced => Get(nameof(SettingAdvanced));

    public static string SettingAdvancedHelp => Get(nameof(SettingAdvancedHelp));

    public static string SettingOpen => Get(nameof(SettingOpen));

    public static string SettingCoresCount => Get(nameof(SettingCoresCount));

    public static string CoresCommand => Get(nameof(CoresCommand));

    public static string CoresPageTitle(string name) => string.Format(CultureInfo.CurrentCulture, Get("CoresPageTitle"), name);

    public static string CoresEmptyTitle => Get(nameof(CoresEmptyTitle));

    public static string CoresEmptySubtitle => Get(nameof(CoresEmptySubtitle));

    public static string SettingsSaved => Get(nameof(SettingsSaved));

    public static string SettingsEmpty => Get(nameof(SettingsEmpty));

    public static string SettingsNumberIgnored(string label) => string.Format(CultureInfo.CurrentCulture, Get("SettingsNumberIgnored"), label);

    public static string SettingsNumberLimited(string label, int value, int min, int max) => string.Format(CultureInfo.CurrentCulture, Get("SettingsNumberLimited"), label, value, min, max);

    public static string SettingsHexIgnored(string band) => string.Format(CultureInfo.CurrentCulture, Get("SettingsHexIgnored"), band);

    public static string SettingsBandsReordered(int cool, int warm, int hot, int critical) => string.Format(CultureInfo.CurrentCulture, Get("SettingsBandsReordered"), cool, warm, hot, critical);

    public static string SettingsAutoOffLowered(int value) => string.Format(CultureInfo.CurrentCulture, Get("SettingsAutoOffLowered"), value);

    private static string Get(string name) => Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
