using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;

namespace BundlePoolProbe
{
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void Unregister(IManifest mod);

        void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null);

        // 注意：签名必须与 GMCM 1.16 的真实 API 完全一致（经反射探针核实），
        // AddBoolOption 没有 formatValue 参数；SMAPI 的接口代理是整体映射，
        // 任何一个方法签名不匹配都会导致 GetApi 整体失败（GMCM 设置一节消失）。
        void AddBoolOption(
            IManifest mod,
            Func<bool> getValue,
            Action<bool> setValue,
            Func<string> name,
            Func<string>? tooltip = null,
            string? fieldId = null
        );

        void AddNumberOption(
            IManifest mod,
            Func<int> getValue,
            Action<int> setValue,
            Func<string> name,
            Func<string>? tooltip = null,
            int? min = null,
            int? max = null,
            int? interval = null,
            Func<int, string>? formatValue = null,
            string? fieldId = null
        );
    }
}