using Newtonsoft.Json;
using System.Collections.Generic;

namespace Nekres.Music_Mixer.Core.Services.Data {
    internal sealed class Tracklist {
        [JsonProperty("id")]
        public string ExternalId { get; set; }

        [JsonProperty("tracks")]
        public List<Track> Tracks { get; set; }

        public sealed class Track {
            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("daytime")]
            public int DayCycle { get; set; }
        }
    }
}
