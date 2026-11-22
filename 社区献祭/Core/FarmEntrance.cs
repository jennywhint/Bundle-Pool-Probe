using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Text.Json;

namespace BundlePoolProbe
{
    /// <summary>农场入口的两件事：
    /// 1) 注册 CP 动态 token（ActualFarmMapName），让 [CP] 包的入口补丁自动适配任何农场地图；
    /// 2) Hub 出口的右键传送（农场坐标来自 CP 配置，是动态值所以不能用地图 Warp 实现）。</summary>
    public class FarmEntrance
    {
        private const string CpPackFolderName = "[CP] BundlePoolProbe";

        private const int HUB_EXIT_X = 26;
        private const int HUB_EXIT_Y = 16;

        private readonly ModEntry _mod;

        public FarmEntrance(ModEntry mod)
        {
            _mod = mod;
        }

        // ===================== CP token =====================
        public void RegisterCpToken()
        {
            var cpApi = _mod.Helper.ModRegistry.GetApi<IContentPatcherApi>("Pathoschild.ContentPatcher");
            if (cpApi != null)
            {
                cpApi.RegisterToken(_mod.ModManifest, "ActualFarmMapName", GetActualFarmMapName);
                _mod.Monitor.Log("已注册 CP token Awassakura.BundlePoolProbe/ActualFarmMapName。", LogLevel.Info);
            }
            else
            {
                _mod.Monitor.Log("未检测到 Content Patcher API，农场入口补丁将依赖 [CP] 包自身。", LogLevel.Warn);
            }
        }

        // 供 [CP] BundlePoolProbe 的 Target 使用：Maps/{{Awassakura.BundlePoolProbe/ActualFarmMapName}}
        // 返回不带 "Maps/" 前缀的资产名（如 Farm / Farm_Fishing / Farm_Meadowlands / 自定义农场地图名）。
        private IEnumerable<string>? GetActualFarmMapName()
        {
            try
            {
                if (Game1.whichFarm < 0)
                    return null;

                // 1. 优先读当前农场实例的真实 mapPath（覆盖存档加载完成后的所有请求，
                //    也覆盖任何模组在加载后改写过 mapPath 的情况）
                var farm = Game1.getFarm();
                string? liveMapPath = farm?.mapPath?.Value;
                if (!string.IsNullOrWhiteSpace(liveMapPath))
                    return new[] { NormalizeFarmMapName(liveMapPath) };

                // 2. 存档加载早期（农场 Location 尚在构造、getFarm() 为 null）：
                //    调用游戏本体同一套推导逻辑，保证与游戏实际加载的地图完全一致。
                //    whichFarm 0~6 = 原版农场；7 = ModFarmType（含官方瑞奇草场和
                //    SpaceCore/FTM 自定义农场，地图名取 Game1.whichModFarm.MapName）。
                string mapName = Farm.getMapNameFromTypeInt(Game1.whichFarm);
                if (!string.IsNullOrWhiteSpace(mapName))
                    return new[] { NormalizeFarmMapName(mapName) };
            }
            catch (Exception ex)
            {
                // token 回调不允许抛异常，否则 CP 会在每次上下文刷新时报错刷屏
                _mod.Monitor.Log($"[CP token] ActualFarmMapName 取值失败：{ex.Message}", LogLevel.Trace);
            }

            return null;
        }

        private static string NormalizeFarmMapName(string mapPath)
        {
            string name = mapPath.Replace('\\', '/');
            const string prefix = "Maps/";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = name[prefix.Length..];
            return name;
        }

        // ===================== Hub 出口右键传送 =====================
        public void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
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

            if (loc.Name == LocationRegistry.HubLocationName && IsAdjacentTo(x, y, HUB_EXIT_X, HUB_EXIT_Y))
            {
                var (cfgX, cfgY) = ReadFarmWarpConfig();
                int targetX = cfgX;
                int targetY = cfgY + 2;
                Game1.warpFarmer("Farm", targetX, targetY, false);
                _mod.Monitor.Log($"Hub → Farm 传送至 ({targetX}, {targetY})", LogLevel.Trace);
                return;
            }
        }

        private static bool IsAdjacentTo(int px, int py, int targetX, int targetY)
        {
            return Math.Abs(px - targetX) <= 1 && Math.Abs(py - targetY) <= 1;
        }

        // ===================== 读取 CP 配置 =====================
        private (int X, int Y) ReadFarmWarpConfig()
        {
            // 兜底值必须和 [CP] BundlePoolProbe/content.json 里 ConfigSchema 的 Default 一致
            const int fallbackX = 55;
            const int fallbackY = 10;

            string? parentDir = Path.GetDirectoryName(_mod.Helper.DirectoryPath);
            if (string.IsNullOrEmpty(parentDir))
            {
                _mod.Monitor.Log($"[诊断] 无法定位 Mods 目录，使用默认坐标 ({fallbackX}, {fallbackY})", LogLevel.Warn);
                return (fallbackX, fallbackY);
            }

            string configPath = Path.Combine(parentDir, CpPackFolderName, "config.json");
            if (!File.Exists(configPath))
            {
                _mod.Monitor.Log($"[诊断] CP config.json 不存在，使用默认坐标 ({fallbackX}, {fallbackY})，期望路径={configPath}", LogLevel.Info);
                return (fallbackX, fallbackY);
            }

            try
            {
                string text = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                int x = ReadIntProperty(root, "Coordinates_X", fallbackX);
                int y = ReadIntProperty(root, "Coordinates_Y", fallbackY);

                _mod.Monitor.Log($"[诊断] 读到 CP config：X={x}, Y={y}，路径={configPath}", LogLevel.Trace);
                return (x, y);
            }
            catch (Exception ex)
            {
                _mod.Monitor.Log($"[诊断] 读取 CP config.json 失败：{ex.Message}，使用默认坐标 ({fallbackX}, {fallbackY})", LogLevel.Warn);
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
    }
}
