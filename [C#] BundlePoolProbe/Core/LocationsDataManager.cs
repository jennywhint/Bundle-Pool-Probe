using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BundlePoolProbe
{
    /// <summary>Data/Locations 侧的一切：
    /// 1) 从物品池提取鱼竿鱼（蟹笼渔获不算，留地面）并维护鱼缸/通用房的鱼群 ID；
    /// 2) 为全部 7 个托管地点动态创建/刷新 Data/Locations 条目（含 CreateOnLoad，保证存档加载可重建）；
    /// 3) 运行时直写 Game1.locationData 使鱼群当次会话立即生效。</summary>
    public class LocationsDataManager
    {
        private readonly ModEntry _mod;

        // 鱼缸（BPP_FishTank）水体鱼群：Fish Tank 组里的鱼竿鱼（蟹笼渔获不算，留地面）
        private List<string> _fishTankFishIds = new();

        // 通用房（BPP_General）水体鱼群：通用池（未映射房间组）聚合后的鱼竿鱼
        private List<string> _generalFishIds = new();

        public LocationsDataManager(ModEntry mod)
        {
            _mod = mod;
        }

        // ===================== 提取与刷新 =====================
        /// <summary>从池中提取鱼竿鱼并刷新全部地点的 Data/Locations 条目。
        /// SaveLoaded 与延迟建池完成后都会调用——后者修复新档首日的时序问题
        ///（SaveLoaded 时池还没建好，建完池若不补一次刷新，首次进档钓不到池鱼）。</summary>
        public void RefreshFishData(Dictionary<string, List<PoolItem>>? pool)
        {
            List<string> fishTankFish = new();
            List<string> generalFish = new();

            if (pool != null)
            {
                if (pool.TryGetValue("Fish Tank", out var fishTankItems))
                {
                    fishTankFish = fishTankItems
                        .Select(i => i.QualifiedItemId)
                        .Where(IsRodFish)
                        .Distinct()
                        .ToList();
                }

                generalFish = pool
                    .Where(kv => !LocationRegistry.FixedPoolGroups.Contains(kv.Key))
                    .SelectMany(kv => kv.Value)
                    .Select(i => i.QualifiedItemId)
                    .Where(IsRodFish)
                    .Distinct()
                    .ToList();
            }

            _fishTankFishIds = fishTankFish;
            _generalFishIds = generalFish;
            _mod.Debug.Log($"[鱼群] 提取完成：鱼缸 {fishTankFish.Count} 条 / 通用房 {generalFish.Count} 条（均为鱼竿鱼）。");

            // 关键：Game1.locationData 是读条加载器整体赋值的静态字典（Game1.cs 加载步骤
            // locationData = DataLoader.Locations(content)），单纯 InvalidateCache 不会刷新它，
            // 鱼群要等下次读档才生效。这里直接写入运行时字典，让本次会话立即生效（全部地点
            // 同步刷新，保证 Game1.locationData 与资产层一致）；
            // 下面的 InvalidateCache 只负责同步资产缓存（下次读档重建资产时仍带全部条目）。
            if (Game1.locationData != null)
            {
                foreach (string locationName in LocationRegistry.ManagedLocationMapAssets.Keys)
                {
                    var entry = CreateLocationDataFor(locationName);
                    if (entry != null)
                        Game1.locationData[locationName] = entry;
                }
            }

            _mod.Helper.GameContent.InvalidateCache("Data/Locations");
        }

        // ===================== 资产编辑 =====================
        public void OnAssetRequestedForLocations(object? sender, AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
                return;

            e.Edit(asset =>
            {
                var dict = asset.AsDictionary<string, StardewValley.GameData.Locations.LocationData>().Data;

                // 为模组管理的全部地点确保 Data/Locations 条目（含 CreateOnLoad，鱼群随条目一并写入）。
                // 资产编辑是每次加载重新应用的，属于正常幂等行为。
                foreach (string locationName in LocationRegistry.ManagedLocationMapAssets.Keys)
                {
                    List<string> fishIds = locationName switch
                    {
                        LocationRegistry.FishTankLocationName => _fishTankFishIds,
                        LocationRegistry.GeneralLocationName => _generalFishIds,
                        _ => EmptyFishIds,
                    };
                    EnsureDataLocationsEntry(dict, locationName, () => CreateLocationDataFor(locationName), fishIds);
                }
            });
        }

        private static readonly List<string> EmptyFishIds = new();

        // 确保 Data/Locations 里有指定条目：不存在则创建；已存在时若有鱼群数据则覆写
        private void EnsureDataLocationsEntry(
            IDictionary<string, StardewValley.GameData.Locations.LocationData> dict,
            string entryKey,
            Func<StardewValley.GameData.Locations.LocationData?> createEntry,
            List<string> fishIds)
        {
            if (!dict.ContainsKey(entryKey))
            {
                var created = createEntry();
                if (created == null)
                    return;

                dict[entryKey] = created;
                _mod.Debug.Log($"[鱼群] 已在 Data/Locations 中动态创建 {entryKey} 条目（含 CreateOnLoad）。");
                return;
            }

            if (fishIds.Count == 0)
                return;

            dict[entryKey].Fish = BuildFishSpawnList(fishIds);
            _mod.Debug.Log($"[鱼群] 已向 {entryKey} 写入 {fishIds.Count} 条鱼。");
        }

        // ===================== 条目构建 =====================
        // 构建 Data/Locations 条目（类型在 StardewValley.GameData.dll，1.6 数据模型）。
        // 注意 Fish 是 List<SpawnFishData>（不是 1.5 的 season→字符串字典），语义已用反编译源码核实：
        //   - Chance 是绝对概率，默认 0 = 永不上钩，必须显式设 1；
        //   - Precedence 越小越先尝试，设 -1 让池鱼优先于原版 Default 的垃圾鱼条目（垃圾鱼 Precedence 2000）；
        //   - IgnoreFishDataRequirements = true 忽略鱼自身 Data/Fish 的季节/时间/地点限制（四季全可钓）。
        // CreateOnLoad.Type 必须留空：CreateGameLocation 里 Type.GetType("GameLocation") 解析不到
        // 非限定名（实际类型是 StardewValley.GameLocation），会抛 "Invalid type"；Type 为 null 时
        // 游戏直接走 new GameLocation(MapPath, id) 分支，与手动注册完全一致。
        private StardewValley.GameData.Locations.LocationData? BuildLocationData(string mapAssetPath, List<StardewValley.GameData.Locations.SpawnFishData> fish)
        {
            try
            {
                string mapAsset = _mod.Helper.ModContent.GetInternalAssetName(mapAssetPath).Name;

                return new StardewValley.GameData.Locations.LocationData
                {
                    Fish = fish,
                    CreateOnLoad = new StardewValley.GameData.Locations.CreateLocationData
                    {
                        MapPath = mapAsset
                    }
                };
            }
            catch (Exception ex)
            {
                _mod.Monitor.Log($"[鱼群] 构建 {mapAssetPath} 的 LocationData 失败：{ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        private StardewValley.GameData.Locations.LocationData? CreateLocationDataFor(string locationName)
        {
            if (!LocationRegistry.ManagedLocationMapAssets.TryGetValue(locationName, out var mapPath))
                return null;

            var fish = locationName switch
            {
                LocationRegistry.FishTankLocationName => BuildFishSpawnList(_fishTankFishIds),
                LocationRegistry.GeneralLocationName => BuildFishSpawnList(_generalFishIds),
                _ => new List<StardewValley.GameData.Locations.SpawnFishData>()
            };

            return BuildLocationData(mapPath, fish);
        }

        // 把鱼竿鱼 ID 转成 1.6 的鱼群生成条目（四季均可钓、必中）
        private List<StardewValley.GameData.Locations.SpawnFishData> BuildFishSpawnList(List<string> fishIds)
        {
            var fishList = new List<StardewValley.GameData.Locations.SpawnFishData>();
            foreach (string qualifiedId in fishIds)
            {
                fishList.Add(new StardewValley.GameData.Locations.SpawnFishData
                {
                    ItemId = qualifiedId,
                    Chance = 1f,
                    Precedence = -1,
                    IgnoreFishDataRequirements = true
                });
            }

            return fishList;
        }

        // ===================== 分类 =====================
        // 是否为"鱼竿可钓的鱼"：-4 鱼类且 Data/Fish 条目第 2 字段不是 "trap"。
        // 蟹笼渔获（龙虾/螃蟹/蜗牛等）虽然是 -4，但 Data/Fish 标记为 trap，不算鱼竿鱼
        //（与原版 CheckGenericFishRequirements 的判定一致）。非鱼类物品返回 false。
        // （公开给 SpawnManager 做地面池过滤：鱼竿鱼不落地，蟹笼渔获和非鱼类物品落地）
        public bool IsRodFish(string qualifiedItemId)
        {
            var data = ItemRegistry.GetData(qualifiedItemId);
            if (data == null || data.Category != -4)
                return false;

            string raw = DataLoader.Fish(Game1.content).TryGetValue(data.ItemId, out var fishEntry)
                ? fishEntry
                : "";
            string[] fields = raw.Split('/');
            return fields.Length > 1 && fields[1] != "trap";
        }
    }
}
