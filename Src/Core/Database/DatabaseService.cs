using System.IO;
using Microsoft.Data.Sqlite;
using MochiBot.Src.Core;

namespace MochiBot.Src.Core.Database
{
    /// <summary>
    /// 数据库服务实现 - 基于 SQLite
    /// 仅负责连接字符串管理和统一建表，业务 SQL 由各 Repository 层负责
    /// </summary>
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;

        public string GetConnectionString() => _connectionString;

        public DatabaseService()
        {
            var dbDir = Path.Combine(AppPaths.ResourcesDir, "Data");
            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);

            var dbPath = Path.Combine(dbDir, "mochibot.db");
            _connectionString = $"Data Source={dbPath};Pooling=False";

            InitializeDatabase();
        }

        /// <summary>
        /// 可注入自定义连接字符串的构造函数（用于单元测试）
        /// </summary>
        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString.Contains("Pooling=") ? connectionString : $"{connectionString};Pooling=False";
            InitializeDatabase();
        }

        /// <summary>
        /// 统一建表，保证所有表结构完整
        /// </summary>
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // 用户配置表
            using var cmd1 = connection.CreateCommand();
            cmd1.CommandText = """
            CREATE TABLE IF NOT EXISTS user_config (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL DEFAULT '小可爱',
                Personality TEXT NOT NULL DEFAULT '温柔',
                Opacity     REAL NOT NULL DEFAULT 1.0,
                MurmurEnabled INTEGER NOT NULL DEFAULT 1,
                MurmurInterval INTEGER NOT NULL DEFAULT 30,
                WindowPosX  INTEGER NOT NULL DEFAULT 100,
                WindowPosY  INTEGER NOT NULL DEFAULT 100
            );
            """;
            cmd1.ExecuteNonQuery();

            // 聊天记录表
            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_history (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Role        TEXT NOT NULL,
                Content     TEXT NOT NULL,
                Timestamp   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_chat_timestamp ON chat_history(Timestamp);
            """;
            cmd2.ExecuteNonQuery();

            // 情绪日志表
            using var cmd3 = connection.CreateCommand();
            cmd3.CommandText = """
            CREATE TABLE IF NOT EXISTS mood_log (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp   TEXT NOT NULL,
                Mood        INTEGER NOT NULL,
                Trigger     TEXT NOT NULL
            );
            """;
            cmd3.ExecuteNonQuery();

            // 长期记忆表
            using var cmd4 = connection.CreateCommand();
            cmd4.CommandText = """
            CREATE TABLE IF NOT EXISTS long_memory (
                id              TEXT PRIMARY KEY,
                keyword1        TEXT NOT NULL,
                keyword2        TEXT NOT NULL,
                keyword3        TEXT NOT NULL,
                description     TEXT NOT NULL,
                event_timestamp TEXT NOT NULL,
                importance      INTEGER NOT NULL DEFAULT 0,
                created_at      TEXT NOT NULL,
                last_accessed_at TEXT NOT NULL,
                access_count    INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_kw1 ON long_memory(keyword1);
            CREATE INDEX IF NOT EXISTS idx_kw2 ON long_memory(keyword2);
            CREATE INDEX IF NOT EXISTS idx_kw3 ON long_memory(keyword3);
            CREATE INDEX IF NOT EXISTS idx_importance ON long_memory(importance);
            """;
            cmd4.ExecuteNonQuery();

            // 确保用户配置有默认行
            using var cmd5 = connection.CreateCommand();
            cmd5.CommandText = "SELECT COUNT(*) FROM user_config";
            var count = (long)(cmd5.ExecuteScalar() ?? 0);
            if (count == 0)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = """
                INSERT INTO user_config (Name, Personality, Opacity, MurmurEnabled, MurmurInterval, WindowPosX, WindowPosY)
                VALUES ('小可爱', '温柔', 1.0, 1, 30, 100, 100);
                """;
                insert.ExecuteNonQuery();
            }
        }
    }
}
