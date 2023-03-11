using Blish_HUD;
using Blish_HUD.Extended;
using Blish_HUD.Modules.Managers;
using Gw2Sharp.WebApi.Exceptions;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services {
    internal class MapService : IDisposable
    {
        private Dictionary<int, IEnumerable<int>> _continentRegions;

        private Dictionary<int, IEnumerable<int>> _regionMaps;

        private Dictionary<int, string> _mapNames;

        private Dictionary<int, string> _regionNames;

        private readonly IProgress<string> _loadingIndicator;

        public bool IsLoading { get; private set; }

        private ContentsManager _ctnMgr;

        public MapService(ContentsManager ctnMgr, IProgress<string> loadingIndicator)
        {
            _ctnMgr = ctnMgr;
            _loadingIndicator = loadingIndicator;
            _continentRegions = new Dictionary<int, IEnumerable<int>>();
            _regionMaps = new Dictionary<int, IEnumerable<int>>();
            _regionNames = new Dictionary<int, string>();
            _mapNames = new Dictionary<int, string>();
        }

        public IEnumerable<int> GetRegionsForContinent(int continentId)
        {
            return _continentRegions.TryGetValue(continentId, out var regions) ? regions.ToList() : Enumerable.Empty<int>();
        }

        public bool RegionHasMaps(int regionId)
        {
            return _regionMaps.TryGetValue(regionId, out var maps) && maps.Any();
        }

        public IEnumerable<int> GetMapsForRegion(int regionId)
        {
            return _regionMaps.TryGetValue(regionId, out var maps) ? maps.ToList() : Enumerable.Empty<int>();
        }

        public string GetRegionName(int regionId)
        {
            return _regionNames.TryGetValue(regionId, out var name) ? name : string.Empty;
        }

        public Texture2D GetMapThumb(int mapId)
        {
            return _ctnMgr.GetTexture($"regions/maps/map_{mapId}.jpg");
        }

        public string GetMapName(int mapId)
        {
            return _mapNames.ContainsKey(mapId) ? _mapNames[mapId] : string.Empty;
        }

        public Dictionary<int, string> GetAllMaps()
        {
            return new Dictionary<int, string>(_mapNames);
        }

        public void DownloadRegions()
        {
            var thread = new Thread(LoadRegionsInBackground)
            {
                IsBackground = true
            };
            thread.Start();
        }

        private void LoadRegionsInBackground()
        {
            this.IsLoading = true;
            this.RequestRegions().Wait();
            this.IsLoading = false;
            _loadingIndicator.Report(null);
        }

        private async Task RequestRegions()
        {
            var mapsLookUp = await LoadMapLookUp();
            var continents = await RetryAsync(() => GameService.Gw2WebApi.AnonymousConnection.Client.V2.Continents.AllAsync()).Unwrap();

            if (continents == default || !continents.Any()) {
                return;
            }

            var allRegionNames   = new Dictionary<int, string>();
            var allRegionMaps    = new Dictionary<int, IEnumerable<int>>();
            var allMapNames      = new Dictionary<int, string>();

            int totalMaps = 0;
            int progress  = 0;
            foreach (var continent in continents) {

                // Crawl each floor to get all maps...
                var floors = await RetryAsync(() => GameService.Gw2WebApi.AnonymousConnection.Client.V2
                                                                .Continents[continent.Id]
                                                                .Floors.AllAsync()).Unwrap();

                if (floors == default || !floors.Any()) {
                    continue;
                }

                var regions = floors.SelectMany(x => x.Regions.Values).ToList();

                foreach (var region in regions) {
                    
                    var validMaps = region.Maps.Values.Where(x => 
                                                                 mapsLookUp.Values.Any(id => x.Id == id) && !allMapNames.ContainsKey(x.Id)).ToList();

                    var newTotal = Interlocked.Add(ref totalMaps, validMaps.Count);

                    foreach (var map in validMaps) {
                        // Report progress
                        var newProgress = Interlocked.Increment(ref progress);
                        _loadingIndicator.Report($"Loading... {map.Name} ({Math.Round(newProgress * 100f / newTotal)}%)");

                        allMapNames.Add(map.Id, map.Name);
                    }

                    var publicMapIds = region.Maps.Values.Select(x => MapUtil.GetSHA1(continent.Id, 
                                                                                (int)x.ContinentRect.TopLeft.X, 
                                                                                (int)x.ContinentRect.TopLeft.Y, 
                                                                                (int)x.ContinentRect.BottomRight.X,
                                                                                (int)x.ContinentRect.BottomRight.Y)).Where(x => mapsLookUp.ContainsKey(x)).Select(x => mapsLookUp[x]).Distinct();

                    if (allRegionMaps.ContainsKey(region.Id)) {
                        // Maps from different floors have to be merged in.
                        allRegionMaps[region.Id] = allRegionMaps[region.Id].Union(publicMapIds);
                    } else {
                        // Add region if it wasn't yet.
                        allRegionMaps.Add(region.Id, publicMapIds);
                        allRegionNames.Add(region.Id, region.Name);
                    }
                }

                _continentRegions.Add(continent.Id, regions.Select(x => x.Id));
            }
            _regionMaps  = allRegionMaps;
            _regionNames = allRegionNames;
            _mapNames    = allMapNames;
        }

        private async Task<T> RetryAsync<T>(Func<T> func, int retries = 2, int delayMs = 30000) {
            try {
                return func();
            } catch (Exception e) {

                // Do not retry if requested resource does not exist or access is denied.
                if (e is NotFoundException or BadRequestException or AuthorizationRequiredException) { 
                    MusicMixer.Logger.Debug(e, e.Message);
                    return default; 
                }

                if (retries > 0) {
                    MusicMixer.Logger.Warn(e, $"Failed to pull data from the GW2 API. Retrying in 30 seconds (remaining retries: {retries}).");
                    await Task.Delay(delayMs);
                    return await RetryAsync(func, retries - 1);
                }

                switch (e) {
                    case TooManyRequestsException:
                        MusicMixer.Logger.Warn(e, "After multiple attempts no data could be loaded due to being rate limited by the API.");
                        break;
                    case RequestException or RequestException<string>:
                        MusicMixer.Logger.Debug(e, e.Message);
                        break;
                    default:
                        MusicMixer.Logger.Error(e, e.Message);
                        break;
                }

                return default;
            }
        }

        private async Task<Dictionary<string, int>> LoadMapLookUp()
        {
            using var stream = _ctnMgr.GetFileStream("regions/maps/maps.jsonc");
            stream.Position = 0;
            var buffer = new byte[stream.Length];
            await stream.ReadAsync(buffer, 0, buffer.Length);
            return JsonConvert.DeserializeObject<Dictionary<string, int>>(Encoding.UTF8.GetString(buffer));
        }

        public void Dispose()
        {
        }
    }
}
