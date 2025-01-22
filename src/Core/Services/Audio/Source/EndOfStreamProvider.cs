using NAudio.Wave;
using System;

namespace Nekres.Music_Mixer.Core.Services.Audio.Source {
    internal class EndOfStreamProvider : ISampleProvider
    {
        public event EventHandler<EventArgs> EndReached;
        
        public WaveFormat WaveFormat => _sourceProvider.WaveFormat;

        public bool IsBuffering { get; private set; }

        public bool Ended { get; private set; }

        private MediaFoundationReader _mediaProvider;

        private ISampleProvider _sourceProvider;

        private double _endTimeOffsetMs;

        /// <summary>
        /// Provides an event that fires when the stream reaches its end.
        /// </summary>
        /// <param name="mediaProvider">Media that provides the stream.</param>
        /// <param name="endTimeOffsetMs">Offset in milliseconds to signal the end prematurely.</param>
        public EndOfStreamProvider(MediaFoundationReader mediaProvider, int endTimeOffsetMs = 0)
        {
            _mediaProvider = mediaProvider;
            _sourceProvider = mediaProvider.ToSampleProvider();
            _endTimeOffsetMs = Math.Abs(endTimeOffsetMs);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _sourceProvider.Read(buffer, offset, count);
            this.IsBuffering = read <= 0;
            if (Ended || _mediaProvider.CurrentTime.TotalMilliseconds < _mediaProvider.TotalTime.TotalMilliseconds - _endTimeOffsetMs) {
                return read;
            }
            Ended = true;
            this.EndReached?.Invoke(this, EventArgs.Empty);
            return read;
        }
    }
}
