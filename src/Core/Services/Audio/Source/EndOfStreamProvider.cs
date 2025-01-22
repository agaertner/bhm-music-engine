using NAudio.Wave;
using System;

namespace Nekres.Music_Mixer.Core.Services.Audio.Source {
    internal class EndOfStreamProvider : ISampleProvider
    {
        public event EventHandler<EventArgs> Ended;
        
        public WaveFormat WaveFormat => _sourceProvider.WaveFormat;

        public bool IsBuffering { get; private set; }

        private MediaFoundationReader _mediaProvider;

        private ISampleProvider _sourceProvider;

        private bool _ended;

        private double _endTimeOffsetSeconds;

        /// <summary>
        /// Provides an event that fires when the stream reaches its end.
        /// </summary>
        /// <param name="mediaProvider">Media that provides the stream.</param>
        /// <param name="endTimeOffsetSeconds">Offset in seconds to signal the end prematurely.</param>
        public EndOfStreamProvider(MediaFoundationReader mediaProvider, double endTimeOffsetSeconds = 5)
        {
            _mediaProvider = mediaProvider;
            _sourceProvider = mediaProvider.ToSampleProvider();
            _endTimeOffsetSeconds = Math.Abs(endTimeOffsetSeconds);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _sourceProvider.Read(buffer, offset, count);
            this.IsBuffering = read <= 0;
            if (_ended || _mediaProvider.CurrentTime.TotalSeconds < _mediaProvider.TotalTime.TotalSeconds - _endTimeOffsetSeconds) {
                return read;
            }
            _ended = true;
            this.Ended?.Invoke(this, EventArgs.Empty);
            return read;
        }
    }
}
