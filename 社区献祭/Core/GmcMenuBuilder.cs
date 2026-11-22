using StardewModdingAPI;

namespace BundlePoolProbe
{
    /// <summary>GMCM 菜单构建：全局设置（清理周期 / 逐步解锁 / 通用池去重）+ 按房间的每日生成数量。</summary>
    public class GmcMenuBuilder
    {
        private readonly ModEntry _mod;
        private readonly ConfigManager _config;

        public GmcMenuBuilder(ModEntry mod, ConfigManager config)
        {
            _mod = mod;
            _config = config;
        }

        public void Register()
        {
            var configMenu = _mod.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
            {
                _mod.Monitor.Log("未检测到 GMCM，配置将仅从 JSON 读取。", LogLevel.Info);
                return;
            }

            configMenu.Register(
                mod: _mod.ModManifest,
                reset: () => _config.ResetToDefault(),
                save: () => _config.SaveToDisk()
            );

            // 全局设置
            configMenu.AddSectionTitle(_mod.ModManifest,
                () => _mod.Helper.Translation.Get("gmcm.section.global"));

            configMenu.AddNumberOption(
                mod: _mod.ModManifest,
                getValue: () => _config.Global.CleanupDays,
                setValue: value => _config.Global.CleanupDays = value,
                name: () => _mod.Helper.Translation.Get("gmcm.cleanup-days.name"),
                tooltip: () => _mod.Helper.Translation.Get("gmcm.cleanup-days.tooltip"),
                min: 0,
                max: 28,
                interval: 1
            );

            configMenu.AddBoolOption(
                mod: _mod.ModManifest,
                getValue: () => _config.Global.ProgressiveUnlock,
                setValue: value => _config.Global.ProgressiveUnlock = value,
                name: () => _mod.Helper.Translation.Get("gmcm.progressive-unlock.name"),
                tooltip: () => _mod.Helper.Translation.Get("gmcm.progressive-unlock.tooltip")
            );

            configMenu.AddBoolOption(
                mod: _mod.ModManifest,
                getValue: () => _config.Global.GeneralPoolDedup,
                setValue: value => _config.Global.GeneralPoolDedup = value,
                name: () => _mod.Helper.Translation.Get("gmcm.general-dedup.name"),
                tooltip: () => _mod.Helper.Translation.Get("gmcm.general-dedup.tooltip")
            );

            // 各房间每日生成数量（区标题用 i18n 美化，缺失时回退地点原名）
            foreach (string locationName in LocationRegistry.SpawnLocationNames)
            {
                var roomConfig = _config.GetRoomConfig(locationName);
                if (roomConfig == null)
                    continue;

                var capturedConfig = roomConfig;
                string sectionTitle = LocationRegistry.SectionTitleKeys.TryGetValue(locationName, out var sectionKey)
                    ? _mod.Helper.Translation.Get(sectionKey).ToString()
                    : locationName;
                configMenu.AddSectionTitle(_mod.ModManifest, () => sectionTitle);
                configMenu.AddNumberOption(
                    mod: _mod.ModManifest,
                    getValue: () => capturedConfig.DailySpawnCount,
                    setValue: value => capturedConfig.DailySpawnCount = value,
                    name: () => _mod.Helper.Translation.Get("gmcm.spawn-count.name"),
                    tooltip: () => _mod.Helper.Translation.Get(
                        "gmcm.spawn-count.tooltip",
                        new { locationName }),
                    min: 0,
                    max: 50,
                    interval: 1
                );
            }
        }
    }
}
