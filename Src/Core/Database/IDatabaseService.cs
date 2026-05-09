namespace MochiBot.Src.Core.Database
{
    /// <summary>
    /// 数据库服务接口
    /// 仅提供最基础的数据库连接能力，业务 SQL 由各 Repository 层负责
    /// </summary>
    public interface IDatabaseService
    {
        /// <summary>获取数据库连接字符串（供 Repository 层使用）</summary>
        string GetConnectionString();
    }
}
