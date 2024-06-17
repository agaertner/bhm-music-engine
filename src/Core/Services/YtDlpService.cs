using Blish_HUD;
using Gapotchenko.FX.Diagnostics;
using Nekres.Music_Mixer.Core.Services.YtDlp;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services {
    public class YtDlpService
    {
        public enum AudioFormat {
            Best,
            MP3,
            AAC,
            WMA,
            FLAC
        }

        public enum AudioBitrate {
            B64,
            B96,
            B128,
            B160,
            B192,
            B256,
            B320
        }

        private readonly Regex _mediaId        = new("^([a-zA-Z0-9_-]+)$", RegexOptions.Compiled);
        private readonly Regex _youtubeVideoId = new(@"youtu(?:\.be|be\.com)/(?:.*v(?:/|=)|(?:.*/)?)(?<id>[a-zA-Z0-9-_]+).*", RegexOptions.Compiled);
        private readonly Regex _progressReport = new(@"^\[download\].*?(?<percentage>(.*?))% of (?<size>(.*?))MiB at (?<speed>(.*?)) ETA (?<eta>(.*?))$", RegexOptions.Compiled); //[download]   2.7% of 4.62MiB at 200.00KiB/s ETA 00:23
        private readonly Regex _upToDate       = new(@"^yt-dlp is up to date \((?<version>.*?)\)$", RegexOptions.Compiled);
        private readonly Regex _updating       = new(@"^Updating to (?<version>.*?) \.\.\.$", RegexOptions.Compiled);
        private readonly Regex _updated        = new("^Updated yt-dlp to (?<version>.*?)$", RegexOptions.Compiled);
        private readonly Regex _version        = new(@"^Available version: (?<available>.*?), Current version: (?<current>.*?)$", RegexOptions.Compiled);
        private readonly Regex _warning        = new(@"^WARNING:[^\S\r\n](.*?)$", RegexOptions.Compiled);
        private readonly Regex _error          = new(@"^ERROR:[^\S\r\n](.*?)$", RegexOptions.Compiled);

        private string _executablePath;

        private Logger _logger = Logger.GetLogger(typeof(YtDlpService));

        private const string _globalYtDlpArgs = "--ignore-config --no-call-home --no-warnings --no-get-comments --extractor-retries 0 "
                                              + "--extractor-args \"youtube:max_comments=0;player_client=web,web_music;skip=configs,webpage\"";

        public YtDlpService() {
            ExtractFile("bin/yt-dlp.exe");
        }

        private void ExtractFile(string filePath) {
            _executablePath = Path.Combine(MusicMixer.Instance.ModuleDirectory, filePath);
            if (File.Exists(_executablePath)) {
                return;
            }
            using var fs = MusicMixer.Instance.ContentsManager.GetFileStream(filePath);
            fs.Position = 0;
            byte[] buffer  = new byte[fs.Length];
            var content = fs.Read(buffer, 0, (int)fs.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(_executablePath));
            File.WriteAllBytes(_executablePath, buffer);
        }

        public bool GetYouTubeVideoId(string url, out string id) {
            var match = _youtubeVideoId.Match(url);
            if (match.Success && match.Groups["id"].Success) {
                id = match.Groups["id"].Value;
                return true;
            }
            id = string.Empty;
            return false;
        }

        public void RemoveCache() {
            var p = new Process {
                StartInfo =
                {
                    CreateNoWindow         = true,
                    FileName               = _executablePath,
                    Arguments              = $"--rm-cache-dir {_globalYtDlpArgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                }
            };
            p.ErrorDataReceived += (_, e) => {
                if (string.IsNullOrWhiteSpace(e.Data)) {
                    return;
                }
                _logger.Info(e.Data);
            };
            p.Start();
            p.BeginErrorReadLine();
        }

        public async Task Update(IProgress<string> progressHandler) {

            var p = new Process
            {
                StartInfo =
                {
                    CreateNoWindow         = true,
                    FileName               = _executablePath,
                    Arguments              = $"-U {_globalYtDlpArgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                }
            };
            p.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) {
                    return;
                }

                // Identify yt-dlp versions
                var versionCheck = _version.Match(e.Data);
                if (versionCheck.Success) {

                    var current   = versionCheck.Groups["current"].Value;
                    var available = versionCheck.Groups["available"].Value;

                    _logger.Info($"Current version \"{current}\"");

                    if (!string.Equals(available,current)) {
                        _logger.Info($"Available version \"{available}\"");
                    }
                }

                // Check if yt-dlp is updating
                var isUpdating = _updating.Match(e.Data);
                if (isUpdating.Success) {
                    var version = isUpdating.Groups["version"].Value;
                    progressHandler.Report($"Updating yt-dlp to \"{version}\"…");
                }

                // Check if yt-dlp has updated
                var hasUpdated = _updated.Match(e.Data);
                if (hasUpdated.Success) {
                    var version = hasUpdated.Groups["version"].Value;
                    progressHandler.Report(null);

                    _logger.Info($"Updated to \"{version}\"");
                }
            };
            p.ErrorDataReceived += (_, e) => {
                if (string.IsNullOrWhiteSpace(e.Data)) {
                    return;
                }
                _logger.Info(e.Data);
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();
        }

        public void GetThumbnail(string link, Action<string> callback)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    FileName = _executablePath,
                    Arguments = $"--get-thumbnail {link} {_globalYtDlpArgs}"
                }
            };
            p.OutputDataReceived += (_, e) => callback(e.Data);
            p.Start();
            p.BeginOutputReadLine();
        }

        public async Task<string> GetAudioOnlyUrl(string link)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    FileName = _executablePath,
                    Arguments = string.Format("-g {0} -f \"bestaudio[ext=m4a][abr<={1}]/bestaudio[ext=aac][abr<={1}]/bestaudio[abr<={1}]/bestaudio\" {2}", 
                                              link, 
                                              MusicMixer.Instance.ModuleConfig.Value.AverageBitrate.ToString().Substring(1), 
                                              _globalYtDlpArgs)
                }
            };
            var result = string.Empty;
            p.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data) || e.Data.ToLower().StartsWith("error")) {
                    return;
                }
                result = e.Data;
            };
            p.Start();
            p.BeginOutputReadLine();
            await p.WaitForExitAsync();
            return result;
        }

        public async Task<MetaData> GetMetaData(string link)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    FileName               = _executablePath,
                    Arguments              = $"--print id,webpage_url,title,uploader,duration {link} {_globalYtDlpArgs}" // Url is supported if we get any results.
                }
            };

            string   externalId = string.Empty;
            string   url        = string.Empty;
            string   title      = string.Empty;
            string   uploader   = string.Empty;
            TimeSpan duration   = TimeSpan.Zero;

            int i = 0;
            p.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) {
                    return;
                }

                switch (i) {
                    case 0:
                        var id = _mediaId.Match(e.Data);
                        if (!id.Success) {
                            return;
                        }
                        externalId = e.Data;
                        break;
                    case 1:
                        url = e.Data;
                        break;
                    case 2:
                        title = e.Data;
                        break;
                    case 3:
                        uploader = e.Data;
                        break;
                    case 4:
                        duration = int.TryParse(Regex.Replace(e.Data, "[^0-9]", string.Empty), out var dur) ? 
                                                TimeSpan.FromSeconds(dur) : TimeSpan.Zero;
                        break;
                    default: break;
                }
                ++i;
            };

            p.ErrorDataReceived += (_, e) => {
                if (string.IsNullOrWhiteSpace(e.Data)) {
                    return;
                }
                var error = _error.Match(e.Data);
                if (error.Success) {
                    _logger.Info($"{error}");
                }
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync();
            return new MetaData(externalId, title, url, uploader, duration);
        }

        /*
        public void Download(string link, string outputFolder, AudioFormat format, IProgress<string> progress)
        {
            Directory.CreateDirectory(outputFolder);

            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    FileName = ExecutablePath,
                    Arguments = $"{link} -o \"{outputFolder}/%(title)s.%(ext)s\" --restrict-filenames --extract-audio --audio-format {format.ToString().ToLower()} --ffmpeg-location \"{ffmpeg.ExecutablePath}\""
                }
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) {
                    return;
                }

                var match = _progressReport.Match(e.Data);
                if (!match.Success) {
                    return;
                }

                var percent = double.Parse(match.Groups["percentage"].Value, CultureInfo.InvariantCulture) / 100;
                var totalSize = double.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
                var size = percent * totalSize;
                var speed = match.Groups["speed"].Value;
                var eta = match.Groups["eta"].Value;
                var message = $"{size}/{totalSize}MB ({percent}%), {eta}, {speed}";
                progress.Report(message);
                Debug.WriteLine(message);
            };
            p.Start();
            p.BeginErrorReadLine();
        }*/
    }
}