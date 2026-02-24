using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace MediaExtractor
{
    class Program
    {
        private static string lastTitle = "";
        private static string lastStatus = "";
        private static long lastMaxMs = -1;

        static async Task Main(string[] args)
        {
            // Output encoding pour conserver les accents
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                var sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                
                if (sessionManager == null)
                {
                    Console.Error.WriteLine("Impossible d'obtenir le SessionManager.");
                    return;
                }

                while (true)
                {
                    var sessions = sessionManager.GetSessions();
                    GlobalSystemMediaTransportControlsSession targetSession = null;

                    // Filtrage strict : Whitelist pour chercher Apple Music / iTunes parmi toutes les applications en cours
                    foreach (var s in sessions)
                    {
                        if (!string.IsNullOrEmpty(s.SourceAppUserModelId) && 
                            (s.SourceAppUserModelId.IndexOf("AppleMusic", StringComparison.OrdinalIgnoreCase) >= 0 || 
                             s.SourceAppUserModelId.IndexOf("iTunes", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            targetSession = s;
                            break;
                        }
                    }

                    if (targetSession != null)
                    {
                        var mediaProps = await targetSession.TryGetMediaPropertiesAsync();
                        var playbackInfo = targetSession.GetPlaybackInfo();
                        var timeline = targetSession.GetTimelineProperties();

                        if (mediaProps != null && playbackInfo != null)
                        {
                            string title = mediaProps.Title ?? "";
                            string artist = mediaProps.Artist ?? "";
                            string album = mediaProps.AlbumTitle ?? "";
                            string status = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "Playing" : "Paused";
                            string source = targetSession.SourceAppUserModelId ?? "";

                            long posMs = 0;
                            long maxMs = 0;

                            if (timeline != null)
                            {
                                posMs = (long)timeline.Position.TotalMilliseconds;
                                maxMs = (long)timeline.EndTime.TotalMilliseconds;
                            }

                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            long startTs = now - posMs;
                            long endTs = now + (maxMs - posMs);

                            // Déduplication stricte
                            if (title != lastTitle || status != lastStatus || Math.Abs(maxMs - lastMaxMs) > 1000)
                            {
                                string json = $@"{{""source"":""{EscapeJson(source)}"",""title"":""{EscapeJson(title)}"",""artist"":""{EscapeJson(artist)}"",""album"":""{EscapeJson(album)}"",""status"":""{status}"",""startTime"":{startTs},""endTime"":{endTs}}}";
                                
                                Console.WriteLine(json);
                                
                                lastTitle = title;
                                lastStatus = status;
                                lastMaxMs = maxMs;
                            }
                        }
                    }
                    else
                    {
                        if (lastStatus != "Stopped")
                        {
                            Console.WriteLine(@"{""status"":""Stopped""}");
                            lastStatus = "Stopped";
                            lastTitle = "";
                            lastMaxMs = -1;
                        }
                    }

                    // Polling léger
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
