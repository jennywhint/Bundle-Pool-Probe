using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BundlePoolProbe
{
    /// <summary>每日循环：清理、延迟建池（新档首日等混合数据就绪）、延迟生成、逐房间地面物品生成。</summary>
    public class SpawnManager
    {
        private const int BUILD_DELAY_TICKS = 300;  // 5 秒
        private const int SPAWN_DELAY_TICKS = 400;

        private readonly ModEntry _mod;

        private int _buildDelayCounter;
        private bool _isWaitingToBuild;

        private int _spawnDelayCounter;
        private bool _isWaitingToSpawn;

        public SpawnManager(ModEntry mod)
        {
            _mod = mod;
        }

        // ===================== 每日开始：清理 + 生成 + 池为空则延迟构建 =====================
        public void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            CleanupAllLocations();
            ScheduleSpawnForToday();

            var pool = _mod.Pool.ReadPool();
            _mod.Monitor.Log(
                $"[诊断] DayStarted 读到已有池：{(pool == null ? "null" : $"{pool.Count} 组 / {_mod.Pool.CountItems(pool)} 条目")}",
                LogLevel.Info
            );

            if (pool != null && pool.Count > 0)
                return;

            _mod.Monitor.Log(
                $"[诊断] DayStarted 池为空，延迟 {BUILD_DELAY_TICKS} tick 后构建（等待混合数据就绪）",
                LogLevel.Info
            );

            _buildDelayCounter = 0;
            if (!_isWaitingToBuild)
            {
                _isWaitingToBuild = true;
                _mod.Helper.Events.GameLoop.UpdateTicked += OnDelayedBuild;
            }
        }

        private void OnDelayedBuild(object? sender, UpdateTickedEventArgs e)
        {
            _buildDelayCounter++;
            if (_buildDelayCounter < BUILD_DELAY_TICKS)
                return;

            _mod.Helper.Events.GameLoop.UpdateTicked -= OnDelayedBuild;
            _isWaitingToBuild = false;

            var pool = _mod.Pool.ReadPool();
            if (pool != null && pool.Count > 0)
            {
                _mod.Monitor.Log(
                    $"[诊断] 延迟结束后读到已有池（可能被手动构建）：{pool.Count} 组 / {_mod.Pool.CountItems(pool)} 条目",
                    LogLevel.Info
                );
                return;
            }

            var bundleData = _mod.Pool.GetBundleData();
            if (bundleData == null || bundleData.Count == 0)
            {
                _mod.Monitor.Log("[诊断] 延迟结束后 BundleData 仍为空，放弃构建。", LogLevel.Warn);
                return;
            }

            pool = _mod.Pool.BuildPool(bundleData);
            _mod.Pool.WritePool(pool);
            _mod.Monitor.Log(
                $"[诊断] 延迟构建池：{pool.Count} 组 / {_mod.Pool.CountItems(pool)} 条目，已写入 SaveData",
                LogLevel.Info
            );

            // 新档首日时序修复：SaveLoaded 时池还没建好、已错过鱼群提取；
            // 这里补一次提取 + 刷新 Data/Locations，否则首次进档钓不到池鱼
            _mod.Fish.RefreshFishData(pool);
        }

        // ===================== 每日生成 =====================
        private void ScheduleSpawnForToday()
        {
            _spawnDelayCounter = 0;
            if (!_isWaitingToSpawn)
            {
                _isWaitingToSpawn = true;
                _mod.Helper.Events.GameLoop.UpdateTicked += OnDelayedSpawn;
            }
        }

        private void OnDelayedSpawn(object? sender, UpdateTickedEventArgs e)
        {
            _spawnDelayCounter++;
            if (_spawnDelayCounter < SPAWN_DELAY_TICKS)
                return;

            _mod.Helper.Events.GameLoop.UpdateTicked -= OnDelayedSpawn;
            _isWaitingToSpawn = false;

            foreach (string locationName in LocationRegistry.SpawnLocationNames)
            {
                SpawnForLocation(locationName);
            }
        }

        // ===================== 清理 =====================
        private void CleanupAllLocations()
        {
            var global = _mod.Config.Global;

            if (global.CleanupDays < 1)
            {
                _mod.Monitor.Log("[清理] CleanupDays < 1，不自动清理。", LogLevel.Trace);
                return;
            }

            if (Game1.Date.TotalDays % global.CleanupDays != 0)
                return;

            int totalRemoved = 0;

            foreach (string locationName in LocationRegistry.SpawnLocationNames)
            {
                // 房间未解锁时不清理（条件未满足时不生成、不清理）
                if (!_mod.Unlock.IsRoomUnlocked(locationName))
                {
                    _mod.Monitor.Log($"[清理] {locationName}：房间未解锁，跳过。", LogLevel.Trace);
                    continue;
                }

                var location = Game1.getLocationFromName(locationName);
                if (location == null)
                {
                    _mod.Monitor.Log($"[清理] 找不到 {locationName}，跳过。", LogLevel.Warn);
                    continue;
                }

                var toRemove = new List<Vector2>();
                foreach (var pair in location.objects.Pairs)
                {
                    if (pair.Value.IsSpawnedObject)
                        toRemove.Add(pair.Key);
                }

                foreach (var tile in toRemove)
                    location.objects.Remove(tile);

                totalRemoved += toRemove.Count;
                _mod.Monitor.Log($"[清理] {locationName}：清理 {toRemove.Count} 个", LogLevel.Info);
            }

            _mod.Monitor.Log($"[清理] 共清理 {totalRemoved} 个物品（周期 {global.CleanupDays} 天）", LogLevel.Info);
        }

        // ===================== 逐房间生成 =====================
        private void SpawnForLocation(string locationName)
        {
            var config = _mod.Config.GetRoomConfig(locationName);
            if (config == null)
            {
                _mod.Monitor.Log($"[生成] {locationName} 配置不可用，跳过今日生成。", LogLevel.Warn);
                return;
            }

            // 房间未解锁时不生成、也不写日期键（解锁当天次日才会正常开始生成）
            if (!_mod.Unlock.IsRoomUnlocked(locationName))
            {
                _mod.Monitor.Log($"[生成] {locationName}：房间未解锁，跳过今日生成。", LogLevel.Trace);
                return;
            }

            // 单键记录"最后生成日期"，每张地图永远只占 1 个键（替代累积式 SpawnDone_{loc}_{day}）
            string lastDayKey = $"SpawnLastDay_{config.LocationName}";
            string today = Game1.Date.TotalDays.ToString();
            if (_mod.Helper.Data.ReadSaveData<string>(lastDayKey) == today)
            {
                _mod.Monitor.Log($"[生成] {locationName}：今日已生成过，跳过。", LogLevel.Trace);
                return;
            }

            var pool = _mod.Pool.ReadPool();
            if (pool == null || pool.Count == 0)
            {
                _mod.Monitor.Log($"[生成] {locationName}：池为空，跳过。", LogLevel.Warn);
                return;
            }

            List<PoolItem> groupItems;
            if (config.PoolGroup == LocationRegistry.GeneralPoolGroup)
            {
                // 通用池：聚合所有未映射到固定子地图的房间组
                //（如 Abandoned Joja Mart / 迷失献祭包，以及任何模组新增的献祭房间）
                groupItems = pool
                    .Where(kv => !LocationRegistry.FixedPoolGroups.Contains(kv.Key))
                    .SelectMany(kv => kv.Value)
                    .ToList();

                // GMCM 开关：同一物品出现在多个未映射包里时，去重（只算一份）或保留（权重更高）
                if (_mod.Config.Global.GeneralPoolDedup)
                {
                    int before = groupItems.Count;
                    groupItems = groupItems.DistinctBy(i => i.QualifiedItemId).ToList();
                    _mod.Debug.Log($"[生成] {locationName}：通用池去重 {before} → {groupItems.Count}。");
                }

                // 通用房同样区分：鱼竿鱼只进水体鱼群（LocationsDataManager 处理），地面只出其余物品
                groupItems = groupItems
                    .Where(item => !_mod.Fish.IsRodFish(item.QualifiedItemId))
                    .ToList();

                if (groupItems.Count == 0)
                {
                    _mod.Monitor.Log($"[生成] {locationName}：通用池过滤鱼竿鱼后为空，跳过。", LogLevel.Warn);
                    return;
                }

                _mod.Monitor.Log($"[生成] {locationName}：通用池聚合 {groupItems.Count} 条物品。", LogLevel.Trace);
            }
            else if (!pool.TryGetValue(config.PoolGroup, out groupItems) || groupItems.Count == 0)
            {
                _mod.Monitor.Log($"[生成] {locationName}：池中没有 \"{config.PoolGroup}\" 组，跳过。", LogLevel.Warn);
                return;
            }

            // FishTank：鱼竿鱼只进水体鱼群（LocationsDataManager 处理）；
            // 蟹笼渔获（-4 鱼类但 Data/Fish 标记 trap）和非鱼类物品留在地面池。
            // 注意不能用 Category != -4 过滤——蟹笼渔获也是 -4，会把地面池清空。
            if (config.LocationName == LocationRegistry.FishTankLocationName)
            {
                groupItems = groupItems
                    .Where(item => !_mod.Fish.IsRodFish(item.QualifiedItemId))
                    .ToList();

                if (groupItems.Count == 0)
                {
                    _mod.Monitor.Log($"[生成] {locationName}：过滤鱼竿鱼后地面池为空，跳过。", LogLevel.Warn);
                    return;
                }
            }

            var location = Game1.getLocationFromName(config.LocationName);
            if (location == null)
            {
                _mod.Monitor.Log($"[生成] {locationName}：找不到地点 {config.LocationName}，跳过。", LogLevel.Warn);
                return;
            }

            int spawnCount = config.DailySpawnCount;
            if (spawnCount <= 0)
            {
                _mod.Monitor.Log($"[生成] {locationName}：DailySpawnCount={spawnCount}，跳过。", LogLevel.Trace);
                return;
            }

            // 稳定哈希代替 GetHashCode（.NET 中字符串哈希每进程随机化，跨会话不可复现，多人下各端还会不一致）
            var rng = new Random((int)Game1.Date.TotalDays ^ GetStableHashCode(config.LocationName));
            int spawned = 0;

            for (int i = 0; i < spawnCount; i++)
            {
                if (TrySpawnGroundItem(location, rng, groupItems))
                    spawned++;
                else
                    _mod.Monitor.Log($"[生成] {locationName}：第 {i + 1} 次生成失败，跳过。", LogLevel.Trace);
            }

            _mod.Helper.Data.WriteSaveData(lastDayKey, today);
            _mod.Monitor.Log($"[生成] {locationName}：今日生成 {spawned}/{spawnCount} 个。", LogLevel.Info);
        }

        // ===================== 地面物品 =====================
        private bool TrySpawnGroundItem(GameLocation location, Random rng, List<PoolItem> pool)
        {
            if (pool.Count == 0)
                return false;

            if (!TryFindEmptyTile(location, rng, out int tx, out int ty))
                return false;

            var poolItem = pool[rng.Next(pool.Count)];

            StardewValley.Object? item = null;
            try
            {
                item = ItemRegistry.Create<StardewValley.Object>(poolItem.QualifiedItemId, 1);
            }
            catch (Exception ex)
            {
                _mod.Monitor.Log($"[生成] 创建地面物品 {poolItem.QualifiedItemId} 失败：{ex.Message}", LogLevel.Warn);
                return false;
            }

            if (item == null)
                return false;

            item.IsSpawnedObject = true;
            return location.dropObject(item, new Vector2(tx, ty) * Game1.tileSize, Game1.viewport, true, null);
        }

        private bool TryFindEmptyTile(GameLocation location, Random rng, out int tx, out int ty)
        {
            const int maxAttempts = 50;
            int width = location.map.Layers[0].LayerWidth;
            int height = location.map.Layers[0].LayerHeight;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int x = rng.Next(width);
                int y = rng.Next(height);
                if (IsValidSpawnTile(location, x, y))
                {
                    tx = x;
                    ty = y;
                    return true;
                }
            }

            tx = -1;
            ty = -1;
            return false;
        }

        private static bool IsValidSpawnTile(GameLocation location, int x, int y)
        {
            var tile = new Vector2(x, y);

            if (!location.isTileOnMap(tile))
                return false;

            if (location.objects.ContainsKey(tile))
                return false;

            if (location.terrainFeatures.ContainsKey(tile))
                return false;

            string? waterProp = location.doesTileHaveProperty(x, y, "Water", "Back");
            if (!string.IsNullOrEmpty(waterProp))
                return false;

            if (!location.isTilePassable(tile))
                return false;

            return true;
        }

        // FNV-1a 稳定哈希：String.GetHashCode 在 .NET 中每进程随机化，跨会话不可复现，
        // 同一天重进游戏/多人各端的生成序列会漂移；改用与进程无关的稳定哈希做随机种子
        private static int GetStableHashCode(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }

                return (int)hash;
            }
        }
    }
}
