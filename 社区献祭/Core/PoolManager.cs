using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace BundlePoolProbe
{
    /// <summary>物品池管理：BundleData 访问、按存档重建物品池、以及 bpp_dump/bpp_rebuild/bpp_rawbundle 管理命令。</summary>
    public class PoolManager
    {
        private const string PoolKey = "BundlePool";

        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;

        public PoolManager(IModHelper helper, IMonitor monitor)
        {
            _helper = helper;
            _monitor = monitor;
        }

        /// <summary>读取存档里的静态物品池（可能为 null：尚未构建）。</summary>
        public Dictionary<string, List<PoolItem>>? ReadPool()
        {
            return _helper.Data.ReadSaveData<Dictionary<string, List<PoolItem>>>(PoolKey);
        }

        /// <summary>把重建好的池写回存档。</summary>
        public void WritePool(Dictionary<string, List<PoolItem>> pool)
        {
            _helper.Data.WriteSaveData(PoolKey, pool);
        }

        public int CountItems(Dictionary<string, List<PoolItem>> pool)
        {
            return pool.Sum(kv => kv.Value.Count);
        }

        public string GetDisplayName(string qualifiedId)
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

        // ===================== BundleData 访问 =====================
        public IDictionary<string, string>? GetBundleData()
        {
            try
            {
                var state = Game1.netWorldState?.Value;
                if (state == null)
                {
                    _monitor.Log("netWorldState 为空。", LogLevel.Warn);
                    return null;
                }

                // 1.6 的公开属性（NetWorldState.BundleData），无需反射私有字段
                return state.BundleData;
            }
            catch (Exception ex)
            {
                _monitor.Log($"读取 BundleData 失败：{ex.Message}", LogLevel.Error);
                return null;
            }
        }

        // ===================== 池构建 =====================
        public Dictionary<string, List<PoolItem>> BuildPool(IDictionary<string, string> bundleData)
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
                            var expanded = ExpandCategory(numericId, bundleName);
                            pool[roomName].AddRange(expanded);
                            continue;
                        }

                        // 其他负数：未知占位符，跳过并记日志
                        if (numericId < 0)
                        {
                            _monitor.Log($"[构建] 跳过未知负数 ID {numericId}（包 {bundleName}）", LogLevel.Trace);
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

        private List<PoolItem> ExpandCategory(int categoryId, string bundleName)
        {
            var result = new List<PoolItem>();

            if (Game1.objectData == null)
            {
                _monitor.Log($"[构建] objectData 未加载，无法展开类别 {categoryId}（包 {bundleName}）", LogLevel.Warn);
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
                _monitor.Log($"[构建] 类别 {categoryId} 在 objectData 中无匹配物品（包 {bundleName}）", LogLevel.Warn);
            }

            return result;
        }

        // ===================== 管理命令 =====================
        public void OnDumpCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var pool = ReadPool();
            if (pool == null || pool.Count == 0)
            {
                _monitor.Log("池为空。", LogLevel.Warn);
                return;
            }

            _monitor.Log($"静态池：{pool.Count} 组，{CountItems(pool)} 个条目。", LogLevel.Info);
            foreach (var room in pool.OrderBy(kv => kv.Key))
            {
                _monitor.Log($"【{room.Key}】", LogLevel.Info);
                foreach (var bundle in room.Value.GroupBy(i => i.BundleName))
                {
                    var entries = bundle
                        .Select(i => $"{GetDisplayName(i.QualifiedItemId)}[{i.QualifiedItemId}]")
                        .Distinct();
                    _monitor.Log($"  {bundle.Key}：{string.Join("、", entries)}", LogLevel.Info);
                }
            }
        }

        public void OnRebuildCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var bundleData = GetBundleData();
            if (bundleData == null || bundleData.Count == 0)
            {
                _monitor.Log("未能读取 BundleData，池未重建。", LogLevel.Warn);
                return;
            }

            var pool = BuildPool(bundleData);
            WritePool(pool);
            _monitor.Log($"池已重建：{pool.Count} 组，{CountItems(pool)} 个条目。", LogLevel.Info);
        }

        public void OnRawBundleCommand(string command, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                _monitor.Log("请先加载一个存档。", LogLevel.Warn);
                return;
            }

            var data = GetBundleData();
            if (data == null)
            {
                _monitor.Log("[诊断] 当前 BundleData 为 null。", LogLevel.Warn);
                return;
            }

            string room = args.Length > 0 ? string.Join(" ", args) : "Crafts Room";

            _monitor.Log($"[诊断] 当前 BundleData 总条目数：{data.Count}，打印房间：{room}", LogLevel.Info);

            var entries = data
                .Where(k => k.Key.StartsWith(room + "/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k =>
                {
                    var parts = k.Key.Split('/');
                    return parts.Length > 1 && int.TryParse(parts[1], out int idx) ? idx : 0;
                });

            foreach (var kv in entries)
            {
                _monitor.Log($"[诊断] {kv.Key} => {kv.Value}", LogLevel.Info);
            }
        }
    }
}
