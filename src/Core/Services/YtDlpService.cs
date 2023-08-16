using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gapotchenko.FX.Diagnostics;
using Nekres.Music_Mixer.Core.Services.YtDlp;

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

        private readonly Regex _youtubeVideoId = new(@"youtu(?:\.be|be\.com)/(?:.*v(?:/|=)|(?:.*/)?)(?<id>[a-zA-Z0-9-_]+)", RegexOptions.Compiled);
        private readonly Regex _progressReport = new(@"^\[download\].*?(?<percentage>(.*?))% of (?<size>(.*?))MiB at (?<speed>(.*?)) ETA (?<eta>(.*?))$", RegexOptions.Compiled); //[download]   2.7% of 4.62MiB at 200.00KiB/s ETA 00:23
        private readonly Regex _upToDate = new(@"^yt-dlp is up to date \((?<version>.*?)\)$", RegexOptions.Compiled);
        private readonly Regex _updating = new(@"^Updating to (?<version>.*?) \.\.\.$", RegexOptions.Compiled);
        private readonly Regex _updated = new("^Updated yt-dlp to (?<version>.*?)$", RegexOptions.Compiled);

        private string _executablePath;

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

        public async Task Update(IProgress<string> progressHandler) {

            var result = string.Empty;

            var p = new Process
            {
                StartInfo =
                {
                    CreateNoWindow = true,
                    FileName = _executablePath,
                    Arguments = "-U",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            };
            p.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) {
                    return;
                }

                var version = string.Empty;

                var isUpdating = _updating.Match(e.Data);

                if (isUpdating.Success) {
                    progressHandler.Report("YT-DLP is updating…");
                }

                // Check if yt-dlp is up to date
                var isUpToDate = _upToDate.Match(e.Data);
                if (isUpToDate.Success) {
                    version = isUpToDate.Groups["version"].Value;
                    progressHandler.Report(null);
                }

                // Check if yt-dlp has updated
                var hasUpdated = _updated.Match(e.Data);
                if (hasUpdated.Success) {
                    version = hasUpdated.Groups["version"].Value;
                    progressHandler.Report(null);
                }

                result = version;
            };
            p.ErrorDataReceived += (_, e) => {
                if (string.IsNullOrEmpty(e.Data)) {
                    return;
                }
                result = e.Data;
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
                    Arguments = $"--get-thumbnail {link}"
                }
            };
            p.OutputDataReceived += (_, e) => callback(e.Data);
            p.Start();
            p.BeginOutputReadLine();
        }

        public async Task<bool> IsUrlSupported(string link)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    FileName = _executablePath,
                    Arguments = $"--dump-json {link}" // Url is supported if we get any results.
                }
            };
            p.Start();
            return await p.WaitForExitAsync().ContinueWith(t => !t.IsFaulted && p.ExitCode == 0);
        }

        public void GetAudioOnlyUrl<TModel>(string link, Func<string, TModel, Task> callback, TModel model = default)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    FileName = _executablePath,
                    Arguments = string.Format("-g {0} -f \"bestaudio[ext=m4a][abr<={1}]/bestaudio[ext=aac][abr<={1}]/bestaudio[abr<={1}]/bestaudio\"", 
                                              link, 
                                              MusicMixer.Instance.AverageBitrateSetting.Value.ToString().Substring(1))
                }
            };
            p.OutputDataReceived += async (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data) || e.Data.ToLower().StartsWith("error")) {
                    return;
                }

                await callback(e.Data, model);
            };
            p.Start();
            p.BeginOutputReadLine();
        }

        public void GetMetaData(string link, Action<MetaData> callback)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    FileName = _executablePath,
                    Arguments = $"--dump-json {link}"
                }
            };
            p.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) {
                    return;
                }

                callback(JsonConvert.DeserializeObject<MetaData>(e.Data));
            };
            p.Start();
            p.BeginOutputReadLine();
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