
using StardewModdingAPI;

namespace 社区献祭
{
    public class ModEntry : Mod
    {
        public override void Entry(IModHelper helper)
        {
            this.Monitor.Log("Hello, Stardew Valley! 我的第一个 Mod 已加载。", LogLevel.Info);
        }
    }
}