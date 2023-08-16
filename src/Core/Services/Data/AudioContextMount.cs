using LiteDB;

namespace Nekres.Music_Mixer.Core.Services.Data {
    internal class AudioContextMount : AudioContext {
        [BsonField("mount_id")]
        public int MountType { get; set; }

        [BsonField("day_cycle")]
        public int DayCycle { get; set; }
    }
}
