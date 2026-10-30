using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BundlePoolProbe
{
    // 池里的单个条目：带限定物品 ID、来源类型、所属献祭包名
    public class PoolItem
    {
        public string QualifiedItemId { get; set; } = "";
        public string SourceType { get; set; } = "";
        public string BundleName { get; set; } = "";
    }

    public class ModEntry : Mod
    {
        private const string MailId = "BundlePoolProbe.Mail";
        private const string QueuedKey = "ProbeMailQueued";
        private const string BodyKey = "BundlePoolProbe.MailBody";
        private const string PoolKey = "BundlePool";

        private string? _pendingMailBody;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;

            helper.ConsoleCommands.Add("bpp_dump", "打印当前存档的静态池内容。", this.OnDumpCommand);
            helper.ConsoleCommands.Add("bpp_rebuild", "从 BundleData 重建静态池并写入存档。", this.OnRebuildCommand);
        }

        // ===================== 存档加载 =====================
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            var pool = this.Helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);

            if (pool == null || pool.Count == 0)
            {
                var bundleData = this.GetBundleData();
                if (bundleData == null || bundleData.Count == 0)
                {
                    this.Monitor.Log("未能读取 BundleData，池未构建。", LogLevel.Warn);
                    return;
                }

                pool = this.BuildPool(bundleData);
                this.Helper.Data.WriteSaveData(PoolKey, pool);
                this.Monitor.Log($"池已构建并写入存档：{pool.Count} 组，{this.CountItems(pool)} 个条目。", LogLevel.Info);
            }
            else
            {
                this.Monitor.Log($"从存档读取池：{pool.Count} 组，{this.CountItems(pool)} 个条目。", LogLevel.Info);
            }

            // 邮件：从池构建正文，只发一次
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

                    // -1 是金钱献祭的占位符，不进池
                    if (id == "-1")
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