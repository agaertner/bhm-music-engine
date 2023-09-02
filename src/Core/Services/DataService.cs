using Blish_HUD;
using Blish_HUD.Content;
using LiteDB;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core.Services.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using System;
using System.IO;
using System.Net;
using System.Threading;
using Image = SixLabors.ImageSharp.Image;
using MountType = Gw2Sharp.Models.MountType;

namespace Nekres.Music_Mixer.Core.Services {
    internal class DataService : IDisposable
    {
        private ConnectionString _connectionString;

        private readonly ReaderWriterLockSlim _rwLock       = new();
        private          ManualResetEvent     _lockReleased = new(false);
        private          bool                 _lockAcquired = false;

        private const  string TBL_PLAYLISTS        = "playlists";
        public const  string TBL_AUDIO_SOURCES    = "audio_sources";
        private const string TBL_THUMBNAILS       = "thumbnails";
        private const string TBL_THUMBNAIL_CHUNKS = "thumbnail_chunks";

        private const string LITEDB_FILENAME = "music.db";

        public DataService() {
            _connectionString = new ConnectionString {
                Filename   = Path.Combine(MusicMixer.Instance.ModuleDirectory, LITEDB_FILENAME),
                Connection = ConnectionType.Shared
            };
        }

        public AsyncTexture2D GetThumbnail(AudioSource source) {
            LiteFileStream<string> stream = null;

            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var thumbnails = db.GetStorage<string>(TBL_THUMBNAILS, TBL_THUMBNAIL_CHUNKS);
                stream = thumbnails.OpenRead(source.PageUrl);

            } catch (Exception e) {

                MusicMixer.Logger.Warn(e, e.Message);

            } finally {
                this.ReleaseWriteLock();
            }

            var texture = new AsyncTexture2D();

            if (stream == null) {
                // Thumbnail not cached, request it.
                MusicMixer.Instance.YtDlp.GetThumbnail(source.PageUrl, thumbnailUri => ThumbnailUrlReceived(source.PageUrl, thumbnailUri, texture));
                return texture;
            }

            try {
                using var gdx = GameService.Graphics.LendGraphicsDeviceContext();
                texture.SwapTexture(Texture2D.FromStream(gdx.GraphicsDevice, stream));
                return texture;
            } catch (InvalidOperationException e) {
                // Unsupported image format.
                MusicMixer.Logger.Info(e,e.Message);
            }
            return texture;
        }

        public bool GetTrackByMediaId(string mediaId, out AudioSource source) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>(TBL_AUDIO_SOURCES);
                source = collection.FindOne(x => x.ExternalId.Equals(mediaId));
                return source != null;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        private bool GetPlaylist(string externalId, out Playlist playlist) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<Playlist>(TBL_PLAYLISTS);
                playlist = collection.Include(x => x.Tracks).FindOne(x => x.ExternalId == externalId);
                return playlist != null;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public bool GetMountPlaylist(MountType mountType, out Playlist playlist) {
            return GetPlaylist(mountType.ToString(), out playlist);
        }

        public bool GetDefeatedPlaylist(out Playlist context) {
            return GetPlaylist("Defeated", out context);
        }

        public bool GetMapPlaylist(int mapId, out Playlist playlist) {
            return GetPlaylist(mapId.ToString(), out playlist);
        }

        public bool Remove(AudioSource model) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>(TBL_AUDIO_SOURCES);
                return collection.Delete(model.Id);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public bool RemoveAudioUrls() {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>(TBL_AUDIO_SOURCES);

                return collection.Count() == collection.UpdateMany(src => new AudioSource {
                    Id         = src.Id,
                    ExternalId = src.ExternalId,
                    PageUrl    = src.PageUrl,
                    Title      = src.Title,
                    Uploader   = src.Uploader,
                    Duration   = src.Duration,
                    Volume     = src.Volume,
                    DayCycles  = src.DayCycles,
                    AudioUrl   = string.Empty
                }, src => true);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public bool Upsert(AudioSource model)
        {
            this.AcquireWriteLock();

            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>(TBL_AUDIO_SOURCES);
                collection.Upsert(model); // Returns true on insertion and false on update.
                return true;
            } catch (Exception e) {
                MusicMixer.Logger.Warn(e, e.Message);
                return false;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public bool Upsert(Playlist model) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<Playlist>(TBL_PLAYLISTS);
                collection.Upsert(model);
                return true;
            } catch (Exception e) {
                MusicMixer.Logger.Warn(e, e.Message);
                return false;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public bool Upsert(Playlist model, string table) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<Playlist>(table);
                collection.Upsert(model);
                return true;
            } catch (Exception e) {
                MusicMixer.Logger.Warn(e, e.Message);
                return false;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        /// <summary>
        /// Releases all resources held by <see cref="DataService"/>.
        /// </summary>
        /// <remarks>
        ///  <see cref="LiteDatabase.Dispose"/> will do nothing if the connection type is shared since it's closed after each operation.<br/>
        ///  See also: <seealso href="https://www.litedb.org/docs/connection-string/"/>
        /// </remarks>
        public void Dispose()
        {
            // Wait for the lock to be released
            if (_lockAcquired) {
                _lockReleased.WaitOne(500);
            }
            
            _lockReleased.Dispose();

            // Dispose the lock
            try {
                _rwLock.Dispose();
            } catch (Exception ex) {
                MusicMixer.Logger.Debug(ex, ex.Message);
            }
        }

        private void ThumbnailUrlReceived(string id, string url, AsyncTexture2D texture) {
            if (string.IsNullOrEmpty(url)) {
                return;
            }

            var client = new WebClient();
            client.OpenReadAsync(new Uri(url));

            client.OpenReadCompleted += (o, e) => {
                try {
                    if (e.Cancelled) {
                        return;
                    }

                    if (e.Error != null) {
                        throw e.Error;
                    }

                    var       stream = e.Result;
                    using var image  = Image.Load(stream);
                    using var ms     = new MemoryStream();
                    image.Save(ms, JpegFormat.Instance);
                    ms.Position = 0;

                    // Cache the thumbnail
                    this.AcquireWriteLock();
                    try {
                        using var db = new LiteDatabase(_connectionString);

                        var thumbnails = db.GetStorage<string>(TBL_THUMBNAILS, TBL_THUMBNAIL_CHUNKS);
                        thumbnails.Upload(id, url, ms);
                    } finally {
                        this.ReleaseWriteLock();
                    }

                    // Swap texture with the thumbnail
                    using var gdx = GameService.Graphics.LendGraphicsDeviceContext();
                    texture.SwapTexture(Texture2D.FromStream(gdx.GraphicsDevice, ms));

                    stream.Close();
                    ((WebClient)o).Dispose();
                } catch (Exception ex) when (ex is WebException or ImageFormatException or ArgumentException or InvalidOperationException) {
                    MusicMixer.Logger.Info(ex, ex.Message);
                }
            };
        }

        private void AcquireWriteLock() {
            try {
                _rwLock.EnterWriteLock();
                _lockAcquired = true;
            } catch (Exception ex) {
                MusicMixer.Logger.Debug(ex, ex.Message);
            }
        }

        private void ReleaseWriteLock() {
            try {
                if (_lockAcquired) {
                    _rwLock.ExitWriteLock();
                    _lockAcquired = false;
                }
            } catch (Exception ex) {
                MusicMixer.Logger.Debug(ex, ex.Message);
            } finally {
                _lockReleased.Set();
            }
        }
    }
}
