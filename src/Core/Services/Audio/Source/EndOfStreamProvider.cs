 using NAudio.Wave;
using System;

namespace Nekres.Music_Mixer.Core.Services.Audio.Source
{
    internal class EndOfStreamProvider : ISampleProvider
    {
        public event EventHandler<EventArgs> Ended;
        
        public WaveFormat WaveFormat => _sourceProvider.WaveFormat;

        public bool IsBuffering { get; private set; }

        private MediaFoundationReader _mediaProvider;

        private ISampleProvider _sourceProvider;

        private bool _ended;

        public EndOfStreamProvider(MediaFoundationReader mediaProvider)
        {
            _mediaProvider = mediaProvider;
            _sourceProvider = mediaProvider.ToSampleProvider();
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _sourceProvider.Read(buffer, offset, count);

            this.IsBuffering = read <= 0;

            if (_mediaProvider.CurrentTime < _mediaProvider.TotalTime || _ended) {
                return read;
            }

            _ended = true;
            this.Ended?.Invoke(this, EventArgs.Empty);
            return read;
        }
    }
}
