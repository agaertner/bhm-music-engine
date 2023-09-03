using Blish_HUD;
using Blish_HUD.Extended;
using Gw2Sharp.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using static Blish_HUD.GameService;
namespace Nekres.Music_Mixer.Core.Services {
    public class Gw2StateService : IDisposable
    {
        public event EventHandler<ValueChangedEventArgs<State>> StateChanged;
        public event EventHandler<ValueEventArgs<TyrianTime>>   TyrianTimeChanged;
        public event EventHandler<ValueEventArgs<bool>>         IsSubmergedChanged;

        public enum State
        {
            StandBy, // silence
            Mounted,
            Battle,
            Competitive,
            Defeated
        }

        #region Public Fields

        private TyrianTime _prevTyrianTime = TyrianTime.NONE;
        public TyrianTime TyrianTime { 
            get => _prevTyrianTime;
            private set {
                if (_prevTyrianTime == value) {
                    return;
                }

                _prevTyrianTime = value;
                TyrianTimeChanged?.Invoke(this, new ValueEventArgs<TyrianTime>(value));
            }
        }

        private bool _prevIsSubmerged = Gw2Mumble.PlayerCamera.Position.Z <= 0; // for character pos: < -1.25f;
        public bool IsSubmerged {
            get => _prevIsSubmerged; 
            private set {
                if (_prevIsSubmerged == value) {
                    return;
                }

                _prevIsSubmerged = value;
                IsSubmergedChanged?.Invoke(this, new ValueEventArgs<bool>(value));
            }
        }

        private State _currentState = State.StandBy;
        public State CurrentState {
            get => _currentState;
            set {
                if (_currentState == value) {
                    return;
                }
                StateChanged?.Invoke(this, new ValueChangedEventArgs<State>(_currentState, value));
                _currentState = value;
            }
        }

        #endregion

        private readonly NTimer _inCombatTimer;
        private readonly NTimer _outOfCombatTimer;
        private readonly NTimer _outOfCombatTimerLong;

        private string   _defeatedLockFile  = Path.Combine(DirectoryUtil.MusicPath, "Defeated.wav");
        private DateTime _lastLockFileCheck = DateTime.UtcNow.AddSeconds(10);
        public Gw2StateService() {

            _inCombatTimer = new NTimer(6500) { AutoReset = false };
            _inCombatTimer.Elapsed += InCombatTimerElapsed;
            _outOfCombatTimer = new NTimer(3250) { AutoReset = false };
            _outOfCombatTimer.Elapsed += OutOfCombatTimerElapsed;
            _outOfCombatTimerLong = new NTimer(20250) { AutoReset = false };
            _outOfCombatTimerLong.Elapsed += OutOfCombatTimerElapsed;
            Initialize();
        }

        public async Task SetupLockedAudioFileHack() {
            await SetupLockFiles(_defeatedLockFile);
        }

        private async Task SetupLockFiles(string lockFile) {
            await MusicMixer.Instance.ContentsManager.Extract("audio/silence.wav", lockFile, false);
            try {
                using var file = File.Create(Path.Combine(DirectoryUtil.MusicPath, $"{Path.GetFileNameWithoutExtension(lockFile)}.m3u"));
                file.Position = 0;
                var name    = Path.GetFileName(lockFile);
                var content = $"#EXTM3U\r\n#EXTINF:5,{name}\r\n{name}\r\n".GetBytes();
                await file.WriteAsync(content, 0, content.Length);
            } catch (IOException e) {
                MusicMixer.Logger.Warn(e, e.Message);
            }
        }

        private void InCombatTimerElapsed(object sender, EventArgs e) {
            this.CurrentState = State.Battle;
        }

        private void OutOfCombatTimerElapsed(object sender, EventArgs e) {
            this.CurrentState = State.StandBy;
            
        }

        public void Dispose() {
            _inCombatTimer.Elapsed -= InCombatTimerElapsed;
            _inCombatTimer?.Dispose();
            _outOfCombatTimer.Elapsed -= OutOfCombatTimerElapsed;
            _outOfCombatTimer?.Dispose();
            _outOfCombatTimerLong.Elapsed -= OutOfCombatTimerElapsed;
            _outOfCombatTimerLong?.Dispose();
            GameIntegration.Gw2Instance.Gw2Closed         -= OnGw2Closed;
            Gw2Mumble.PlayerCharacter.CurrentMountChanged -= OnMountChanged;
            Gw2Mumble.CurrentMap.MapChanged               -= OnMapChanged;
            Gw2Mumble.PlayerCharacter.IsInCombatChanged   -= OnIsInCombatChanged;
        }

        private void Initialize() {
            Gw2Mumble.PlayerCharacter.CurrentMountChanged += OnMountChanged;
            Gw2Mumble.CurrentMap.MapChanged += OnMapChanged;
            Gw2Mumble.PlayerCharacter.IsInCombatChanged += OnIsInCombatChanged;
            GameIntegration.Gw2Instance.Gw2Closed += OnGw2Closed;
        }

        public void Update() {
            CheckTyrianTime();
            CheckWaterLevel();

            if (DateTime.UtcNow.Subtract(_lastLockFileCheck).TotalMilliseconds > 200) {
                _lastLockFileCheck = DateTime.UtcNow;
                if (FileUtil.IsFileLocked(_defeatedLockFile)) {
                    this.CurrentState = State.Defeated;
                } else if (this.CurrentState == State.Defeated) {
                    this.CurrentState = State.StandBy;
                }
            }
        }

        private void CheckWaterLevel() => IsSubmerged = Gw2Mumble.PlayerCamera.Position.Z <= 0;
        private void CheckTyrianTime() => TyrianTime = TyrianTimeUtil.GetCurrentDayCycle();

        private void OnGw2Closed(object sender, EventArgs e) {
            StateChanged?.Invoke(this, new ValueChangedEventArgs<State>(CurrentState, State.StandBy));
        }

        #region Mumble Events

        private void OnMountChanged(object o, ValueEventArgs<MountType> e)
        {
            if (!MusicMixer.Instance.ToggleMountedPlaylist.Value) {
                return;
            }

            if (_outOfCombatTimer.IsRunning || _outOfCombatTimerLong.IsRunning) {
                return;
            }

            this.CurrentState = e.Value > 0 ? State.Mounted : State.StandBy;
        }

        private void OnMapChanged(object o, ValueEventArgs<int> e) {
            this.CurrentState = State.StandBy;
            _outOfCombatTimer.Stop();
            _outOfCombatTimerLong.Stop();
            _inCombatTimer.Stop();
        }

        private void OnIsInCombatChanged(object o, ValueEventArgs<bool> e) {
            if (e.Value)
            {
                _inCombatTimer.Restart();
            }
            else if (CurrentState == State.Battle)
            {
                if (Gw2Mumble.CurrentMap.Type.IsInstance() || Gw2Mumble.CurrentMap.Type.IsWvW() || Gw2Mumble.CurrentMap.Type == MapType.PublicMini)
                {
                    _outOfCombatTimerLong.Restart();
                }
                else
                {
                    _outOfCombatTimer.Restart();
                }
            }
            else
            {
                _inCombatTimer.Stop();
            }
        }

        #endregion
    }
}
