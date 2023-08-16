using LiteDB;
using System;

namespace Nekres.Music_Mixer.Core.Services.Data {
    public abstract class DbEntity {
        [BsonId(true)]
        public Guid Id { get; set; }

        [BsonField("created_at")]
        public DateTime CreatedAt { get; set; }

        [BsonField("modified_at")]
        public DateTime ModifiedAt { get; set; }
    }
}
