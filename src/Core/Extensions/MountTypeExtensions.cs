using Gw2Sharp.Models;
using Nekres.Music_Mixer.Properties;

namespace Nekres.Music_Mixer.Core {
    internal static class MountTypeExtensions {

        public static string FriendlyName(this MountType mountType) {
            return mountType switch {
                MountType.Jackal       => Resources.Jackal,
                MountType.Griffon      => Resources.Griffon,
                MountType.Springer     => Resources.Springer,
                MountType.Skimmer      => Resources.Skimmer,
                MountType.Raptor       => Resources.Raptor,
                MountType.RollerBeetle => Resources.Roller_Beetle,
                MountType.Warclaw      => Resources.Warclaw,
                MountType.Skyscale     => Resources.Skyscale,
                MountType.Skiff        => Resources.Skiff,
                MountType.SiegeTurtle  => Resources.Siege_Turtle,
                _                      => mountType.ToString().SplitCamelCase()
            };
        }
    }
}
