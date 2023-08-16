using LiteDB;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;

namespace Nekres.Music_Mixer.Core.Services.Data {
    public abstract class AudioContext : DbEntity {
        [BsonRef("audio_sources")]
        public List<AudioSource> AudioSources { get; set; }

        public bool IsEmpty() {
            return AudioSources?.Any() ?? true;
        }

        public AudioSource GetRandom() {
            if (IsEmpty()) {
                return null;
            }
            return AudioSources.ElementAt(RandomUtil.GetRandom(0, AudioSources.Count - 1));
        }
    }
}
