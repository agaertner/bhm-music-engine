using System;
namespace Nekres.Music_Mixer.Core {
    public class ProgressTotal : Progress<string> {

        public int Total;
        private int _current;

        public ProgressTotal() : base() { 
            /* NOOP */
        }
        public ProgressTotal(Action<string> handler) : base(handler) {
            /* NOOP */
        }

        protected override void OnReport(string value) {
            if (Total < 1) {
                base.OnReport(value);
            }
            base.OnReport($"({(int)Math.Round(_current / (float)Total * 100)}%) " + value);
        }

        public void Report(string val, bool increment = false) {
            _current++;
            OnReport(val);
        }
    }
}
