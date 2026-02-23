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
                    var session = sessionManager.GetCurrentSession();
                    if (session != null)
                    {
                        var mediaProps = await session.TryGetMediaPropertiesAsync();
                        var playbackInfo = session.GetPlaybackInfo();
                        var timeline = session.GetTimelineProperties();

                        if (mediaProps != null && playbackInfo != null)
                        {
                            string title = mediaProps.Title ?? "";
                            string artist = mediaProps.Artist ?? "";
                            string album = mediaProps.AlbumTitle ?? "";
                            string status = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "Playing" : "Paused";
                            string source = session.SourceAppUserModelId ?? "";

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
                            if (title != lastTitle || status != lastStatus)
                            {
                                string json = $@"{{""source"":""{EscapeJson(source)}"",""title"":""{EscapeJson(title)}"",""artist"":""{EscapeJson(artist)}"",""album"":""{EscapeJson(album)}"",""status"":""{status}"",""startTime"":{startTs},""endTime"":{endTs}}}";
                                
                                Console.WriteLine(json);
                                
                                lastTitle = title;
                                lastStatus = status;
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
