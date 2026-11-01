using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace BundlePoolProbe
{
    public class PoolItem
    {
        public string QualifiedItemId { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string BundleName { get; set; } = "";
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


    public class ModEntry : Mod
    {
        private const string PoolKey = "BundlePool";

        private const string CpPackFolderName = "[CP] BundlePoolProbe";

        private const int HUB_EXIT_X = 26;
        private const int HUB_EXIT_Y = 16;

        private const int BUILD_DELAY_TICKS = 300;  // 5 秒

        private const int SPAWN_DELAY_TICKS = 400;
        private static readonly string[] SpawnLocationNames = new[]
        {
    "BPP_CraftsRoom",
    "BPP_Pantry",
    "BPP_FishTank",
    "BPP_BoilerRoom",
    "BPP_BulletinBoard",
};

        private int _buildDelayCounter;
        private bool _isWaitingToBuild;

        private int _spawnDelayCounter;
        private bool _isWaitingToSpawn;
        private List<string> _fishTankFishIds = new();
        private int _fishTankCreateCounter;
        private bool _isWaitingToCreateFishTank;
        private GlobalConfig _globalConfig = new();
        private Dictionary<string, LocationSpawnConfig> _locationConfigs = new();



        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.Content.AssetRequested += this.OnAssetRequestedForFish;

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            helper.ConsoleCommands.Add("bpp_dump", "打印当前存档的静态池内容。", this.OnDumpCommand);
            helper.ConsoleCommands.Add("bpp_rebuild", "从 BundleData 重建静态池并写入存档。", this.OnRebuildCommand);
            helper.ConsoleCommands.Add("bpp_rawbundle", "打印当前 _bundleData 的原始条目。", this.OnRawBundleCommand);
        }
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            // 1. 启动时一次性加载所有 JSON 到缓存
            this.LoadAllConfigsToCache();

            // 2. 尝试获取 GMCM API
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
            {
                this.Monitor.Log("未检测到 GMCM，配置将仅从 JSON 读取。", LogLevel.Info);
                return;
            }

            // 3. 注册菜单
            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.ResetAllConfigsToDefault(),
                save: () => this.SaveAllConfigsToDisk()
            );

            // 4. 添加全局清理周期
            configMenu.AddSectionTitle(this.ModManifest,
    () => this.Helper.Translation.Get("gmcm.section.global"));

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this._globalConfig.CleanupDays,
                setValue: value => this._globalConfig.CleanupDays = value,
                name: () => this.Helper.Translation.Get("gmcm.cleanup-days.name"),
                tooltip: () => this.Helper.Translation.Get("gmcm.cleanup-days.tooltip"),
                min: 0,
                max: 28,
                interval: 1
            );

            foreach (string locationName in SpawnLocationNames)
            {
                if (!this._locationConfigs.TryGetValue(locationName, out var locConfig))
                    continue;

                var capturedConfig = locConfig;
                configMenu.AddSectionTitle(this.ModManifest, () => locationName);
                configMenu.AddNumberOption(
                    mod: this.ModManifest,
                    getValue: () => capturedConfig.DailySpawnCount,
                    setValue: value => capturedConfig.DailySpawnCount = value,
                    name: () => this.Helper.Translation.Get("gmcm.spawn-count.name"),
                    tooltip: () => this.Helper.Translation.Get(
                        "gmcm.spawn-count.tooltip",
                        new { locationName }),
                    min: 0,
                    max: 50,
                    interval: 1
                );
            }
        }
        private void LoadAllConfigsToCache()
        {
            // 全局配置
            var globalPath = "assets/Config/Global.json";
            this._globalConfig = this.Helper.Data.ReadJsonFile<GlobalConfig>(globalPath)
                                 ?? new GlobalConfig { CleanupDays = 1 };
            if (this._globalConfig.CleanupDays < 0)
            {
                this.Monitor.Log($"Global.json 中 CleanupDays={this._globalConfig.CleanupDays} 非法，已设为 0（不清理）。", LogLevel.Warn);
                this._globalConfig.CleanupDays = 0;
            }

            // 子地图配置
            this._locationConfigs.Clear();
            foreach (string locationName in SpawnLocationNames)
            {
                string path = $"assets/Config/{locationName}.json";
                var config = this.Helper.Data.ReadJsonFile<LocationSpawnConfig>(path);
                if (config == null)
                {
                    this.Monitor.Log($"子地图配置缺失或为空：{path}，将使用默认值。", LogLevel.Warn);
                    config = new LocationSpawnConfig
                    {
                        LocationName = locationName,
                        PoolGroup = this.GetDefaultPoolGroup(locationName),
                        DailySpawnCount = 10
                    };
                }
                this._locationConfigs[locationName] = config;
            }
        }

        private void ResetAllConfigsToDefault()
        {
            this._globalConfig = new GlobalConfig { CleanupDays = 1 };
            foreach (string locationName in SpawnLocationNames)
            {
                if (this._locationConfigs.TryGetValue(locationName, out var config))
                {
                    config.DailySpawnCount = 10; // 只重置玩家可改的字段，PoolGroup 不动
                }
            }
        }

        private void SaveAllConfigsToDisk()
        {
            this.Helper.Data.WriteJsonFile("assets/Config/Global.json", this._globalConfig);
            foreach (var kv in this._locationConfigs)
            {
                this.Helper.Data.WriteJsonFile($"assets/Config/{kv.Key}.json", kv.Value);
            }
            this.Monitor.Log("GMCM 配置已保存回 JSON 文件。", LogLevel.Info);
        }

        private string GetDefaultPoolGroup(string locationName)
        {
            return locationName switch
            {
                "BPP_CraftsRoom" => "Crafts Room",
                "BPP_Pantry" => "Pantry",
                "BPP_FishTank" => "Fish Tank",
                "BPP_BoilerRoom" => "Boiler Room",
                "BPP_BulletinBoard" => "Bulletin Board",
                _ => locationName
            };
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
            this.PrepareFishTank();
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
            this.PrepareFishTank();

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
            return this._locationConfigs.TryGetValue(locationName, out var config) ? config : null;
        }

        private GlobalConfig ReadGlobalConfig()
        {
            return this._globalConfig;
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

            // 仅对 FishTank 生效：排除鱼类，让鱼只出现在水体里
            if (config.LocationName == "BPP_FishTank")
            {
                groupItems = groupItems
                    .Where(item =>
                    {
                        if (item.BundleName == "Crab Pot") return true;
                        var data = ItemRegistry.GetData(item.QualifiedItemId);
                        return data?.Category != -4;
                    })
                    .ToList();

                if (groupItems.Count == 0)
                {
                    this.Monitor.Log($"[生成] {locationName}：过滤鱼类后地面池为空，跳过。", LogLevel.Warn);
                    return;
                }
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


        // ===================== 自定义 Location =====================
        private void EnsureCustomLocations()
        {
            // 按游戏内解锁顺序注册，便于日后排查
            this.RegisterLocation("BPP_Hub");
            this.RegisterLocation("BPP_CraftsRoom");
            this.RegisterLocation("BPP_Pantry");


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

        private void PrepareFishTank()
        {
            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);
            if (pool == null || !pool.TryGetValue("Fish Tank", out var fishTankItems))
            {
                _fishTankFishIds.Clear();
                return;
            }

            _fishTankFishIds.Clear();
            foreach (var item in fishTankItems)
            {
                if (item.BundleName == "Crab Pot")
                    continue;
                var data = ItemRegistry.GetData(item.QualifiedItemId);
                if (data?.Category == -4)
                    _fishTankFishIds.Add(item.QualifiedItemId.Replace("(O)", ""));
            }

            if (_fishTankFishIds.Count == 0)
            {
                this.Monitor.Log("[鱼群] 池中没有鱼类，跳过。", LogLevel.Warn);
                return;
            }

            // 通知游戏重新加载 Data/Locations，让 OnAssetRequestedForFish 把鱼写进去
            this.Helper.GameContent.InvalidateCache("Data/Locations");
            this.Monitor.Log($"[鱼群] 从池中提取到 {_fishTankFishIds.Count} 条鱼，等待创建 BPP_FishTank。", LogLevel.Info);

            _fishTankCreateCounter = 0;
            if (!_isWaitingToCreateFishTank)
            {
                _isWaitingToCreateFishTank = true;
                this.Helper.Events.GameLoop.UpdateTicked += this.OnDelayedCreateFishTank;
            }
        }

        private void OnDelayedCreateFishTank(object? sender, UpdateTickedEventArgs e)
        {
            _fishTankCreateCounter++;
            if (_fishTankCreateCounter < 200)
                return;

            this.Helper.Events.GameLoop.UpdateTicked -= this.OnDelayedCreateFishTank;
            _isWaitingToCreateFishTank = false;

            if (Game1.getLocationFromName("BPP_FishTank") == null)
            {
                this.RegisterLocation("BPP_FishTank");
                this.Monitor.Log("[鱼群] 鱼群数据就绪后，已创建 BPP_FishTank 地点。", LogLevel.Info);
            }
        }
        private void OnAssetRequestedForFish(object? sender, AssetRequestedEventArgs e)
        {
            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
                return;

            e.Edit(asset =>
            {
                // 1. 获取或创建 BPP_FishTank 的 LocationData 实例
                LocationData locData;
                var data = asset.AsDictionary<string, LocationData>().Data;

                if (data.TryGetValue("BPP_FishTank", out var existingData))
                {
                    locData = existingData; // 已存在（可能被其他模组添加），直接使用
                }
                else
                {
                    // 2. 通过反射创建 LocationData 新实例
                    var locationDataType = typeof(LocationData);
                    locData = (LocationData)Activator.CreateInstance(locationDataType)!;
                    data["BPP_FishTank"] = locData;
                    this.Monitor.Log("[鱼群] 已在 Data/Locations 中动态创建 BPP_FishTank 条目。", LogLevel.Info);
                }

                // 确保基础字段存在
                if (string.IsNullOrEmpty(locData.DisplayName))
                    locData.DisplayName = "Bundle Fish Tank";

                // 确保 Fish 列表已初始化
                locData.Fish ??= new List<SpawnFishData>();

                // 4. 清空旧数据，填入池中的鱼
                locData.Fish.Clear();
                if (_fishTankFishIds.Count == 0)
                    return;

                foreach (string fishId in _fishTankFishIds)
                {
                    locData.Fish.Add(new SpawnFishData
                    {
                        ItemId = fishId,       // 鱼ID (如 "128")
                        FishAreaId = null,// 关键修正：字段名是 FishAreaId，且是字符串类型
                        Chance = 1f            // 等概率
                    });
                }

                this.Monitor.Log($"[鱼群] 已向 BPP_FishTank 写入 {locData.Fish.Count} 条鱼。", LogLevel.Info);

            }, AssetEditPriority.Early); // 在 Late 阶段执行，确保覆盖其他模组的默认设置
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