using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Extended;
using LiteDB;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core.Player.API;
using Nekres.Music_Mixer.Core.Services.Entities;
using Nekres.Music_Mixer.Core.UI.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using Gw2Sharp.Models;
using Image = SixLabors.ImageSharp.Image;

namespace Nekres.Music_Mixer.Core.Services {
    internal class DataService : IDisposable
    {
        private readonly ReaderWriterLockSlim              _rwLock       = new ReaderWriterLockSlim();
        private          ManualResetEvent                  _lockReleased = new ManualResetEvent(false);
        private          bool                              _lockAcquired = false;

        private          ConnectionString                  _connectionString;
        private          Dictionary<string, HashSet<Guid>> _playlists;

        public DataService(string cacheDir) {
            _connectionString = new ConnectionString {
                Filename   = Path.Combine(cacheDir, "data.db"),
                Connection = ConnectionType.Shared
            };

            _playlists = new Dictionary<string, HashSet<Guid>>();
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

        public void DownloadThumbnail(MusicContextModel model)
        { 
            youtube_dl.GetThumbnail(model.Thumbnail, model.Uri, model.Uri, ThumbnailUrlReceived);
        }

        private void ThumbnailUrlReceived(AsyncTexture2D tex, string id, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            var client = new WebClient();
            client.OpenReadAsync(new Uri(url));

            client.OpenReadCompleted += (o, e) =>
            {
                try
                {
                    if (e.Cancelled) return;
                    if (e.Error != null) throw e.Error;

                    var stream = e.Result;
                    using var image = Image.Load(stream);
                    using var ms    = new MemoryStream();
                    image.Save(ms, JpegFormat.Instance);
                    ms.Position = 0;

                    this.AcquireWriteLock();
                    try {
                        using var db = new LiteDatabase(_connectionString);
                        var       thumbnails = db.GetStorage<string>("thumbnails", "thumbnail_chunks");
                        thumbnails.Upload(id, url, ms);
                    } finally {
                        this.ReleaseWriteLock();
                    }

                    stream.Close();
                    ((WebClient)o).Dispose();

                    using var gdx   = GameService.Graphics.LendGraphicsDeviceContext();
                    var       thumb = Texture2D.FromStream(gdx.GraphicsDevice, ms);
                    tex.SwapTexture(thumb);
                }
                catch (Exception ex) when (ex is WebException or ImageFormatException or ArgumentException or InvalidOperationException)
                {
                    MusicMixer.Logger.Info(ex, ex.Message);
                }
            };
        }

        public void GetThumbnail(MusicContextModel model) {

            LiteFileStream<string> texture;

            this.AcquireWriteLock();
            try {
                using var db         = new LiteDatabase(_connectionString);
                var       thumbnails = db.GetStorage<string>("thumbnails", "thumbnail_chunks");
                texture = thumbnails.OpenRead(model.Uri);
            } finally {
                this.ReleaseWriteLock();
            }

            if (texture == null) {
                return;
            }

            try {
                using var gdx       = GameService.Graphics.LendGraphicsDeviceContext();
                var       thumbnail = Texture2D.FromStream(gdx.GraphicsDevice, texture);
                model.Thumbnail.SwapTexture(thumbnail);
            }
            catch (InvalidOperationException e)
            {
                // Unsupported image format.
                MusicMixer.Logger.Info(e,e.Message);
            }
        }

        public void Upsert(MusicContextModel model)
        {
            var entity = this.Search(x => x.Id.Equals(model.Id)).FirstOrDefault();

            if (entity == null)
            {
                entity = MusicContextEntity.FromModel(model);
                this.Insert(entity);
            }
            else
            {
                entity.DayTimes = model.DayTimes.ToList();
                entity.MapIds = model.MapIds.ToList();
                entity.ExcludedMapIds = model.ExcludedMapIds.ToList();
                entity.MountTypes = model.MountTypes.ToList();
                entity.State = model.State;
                entity.Volume = model.Volume;
                this.Update(entity);
            }
        }



        public MusicContextEntity GetRandom()
        {
            var state    = MusicMixer.Instance.Gw2State.CurrentState;
            var mapId    = GameService.Gw2Mumble.CurrentMap.Id;
            var dayCycle = MusicMixer.Instance.Gw2State.TyrianTime;
            var mount    = GameService.Gw2Mumble.PlayerCharacter.CurrentMount;

            // Get all tracks for state.
            var tracks = this.Search(x => 
                                         x.State == state
                                      && x.DayTimes.Contains(dayCycle)
                                      && (x.State != Gw2StateService.State.Mounted 
                                      && x.MapIds.Contains(mapId)
                                      || x.State == Gw2StateService.State.Mounted
                                      && x.MountTypes.Contains(mount))).ToList();

            if (!tracks.Any())
            {
                return null;
            }


            var context  = $"{state}{mapId}{dayCycle}{mount}";
            if (!_playlists.ContainsKey(context))
            {
                _playlists.Add(context, new HashSet<Guid>());
            }

            // Get already played tracks.
            var playlist = _playlists[context];

            var unPlayed = tracks.Where(x => playlist.Contains(x.Id)).ToList();
            // Clear if all songs have been played.
            if (!unPlayed.Any())
            {
                playlist.Clear();
                unPlayed = tracks;
            }

            // Get one random
            var random = unPlayed[RandomUtil.GetRandom(0, Math.Max(0, unPlayed.Count - 1))];
            playlist.Add(random.Id);

            return random;
        }

        public void Insert(MusicContextEntity track) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);
                var tracks = db.GetCollection<MusicContextEntity>("music_contexts");
                tracks.Insert(track);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public void Remove(Guid id) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);
                var tracks = db.GetCollection<MusicContextEntity>("music_contexts");
                tracks.DeleteMany(x => x.Id.Equals(id));
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public List<MusicContextEntity> Search(Expression<Func<MusicContextEntity, bool>> expr) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);
                var tracks = db.GetCollection<MusicContextEntity>("music_contexts");
                return tracks.Query().Where(expr).ToList();
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public void Update(MusicContextEntity track) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);
                var tracks = db.GetCollection<MusicContextEntity>("music_contexts");
                tracks.Update(track);
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
    }
}
