using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace BundlePoolProbe
{
    /// <summary>组合根：创建各功能管理器并挂接事件与控制台命令，自身不承载业务逻辑。
    ///
    /// 职责分工（详见各文件头注释）：
    ///   DebugLogger          —— 调试日志开关（bpp_debug）
    ///   ConfigManager        —— 配置读取/保存/重置（config.json 优先，assets/Config 兜底）
    ///   PoolManager          —— BundleData 访问、物品池构建、bpp_dump/rebuild/rawbundle 命令
    ///   SpawnManager         —— 每日循环：清理、延迟建池、延迟生成、逐房间地面物品
    ///   LocationsDataManager —— Data/Locations 动态条目（7 地点）+ 鱼群分类/提取/直写
    ///   LocationRegistrar    —— 手动注册地点（CreateOnLoad 自动创建的兜底）
    ///   UnlockGate           —— 逐步解锁判定 + 未解锁房间的入口门控
    ///   FarmEntrance         —— CP token（农场地图自动适配）+ Hub 出口右键传送
    ///   GmcMenuBuilder       —— GMCM 菜单构建
    /// </summary>
    public class ModEntry : Mod
    {
        public DebugLogger Debug { get; private set; } = null!;
        public ConfigManager Config { get; private set; } = null!;
        public PoolManager Pool { get; private set; } = null!;
        public LocationsDataManager Fish { get; private set; } = null!;
        public UnlockGate Unlock { get; private set; } = null!;

        private LocationRegistrar _registrar = null!;
        private SpawnManager _spawn = null!;
        private FarmEntrance _entrance = null!;
        private GmcMenuBuilder _gmcm = null!;

        public override void Entry(IModHelper helper)
        {
            // 组合根：按依赖顺序创建（管理器之间通过本类引用协作）
            Debug = new DebugLogger(this.Monitor);
            Config = new ConfigManager(helper, this.Monitor);
            Pool = new PoolManager(helper, this.Monitor);
            Fish = new LocationsDataManager(this);
            Unlock = new UnlockGate(this);
            _registrar = new LocationRegistrar(this);
            _spawn = new SpawnManager(this);
            _entrance = new FarmEntrance(this);
            _gmcm = new GmcMenuBuilder(this, Config);

            // 事件挂接（各处理器定义在对应管理器内）
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += this._spawn.OnDayStarted;
            helper.Events.Input.ButtonsChanged += this._entrance.OnButtonsChanged;
            helper.Events.Content.AssetRequested += this.Fish.OnAssetRequestedForLocations;
            helper.Events.Player.Warped += this.Unlock.OnPlayerWarped;
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

            // 控制台命令
            helper.ConsoleCommands.Add("bpp_dump", "打印当前存档的静态池内容。", this.Pool.OnDumpCommand);
            helper.ConsoleCommands.Add("bpp_rebuild", "从 BundleData 重建静态池并写入存档。", this.Pool.OnRebuildCommand);
            helper.ConsoleCommands.Add("bpp_rawbundle", "打印指定房间的原始捆绑包条目。", this.Pool.OnRawBundleCommand);
            helper.ConsoleCommands.Add("bpp_debug", "切换调试日志（Info 级详细输出，仅当前会话有效）。", (command, args) => this.Debug.Toggle());
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            // 1. 配置加载（GMCM 与各管理器都依赖它）
            this.Config.LoadAll();

            // 2. 注册 CP 动态 token：农场实际地图资产名（供 [CP] 包的 Target 使用）
            this._entrance.RegisterCpToken();

            // 3. GMCM 菜单
            this._gmcm.Register();
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            // 地点注册兜底（正常情况下 CreateOnLoad 已自动创建）
            this._registrar.EnsureCustomLocations();

            var pool = this.Pool.ReadPool();
            this.Monitor.Log(
                $"[诊断] SaveLoaded 读到池：{(pool == null ? "null（等待构建）" : $"{pool.Count} 组 / {this.Pool.CountItems(pool)} 条目")}",
                LogLevel.Info
            );

            // 从池中提取鱼竿鱼并刷新全部地点的 Data/Locations 条目
            this.Fish.RefreshFishData(pool);
        }
    }
}
