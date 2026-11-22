using System.Collections.Generic;

namespace BundlePoolProbe
{
    /// <summary>模组地点与池组的静态注册表：所有名称、映射与常量的唯一权威来源。</summary>
    public static class LocationRegistry
    {
        public const string HubLocationName = "BPP_Hub";
        public const string FishTankLocationName = "BPP_FishTank";
        public const string GeneralLocationName = "BPP_General";

        // 通用池组名：PoolGroup 为此值时，聚合所有未映射到固定地图的房间组
        public const string GeneralPoolGroup = "General";

        /// <summary>参与每日生成/清理的房间（不含枢纽）。</summary>
        public static readonly string[] SpawnLocationNames = new[]
        {
            "BPP_CraftsRoom",
            "BPP_Pantry",
            "BPP_FishTank",
            "BPP_BoilerRoom",
            "BPP_BulletinBoard",
            "BPP_General",
        };

        /// <summary>已映射到 5 张固定子地图的池组（即 BundleData 的房间名），通用池排除这些。</summary>
        public static readonly HashSet<string> FixedPoolGroups = new()
        {
            "Crafts Room",
            "Pantry",
            "Fish Tank",
            "Boiler Room",
            "Bulletin Board",
        };

        /// <summary>模组管理的全部地点 → 地图资产路径（含枢纽共 7 个）。
        /// 每个地点都会在 Data/Locations 里动态创建条目（含 CreateOnLoad）：存档加载时
        /// Game1.AddLocations()（loadForNewGame 内调用）只会预创建带 CreateOnLoad 条目的地点，
        /// 缺条目的地点会被 SaveGame.loadDataToLocations 以 "Ignored unknown location" 跳过，
        /// 玩家放在房间里的箱子等内容会随存档丢失——所以 7 个地点必须全部有条目。</summary>
        public static readonly Dictionary<string, string> ManagedLocationMapAssets = new()
        {
            ["BPP_Hub"] = "assets/Maps/BPP_Hub.tmx",
            ["BPP_CraftsRoom"] = "assets/Maps/BPP_CraftsRoom.tmx",
            ["BPP_Pantry"] = "assets/Maps/BPP_Pantry.tmx",
            ["BPP_FishTank"] = "assets/Maps/BPP_FishTank.tmx",
            ["BPP_BoilerRoom"] = "assets/Maps/BPP_BoilerRoom.tmx",
            ["BPP_BulletinBoard"] = "assets/Maps/BPP_BulletinBoard.tmx",
            ["BPP_General"] = "assets/Maps/BPP_General.tmx",
        };

        /// <summary>GMCM 区标题的 i18n key（缺失时回退显示地点原名）。</summary>
        public static readonly Dictionary<string, string> SectionTitleKeys = new()
        {
            ["BPP_CraftsRoom"] = "gmcm.section.craftsroom",
            ["BPP_Pantry"] = "gmcm.section.pantry",
            ["BPP_FishTank"] = "gmcm.section.fishtank",
            ["BPP_BoilerRoom"] = "gmcm.section.boilerroom",
            ["BPP_BulletinBoard"] = "gmcm.section.bulletinboard",
            ["BPP_General"] = "gmcm.section.general",
        };
    }
}
