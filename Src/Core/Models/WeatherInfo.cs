namespace MochiBot.Src.Core.Models
{
    /// <summary>
    /// 天气信息模型
    /// </summary>
    public class WeatherInfo
    {
        /// <summary>城市名称</summary>
        public string City { get; set; } = string.Empty;

        /// <summary>当前温度</summary>
        public string CurrentTemp { get; set; } = string.Empty;

        /// <summary>天气状况（晴/阴/雨等）</summary>
        public string Condition { get; set; } = string.Empty;

        /// <summary>今日最高温</summary>
        public string TodayHigh { get; set; } = string.Empty;

        /// <summary>今日最低温</summary>
        public string TodayLow { get; set; } = string.Empty;

        /// <summary>出行建议</summary>
        public string Advice { get; set; } = string.Empty;
    }
}
