using Microsoft.Data.Sqlite;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Database.Models;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 用户配置数据访问中间层
    /// 负责用户配置表的所有 SQL 操作
    /// </summary>
    public class UserConfigRepository
    {
        private readonly IDatabaseService _databaseService;

        public UserConfigRepository(IDatabaseService databaseService)
        {
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        }

        private SqliteConnection CreateConnection()
        {
            var connectionString = _databaseService.GetConnectionString();
            return new SqliteConnection(connectionString.Contains("Pooling=")
                ? connectionString
                : $"{connectionString};Pooling=False");
        }

        /// <summary>加载用户配置</summary>
        public async Task<UserConfig> LoadConfigAsync()
        {
            await using var connection = CreateConnection();
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

        /// <summary>保存用户配置</summary>
        public async Task SaveConfigAsync(UserConfig config)
        {
            await using var connection = CreateConnection();
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
    }
}
