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

namespace BundlePoolProbe
{
    public class PoolItem
    {
        public string QualifiedItemId { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string BundleName { get; set; } = "";
    }

    public class FarmWarpConfig
    {
        public int Coordinates_X { get; set; } = 45;
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

        private string? _pendingMailBody;
        private int _buildDelayCounter;
        private bool _isWaitingToBuild;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;

            helper.ConsoleCommands.Add("bpp_dump", "打印当前存档的静态池内容。", this.OnDumpCommand);
            helper.ConsoleCommands.Add("bpp_rebuild", "从 BundleData 重建静态池并写入存档。", this.OnRebuildCommand);
            helper.ConsoleCommands.Add("bpp_rawbundle", "打印当前 _bundleData 的原始条目。", this.OnRawBundleCommand);
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

            if (loc.Name == "BPP_Hub" && x == HUB_EXIT_X && y == HUB_EXIT_Y)
            {
                var cfg = this.ReadFarmWarpConfig();
                int targetX = cfg.Coordinates_X;
                int targetY = cfg.Coordinates_Y + 2;
                Game1.warpFarmer("Farm", targetX, targetY, false);
                this.Monitor.Log($"Hub → Farm 传送至 ({targetX}, {targetY})", LogLevel.Trace);
                return;
            }
        }

        // ===================== 读取 CP 配置 =====================
        private FarmWarpConfig ReadFarmWarpConfig()
        {
            var defaults = new FarmWarpConfig { Coordinates_X = 45, Coordinates_Y = 10 };

            string? parentDir = Path.GetDirectoryName(this.Helper.DirectoryPath);
            if (string.IsNullOrEmpty(parentDir))
            {
                this.Monitor.Log("无法定位 Mods 目录，使用默认坐标 (45, 10)。", LogLevel.Warn);
                return defaults;
            }

            string configPath = Path.Combine(parentDir, CpPackFolderName, "config.json");
            if (!File.Exists(configPath))
            {
                this.Monitor.Log("CP 包 config.json 不存在，使用默认坐标 (45, 10)。", LogLevel.Trace);
                return defaults;
            }

            try
            {
                string text = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cfg = JsonSerializer.Deserialize<FarmWarpConfig>(text, options);
                return cfg ?? defaults;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"读取 CP config.json 失败：{ex.Message}，使用默认坐标。", LogLevel.Warn);
                return defaults;
            }
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

                    // 跳过所有负数 ID（-1 = 金钱，-5/-6 = 配方等特殊占位符）
                    if (int.TryParse(id, out int numericId) && numericId < 0)
                        continue;

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