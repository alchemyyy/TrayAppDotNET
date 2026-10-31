namespace FanControlTrayAppDotNET;

internal static class Constants
{
    public const string ApplicationName = "FanControlTrayAppDotNET";
    public const string AppBaseURI = "avares://" + ApplicationName + "/";
    public const string AppIconResourceUri = AppBaseURI + "Assets/" + AppIconLoader.DefaultFileName;
    public const string FanFontRelativePath = "Visuals/FanFont.ttf";
    public const string FanFontResourceUri = AppBaseURI + FanFontRelativePath;
    public const string FanFontFamilyName = FanFontResourceUri + "#Untitled1";
    public const string AppGUID = "068620a8-fbc6-4f04-b73d-8ed820b36554";
    public const string TrayIconGUID = "068620a8-fbc6-4f04-b73d-8ed820b36554";
}
