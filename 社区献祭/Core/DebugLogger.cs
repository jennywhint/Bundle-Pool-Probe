using StardewModdingAPI;

namespace BundlePoolProbe
{
    /// <summary>调试日志：bpp_debug 开启时用 Info 级输出（模组作者排查用），平时 Trace 级对玩家降噪。
    /// 开关仅当前会话有效，不落盘。</summary>
    public class DebugLogger
    {
        private readonly IMonitor _monitor;
        private bool _enabled;

        public DebugLogger(IMonitor monitor)
        {
            _monitor = monitor;
        }

        public void Toggle()
        {
            _enabled = !_enabled;
            _monitor.Log($"BundlePoolProbe 调试日志已{(_enabled ? "开启" : "关闭")}（仅当前会话有效，不影响存档）。", LogLevel.Info);
        }

        public void Log(string message)
        {
            _monitor.Log(message, _enabled ? LogLevel.Info : LogLevel.Trace);
        }
    }
}
