using System.Collections.Generic;
using StardewModdingAPI;

namespace BundlePoolProbe
{
    /// <summary>配置管理：读档加载（config.json 优先，assets/Config 出厂值兜底兼迁移源）、
    /// GMCM 保存与重置、房间配置访问。</summary>
    public class ConfigManager
    {
        // 玩家设置文件（模组根目录）；SMAPI 更新模组时会保留它，assets/Config 出厂值则会被覆盖
        private const string ConfigFileName = "config.json";

        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;

        public GlobalConfig Global { get; private set; } = new();

        private readonly Dictionary<string, LocationSpawnConfig> _rooms = new();

        public ConfigManager(IModHelper helper, IMonitor monitor)
        {
            _helper = helper;
            _monitor = monitor;
        }

        public void LoadAll()
        {
            // 玩家设置存模组根目录 config.json（SMAPI 更新模组时会保留）；
            // assets/Config/*.json 仅作为出厂默认值随包发布，同时兼作旧版设置的迁移来源。
            ModConfigFile? playerConfig = _helper.Data.ReadJsonFile<ModConfigFile>(ConfigFileName);

            // 全局配置：玩家 config.json → 旧版 assets/Config/Global.json → 内置默认
            Global = playerConfig?.Global
                     ?? _helper.Data.ReadJsonFile<GlobalConfig>("assets/Config/Global.json")
                     ?? new GlobalConfig();
            if (Global.CleanupDays < 0)
            {
                _monitor.Log($"CleanupDays={Global.CleanupDays} 非法，已设为 0（不清理）。", LogLevel.Warn);
                Global.CleanupDays = 0;
            }

            // 房间配置：玩家 config.json → 旧版 assets/Config/{图}.json → 内置默认
            _rooms.Clear();
            foreach (string locationName in LocationRegistry.SpawnLocationNames)
            {
                LocationSpawnConfig? config = null;
                playerConfig?.Locations.TryGetValue(locationName, out config);

                if (config == null)
                    config = _helper.Data.ReadJsonFile<LocationSpawnConfig>($"assets/Config/{locationName}.json");

                if (config == null)
                {
                    _monitor.Log($"{locationName} 无配置（config.json 与出厂文件均缺失），使用默认值。", LogLevel.Trace);
                    config = new LocationSpawnConfig
                    {
                        LocationName = locationName,
                        PoolGroup = GetDefaultPoolGroup(locationName),
                        DailySpawnCount = 2
                    };
                }
                else
                {
                    // 结构字段以代码为准，防手改损坏；玩家可调的只有 DailySpawnCount
                    config.LocationName = locationName;
                    if (string.IsNullOrWhiteSpace(config.PoolGroup))
                        config.PoolGroup = GetDefaultPoolGroup(locationName);
                }

                _rooms[locationName] = config;
            }
        }

        public void ResetToDefault()
        {
            Global = new GlobalConfig { CleanupDays = 1, ProgressiveUnlock = true, GeneralPoolDedup = false };
            foreach (string locationName in LocationRegistry.SpawnLocationNames)
            {
                if (_rooms.TryGetValue(locationName, out var config))
                {
                    config.DailySpawnCount = 2; // 只重置玩家可改的字段，PoolGroup 不动
                }
            }
        }

        public void SaveToDisk()
        {
            var playerConfig = new ModConfigFile
            {
                Global = Global,
                Locations = new Dictionary<string, LocationSpawnConfig>(_rooms)
            };
            _helper.Data.WriteJsonFile(ConfigFileName, playerConfig);
            _monitor.Log("GMCM 配置已保存到 config.json（模组更新时 SMAPI 会保留该文件）。", LogLevel.Info);
        }

        public LocationSpawnConfig? GetRoomConfig(string locationName)
        {
            return _rooms.TryGetValue(locationName, out var config) ? config : null;
        }

        private static string GetDefaultPoolGroup(string locationName)
        {
            return locationName switch
            {
                "BPP_CraftsRoom" => "Crafts Room",
                "BPP_Pantry" => "Pantry",
                "BPP_FishTank" => "Fish Tank",
                "BPP_BoilerRoom" => "Boiler Room",
                "BPP_BulletinBoard" => "Bulletin Board",
                LocationRegistry.GeneralLocationName => LocationRegistry.GeneralPoolGroup,
                _ => locationName
            };
        }
    }
}
