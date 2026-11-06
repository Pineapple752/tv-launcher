using System.Text.Json;

namespace TVBrowser;

sealed class AppConfig
{
    public string HomeUrl { get; set; } = "https://pineapple752.github.io/tv-launcher/";

    // true = sites can't go fullscreen (videos stay windowed inside the already full screen window)
    public bool BlockFullscreen { get; set; } = false;

    // A domain also allows all of its subdomains. Entries like "accounts.google.com" allow just that part of a site.
    public string[] AllowedDomains { get; set; } =
    {
        "youtube.com", "youtu.be", "googlevideo.com", "ytimg.com",
        "accounts.google.com", "consent.google.com",
        "netflix.com", "primevideo.com", "amazon.com", "disneyplus.com", "disney.com",
        "hulu.com", "hbomax.com", "max.com", "hbo.com", "apple.com", "paramountplus.com",
        "peacocktv.com", "twitch.tv", "crunchyroll.com", "spotify.com", "plex.tv",
        "tubitv.com", "pluto.tv"
    };

    // set when config.json exists but couldn't be read, so the app can tell you instead of silently using defaults
    public static string? LoadError { get; private set; }

    // reads config.json from the folder next to TVBrowser.exe; falls back to the defaults above
    public static AppConfig Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), options) ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            LoadError = "config.json couldn't be read, so the built-in settings are being used instead.\n\n" + ex.Message;
        }
        return new AppConfig();
    }
}
