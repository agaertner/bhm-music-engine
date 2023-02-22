using Blish_HUD;
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
using Gw2Sharp.WebApi.V2;
using Gw2Sharp.WebApi.V2.Models;

namespace Nekres.Music_Mixer.Core.Services
{
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
#if DEBUG
            var notFound = new List<(int, ContinentFloorRegionMap)>();
#endif
            IApiV2ObjectList<int> continentIds;

            try {
                continentIds = await GameService.Gw2WebApi.AnonymousConnection.Client.V2.Continents.IdsAsync();
            } catch (RequestException e) {
                MusicMixer.Logger.Error(e, e.Message);
                return;
            }

            if (!continentIds.Any()) {
                return;
            }

            var allRegionNames = new Dictionary<int, string>();
            var allRegionMaps = new Dictionary<int, IEnumerable<int>>();
            var allMapNames = new Dictionary<int, string>();
            foreach (var continentId in continentIds) {

                IApiV2ObjectList<int> floorIds;

                try {
                    floorIds = await GameService.Gw2WebApi.AnonymousConnection.Client.V2.Continents[continentId].Floors.IdsAsync();
                } catch (RequestException e) {
                    MusicMixer.Logger.Error(e, e.Message);
                    break;
                }

                if (!floorIds.Any()) {
                    continue;
                }

                // Crawl each floor to get all maps...
                var   regions = new List<int>();
                float i = 0;
                foreach (var floorId in floorIds)
                {
                    _loadingIndicator.Report($"Loading.. {Math.Round(i++ / floorIds.Count * 100)}%");

                    IApiV2ObjectList<ContinentFloorRegion> floorRegions;

                    try {
                        floorRegions = await GameService.Gw2WebApi.AnonymousConnection.Client.V2
                                                        .Continents[continentId]
                                                        .Floors[floorId]
                                                        .Regions.AllAsync();
                    } catch (NotFoundException) {
                        continue; // Ignore. Floor id does not exist somehow...
                    } catch (RequestException e) {
                        MusicMixer.Logger.Error(e, e.Message);
                        break;
                    }

                    if (!floorRegions.Any()) {
                        continue;
                    }

                    var mapsByRegion = floorRegions.ToDictionary(x => x.Id, x => x.Maps.Select(y => y.Value));

                    foreach (var region in mapsByRegion) {
                        if (regions.All(x => x != region.Key)) {
                            regions.Add(region.Key);
                        }

                        var regionName = floorRegions.First(x => x.Id == region.Key).Name;

                        var validMaps = region.Value.Where(x => mapsLookUp.Values.Any(id => x.Id == id)).ToList();
                        foreach (var map in validMaps) {
                            if (allMapNames.ContainsKey(map.Id)) {
                                continue;
                            }

                            allMapNames.Add(map.Id, map.Name);
                        }

                        #if DEBUG
                        // Helping out Discord RPC (https://github.com/OpNop/GW2-RPC-Resources) to gather unresolved maps..
                        notFound.AddRange(region.Value
                                                .Where(x => !mapsLookUp.ContainsKey(MapUtil.GetSHA1(continentId, x.ContinentRect)))
                                                .Select(x => (continentId, x)));
                        #endif

                        var publicMapIds = region.Value.Select(x => MapUtil.GetSHA1(continentId, x.ContinentRect)).Where(x => mapsLookUp.ContainsKey(x)).Select(x => mapsLookUp[x]).Distinct();

                        if (allRegionMaps.ContainsKey(region.Key)) {
                            // Maps from different floors have to be merged in.
                            allRegionMaps[region.Key] = allRegionMaps[region.Key].Union(publicMapIds);
                        } else {
                            // Add region if it wasn't yet.
                            allRegionMaps.Add(region.Key, publicMapIds);
                            allRegionNames.Add(region.Key, regionName);
                        }
                    }
                }
                _continentRegions.Add(continentId, regions);
            }
            _regionMaps  = allRegionMaps;
            _regionNames = allRegionNames;
            _mapNames    = allMapNames;

            #if DEBUG
            System.IO.File.WriteAllLines(System.IO.Path.Combine(MusicMixer.Instance.ModuleDirectory, "unresolved_maps.txt"), 
                                         notFound.DistinctBy(x => MapUtil.GetSHA1(x.Item1, x.Item2.ContinentRect))
                                                 .Select(x => $"\"{MapUtil.GetSHA1(x.Item1, x.Item2.ContinentRect)}\": {x.Item2.Id}, // {x.Item2.Name} ({x.Item2.Id})"));
            #endif
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
