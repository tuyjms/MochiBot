using Microsoft.Data.Sqlite;
using MochiBot.Src.Core.Database;
using MochiBot.Src.EventModels;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 情绪日志数据访问中间层
    /// 负责情绪日志表的所有 SQL 操作
    /// </summary>
    public class MoodLogRepository
    {
        private readonly IDatabaseService _databaseService;

        public MoodLogRepository(IDatabaseService databaseService)
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

        /// <summary>记录情绪变化</summary>
        public async Task LogMoodChangeAsync(AgentMood mood, string trigger)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO mood_log (Timestamp, Mood, Trigger) VALUES (@Timestamp, @Mood, @Trigger)";
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("@Mood", (int)mood);
            cmd.Parameters.AddWithValue("@Trigger", trigger);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>查询指定时间范围内的情绪日志</summary>
        public async Task<List<MoodLogEntry>> GetMoodLogAsync(DateTime start, DateTime end)
        {
            var result = new List<MoodLogEntry>();

            await using var connection = CreateConnection();
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
