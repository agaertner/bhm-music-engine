﻿using Blish_HUD;
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

namespace Nekres.Music_Mixer.Core.Services {
    internal class DataService : IDisposable
    {
        private ConnectionString _connectionString;

        private readonly ReaderWriterLockSlim _rwLock       = new();
        private          ManualResetEvent     _lockReleased = new(false);
        private          bool                 _lockAcquired = false;

        public DataService() {
            _connectionString = new ConnectionString {
                Filename   = Path.Combine(MusicMixer.Instance.ModuleDirectory, "music.db"),
                Connection = ConnectionType.Shared
            };
        }

        public AsyncTexture2D GetThumbnail(AudioSource source) {
            LiteFileStream<string> stream = null;

            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var thumbnails = db.GetStorage<string>("thumbnails", "thumbnail_chunks");
                stream = thumbnails.OpenRead(source.Uri);

            } catch (Exception e) {

                MusicMixer.Logger.Warn(e, e.Message);

            } finally {
                this.ReleaseWriteLock();
            }

            if (stream == null) {
                // Thumbnail not cached, request it.
                var texture = new AsyncTexture2D();
                MusicMixer.Instance.YtDlp.GetThumbnail(source.Uri, thumbnailUri => ThumbnailUrlReceived(source.Uri, thumbnailUri, texture));
                return texture;
            }

            try {
                using var gdx = GameService.Graphics.LendGraphicsDeviceContext();
                return Texture2D.FromStream(gdx.GraphicsDevice, stream);
            } catch (InvalidOperationException e) {
                // Unsupported image format.
                MusicMixer.Logger.Info(e,e.Message);
            }
            return ContentService.Textures.TransparentPixel;
        }

        public AudioContextLocation GetContextLocation(int mapId, int dayCycle) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioContextLocation>("audio_sources");
                return collection.Include(x => x.AudioSources)
                                 .FindOne(x => 
                                              x.MapId    == mapId &&
                                              x.DayCycle == dayCycle);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public AudioContextMount GetContextMount(int mountType, int dayCycle) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioContextMount>("audio_sources");
                return collection.Include(x => x.AudioSources)
                                 .FindOne(x => 
                                              x.MountType == mountType &&
                                              x.DayCycle  == dayCycle);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public bool Remove(AudioSource model) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>("audio_sources");
                return collection.Delete(model.Id);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public void Upsert(AudioSource model)
        {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>("audio_sources");
                collection.Upsert(model);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public void Upsert(AudioContextLocation model) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioContextLocation>("context_locations");
                collection.Upsert(model);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public void Upsert(AudioContextMount model) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioContextMount>("context_mounts");
                collection.Upsert(model);
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

                        var thumbnails = db.GetStorage<string>("thumbnails", "thumbnail_chunks");
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
