using LiteDB;
using System;
using Blish_HUD;

namespace Nekres.Music_Mixer.Core.Services.Data {
    public class AudioSource : DbEntity {

        public event EventHandler<ValueEventArgs<float>> VolumeChanged;

        [BsonField("title")]
        public string Title { get; set; }

        [BsonField("artist")]
        public string Artist { get; set; }

        [BsonField("uri")]
        public string Uri { get; set; }

        [BsonField("audioUrl")]
        public string AudioUrl { get; set; }

        [BsonField("duration")]
        public TimeSpan Duration { get; set; }

        private float _volume;
        [BsonField("volume")]
        public float Volume {
            get => _volume;
            set {
                _volume = value;
                VolumeChanged?.Invoke(this, new ValueEventArgs<float>(value));
            }
        }
    }
}