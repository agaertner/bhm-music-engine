using Newtonsoft.Json;
using System;

namespace Nekres.Music_Mixer.Core.Services.YtDlp {
    public class MetaData
    {
        public static MetaData Empty = new() {
            IsEmpty = true
        };

        [JsonIgnore]
        public bool IsEmpty { get; private init; }

        /// <summary>
        /// Video identifier
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Video title
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>
        /// Video URL
        /// </summary>
        [JsonProperty("webpage_url")]
        public string Url { get; set; }

        /// <summary>
        /// Full name of the video uploader
        /// </summary>
        [JsonProperty("uploader")]
        public string Uploader { get; set; }

        /// <summary>
        /// Length of the video
        /// </summary>
        [JsonProperty("duration"), JsonConverter(typeof(TimeSpanFromSecondsConverter))]
        public TimeSpan Duration { get; set; }
    }
}
