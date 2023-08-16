using LiteDB;

namespace Nekres.Music_Mixer.Core.Services.Data {
    internal class AudioContextLocation : AudioContext {
        [BsonField("map_id")]
        public int MapId { get; set; }

        [BsonField("day_cycle")]
        public int DayCycle { get; set; }
    }
}
