using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nekres.Music_Mixer.Core.Services;
using Nekres.Music_Mixer.Core.Services.Audio;
using System;
using System.ComponentModel.Composition;
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

        internal SettingEntry<float>                     MasterVolumeSetting;
        internal SettingEntry<bool>                      ToggleMountedPlaylistSetting;
        internal SettingEntry<bool>                      ToggleFourDayCycleSetting;
        internal SettingEntry<YtDlpService.AudioBitrate> AverageBitrateSetting;
        internal SettingEntry<bool>                      MuteWhenInBackgroundSetting;

        public string ModuleDirectory { get; private set; }

        internal YtDlpService    YtDlp;
        internal AudioService    Audio;
        internal DataService     Data;
        internal Gw2StateService Gw2State;

        private TabbedWindow2 _moduleWindow;
        private CornerIcon _cornerIcon;

        // Textures
        private Texture2D _cornerTexture;

        [ImportingConstructor]
        public MusicMixer([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters) { Instance = this; }

        protected override void DefineSettings(SettingCollection settings) {
            MasterVolumeSetting = settings.DefineSetting("master_volume", 50f, 
                () => "Master Volume", 
                () => "Sets the audio volume.");

            MuteWhenInBackgroundSetting = settings.DefineSetting("mute_in_background", false,
                () => "Mute when GW2 is in the background");

            ToggleMountedPlaylistSetting = settings.DefineSetting("enable_mounted_playlist", true, 
                () => "Use mounted playlist", 
                () => "Whether songs of the mounted playlist should be played while mounted.");
            
            ToggleFourDayCycleSetting = settings.DefineSetting("enable_four_day_cycle", false, 
                () => "Use dusk and dawn day cycles", 
                () => "Whether dusk and dawn track attributes should be interpreted as unique day cycles.\nOtherwise dusk and dawn will be interpreted as night and day respectively.");
            
            AverageBitrateSetting = settings.DefineSetting("average_bitrate", YtDlpService.AudioBitrate.B320, 
                () => "Average bitrate limit", 
                () => "Sets the average bitrate of the audio used in streaming.");
        }

        protected override void Initialize()
        {
            ModuleDirectory = DirectoriesManager.GetFullDirectoryPath("music_mixer");

            YtDlp    = new YtDlpService();
            Data     = new DataService();
            Gw2State = new Gw2StateService();
            Audio    = new AudioService();
        }

        protected override void Update(GameTime gameTime) {
            this.Gw2State.Update();
        }

        public IProgress<string> GetModuleProgressHandler() {
            return new Progress<string>(UpdateModuleLoading);
        }

        private void UpdateModuleLoading(string loadingMessage) {
            if (_cornerIcon == null) {
                return;
            }

            _cornerIcon.LoadingMessage = loadingMessage;
        }

        protected override async Task LoadAsync() {
            await YtDlp.Update(GetModuleProgressHandler());
        }

        protected override void OnModuleLoaded(EventArgs e) {
            MasterVolumeSetting.Value = MathHelper.Clamp(MasterVolumeSetting.Value, 0f, 100f);

            _cornerTexture = ContentsManager.GetTexture("corner_icon.png");
            var windowRegion = new Rectangle(40, 26, 913, 691);
            _moduleWindow = new TabbedWindow2(GameService.Content.DatAssetCache.GetTextureFromAssetId(155985),
                                              windowRegion,
                                              new Rectangle(70, 71, 839, 605)) {
                Parent        = GameService.Graphics.SpriteScreen,
                Title         = "Background Music",
                Emblem        = _cornerTexture,
                Subtitle      = this.Name,
                SavesPosition = true,
                Id            = $"{nameof(MusicMixer)}_d42b52ce-74f1-4e6d-ae6b-a8724029f0a3",
                Left          = (GameService.Graphics.SpriteScreen.Width  - windowRegion.Width) / 2,
                Top           = (GameService.Graphics.SpriteScreen.Height - windowRegion.Height)  / 2
            };
            
            _cornerIcon = new CornerIcon
            {
                Icon = _cornerTexture
            };
            _cornerIcon.LeftMouseButtonReleased += OnModuleIconClick;

            // Base handler must be called
            base.OnModuleLoaded(e);
        }

        public void OnModuleIconClick(object o, MouseEventArgs e)
        {
            _moduleWindow?.ToggleWindow();
        }

        /// <inheritdoc />
        protected override void Unload()
        {
            _moduleWindow?.Dispose();
            if (_cornerIcon != null)
            {
                _cornerIcon.LeftMouseButtonReleased -= OnModuleIconClick;
                _cornerIcon.Dispose();
            }

            // Dispose services
            Audio?.Dispose();
            Gw2State?.Dispose();
            Data?.Dispose();

            _cornerTexture?.Dispose();

            // Reset GW2 volume
            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) {
                AudioUtil.SetVolume(GameService.GameIntegration.Gw2Instance.Gw2Process.Id, 1);
            }

            // All static members must be manually unset
            Instance = null;
        }
    }
}
