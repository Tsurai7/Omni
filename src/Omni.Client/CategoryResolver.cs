namespace Omni.Client;

public static class CategoryResolver
{
    private static readonly Dictionary<string, string> AppCategoryMappings = new()
    {
        {"Steam", "Gaming"},
        {"Battle.net", "Gaming"},
        {"Origin", "Gaming"},
        {"Epic Games", "Gaming"},
        {"Riot Client", "Gaming"},
        {"Minecraft", "Gaming"},
        {"Blizzard", "Gaming"},
        {"EA", "Gaming"},
        {"Ubisoft Connect", "Gaming"},
        {"PlayStation", "Gaming"},
        {"Xbox", "Gaming"},
        {"Safari", "Browsing"},
        {"Chrome", "Browsing"},
        {"Firefox", "Browsing"},
        {"Edge", "Browsing"},
        {"Brave", "Browsing"},
        {"Opera", "Browsing"},
        {"Visual Studio", "Coding"},
        {"VS Code", "Coding"},
        {"JetBrains", "Coding"},
        {"Xcode", "Coding"},
        {"Android Studio", "Coding"},
        {"Sublime Text", "Coding"},
        {"Atom", "Coding"},
        {"Eclipse", "Coding"},
        {"PyCharm", "Coding"},
        {"IntelliJ", "Coding"},
        {"Messages", "Messaging"},
        {"WhatsApp", "Messaging"},
        {"Telegram", "Messaging"},
        {"Slack", "Messaging"},
        {"Discord", "Messaging"},
        {"Microsoft Teams", "Messaging"},
        {"Zoom", "Messaging"},
        {"Skype", "Messaging"},
        {"Signal", "Messaging"},
        {"Spotify", "Chilling"},
        {"Apple Music", "Chilling"},
        {"Netflix", "Chilling"},
        {"YouTube", "Chilling"},
        {"VLC", "Chilling"},
        {"Twitch", "Chilling"},
        {"Plex", "Chilling"},
        {"Disney+", "Chilling"},
        {"Prime Video", "Chilling"},
        {"Microsoft Word", "Productivity"},
        {"Microsoft Excel", "Productivity"},
        {"Pages", "Productivity"},
        {"Numbers", "Productivity"},
        {"Keynote", "Productivity"},
        {"Notion", "Productivity"},
        {"Evernote", "Productivity"},
        {"OmniFocus", "Productivity"},
        {"Things", "Productivity"}
    };

    private static readonly Dictionary<string, string> CategoryKeywords = new()
    {
        {"game", "Gaming"},
        {"editor", "Coding"},
        {"ide", "Coding"},
        {"browser", "Browsing"},
        {"player", "Chilling"},
        {"music", "Chilling"},
        {"video", "Chilling"},
        {"chat", "Messaging"},
        {"message", "Messaging"},
        {"mail", "Messaging"}
    };

    public static string ResolveCategory(string appName)
    {
        if (string.IsNullOrEmpty(appName))
            return "Other";

        foreach (var mapping in AppCategoryMappings)
        {
            if (appName.Contains(mapping.Key))
                return mapping.Value;
        }

        foreach (var keyword in CategoryKeywords)
        {
            if (appName.Contains(keyword.Key, StringComparison.OrdinalIgnoreCase))
                return keyword.Value;
        }

        return "Other";
    }
}
