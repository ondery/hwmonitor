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

    private static string Get(string name) => Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
