using Blish_HUD;
using LiteDB;
using System.Collections.Generic;
using System.Linq;

namespace Nekres.Music_Mixer.Core.Services.Data {
    public class Playlist : DbEntity {

        [BsonIgnore]
        public bool IsEmpty => !this.Tracks?.Any() ?? true;

        [BsonField("external_id")]
        public string ExternalId { get; set; }

        [BsonField("playlist")]
        [BsonRef(DataService.TBL_AUDIO_SOURCES)]
        public List<AudioSource> Tracks { get; set; }

        public AudioSource GetRandom(int dayCycle) {
            return dayCycle switch {
                1 => GetRandom(AudioSource.DayCycle.Dawn),
                2 => GetRandom(AudioSource.DayCycle.Day),
                3 => GetRandom(AudioSource.DayCycle.Dusk),
                4 => GetRandom(AudioSource.DayCycle.Night),
                _ => AudioSource.Empty
            };
        }

        private AudioSource GetRandom(AudioSource.DayCycle dayCycle) {
            if (!this.Tracks?.Any() ?? true) {
                return AudioSource.Empty;
            }

            var cycle = this.Tracks.Where(x => x.HasDayCycle(dayCycle)).ToList();

            if (!cycle.Any()) {
                return AudioSource.Empty;
            }

            return cycle.ElementAt(RandomUtil.GetRandom(0, cycle.Count - 1));
        }
    }
}
