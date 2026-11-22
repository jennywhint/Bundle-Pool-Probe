using System;
using System.Collections.Generic;

namespace BundlePoolProbe
{
    /// <summary>物品池条目：从 BundleData 展开出的单个物品。</summary>
    public class PoolItem
    {
        public string QualifiedItemId { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string BundleName { get; set; } = "";
    }

    /// <summary>单个收集房间的生成配置（玩家可经 GMCM 调整 DailySpawnCount）。</summary>
    public class LocationSpawnConfig
    {
        public string LocationName { get; set; } = "";
        public string PoolGroup { get; set; } = "";
        public int DailySpawnCount { get; set; }
    }

    /// <summary>全局配置。</summary>
    public class GlobalConfig
    {
        public int CleanupDays { get; set; } = 1;

        // true = 房间随原版进度逐步解锁；false = 所有房间常开
        public bool ProgressiveUnlock { get; set; } = true;

        // true = 通用池按物品 ID 去重；false = 保留重复（同一物品在多个未映射包里权重更高）
        public bool GeneralPoolDedup { get; set; } = false;
    }

    /// <summary>config.json（模组根目录）的结构：玩家经 GMCM 修改的设置存这里。
    /// SMAPI 更新模组时会保留根目录 config.json；assets/Config/*.json 仅作为出厂默认值随包发布。</summary>
    public class ModConfigFile
    {
        public GlobalConfig Global { get; set; } = new();
        public Dictionary<string, LocationSpawnConfig> Locations { get; set; } = new();
    }
}
