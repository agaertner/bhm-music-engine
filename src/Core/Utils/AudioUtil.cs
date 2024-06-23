using Blish_HUD;
using Microsoft.Xna.Framework;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Nekres.Music_Mixer {
    public static class AudioUtil {

        private static Glide.Tween             _animEase;
        private static List<SimpleAudioVolume> _volumes;

        public static float GetNormalizedVolume(float volume, float masterVolume) {
            if (volume >= masterVolume) {
                return masterVolume;
            }
            return MathHelper.Clamp(masterVolume - Math.Abs(volume - masterVolume), 0f, 1f);
        }

        /// <summary>
        /// Sets the volume of the target process.
        /// </summary>
        /// <param name="processId">The process id.</param>
        /// <param name="targetVolume">The target volume between 0 and 1.</param>
        /// <param name="duration">Ease duration in seconds.</param>
        public static void SetVolume(int processId, float targetVolume, float duration = 2) {
            _animEase?.Cancel(); // Cancel previous ease.
            // (Do NOT use .CancelAndComplete()! A sudden volume spike can damage hearing!)

            // Ensure disposal of old volumes as we cancel and possibly skip completion functions.
            Dispose(_volumes); 

            // Retrieve new volumes in case new devices were plugged or process output has changed.
            var volumes = GetVolumes(processId);

            if (!volumes.Any()) {
                return;
            }

            var currentVolume = volumes.Average(v => v.Volume);

            _animEase = GameService.Animation.Tweener.Timer(duration).Ease(t => {
                var delta     = (targetVolume - currentVolume) * t;
                var newVolume = currentVolume + delta;

                foreach (var v in volumes) {
                    v.Volume = newVolume > 1 ? 1 : newVolume < 0 ? 0 : newVolume;
                }

                return newVolume;
            }).OnComplete(() => {
                Dispose(volumes);
            });

            _volumes = volumes.ToList();
        }

        private static void Dispose(List<SimpleAudioVolume> disposables) {
            if (disposables == null) {
                return;
            }

            try {
                lock (disposables) {
                    foreach (var v in disposables) {
                        v.Dispose();
                    }

                    disposables.Clear();
                }
            } finally {
                // Ensure static members are nullified.
                _animEase = null;
                _volumes  = null;
            }
        }

        private static List<SimpleAudioVolume> GetVolumes(int processId) {

            var volumes = new List<SimpleAudioVolume>();

            using var deviceEnumerator = new MMDeviceEnumerator();
            foreach (var device in deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                SessionCollection sessionEnumerator = null;

                try {
                    sessionEnumerator = device.AudioSessionManager.Sessions;
                } catch (COMException ex) when ((uint)ex.HResult == 0x88890008) {
                    // Skip this audio device. Something about it is unsupported.
                    continue;
                } catch (COMException ex) when ((uint)ex.HResult == 0x80040154) {
                    // Skip this audio device. Something about it is unsupported.
                    continue;
                } catch (COMException ex) when ((uint)ex.HResult == 0x80070490) {
                    // Skip this audio device. Something about it is unsupported.
                    continue;
                } catch (COMException ex) when ((uint)ex.HResult == 0x80070005) {
                    // Skip this audio device. Something about it is unsupported.
                    continue;
                } catch (Exception) {
                    continue;
                }

                for (int i = 0; i < sessionEnumerator.Count; i++) {
                    using var audioSession = sessionEnumerator[i];

                    if (audioSession.GetProcessID == processId) {
                        volumes.Add(audioSession.SimpleAudioVolume);
                    }
                }

                device.Dispose();
            }

            return volumes;
        }

        public static IEnumerable<(int DeviceNumber, string ProductName, int Channels, Guid DeviceNameGuid, Guid ProductNameGuid, Guid ManufacturerGuid)> OutputDevices {
            get {
                for (int deviceNumber = 0; deviceNumber < WaveInterop.waveOutGetNumDevs(); ++deviceNumber) {
                    var device = GetCapabilities(deviceNumber);
                    yield return (deviceNumber, device.ProductName, device.Channels, device.NameGuid, device.ProductGuid, device.ManufacturerGuid);
                }
            }
        }

        private static WaveOutCapabilities GetCapabilities(int devNumber) {
            WaveOutCapabilities waveOutRends = new WaveOutCapabilities();
            int waveInCapsSize = Marshal.SizeOf<WaveOutCapabilities>(waveOutRends);
            MmException.Try(WaveInterop.waveOutGetDevCaps((IntPtr)devNumber, out waveOutRends, waveInCapsSize), "waveOutGetDevCaps");
            return waveOutRends;
        }

        public static WaveOutEvent GetOutputDeviceById(Guid productNameGuid) {
            var device = OutputDevices.FirstOrDefault(x => x.ProductNameGuid.Equals(productNameGuid));
            return device.Equals(default) ? null : new WaveOutEvent { DeviceNumber = device.DeviceNumber };
        }

        public static MMDevice GetWasApiOutputDeviceById(string id) {
            using var deviceEnumerator = new MMDeviceEnumerator();
            MMDevice  targetDevice     = null;
            foreach (var device in deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) {
                if (id.Equals(device.ID)) {
                    targetDevice = device;
                } else {
                    device.Dispose();
                }
            }
            return targetDevice;
        }

        public static IEnumerable<(int DeviceNumber, string FriendlyName, string DeviceName, string Id, string DeviceId)> WasApiOutputDevices {
            get {
                using var deviceEnumerator = new MMDeviceEnumerator();
                var       endpoints = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                for (int deviceNumber = 0; deviceNumber < endpoints.Count; ++deviceNumber) {
                    var device = endpoints[deviceNumber];

                    SessionCollection sessionEnumerator = null;
                    try {
                        sessionEnumerator = device.AudioSessionManager.Sessions;
                    } catch (COMException ex) when ((uint)ex.HResult == 0x88890008) {
                        // Skip this audio device. Something about it is unsupported.
                        continue;
                    } catch (COMException ex) when ((uint)ex.HResult == 0x80040154) {
                        // Skip this audio device. Something about it is unsupported.
                        continue;
                    } catch (COMException ex) when ((uint)ex.HResult == 0x80070490) {
                        // Skip this audio device. Something about it is unsupported.
                        continue;
                    } catch (COMException ex) when ((uint)ex.HResult == 0x80070005) {
                        // Skip this audio device. Something about it is unsupported.
                        continue;
                    } catch (Exception) {
                        continue;
                    }

                    var ret = (deviceNumber, device.FriendlyName, device.DeviceFriendlyName, device.ID, device.DeviceTopology.DeviceId);
                    device.Dispose();
                    yield return ret;
                }
            }
        }
    }
}
