using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using Microsoft.Xna.Framework;

namespace BundlePoolProbe
{
    public class PoolItem
    {
        public string QualifiedItemId { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string BundleName { get; set; } = "";
    }
    public class SpawnLedgerEntry
    {
        public string LocationName { get; set; } = "";
        public int TileX { get; set; }
        public int TileY { get; set; }
        public string QualifiedItemId { get; set; } = "";
        public string BatchDate { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string BundleName { get; set; } = "";
        public string Type { get; set; } = "";  // "Ground" / "Container"
    }
    public class LocationSpawnConfig
    {
        public string LocationName { get; set; } = "";
        public string PoolGroup { get; set; } = "";
        public int DailySpawnCount { get; set; }
    }
    public class GlobalConfig
    {
        public int CleanupDays { get; set; } = 1;
    }

    public class FarmWarpConfig
    {
        public int Coordinates_X { get; set; } = 55;
        public int Coordinates_Y { get; set; } = 10;
    }

    public class ModEntry : Mod
    {
        private const string MailId = "BundlePoolProbe.Mail";
        private const string QueuedKey = "ProbeMailQueued";
        private const string BodyKey = "BundlePoolProbe.MailBody";
        private const string PoolKey = "BundlePool";

        private const string CpPackFolderName = "[CP] BundlePoolProbe";

        private const int HUB_EXIT_X = 26;
        private const int HUB_EXIT_Y = 16;

        private const int BUILD_DELAY_TICKS = 300;  // 5 秒

        private const string LedgerKey = "SpawnLedger";
        private const int SPAWN_DELAY_TICKS = 400;
        private static readonly string[] SpawnLocationNames = new[]
        {
    "BPP_CraftsRoom",
    "BPP_Pantry",
    "BPP_FishTank",
    "BPP_BoilerRoom",
    "BPP_BulletinBoard",
};

        private string? _pendingMailBody;
        private int _buildDelayCounter;
        private bool _isWaitingToBuild;

        private int _spawnDelayCounter;
        private bool _isWaitingToSpawn;



        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;

            helper.ConsoleCommands.Add("bpp_dump", "打印当前存档的静态池内容。", this.OnDumpCommand);
            helper.ConsoleCommands.Add("bpp_rebuild", "从 BundleData 重建静态池并写入存档。", this.OnRebuildCommand);
            helper.ConsoleCommands.Add("bpp_rawbundle", "打印当前 _bundleData 的原始条目。", this.OnRawBundleCommand);
            helper.ConsoleCommands.Add("bpp_ledger", "打印当前存档的生成账本。", this.OnLedgerCommand);
        }

        // ===================== 存档加载：只注册 Location，不碰池 =====================
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            this.EnsureCustomLocations();

            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);
            this.Monitor.Log(
                $"[诊断] SaveLoaded 读到池：{(pool == null ? "null（等待构建）" : $"{pool.Count} 组 / {this.CountItems(pool)} 条目")}",
                LogLevel.Info
            );
        }

