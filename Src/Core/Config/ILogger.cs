namespace MochiBot.Src.Core.Config
{
    /// <summary>
    /// 日志记录器接口
    /// </summary>
    public interface ILogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? ex = null);
    }
}
