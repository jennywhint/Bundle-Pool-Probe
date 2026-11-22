using System;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace BundlePoolProbe
{
    /// <summary>逐步解锁判定与入口门控：
    /// 解锁规则与原版进度对齐（CommunityCenter.shouldNoteAppearInArea + 剧情门槛）；
    /// 未解锁的房间：允许踩门进入，但在落地后立刻送回 Hub 并提示（不改地图资产、无轮询开销）。</summary>
    public class UnlockGate
    {
        private readonly ModEntry _mod;

        public UnlockGate(ModEntry mod)
        {
            _mod = mod;
        }

        // 解锁条件：
        //   工艺室   ：找到法师并喝下药水（canReadJunimoText 邮件标记，喝药水事件里授予）
        //   食物储藏/水族箱：原版任意 1 个 bundle 完成（原版 area 0/2 的出现条件）
        //   锅炉房   ：任意 2 个 bundle 完成（原版 area 3 的出现条件）
        //   布告板   ：任意 3 个 bundle 完成（原版 area 5 的出现条件）
        //   通用图   ：迷失献祭可用（abandonedJojaMartAccessible 邮件 = 原版废弃 Joja 超市开门）
        public bool IsRoomUnlocked(string locationName)
        {
            // 开关关闭 = 全部常开，直接放行（生成、清理、入口三处共用此判定）
            if (!_mod.Config.Global.ProgressiveUnlock)
                return true;

            return locationName switch
            {
                "BPP_CraftsRoom" => Game1.player.hasOrWillReceiveMail("canReadJunimoText"),
                "BPP_Pantry" or LocationRegistry.FishTankLocationName => GetCompletedCcBundleCount() >= 1,
                "BPP_BoilerRoom" => GetCompletedCcBundleCount() >= 2,
                "BPP_BulletinBoard" => GetCompletedCcBundleCount() >= 3,
                LocationRegistry.GeneralLocationName => Game1.MasterPlayer.hasOrWillReceiveMail("abandonedJojaMartAccessible"),
                _ => true, // Hub 与未知地点不受限
            };
        }

        private static int GetCompletedCcBundleCount()
        {
            if (Game1.getLocationFromName("CommunityCenter") is not CommunityCenter cc)
                return 6; // 找不到社区中心（极端模组环境）时按全部解锁处理
            return cc.numberOfCompleteBundles();
        }

        public void OnPlayerWarped(object? sender, WarpedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.IsLocalPlayer)
                return;

            var loc = e.NewLocation;
            if (loc == null || string.IsNullOrEmpty(loc.Name))
                return;

            string locationName = loc.Name;
            if (locationName == LocationRegistry.HubLocationName || !LocationRegistry.SpawnLocationNames.Contains(locationName))
                return;

            if (IsRoomUnlocked(locationName))
                return;

            (int returnX, int returnY) = GetHubReturnTile(locationName);
            DelayedAction.functionAfterDelay(() =>
            {
                Game1.warpFarmer(LocationRegistry.HubLocationName, returnX, returnY, false);
                Game1.drawObjectDialogue(_mod.Helper.Translation.Get("locked-room.kickout"));
            }, 150);

            _mod.Monitor.Log($"[解锁] {locationName} 未解锁，已送回 Hub ({returnX},{returnY})。", LogLevel.Trace);
        }

        // 各房间被拒后回到 Hub 的落脚点（门口下一格，避免与门口 Warp 重叠）
        private static (int X, int Y) GetHubReturnTile(string locationName)
        {
            return locationName switch
            {
                "BPP_CraftsRoom" => (6, 7),
                "BPP_Pantry" => (14, 7),
                "BPP_BoilerRoom" => (24, 7),
                LocationRegistry.FishTankLocationName => (34, 7),
                "BPP_BulletinBoard" => (42, 7),
                LocationRegistry.GeneralLocationName => (22, 17),
                _ => (26, 16),
            };
        }
    }
}
