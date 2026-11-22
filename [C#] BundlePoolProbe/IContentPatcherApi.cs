using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace BundlePoolProbe
{
    /// <summary>Content Patcher 的动态 token 注册 API（由 ModRegistry.GetApi 获取）。</summary>
    public interface IContentPatcherApi
    {
        /// <summary>注册一个自定义 token，供 CP 内容包以 {{模组ID/token名}} 形式引用。</summary>
        void RegisterToken(IManifest mod, string name, Func<IEnumerable<string>?> getValue);
    }
}
