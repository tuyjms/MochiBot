namespace MochiBot.Src.Core.Models
{
    /// <summary>
    /// 用户配置模型
    /// </summary>
    public class UserConfig
    {
        /// <summary>AI桌宠的名字</summary>
        public string Name { get; set; } = "小可爱";

        /// <summary>性格：温柔 / 毒舌 / 活泼</summary>
        public string Personality { get; set; } = "温柔";

        /// <summary>窗口透明度 0.0-1.0</summary>
        public double Opacity { get; set; } = 1.0;

        /// <summary>碎碎念功能开关</summary>
        public bool MurmurEnabled { get; set; } = true;

        /// <summary>碎碎念间隔（分钟）</summary>
        public int MurmurInterval { get; set; } = 30;

        /// <summary>窗口位置 X</summary>
        public int WindowPosX { get; set; } = 100;

        /// <summary>窗口位置 Y</summary>
        public int WindowPosY { get; set; } = 100;
    }
}
