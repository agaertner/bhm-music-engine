using Blish_HUD;
using LiteDB;
using System;
using Blish_HUD.Content;

namespace Nekres.Music_Mixer.Core.Services.Data {
    public class AudioSource : DbEntity {

        [Flags]
        public enum DayCycle {
            None = 0,
            Dawn = 1 << 0,
            Day = Dawn | 1 << 1,
            Dusk = 1 << 2,
            Night = Dusk | 1 << 3
        }

        public static AudioSource Empty = new() {
            Title = string.Empty,
            Uploader = string.Empty,
            PageUrl = string.Empty,
            AudioUrl = string.Empty,
            IsEmpty = true
        };

        public event EventHandler<ValueEventArgs<float>> VolumeChanged;

        [BsonIgnore]
        public bool IsEmpty { get; private init; }

        [BsonIgnore]
        public Gw2StateService.State State { get; set; }

        [BsonField("external_id")]
        public string ExternalId { get; set; }

        [BsonField("page_url")]
        public string PageUrl { get; set; }

        [BsonField("title")]
        public string Title { get; set; }

        [BsonField("uploader")]
        public string Uploader { get; set; }

        [BsonField("duration")]
        public TimeSpan Duration { get; set; }

        private float _volume;
        [BsonField("volume")]
        //TODO: Not implemented. Always 1. Implement volume slider per track in interface.
        public float Volume {
            get => _volume;
            set {
                _volume = value;
                VolumeChanged?.Invoke(this, new ValueEventArgs<float>(value));
            }
        }

        [BsonField("day_cycles")]
        public DayCycle DayCycles { get; set; }

        private AsyncTexture2D _thumbnail;
        [BsonIgnore]
        public AsyncTexture2D Thumbnail {
            get {
                if (_thumbnail != null) {
                    return _thumbnail;
                }

                _thumbnail = MusicMixer.Instance.Data.GetThumbnail(this);
                return _thumbnail;
            }
        }

        /// <summary>
        /// Direct audio URL.
        /// </summary>
        /// <remarks>
        /// Final download URLs are only guaranteed to work on the same machine/IP where extracted.
        /// </remarks>
        [BsonField("audio_url")]
        public string AudioUrl { get; set; }

        public bool HasDayCycle(DayCycle cycle) {
            return (DayCycles & cycle) == cycle;
        }
    }
}