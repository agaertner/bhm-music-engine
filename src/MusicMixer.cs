using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Flurl.Http;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core;
using Nekres.Music_Mixer.Core.Services;
using Nekres.Music_Mixer.Core.Services.Audio;
using Nekres.Music_Mixer.Core.Services.Data;
using Nekres.Music_Mixer.Core.UI.Library;
using Nekres.Music_Mixer.Core.UI.Playlists;
using Nekres.Music_Mixer.Core.UI.Settings;
using Nekres.Music_Mixer.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer {

    [Export(typeof(Module))]
    public class MusicMixer : Module
    {
        internal static readonly Logger Logger = Logger.GetLogger(typeof(MusicMixer));

        internal static MusicMixer Instance;

        #region Service Managers
        internal SettingsManager SettingsManager => this.ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;

        #endregion

        internal SettingEntry<ModuleConfig> ModuleConfig;
        internal SettingEntry<bool>         InitialLoad;

        public string ModuleDirectory { get; private set; }

        internal YtDlpService    YtDlp;
        internal AudioService    Audio;
        internal DataService     Data;
        internal Gw2StateService Gw2State;

        private TabbedWindow2 _moduleWindow;
        private CornerIcon    _cornerIcon;
        private ProgressTotal _moduleProgress;
        private Tab           _settingsTab;

        // Textures
        private Texture2D _cornerTexture;
        private Texture2D _mountTabIcon;
        private Texture2D _defeatedIcon;

        private  float _prevMasterVol;
        internal float MasterVolume => ModuleConfig.Value == null ? _prevMasterVol : _prevMasterVol = ModuleConfig.Value.MasterVolume;

        private const string DEFAULT_MUSIC_URL = "https://github.com/agaertner/bhm-music-engine/raw/main/default_music.json";

        [ImportingConstructor]
        public MusicMixer([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { Instance = this; }

        protected override void DefineSettings(SettingCollection settings) {
            settings.RenderInUi = false; // Does not work on root settings collection. Replacing entire view instead (see below).
            ModuleConfig        = settings.DefineSetting("module_config", Core.UI.Settings.ModuleConfig.Default);
            InitialLoad         = settings.DefineSetting("initial_load",  true);
        }

        public override IView GetSettingsView() {
            return new CustomSettingsView();
        }

        protected override void Initialize() {
            ModuleDirectory  = DirectoriesManager.GetFullDirectoryPath("music_mixer");

            YtDlp    = new YtDlpService();
            Data     = new DataService();
            Gw2State = new Gw2StateService();
            Audio    = new AudioService();

            _cornerTexture = ContentsManager.GetTexture("corner_icon.png");
            _cornerIcon = new CornerIcon {
                Icon = _cornerTexture
            };
        }

        protected override void Update(GameTime gameTime) {
            this.Gw2State.Update();
            this.Audio.AudioTrack?.Invalidate();
            if (ModuleConfig.Value.Paused) {
                this.Audio.Pause();
            }
        }

        public ProgressTotal GetModuleProgressHandler() {
            return _moduleProgress ??= new ProgressTotal(UpdateModuleLoading);
        }

        private void UpdateModuleLoading(string loadingMessage) {
            if (_cornerIcon == null) {
                return;
            }

            _cornerIcon.LoadingMessage = loadingMessage;
        }

        protected override async Task LoadAsync() {
            var progress = GetModuleProgressHandler();
            await YtDlp.Update(progress);

            if (InitialLoad.Value) {
                ScreenNotification.ShowNotification($"{Resources.New_database_created_} {Resources.Importing_default_playlists_}");
                var defaultMusic = await DEFAULT_MUSIC_URL.GetJsonAsync<List<Tracklist>>();
                if (defaultMusic == null) {
                    progress.Report(null);
                    return;
                }
                progress.Total = defaultMusic.SelectMany(x => x.Tracks).Count();
                foreach (var tracklist in defaultMusic) {
                    await Data.LoadTracklist(tracklist, progress);
                }
                progress.Report(null);
                InitialLoad.Value = false;
            }
        }

        protected override void OnModuleLoaded(EventArgs e) {

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Clearing expired download urls..
            YtDlp.RemoveCache();    // ..from cache.
            Data.RemoveAudioUrls(); // ..from database.

            ModuleConfig.Value.MasterVolume = MathHelper.Clamp(ModuleConfig.Value.MasterVolume, 0f, 2f);

            
            var windowRegion = new Rectangle(40, 26, 913, 691);
            _moduleWindow = new TabbedWindow2(GameService.Content.DatAssetCache.GetTextureFromAssetId(155985),
                                              windowRegion,
                                              new Rectangle(100, 36, 839, 605)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = Resources.Background_Music,
                Emblem        = _cornerTexture,
                Subtitle      = Resources.Mounted,
                SavesPosition = true,
                Id            = $"{nameof(MusicMixer)}_d42b52ce-74f1-4e6d-ae6b-a8724029f0a3",
                Left          = (GameService.Graphics.SpriteScreen.Width  - windowRegion.Width) / 2,
                Top           = (GameService.Graphics.SpriteScreen.Height - windowRegion.Height)  / 2
            };

            _mountTabIcon = ContentsManager.GetTexture("tabs/raptor.png");
            var mountTab = new Tab(_mountTabIcon, () => new NpLibraryWrapperView(new MountPlaylistsView()), Resources.Mounted);
            _moduleWindow.Tabs.Add(mountTab);

            _defeatedIcon = ContentsManager.GetTexture("tabs/downed_enemy.png");
            var defeatedTab = new Tab(_defeatedIcon, () => {
                if (!Data.GetDefeatedPlaylist(out var context)) {
                    context = new Playlist {
                        ExternalId = "Defeated",
                        Tracks     = new List<AudioSource>()
                    };
                }
                return new NpLibraryWrapperView(new BgmLibraryView(context, Resources.Defeated));
            }, Resources.Defeated);
            _moduleWindow.Tabs.Add(defeatedTab);

            _settingsTab = new Tab(GameService.Content.DatAssetCache.GetTextureFromAssetId(155052), () => new ModuleSettingsView(this.ModuleConfig.Value), Resources.Settings);
            _moduleWindow.Tabs.Add(_settingsTab);

            _cornerIcon.LeftMouseButtonReleased += OnModuleIconClick;

            _moduleWindow.TabChanged += OnTabChanged;

            // Base handler must be called
            base.OnModuleLoaded(e);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) {
            Audio?.Dispose();
        }

        private void OnTabChanged(object sender, ValueChangedEventArgs<Tab> e) {
            _moduleWindow.Subtitle = e.NewValue.Name;
        }

        private void OnModuleIconClick(object o, MouseEventArgs e) {
            _moduleWindow?.ToggleWindow();
        }

        internal void ShowSettings() {
            if (_moduleWindow == null) {
                return;
            }
            _moduleWindow.Show();
            _moduleWindow.SelectedTab = _settingsTab;
        }

        /// <inheritdoc />
        protected override void Unload()
        {
            if (_moduleWindow != null) {
                _moduleWindow.TabChanged -= OnTabChanged;
                _moduleWindow.Dispose();
            }

            if (_cornerIcon != null) {
                _cornerIcon.LeftMouseButtonReleased -= OnModuleIconClick;
                _cornerIcon.Dispose();
            }

            // Dispose services
            Audio?.Dispose();
            Gw2State?.Dispose();
            Data?.Dispose();

            _cornerTexture?.Dispose();
            _defeatedIcon?.Dispose();
            _mountTabIcon?.Dispose();

            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

            // All static members must be manually unset
            Instance = null;
        }
    }
}
