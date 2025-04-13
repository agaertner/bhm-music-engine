using Blish_HUD;
using Blish_HUD.Content;
using LiteDB;
using Nekres.Music_Mixer.Properties;
using System;
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

        [BsonField("default")]
        public bool? Default { get; set; }

        [BsonField("last_error")]
        public YtDlpService.ErrorCode? LastError { get; set; }

        [BsonIgnore]
        public bool IsPreview { get; set; }

        [BsonIgnore]
        public bool HasError => this.LastError.HasValue && this.LastError > 0;

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

        public string GetErrorMessage() {
            if (!HasError) {
                return string.Empty;
            }
            var reason = this.LastError switch {
                YtDlpService.ErrorCode.Deleted     => Resources.Deleted,
                YtDlpService.ErrorCode.Depublished => Resources.Depublished,
                YtDlpService.ErrorCode.Geoblocked  => Resources.Geo_Blocked,
                _                                  => Resources.Unknown
            };
            return string.Format(Resources.__0___nis_not_available__Reason___1__, this.Title, reason);
        }
    }
}