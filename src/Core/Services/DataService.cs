﻿using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using LiteDB;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Properties;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private const string TBL_PLAYLISTS        = "playlists";
        public const  string TBL_AUDIO_SOURCES    = "audio_sources";
        private const string TBL_THUMBNAILS       = "thumbnails";
        private const string TBL_THUMBNAIL_CHUNKS = "thumbnail_chunks";
        private const string LITEDB_FILENAME      = "music.db";

        public DataService() {
            _connectionString = new ConnectionString {
                Filename   = Path.Combine(MusicMixer.Instance.ModuleDirectory, LITEDB_FILENAME),
                Connection = ConnectionType.Shared
            };
        }

        public string ExportToJson() {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);
                var collection = db.GetCollection<Playlist>(TBL_PLAYLISTS).Include(x => x.Tracks).FindAll();
                var tracklists = collection.Select(playlist => new Tracklist {
                                           ExternalId = playlist.ExternalId,
                                           Tracks = playlist.Tracks.Where(x => !string.IsNullOrEmpty(x.PageUrl))
                                                            .Select(x => new Tracklist.Track {
                                                                 Title    = x.Title,
                                                                 Url      = x.PageUrl,
                                                                 DayCycle = (int)x.DayCycles,
                                                                 Duration = x.Duration
                                                             }).ToList()}).ToList();
                tracklists.RemoveAll(x => !x.Tracks.Any());
                return JsonConvert.SerializeObject(tracklists);

            } catch (Exception e) {

                MusicMixer.Logger.Warn(e, e.Message);
                ScreenNotification.ShowNotification(Resources.Something_went_wrong__Please_try_again_);

                return string.Empty;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public void LoadTracklist(Tracklist list, ProgressTotal progress) {
            Playlist playlist = GetPlaylist(list.ExternalId) ?? new Playlist {
                ExternalId = list.ExternalId,
                Enabled    = true,
                Tracks     = new List<AudioSource>()
            };

            // Create sources from default list.
            var defaults = list.Tracks.Select(track => {
                if (!MusicMixer.Instance.YtDlp.GetYouTubeVideoId(track.Url, out string externalId)) {
                    return AudioSource.Empty;
                };

                return new AudioSource {
                    ExternalId = externalId,
                    Title      = track.Title,
                    PageUrl    = track.Url,
                    Duration   = track.Duration,
                    Volume     = 1,
                    DayCycles = Enum.IsDefined(typeof(AudioSource.DayCycle), track.DayCycle)
                                    ? (AudioSource.DayCycle)track.DayCycle
                                    : AudioSource.DayCycle.Day | AudioSource.DayCycle.Night,
                    Default    = true
                };
            }).Where(src => !src.IsEmpty);

            // Remove DB entries that were automatically imported in the past but are no longer part of the default list.
            var removed = playlist.Tracks.Where(x => x.Default.HasValue && x.Default.Value
                                                                         && !string.IsNullOrEmpty(x.ExternalId)
                                                                         && !defaults.Any(y => y.ExternalId.Equals(x.ExternalId)));
            foreach (var src in removed) {
                this.Remove(src); // Sadly LiteDB has problems converting complex Linq expressions to BsonExpressions so DeleteMany wouldn't work.
            }
            
            // Import new default tracks.
            foreach (var track in defaults) {
                progress?.Report(track.Title, true);
                if (playlist.Tracks.Any(y => !string.IsNullOrEmpty(y.ExternalId) && y.ExternalId.Equals(track.ExternalId))) {
                    continue;
                }
                Upsert(track);
                playlist.Tracks.Add(track);
            }
            Upsert(playlist);
        }

        public AsyncTexture2D GetThumbnail(AudioSource source) {
            LiteFileStream<string> stream = null;

            var texture = new AsyncTexture2D();

            if (string.IsNullOrWhiteSpace(source.ExternalId)) {
                return texture;
            }

            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var thumbnails = db.GetStorage<string>(TBL_THUMBNAILS, TBL_THUMBNAIL_CHUNKS);

                if (thumbnails.Exists(source.ExternalId)) {
                    stream = thumbnails.OpenRead(source.ExternalId);
                }

            } catch (Exception e) {

                MusicMixer.Logger.Warn(e, e.Message);

            } finally {
                this.ReleaseWriteLock();
            }

            if (stream == null) {

                if (string.IsNullOrWhiteSpace(source.PageUrl)) {
                    return texture;
                }

                // Thumbnail not cached, request it.
                MusicMixer.Instance.YtDlp.GetThumbnail(source.PageUrl, thumbnailUri => ThumbnailUrlReceived(source.ExternalId, thumbnailUri, texture));
                return texture;
            }

            try {
                using var gdx = GameService.Graphics.LendGraphicsDeviceContext();
                texture.SwapTexture(Texture2D.FromStream(gdx.GraphicsDevice, stream));
                return texture;
            } catch (Exception e) {
                // Unsupported image format.
                MusicMixer.Logger.Info(e,e.Message);
            }

            stream.Dispose();
            return texture;
        }

        public bool GetTrackByMediaId(string mediaId, out AudioSource source) {
            if (string.IsNullOrEmpty(mediaId)) {
                source = AudioSource.Empty;
                return false;
            }
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<AudioSource>(TBL_AUDIO_SOURCES);
                source = collection.FindOne(x => mediaId.Equals(x.ExternalId));
                return source != null;
            } finally {
                this.ReleaseWriteLock();
            }
        }

        private Playlist GetPlaylist(string externalId) {
            this.AcquireWriteLock();
            try {
                using var db = new LiteDatabase(_connectionString);

                var collection = db.GetCollection<Playlist>(TBL_PLAYLISTS);
                return collection.Include(x => x.Tracks).FindOne(x => x.ExternalId == externalId);
            } finally {
                this.ReleaseWriteLock();
            }
        }

        public Playlist GetMountPlaylist(MountType mountType) {
            return GetPlaylist(mountType.ToString());
        }

        public Playlist GetDefeatedPlaylist() {
            return GetPlaylist("Defeated");
        }

        public Playlist GetMapPlaylist(int mapId) {
            return GetPlaylist($"map_{mapId}");
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
                    Default    = src.Default,
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

            client.OpenReadCompleted += (_, e) => {

                using var stream = e.Result;

                try {
                    if (e.Cancelled) {
                        return;
                    }

                    if (e.Error != null) {
                        throw e.Error;
                    }

                    if (stream == null) {
                        return;
                    }

                    using var image = Image.Load(stream);
                    using var ms    = new MemoryStream();
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

                } catch (Exception ex) {
                    // WebException or ImageFormatException or ArgumentException or InvalidOperationException
                    MusicMixer.Logger.Info(ex, ex.Message);
                } finally {
                    client.Dispose();
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
