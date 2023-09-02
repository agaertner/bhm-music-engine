using Blish_HUD;
using Microsoft.Xna.Framework;
using NAudio.Wave;

namespace Nekres.Music_Mixer.Core.Services.Audio.Source {
    public class SubmergedVolumeProvider : ISampleProvider {

        public float Volume  { get; set; }
        public bool  Enabled { get; set; }

        private readonly ISampleProvider _source;

        public SubmergedVolumeProvider(ISampleProvider source) {
            _source = source;
            this.Volume = 1f;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int sampleCount) {
            var volume = GetDepthAdjustedVolume();
            int num = _source.Read(buffer, offset, sampleCount);
            if (volume != 1.0) {
                for (int index = 0; index < sampleCount; ++index)
                    buffer[offset + index] *= volume;
            }
            return num;
        }

        private float GetDepthAdjustedVolume() {
            var normalized = AudioUtil.GetNormalizedVolume(this.Volume);
            if (this.Enabled) {
                return MathHelper.Clamp(Map(GameService.Gw2Mumble.PlayerCamera.Position.Z,
                                            -130, AudioUtil.GetNormalizedVolume(0.1f), 0, 
                                            normalized), 0f, 0.1f);
            }
            return normalized;
        }

        private static float Map(float value, float fromLow, float fromHigh, float toLow, float toHigh) {
            return (value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow) + toLow;
        }
    }
}
