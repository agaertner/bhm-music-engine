using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Extended;
using Gw2Sharp.Models;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nekres.Music_Mixer.Core.Services {
    public class Gw2StateService : IDisposable
    {
        public event EventHandler<ValueChangedEventArgs<State>> StateChanged;
        public event EventHandler<ValueEventArgs<TyrianTime>>   TyrianTimeChanged;
        public event EventHandler<ValueEventArgs<bool>>         IsSubmergedChanged;

        public enum State
        {
            None, // silence (no mumble activity ie.loading screen, cinematic, character select)
            StandBy, // transition state between states.
            Ambient, // default when no other criteria are met.
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

        private State _currentState = State.None;
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

        private int[] _guildHallIds = {
            1068, 1101, 1107, 1108, 1121, 1125, // Gilded Hollow
            1069, 1071, 1076, 1104, 1124, 1144, // Lost Precipice
            1214, 1215, 1224, 1232, 1243, 1250, // Windswept Haven
            1419, 1426, 1435, 1444, 1462 // Isle of Reflection
        };

        public Gw2StateService() {

            _inCombatTimer                =  new NTimer(6500) { AutoReset = false };
            _inCombatTimer.Elapsed        += InCombatTimerElapsed;
            _outOfCombatTimer             =  new NTimer(3250) { AutoReset = false };
            _outOfCombatTimer.Elapsed     += OutOfCombatTimerElapsed;
            _outOfCombatTimerLong         =  new NTimer(20250) { AutoReset = false };
            _outOfCombatTimerLong.Elapsed += OutOfCombatTimerElapsed;

            GameService.Gw2Mumble.CurrentMap.MapChanged             += OnMapChanged;
            GameService.GameIntegration.Gw2Instance.Gw2Closed       += OnGw2Closed;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged += OnIsInGameChanged;
        }

        private void ChangeState(State newState) {
            switch (newState) {
                case State.Battle when GameService.Gw2Mumble.CurrentMap.Type.IsPublic():
                    CurrentState = newState;
                    break;
                case State.Mounted:
                    if (GameService.Gw2Mumble.CurrentMap.Type.IsPublic() ||
                        _guildHallIds.Contains(GameService.Gw2Mumble.CurrentMap.Id)) 
                    {
                        CurrentState = newState;
                    }
                    break;
                case State.Competitive:
                case State.Defeated:
                case State.StandBy:
                default: CurrentState = newState;
                    break;
            }
        }

        public async Task SetupLockFiles(State state) {
            var relLockFilePath = $"{state}\\{_lockFile}";
            await MusicMixer.Instance.ContentsManager.Extract($"audio/{_lockFile}", Path.Combine(DirectoryUtil.MusicPath, relLockFilePath), false);
            try {
                MusicMixer.Logger.Info($"Creating {state} playlist.");

                var path = Path.Combine(DirectoryUtil.MusicPath, $"{state}.m3u");
                relLockFilePath = $"{relLockFilePath}\r\n";
                if (File.Exists(path)) {
                    var lines = File.ReadAllText(path, Encoding.UTF8);
                    if (lines.Equals(relLockFilePath)) {
                        MusicMixer.Logger.Info($"{state} playlist already exists. Skipping.");
                        return;
                    }
                    MusicMixer.Logger.Info($"Storing existing {state} playlist as backup.");
                    File.Copy(path, Path.Combine(DirectoryUtil.MusicPath, $"{state}.backup.m3u"), true);
                }
                using var file = File.Create(path);
                file.Position = 0;
                var content = Encoding.UTF8.GetBytes(relLockFilePath);
                await file.WriteAsync(content, 0, content.Length);
                MusicMixer.Logger.Info($"{state} playlist created.");
                ScreenNotification.ShowNotification($"{state} playlist created. Game restart required.", ScreenNotification.NotificationType.Warning);
            } catch (Exception e) {
                MusicMixer.Logger.Info(e, e.Message);
            }
        }

        public void RevertLockFiles(State state) {
            try {
                var backupPath = Path.Combine(DirectoryUtil.MusicPath, $"{state}.backup.m3u");
                var path = Path.Combine(DirectoryUtil.MusicPath, $"{state}.m3u");

                if (File.Exists(backupPath)) {
                    File.Copy(backupPath, path, true);
                    File.Delete(backupPath);
                } else if (File.Exists(path)) {
                    File.Delete(path);
                }
                ScreenNotification.ShowNotification($"{state} playlist reverted. Game restart required.", ScreenNotification.NotificationType.Warning);
            } catch (Exception e) {
                MusicMixer.Logger.Info(e, e.Message);
            }
        }

        private void InCombatTimerElapsed(object sender, EventArgs e) {
            ChangeState(State.Battle);
        }

        private void OutOfCombatTimerElapsed(object sender, EventArgs e) {
            ChangeState(State.StandBy);
        }

        public void Dispose() {
            GameService.Gw2Mumble.CurrentMap.MapChanged             -= OnMapChanged;
            GameService.GameIntegration.Gw2Instance.Gw2Closed       -= OnGw2Closed;
            GameService.GameIntegration.Gw2Instance.IsInGameChanged -= OnIsInGameChanged;

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
            if (DateTime.UtcNow.Subtract(_lastLockFileCheck).TotalMilliseconds > 200) {
                _lastLockFileCheck = DateTime.UtcNow;
                CheckLockFile(State.Defeated);

                if (GameService.GameIntegration.Gw2Instance.IsInGame) {
                    CheckTyrianTime();
                    CheckWaterLevel();
                    CheckIsInCombatChanged();
                    CheckMountChanged();
                    if (CurrentState == State.StandBy) {
                        ChangeState(State.Ambient); // In-Game but no other state has its criteria met.
                    }
                }
            }
        }

        private void CheckLockFile(State state) {
            var absLockFilePath = Path.Combine(DirectoryUtil.MusicPath, $"{state}\\{_lockFile}");
            if (FileUtil.IsFileLocked(absLockFilePath)) {
                ChangeState(state);
            } else if (this.CurrentState == state) {
                ChangeState(State.StandBy);
            }
        }

        private void CheckWaterLevel() => IsSubmerged = GameService.Gw2Mumble.PlayerCamera.Position.Z <= 0;
        private void CheckTyrianTime() => TyrianTime = TyrianTimeUtil.GetCurrentDayCycle();
        private void CheckMountChanged() {
            if (GameService.Gw2Mumble.PlayerCharacter.CurrentMount > 0) {
                if (!GameService.Gw2Mumble.PlayerCharacter.IsInCombat) {
                    ChangeState(State.Mounted);
                }
            } else if (this.CurrentState == State.Mounted) {
                ChangeState(State.StandBy);
            }
        }

        private void CheckIsInCombatChanged() {
            if (GameService.Gw2Mumble.PlayerCharacter.IsInCombat) {

                _inCombatTimer.Restart();

            } else if (CurrentState == State.Battle) {

                if (GameService.Gw2Mumble.CurrentMap.Type.IsInstance() ||
                    GameService.Gw2Mumble.CurrentMap.Type.IsWvW()      ||
                    GameService.Gw2Mumble.CurrentMap.Type == MapType.PublicMini) {
                    _outOfCombatTimerLong.Restart();

                } else {

                    _outOfCombatTimer.Restart();
                }

            } else {

                _inCombatTimer.Stop();

            }
        }

        private void OnIsInGameChanged(object sender, ValueEventArgs<bool> e) {
            if (!e.Value) {
                Reset();
            }
        }

        private void OnGw2Closed(object sender, EventArgs e) {
            Reset();
        }

        private void OnMapChanged(object o, ValueEventArgs<int> e) {
            Reset();
        }

        private void Reset() {
            _outOfCombatTimer.Stop();
            _outOfCombatTimerLong.Stop();
            _inCombatTimer.Stop();
            ChangeState(State.None);
        }
    }
}
