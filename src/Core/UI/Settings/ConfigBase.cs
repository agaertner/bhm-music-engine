using Blish_HUD.Input;
using Blish_HUD.Settings;
using System;

namespace Nekres.Music_Mixer.Core.UI.Settings {
    public abstract class ConfigBase {

        protected virtual void BindingChanged() {
            /* NOOP */
        }

        protected void SaveConfig<T>(SettingEntry<T> setting) where T : ConfigBase {
            if (setting?.IsNull ?? true) {
                return;
            }
            /* unset value first otherwise reassigning the same reference would
             not be recognized as a property change and not invoke a save. */
            setting.Value = null;
            setting.Value = this as T;
        }

        protected bool SetProperty<T>(ref T property, T value) {
            if (Equals(property, value)) {
                return false;
            }
            property = value;
            return true;
        }

        protected KeyBinding ResetDelegates(KeyBinding oldBinding, KeyBinding newBinding) {
            if (oldBinding != null) {
                oldBinding.Enabled        =  false;
                oldBinding.BindingChanged -= OnBindingChanged;
            }
            newBinding                ??= new KeyBinding();
            newBinding.BindingChanged +=  OnBindingChanged;
            newBinding.Enabled        =   true;
            return newBinding;
        }

        private void OnBindingChanged(object sender, EventArgs e) {
            BindingChanged();
        }
    }
}
