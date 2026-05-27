namespace AppleMusicRPC.Models;

public class AppConfig
{
    public DisplayConfig Display { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
}

public class DisplayConfig
{
    public bool ShowArtist { get; set; } = true;
    public bool ShowAlbum { get; set; } = true;
    public bool ShowTimestamps { get; set; } = true;
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowButton { get; set; } = true;
    public string ButtonLabel { get; set; } = "Écouter sur Apple Music";
}

public class BehaviorConfig
{
    public bool HideWhenPaused { get; set; } = false;
    public int ClearAfterPauseSeconds { get; set; } = 0;
    public bool StartWithWindows { get; set; } = true;
}
