using Microsoft.Data.Sqlite;
using catgirlwindow.Src.Core.Models;

namespace catgirlwindow.Src.Core.Database
{
    /// <summary>
    /// 数据库业务层实现 - 基于 SQLite
    /// </summary>
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService()
        {
            var dbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Data");
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
            // 确保测试连接也禁用连接池，避免文件锁定
            _connectionString = connectionString.Contains("Pooling=") ? connectionString : $"{connectionString};Pooling=False";
            InitializeDatabase();
        }

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

            // 确保用户配置有默认行
            using var cmd4 = connection.CreateCommand();
            cmd4.CommandText = "SELECT COUNT(*) FROM user_config";
            var count = (long)(cmd4.ExecuteScalar() ?? 0);
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

        // ========== 用户配置 ==========

        public async Task<UserConfig> LoadConfigAsync()
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Name, Personality, Opacity, MurmurEnabled, MurmurInterval, WindowPosX, WindowPosY FROM user_config WHERE Id = 1";

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new UserConfig
                {
                    Name = reader.GetString(0),
                    Personality = reader.GetString(1),
                    Opacity = reader.GetDouble(2),
                    MurmurEnabled = reader.GetInt32(3) == 1,
                    MurmurInterval = reader.GetInt32(4),
                    WindowPosX = reader.GetInt32(5),
                    WindowPosY = reader.GetInt32(6)
                };
            }

            return new UserConfig();
        }

        public async Task SaveConfigAsync(UserConfig config)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            UPDATE user_config SET
                Name = @Name,
                Personality = @Personality,
                Opacity = @Opacity,
                MurmurEnabled = @MurmurEnabled,
                MurmurInterval = @MurmurInterval,
                WindowPosX = @WindowPosX,
                WindowPosY = @WindowPosY
            WHERE Id = 1
        """;

            cmd.Parameters.AddWithValue("@Name", config.Name);
            cmd.Parameters.AddWithValue("@Personality", config.Personality);
            cmd.Parameters.AddWithValue("@Opacity", config.Opacity);
            cmd.Parameters.AddWithValue("@MurmurEnabled", config.MurmurEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@MurmurInterval", config.MurmurInterval);
            cmd.Parameters.AddWithValue("@WindowPosX", config.WindowPosX);
            cmd.Parameters.AddWithValue("@WindowPosY", config.WindowPosY);

            await cmd.ExecuteNonQueryAsync();
        }

        // ========== 聊天记录 ==========

        public async Task SaveChatHistoryAsync(List<ChatMessage> messages)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 清空旧数据后批量插入
            using var clearCmd = connection.CreateCommand();
            clearCmd.CommandText = "DELETE FROM chat_history";
            await clearCmd.ExecuteNonQueryAsync();

            foreach (var msg in messages)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO chat_history (Role, Content, Timestamp) VALUES (@Role, @Content, @Timestamp)";
                cmd.Parameters.AddWithValue("@Role", msg.Role);
                cmd.Parameters.AddWithValue("@Content", msg.Content);
                cmd.Parameters.AddWithValue("@Timestamp", msg.Timestamp.ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<ChatMessage>> LoadChatHistoryAsync(int limit = 50)
        {
            var result = new List<ChatMessage>();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Role, Content, Timestamp FROM chat_history ORDER BY Id DESC LIMIT @Limit";
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new ChatMessage
                {
                    Role = reader.GetString(0),
                    Content = reader.GetString(1),
                    Timestamp = DateTime.Parse(reader.GetString(2))
                });
            }

            // 反转回正序
            result.Reverse();
            return result;
        }

        // ========== 情绪日志 ==========

        public async Task LogMoodChangeAsync(AgentMood mood, string trigger)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO mood_log (Timestamp, Mood, Trigger) VALUES (@Timestamp, @Mood, @Trigger)";
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("@Mood", (int)mood);
            cmd.Parameters.AddWithValue("@Trigger", trigger);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<MoodLogEntry>> GetMoodLogAsync(DateTime start, DateTime end)
        {
            var result = new List<MoodLogEntry>();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Timestamp, Mood, Trigger FROM mood_log WHERE Timestamp >= @Start AND Timestamp <= @End ORDER BY Timestamp DESC";
            cmd.Parameters.AddWithValue("@Start", start.ToString("O"));
            cmd.Parameters.AddWithValue("@End", end.ToString("O"));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new MoodLogEntry
                {
                    Timestamp = DateTime.Parse(reader.GetString(0)),
                    Mood = (AgentMood)reader.GetInt32(1),
                    Trigger = reader.GetString(2)
                });
            }

            return result;
        }
    }
}
