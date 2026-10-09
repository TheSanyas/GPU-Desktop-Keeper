using System.Drawing;
using System.Reflection;

namespace GpuDesktopKeeper {
    internal static class AppIcons {
        internal static Icon Load() {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Keeper.ico"))
            using(var icon=new Icon(stream,32,32)) return (Icon)icon.Clone();
        }
    }
}
