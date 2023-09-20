using Newtonsoft.Json;
using System;

namespace Nekres.Music_Mixer.Core.Services.YtDlp {
    public class MetaData
    {
        [JsonIgnore]
        public bool IsError { get; private set; }

        /// <summary>
        /// Video identifier
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; private set; }

        /// <summary>
        /// Video title
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; private set; }

        /// <summary>
        /// Video URL
        /// </summary>
        [JsonProperty("webpage_url")]
        public string Url { get; private set; }

        /// <summary>
        /// Full name of the video uploader
        /// </summary>
        [JsonProperty("uploader")]
        public string Uploader { get; private set; }

        /// <summary>
        /// Length of the video
        /// </summary>
        [JsonProperty("duration"), JsonConverter(typeof(TimeSpanFromSecondsConverter))]
        public TimeSpan Duration { get; private set; }

        public MetaData(string id, string title, string url, string uploader, TimeSpan duration) {
            Id       = id       ?? string.Empty;
            Title    = title    ?? string.Empty;
            Url      = url      ?? string.Empty;
            Uploader = uploader ?? string.Empty;
            Duration = duration;

            Validate();
        }

        private void Validate() {
            // Id is required to avoid dublicates in a playlist and as thumbnail cache key.
            if (string.IsNullOrWhiteSpace(Id)) {
                IsError = true;
            }

            // Web Url is required to recreate audio urls.
            if (!Url.IsWebLink()) {
                IsError = true;
            }

            // A zero duration indicates an invalid link that wrongly passed previous checks.
            if (Duration.TotalMilliseconds == 0) {
                IsError = true;
            }
        }
    }
}
