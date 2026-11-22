using System.IO;
using StardewValley;

namespace BundlePoolProbe
{
    /// <summary>手动注册兜底：CreateOnLoad 已让游戏在读档时自动创建全部地点
    /// （见 LocationRegistry.ManagedLocationMapAssets 注释），这里再补注册一次（已存在的会跳过）。</summary>
    public class LocationRegistrar
    {
        private readonly ModEntry _mod;

        public LocationRegistrar(ModEntry mod)
        {
            _mod = mod;
        }

        public void EnsureCustomLocations()
        {
            foreach (string locationName in LocationRegistry.ManagedLocationMapAssets.Keys)
            {
                RegisterLocation(locationName);
            }
        }

        private void RegisterLocation(string locationName)
        {
            if (Game1.getLocationFromName(locationName) != null)
                return;

            string relativePath = $"assets/Maps/{locationName}.tmx";
            string fullPath = Path.Combine(_mod.Helper.DirectoryPath, "assets", "Maps", $"{locationName}.tmx");

            if (!File.Exists(fullPath))
            {
                _mod.Monitor.Log($"注册 {locationName} 失败：文件不存在 {fullPath}", StardewModdingAPI.LogLevel.Error);
                return;
            }

            try
            {
                string assetName = _mod.Helper.ModContent.GetInternalAssetName(relativePath).Name;
                var location = new GameLocation(assetName, locationName) { IsOutdoors = true };
                Game1.locations.Add(location);
                _mod.Monitor.Log($"已注册 {locationName}，资产键：{assetName}", StardewModdingAPI.LogLevel.Info);
            }
            catch (System.Exception ex)
            {
                _mod.Monitor.Log($"注册 {locationName} 失败：{ex.GetType().Name} - {ex.Message}，路径 {fullPath}", StardewModdingAPI.LogLevel.Error);
            }
        }
    }
}
