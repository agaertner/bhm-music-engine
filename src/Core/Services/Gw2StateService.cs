using Blish_HUD;
using Blish_HUD.Extended;
using Gw2Sharp.Models;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Blish_HUD.Controls;

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

        private bool _prevIsSubmerged = GameService.Gw2Mumble.PlayerCamera.Position.Z <= 0; // for character pos: < -1.25f;
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

        private string   _lockFile          = "silence.wav";
        private DateTime _lastLockFileCheck = DateTime.UtcNow.AddSeconds(10);
        public Gw2StateService() {

            _inCombatTimer                =  new NTimer(6500) { AutoReset = false };
            _inCombatTimer.Elapsed        += InCombatTimerElapsed;
            _outOfCombatTimer             =  new NTimer(3250) { AutoReset = false };
            _outOfCombatTimer.Elapsed     += OutOfCombatTimerElapsed;
            _outOfCombatTimerLong         =  new NTimer(20250) { AutoReset = false };
            _outOfCombatTimerLong.Elapsed += OutOfCombatTimerElapsed;

            GameService.Gw2Mumble.PlayerCharacter.CurrentMountChanged += OnMountChanged;
            GameService.Gw2Mumble.CurrentMap.MapChanged               += OnMapChanged;
            GameService.Gw2Mumble.PlayerCharacter.IsInCombatChanged   += OnIsInCombatChanged;
            GameService.GameIntegration.Gw2Instance.Gw2Closed         += OnGw2Closed;

            MusicMixer.Instance.ToggleDefeatedPlaylist.SettingChanged += OnToggleDefeatedPlaylistChanged;
        }

        private async void OnToggleDefeatedPlaylistChanged(object sender, ValueChangedEventArgs<bool> e) {
            if (e.NewValue) {
                await SetupLockFiles(State.Defeated);
                ScreenNotification.ShowNotification("Defeated Music requires game restart.");
                GameService.Content.PlaySoundEffectByName("color-change");
            }
        }

        public async Task SetupLockedAudioFileHack() {
            if (MusicMixer.Instance.ToggleDefeatedPlaylist.Value) {
                await SetupLockFiles(State.Defeated);
            }
        }

        private async Task SetupLockFiles(State state) {
            var relLockFilePath = $"{state}\\{_lockFile}";
            await MusicMixer.Instance.ContentsManager.Extract($"audio/{_lockFile}", Path.Combine(DirectoryUtil.MusicPath, relLockFilePath), false);
            try {
                using var file = File.Create(Path.Combine(DirectoryUtil.MusicPath, $"{state}.m3u"));
                file.Position = 0;
                var content = Encoding.UTF8.GetBytes($"{relLockFilePath}\r\n");
                await file.WriteAsync(content, 0, content.Length);
            } catch (Exception e) {
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
            GameService.Gw2Mumble.PlayerCharacter.CurrentMountChanged -= OnMountChanged;
            GameService.Gw2Mumble.CurrentMap.MapChanged               -= OnMapChanged;
            GameService.Gw2Mumble.PlayerCharacter.IsInCombatChanged   -= OnIsInCombatChanged;
            GameService.GameIntegration.Gw2Instance.Gw2Closed         -= OnGw2Closed;

            if (_inCombatTimer != null) {
                _inCombatTimer.Elapsed -= InCombatTimerElapsed;
                _inCombatTimer.Dispose();
            }

            if (_outOfCombatTimer != null) {
                _outOfCombatTimer.Elapsed -= OutOfCombatTimerElapsed;
                _outOfCombatTimer.Dispose();
            }

            if (_outOfCombatTimerLong != null) {
                _outOfCombatTimerLong.Elapsed -= OutOfCombatTimerElapsed;
                _outOfCombatTimerLong.Dispose();
            }
        }

        public void Update() {
            CheckTyrianTime();
            CheckWaterLevel();

            if (DateTime.UtcNow.Subtract(_lastLockFileCheck).TotalMilliseconds > 200) {
                _lastLockFileCheck = DateTime.UtcNow;
                if (MusicMixer.Instance.ToggleDefeatedPlaylist.Value) {
                    CheckLockFile(State.Defeated);
                }
            }
        }

        private void CheckLockFile(State state) {
            var absLockFilePath = Path.Combine(DirectoryUtil.MusicPath, $"{State.Defeated}\\{_lockFile}");
            if (FileUtil.IsFileLocked(absLockFilePath)) {
                this.CurrentState = state;
            } else if (this.CurrentState == state) {
                this.CurrentState = State.StandBy;
            }
        }

        private void CheckWaterLevel() => IsSubmerged = GameService.Gw2Mumble.PlayerCamera.Position.Z <= 0;
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
                if (GameService.Gw2Mumble.CurrentMap.Type.IsInstance() || 
                    GameService.Gw2Mumble.CurrentMap.Type.IsWvW() || 
                    GameService.Gw2Mumble.CurrentMap.Type == MapType.PublicMini)
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
