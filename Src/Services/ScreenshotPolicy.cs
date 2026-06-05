using System.Text.Json;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.EventModels;
using static MochiBot.Src.Core.Constants;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 截图策略模块
    /// 根据事件类型和用户配置，决定是否应截屏
    /// 静态工具类，无状态
    /// </summary>
    public static class ScreenshotPolicy
    {
        /// <summary>根据事件类型和用户配置，判断是否应截屏</summary>
        public static bool ShouldCapture(EventData eventData, ModuleSettings settings)
        {
            if (eventData.Category == EventCategory.UserInput)
                return settings.Vision_AutoScreenshotOnChat;

            if (eventData.Category == EventCategory.SystemAuto)
            {
                var taskType = ExtractTaskType(eventData.Info);
                return taskType switch
                {
                    BuiltinTasks.LateNight => settings.Vision_ScreenshotOnLateNight,
                    BuiltinTasks.EyeRest => settings.Vision_ScreenshotOnEyeRest,
                    _ => false
                };
            }

            return false;
        }

        /// <summary>从事件 Info JSON 中提取 type 字段，解析失败返回 null</summary>
        private static string? ExtractTaskType(string? info)
        {
            if (string.IsNullOrWhiteSpace(info)) return null;
            try
            {
                using var doc = JsonDocument.Parse(info);
                if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    return typeProp.GetString();
            }
            catch { }
            return null;
        }
    }
}
