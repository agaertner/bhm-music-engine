using LiteDB;

namespace Nekres.Music_Mixer.Core.Services.Data {
    public abstract class DbEntity {
        [BsonId(true)]
        public ObjectId Id { get; set; }
    }
}