        // ===================== 每日开始：池为空则延迟构建 =====================
        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            this.CleanupAllLocations();
            this.ScheduleSpawnForToday();  // ← 新增，独立延迟，和池构建解耦
            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);

            if (pool != null && pool.Count > 0)
            {
                this.Monitor.Log(
                    $"[诊断] DayStarted 读到已有池：{pool.Count} 组 / {this.CountItems(pool)} 条目",
                    LogLevel.Info
                );

                this.TrySendMail(pool);
                return;
            }

            this.Monitor.Log(
                $"[诊断] DayStarted 池为空，延迟 {BUILD_DELAY_TICKS} tick 后构建（等待混合数据就绪）",
                LogLevel.Info
            );

            _buildDelayCounter = 0;
            if (!_isWaitingToBuild)
            {
                _isWaitingToBuild = true;
                this.Helper.Events.GameLoop.UpdateTicked += this.OnDelayedBuild;
            }

        }

        private void OnDelayedBuild(object? sender, UpdateTickedEventArgs e)
        {
            _buildDelayCounter++;
            if (_buildDelayCounter < BUILD_DELAY_TICKS)
                return;

            this.Helper.Events.GameLoop.UpdateTicked -= this.OnDelayedBuild;
            _isWaitingToBuild = false;

            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);
            if (pool != null && pool.Count > 0)
            {
                this.Monitor.Log(
                    $"[诊断] 延迟结束后读到已有池（可能被手动构建）：{pool.Count} 组 / {this.CountItems(pool)} 条目",
                    LogLevel.Info
                );
                this.TrySendMail(pool);
                return;
            }

            var bundleData = this.GetBundleData();
            if (bundleData == null || bundleData.Count == 0)
            {
                this.Monitor.Log("[诊断] 延迟结束后 BundleData 仍为空，放弃构建。", LogLevel.Warn);
                return;
            }

            pool = this.BuildPool(bundleData);
            this.Helper.Data.WriteSaveData(PoolKey, pool);
            this.Monitor.Log(
                $"[诊断] 延迟构建池：{pool.Count} 组 / {this.CountItems(pool)} 条目，已写入 SaveData",
                LogLevel.Info
            );

            this.TrySendMail(pool);
        }
        // ===================== 每日生成 =====================
        private void ScheduleSpawnForToday()
        {
            _spawnDelayCounter = 0;
            if (!_isWaitingToSpawn)
            {
                _isWaitingToSpawn = true;
                this.Helper.Events.GameLoop.UpdateTicked += this.OnDelayedSpawn;
            }
        }

        private void OnDelayedSpawn(object? sender, UpdateTickedEventArgs e)
        {
            _spawnDelayCounter++;
            if (_spawnDelayCounter < SPAWN_DELAY_TICKS)
                return;

            this.Helper.Events.GameLoop.UpdateTicked -= this.OnDelayedSpawn;
            _isWaitingToSpawn = false;

            foreach (string locationName in SpawnLocationNames)
            {
                this.SpawnForLocation(locationName);
            }
        }

        private LocationSpawnConfig? ReadLocationConfig(string locationName)
        {
            string path = $"assets/Config/{locationName}.json";

            LocationSpawnConfig? config;
            try
            {
                config = this.Helper.Data.ReadJsonFile<LocationSpawnConfig>(path);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[生成] 读取 {path} 失败：{ex.Message}", LogLevel.Error);
                return null;
            }

            if (config == null)
            {
                this.Monitor.Log($"[生成] 配置文件不存在或为空：{path}", LogLevel.Warn);
                return null;
            }

            if (string.IsNullOrEmpty(config.LocationName)
                || string.IsNullOrEmpty(config.PoolGroup)
                || config.DailySpawnCount < 0)
            {
                this.Monitor.Log($"[生成] {path} 字段缺失或非法（LocationName={config.LocationName}, PoolGroup={config.PoolGroup}, DailySpawnCount={config.DailySpawnCount}）。", LogLevel.Warn);
                return null;
            }

            return config;
        }

        private GlobalConfig ReadGlobalConfig()
        {
            string path = "assets/Config/Global.json";

            try
            {
                var config = this.Helper.Data.ReadJsonFile<GlobalConfig>(path);
                if (config == null)
                {
                    this.Monitor.Log($"[清理] 全局配置不存在或为空：{path}，使用默认 CleanupDays=1", LogLevel.Warn);
                    return new GlobalConfig { CleanupDays = 1 };
                }
                if (config.CleanupDays < 0)
                {
                    this.Monitor.Log($"[清理] CleanupDays={config.CleanupDays} 非法，视为不清理。", LogLevel.Warn);
                    config.CleanupDays = 0;
                }
                return config;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[清理] 读取 {path} 失败：{ex.Message}，使用默认 CleanupDays=1", LogLevel.Warn);
                return new GlobalConfig { CleanupDays = 1 };
            }
        }
        private void CleanupAllLocations()
        {
            var global = this.ReadGlobalConfig();

            if (global.CleanupDays < 1)
            {
                this.Monitor.Log("[清理] CleanupDays < 1，不自动清理。", LogLevel.Trace);
                return;
            }

            if (Game1.Date.TotalDays % global.CleanupDays != 0)
                return;

            int totalRemoved = 0;

            foreach (string locationName in SpawnLocationNames)
            {
                var location = Game1.getLocationFromName(locationName);
                if (location == null)
                {
                    this.Monitor.Log($"[清理] 找不到 {locationName}，跳过。", LogLevel.Warn);
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
                this.Monitor.Log($"[清理] {locationName}：清理 {toRemove.Count} 个", LogLevel.Info);
            }

            this.Monitor.Log($"[清理] 共清理 {totalRemoved} 个物品（周期 {global.CleanupDays} 天）", LogLevel.Info);
        }

        private void SpawnForLocation(string locationName)
        {
            var config = this.ReadLocationConfig(locationName);
            if (config == null)
            {
                this.Monitor.Log($"[生成] {locationName} 配置不可用，跳过今日生成。", LogLevel.Warn);
                return;
            }

            string todayKey = $"SpawnDone_{config.LocationName}_{Game1.Date.TotalDays}";
            if (this.Helper.Data.ReadSaveData<string>(todayKey) == "1")
            {
                this.Monitor.Log($"[生成] {locationName}：今日已生成过，跳过。", LogLevel.Trace);
                return;
            }

            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);
            if (pool == null || pool.Count == 0)
            {
                this.Monitor.Log($"[生成] {locationName}：池为空，跳过。", LogLevel.Warn);
                return;
            }

            if (!pool.TryGetValue(config.PoolGroup, out var groupItems) || groupItems.Count == 0)
            {
                this.Monitor.Log($"[生成] {locationName}：池中没有 \"{config.PoolGroup}\" 组，跳过。", LogLevel.Warn);
                return;
            }

            var location = Game1.getLocationFromName(config.LocationName);
            if (location == null)
            {
                this.Monitor.Log($"[生成] {locationName}：找不到地点 {config.LocationName}，跳过。", LogLevel.Warn);
                return;
            }

            int spawnCount = config.DailySpawnCount;
            if (spawnCount <= 0)
            {
                this.Monitor.Log($"[生成] {locationName}：DailySpawnCount={spawnCount}，跳过。", LogLevel.Trace);
                return;
            }

            var rng = new Random((int)Game1.Date.TotalDays ^ config.LocationName.GetHashCode());
            int spawned = 0;

            for (int i = 0; i < spawnCount; i++)
            {
                if (this.TrySpawnGroundItem(location, rng, groupItems))
                    spawned++;
                else
                    this.Monitor.Log($"[生成] {locationName}：第 {i + 1} 次生成失败，跳过。", LogLevel.Trace);
            }

            this.Helper.Data.WriteSaveData(todayKey, "1");
            this.Monitor.Log($"[生成] {locationName}：今日生成 {spawned}/{spawnCount} 个。", LogLevel.Info);
        }

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
                this.Monitor.Log($"[生成] 创建地面物品 {poolItem.QualifiedItemId} 失败：{ex.Message}", LogLevel.Warn);
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

        private void OnLedgerCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var ledger = this.Helper.Data.ReadSaveData<List<SpawnLedgerEntry>>(LedgerKey);
            if (ledger == null || ledger.Count == 0)
            {
                this.Monitor.Log("账本为空。", LogLevel.Warn);
                return;
            }

            this.Monitor.Log($"账本共 {ledger.Count} 条：", LogLevel.Info);
            foreach (var entry in ledger)
            {
                this.Monitor.Log(
                    $"  {entry.BatchDate} | {entry.LocationName} ({entry.TileX},{entry.TileY}) | " +
                    $"{entry.QualifiedItemId} | {entry.Type} | {entry.SourceType} | {entry.BundleName}",
                    LogLevel.Info
                );
            }
        }

        // ===================== 邮件发送 =====================
        private void TrySendMail(Dictionary<string, List<PoolItem>> pool)
        {
            if (this.Helper.Data.ReadSaveData<string>(QueuedKey) == "1")
            {
                this.Monitor.Log("此存档已发送过探测邮件，跳过。", LogLevel.Trace);
                return;
            }

            _pendingMailBody = this.BuildMailBodyFromPool(pool);
            this.Helper.Data.WriteSaveData(BodyKey, _pendingMailBody);
            this.Helper.Data.WriteSaveData(QueuedKey, "1");

            this.Helper.GameContent.InvalidateCache("Data/mail");
            Game1.mailbox.Add(MailId);

            this.Monitor.Log(
                "探测邮件已放入邮箱。正文预览：\n" + _pendingMailBody.Replace("^", Environment.NewLine),
                LogLevel.Info
            );
        }

        // ===================== 自定义 Location =====================
        private void EnsureCustomLocations()
        {
            // 按游戏内解锁顺序注册，便于日后排查
            this.RegisterLocation("BPP_Hub");
            this.RegisterLocation("BPP_CraftsRoom");
            this.RegisterLocation("BPP_Pantry");
            this.RegisterLocation("BPP_FishTank");
            this.RegisterLocation("BPP_BoilerRoom");
            this.RegisterLocation("BPP_BulletinBoard");
        }

        private void RegisterLocation(string locationName)
        {
            this.Monitor.Log($"开始注册 {locationName}...", LogLevel.Info);

            if (Game1.getLocationFromName(locationName) != null)
                return;

            string relativePath = $"assets/Maps/{locationName}.tmx";
            string fullPath = Path.Combine(this.Helper.DirectoryPath, "assets", "Maps", $"{locationName}.tmx");

            if (!File.Exists(fullPath))
            {
                this.Monitor.Log($"注册 {locationName} 失败：文件不存在 {fullPath}", LogLevel.Error);
                return;
            }

            try
            {
                string assetName = this.Helper.ModContent.GetInternalAssetName(relativePath).Name;
                var location = new GameLocation(assetName, locationName) { IsOutdoors = true };
                Game1.locations.Add(location);
                this.Monitor.Log($"已注册 {locationName}，资产键：{assetName}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"注册 {locationName} 失败：{ex.GetType().Name} - {ex.Message}，路径 {fullPath}", LogLevel.Error);
            }
        }

        // ===================== 传送 =====================
        private static bool IsAdjacentTo(int px, int py, int targetX, int targetY)
        {
            return Math.Abs(px - targetX) <= 1 && Math.Abs(py - targetY) <= 1;
        }
        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            
            

            if (!Context.IsWorldReady)
                return;

            if (!e.Pressed.Contains(SButton.MouseRight))
                return;

            var player = Game1.player;
            var loc = player.currentLocation;
            if (loc == null)
                return;

            int x = player.TilePoint.X;
            int y = player.TilePoint.Y;

            if (loc.Name == "BPP_Hub" && IsAdjacentTo(x, y, HUB_EXIT_X, HUB_EXIT_Y))
            {
                var (cfgX, cfgY) = this.ReadFarmWarpConfig();
                int targetX = cfgX;
                int targetY = cfgY + 2;
                Game1.warpFarmer("Farm", targetX, targetY, false);
                this.Monitor.Log($"Hub → Farm 传送至 ({targetX}, {targetY})", LogLevel.Trace);
                return;
            }
        }
        

        // ===================== 读取 CP 配置 =====================
        private (int X, int Y) ReadFarmWarpConfig()
        {
            // 兜底值必须和 [CP] BundlePoolProbe/content.json 里 ConfigSchema 的 Default 一致
            const int fallbackX = 55;
            const int fallbackY = 10;

            string? parentDir = Path.GetDirectoryName(this.Helper.DirectoryPath);
            if (string.IsNullOrEmpty(parentDir))
            {
                this.Monitor.Log($"[诊断] 无法定位 Mods 目录，使用默认坐标 ({fallbackX}, {fallbackY})", LogLevel.Warn);
                return (fallbackX, fallbackY);
            }

            string configPath = Path.Combine(parentDir, CpPackFolderName, "config.json");
            if (!File.Exists(configPath))
            {
                this.Monitor.Log($"[诊断] CP config.json 不存在，使用默认坐标 ({fallbackX}, {fallbackY})，期望路径={configPath}", LogLevel.Info);
                return (fallbackX, fallbackY);
            }

            try
            {
                string text = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                int x = ReadIntProperty(root, "Coordinates_X", fallbackX);
                int y = ReadIntProperty(root, "Coordinates_Y", fallbackY);

                this.Monitor.Log($"[诊断] 读到 CP config：X={x}, Y={y}，路径={configPath}", LogLevel.Trace);
                return (x, y);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[诊断] 读取 CP config.json 失败：{ex.Message}，使用默认坐标 ({fallbackX}, {fallbackY})", LogLevel.Warn);
                return (fallbackX, fallbackY);
            }
        }

        private static int ReadIntProperty(JsonElement root, string propertyName, int fallback)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return fallback;

            foreach (var prop in root.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // 兼容 CP 可能存成数字 55 或字符串 "55"
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int n))
                    return n;
                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int s))
                    return s;

                return fallback;
            }

            return fallback;
        }
        // ===================== 邮件资产注入 =====================
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
                return;

            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, string>().Data;
                string body = _pendingMailBody
                    ?? this.Helper.Data.ReadSaveData<string>(BodyKey)
                    ?? "探测邮件正文未生成。";
                data[MailId] = body;
            });
        }

        // ===================== 控制台命令 =====================
        private void OnDumpCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);
            if (pool == null || pool.Count == 0)
            {
                this.Monitor.Log("池为空。", LogLevel.Warn);
                return;
            }

            this.Monitor.Log($"静态池：{pool.Count} 组，{this.CountItems(pool)} 个条目。", LogLevel.Info);
            foreach (var room in pool.OrderBy(kv => kv.Key))
            {
                this.Monitor.Log($"【{room.Key}】", LogLevel.Info);
                foreach (var bundle in room.Value.GroupBy(i => i.BundleName))
                {
                    var entries = bundle
                        .Select(i => $"{this.GetDisplayName(i.QualifiedItemId)}[{i.QualifiedItemId}]")
                        .Distinct();
                    this.Monitor.Log($"  {bundle.Key}：{string.Join("、", entries)}", LogLevel.Info);
                }
            }
        }

        private void OnRebuildCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var bundleData = this.GetBundleData();
            if (bundleData == null || bundleData.Count == 0)
            {
                this.Monitor.Log("未能读取 BundleData，池未重建。", LogLevel.Warn);
                return;
            }

            var pool = this.BuildPool(bundleData);
            this.Helper.Data.WriteSaveData(PoolKey, pool);
            this.Monitor.Log($"池已重建：{pool.Count} 组，{this.CountItems(pool)} 个条目。", LogLevel.Info);
        }

        private void OnRawBundleCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var data = this.GetBundleData();
            if (data == null)
            {
                this.Monitor.Log("[诊断] 当前 BundleData 为 null。", LogLevel.Warn);
                return;
            }

            string room = args.Length > 0 ? string.Join(" ", args) : "Crafts Room";

            this.Monitor.Log($"[诊断] 当前 BundleData 总条目数：{data.Count}，打印房间：{room}", LogLevel.Info);

            var entries = data
                .Where(k => k.Key.StartsWith(room + "/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k =>
                {
                    var parts = k.Key.Split('/');
                    return parts.Length > 1 && int.TryParse(parts[1], out int idx) ? idx : 0;
                });

            foreach (var kv in entries)
            {
                this.Monitor.Log($"[诊断] {kv.Key} => {kv.Value}", LogLevel.Info);
            }
        }

        // ===================== 数据访问 =====================
        private IDictionary<string, string>? GetBundleData()
        {
            try
            {
                var state = Game1.netWorldState?.Value;
                if (state == null)
                {
                    this.Monitor.Log("netWorldState 为空。", LogLevel.Warn);
                    return null;
                }

                var field = state.GetType().GetField(
                    "_bundleData",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                );

                if (field == null)
                {
                    this.Monitor.Log("找不到 _bundleData 字段。", LogLevel.Warn);
                    return null;
                }

                return field.GetValue(state) as IDictionary<string, string>;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"反射读取 _bundleData 失败：{ex.Message}", LogLevel.Error);
                return null;
            }
        }

        // ===================== 池构建 =====================
        private List<PoolItem> ExpandCategory(int categoryId, string bundleName)
        {
            var result = new List<PoolItem>();

            if (Game1.objectData == null)
            {
                this.Monitor.Log($"[构建] objectData 未加载，无法展开类别 {categoryId}（包 {bundleName}）", LogLevel.Warn);
                return result;
            }

            foreach (var pair in Game1.objectData)
            {
                if (pair.Value.Category != categoryId)
                    continue;

                string qualified = ItemRegistry.ManuallyQualifyItemId(pair.Key, ItemRegistry.type_object);
                result.Add(new PoolItem
                {
                    QualifiedItemId = qualified,
                    SourceType = "Vanilla",
                    BundleName = bundleName
                });
            }

            if (result.Count == 0)
            {
                this.Monitor.Log($"[构建] 类别 {categoryId} 在 objectData 中无匹配物品（包 {bundleName}）", LogLevel.Warn);
            }

            return result;
        }

        

        private Dictionary<string, List<PoolItem>> BuildPool(IDictionary<string, string> bundleData)
        {
            var pool = new Dictionary<string, List<PoolItem>>();

            foreach (var entry in bundleData)
            {
                string roomName = entry.Key.Split('/')[0];

                string[] fields = entry.Value.Split('/');
                if (fields.Length < 3)
                    continue;

                string bundleName = fields[0];
                string[] tokens = fields[2].Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                if (!pool.ContainsKey(roomName))
                    pool[roomName] = new List<PoolItem>();

                for (int i = 0; i + 2 < tokens.Length; i += 3)
                {
                    string id = tokens[i];

                    if (int.TryParse(id, out int numericId))
                    {
                        // -1 = 金钱献祭占位符
                        if (numericId == -1)
                            continue;

                        // -2 到 -99 = 类别占位符，展开成具体物品
                        if (numericId >= -99 && numericId <= -2)
                        {
                            var expanded = this.ExpandCategory(numericId, bundleName);
                            pool[roomName].AddRange(expanded);
                            continue;
                        }

                        // 其他负数：未知占位符，跳过并记日志
                        if (numericId < 0)
                        {
                            this.Monitor.Log($"[构建] 跳过未知负数 ID {numericId}（包 {bundleName}）", LogLevel.Trace);
                            continue;
                        }
                    }

                    string qualified = ItemRegistry.ManuallyQualifyItemId(id, ItemRegistry.type_object);

                    pool[roomName].Add(new PoolItem
                    {
                        QualifiedItemId = qualified,
                        SourceType = "Vanilla",
                        BundleName = bundleName
                    });
                }
            }

            return pool;
        }

        // ===================== 邮件正文 =====================
        private string BuildMailBodyFromPool(Dictionary<string, List<PoolItem>> pool)
        {
            var sb = new StringBuilder();
            sb.AppendLine("当前存档献祭包需要的物品：");
            sb.AppendLine();

            foreach (var room in pool.OrderBy(kv => kv.Key))
            {
                sb.AppendLine($"【{room.Key}】");

                foreach (var bundle in room.Value.GroupBy(i => i.BundleName))
                {
                    var names = bundle
                        .Select(i => this.GetDisplayName(i.QualifiedItemId))
                        .Distinct()
                        .ToList();

                    if (names.Count == 0)
                        continue;

                    sb.AppendLine($"{bundle.Key}：{string.Join("、", names)}");
                }

                sb.AppendLine();
            }

            return sb.ToString().Replace("\r\n", "^").Replace("\n", "^");
        }

        // ===================== 工具 =====================
        private int CountItems(Dictionary<string, List<PoolItem>> pool)
        {
            return pool.Sum(kv => kv.Value.Count);
        }

        private string GetDisplayName(string qualifiedId)
        {
            try
            {
                var data = ItemRegistry.GetData(qualifiedId);
                return data?.DisplayName ?? $"({qualifiedId})";
            }
            catch
            {
                return $"({qualifiedId})";
            }
        }
    }
}