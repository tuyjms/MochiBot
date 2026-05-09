using Microsoft.Data.Sqlite;
using MochiBot.Src.Core.Database;
using MochiBot.Src.Core.Database.Models;

namespace MochiBot.Src.Services
{
    /// <summary>
    /// 长期记忆数据访问中间层
    /// 负责长期记忆模块的所有 SQL 语句，隔离 DatabaseService 与业务逻辑
    /// </summary>
    public class LongMemoryRepository
    {
        private readonly IDatabaseService _databaseService;

        public LongMemoryRepository(IDatabaseService databaseService)
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

        /// <summary>初始化长期记忆表</summary>
        public void InitializeTable()
        {
            using var connection = CreateConnection();
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
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
            cmd.ExecuteNonQuery();
        }

        /// <summary>添加一条长期记忆条目</summary>
        public async Task AddEntryAsync(LongMemoryEntryModel entry)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            INSERT OR REPLACE INTO long_memory 
                (id, keyword1, keyword2, keyword3, description, event_timestamp, importance, created_at, last_accessed_at, access_count)
            VALUES 
                (@Id, @Keyword1, @Keyword2, @Keyword3, @Description, @EventTimestamp, @Importance, @CreatedAt, @LastAccessedAt, @AccessCount)
            """;

            cmd.Parameters.AddWithValue("@Id", entry.Id);
            cmd.Parameters.AddWithValue("@Keyword1", entry.Keyword1);
            cmd.Parameters.AddWithValue("@Keyword2", entry.Keyword2);
            cmd.Parameters.AddWithValue("@Keyword3", entry.Keyword3);
            cmd.Parameters.AddWithValue("@Description", entry.Description);
            cmd.Parameters.AddWithValue("@EventTimestamp", entry.EventTimestamp);
            cmd.Parameters.AddWithValue("@Importance", entry.Importance);
            cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt);
            cmd.Parameters.AddWithValue("@LastAccessedAt", entry.LastAccessedAt);
            cmd.Parameters.AddWithValue("@AccessCount", entry.AccessCount);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>根据关键词搜索长期记忆</summary>
        public async Task<List<LongMemoryEntryModel>> SearchByKeywordsAsync(string keyword, int limit)
        {
            var result = new List<LongMemoryEntryModel>();

            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            SELECT id, keyword1, keyword2, keyword3, description, event_timestamp, importance, created_at, last_accessed_at, access_count
            FROM long_memory
            WHERE keyword1 LIKE @Keyword OR keyword2 LIKE @Keyword OR keyword3 LIKE @Keyword OR description LIKE @Keyword
            ORDER BY importance DESC, access_count DESC
            LIMIT @Limit
            """;

            cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
            cmd.Parameters.AddWithValue("@Limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(MapReaderToEntry(reader));
            }

            return result;
        }

        /// <summary>根据重要度筛选长期记忆</summary>
        public async Task<List<LongMemoryEntryModel>> GetByImportanceAsync(int minImportance)
        {
            var result = new List<LongMemoryEntryModel>();

            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            SELECT id, keyword1, keyword2, keyword3, description, event_timestamp, importance, created_at, last_accessed_at, access_count
            FROM long_memory
            WHERE importance >= @MinImportance
            ORDER BY importance DESC, access_count DESC
            """;

            cmd.Parameters.AddWithValue("@MinImportance", minImportance);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(MapReaderToEntry(reader));
            }

            return result;
        }

        /// <summary>获取指定时间范围内的长期记忆</summary>
        public async Task<List<LongMemoryEntryModel>> GetByTimeRangeAsync(DateTime start, DateTime end)
        {
            var result = new List<LongMemoryEntryModel>();

            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            SELECT id, keyword1, keyword2, keyword3, description, event_timestamp, importance, created_at, last_accessed_at, access_count
            FROM long_memory
            WHERE event_timestamp >= @Start AND event_timestamp <= @End
            ORDER BY importance DESC, event_timestamp DESC
            """;

            cmd.Parameters.AddWithValue("@Start", start.ToString("O"));
            cmd.Parameters.AddWithValue("@End", end.ToString("O"));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(MapReaderToEntry(reader));
            }

            return result;
        }

        /// <summary>更新访问时间和访问次数</summary>
        public async Task UpdateAccessAsync(string entryId)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            UPDATE long_memory SET
                last_accessed_at = @Now,
                access_count = access_count + 1
            WHERE id = @Id
            """;

            cmd.Parameters.AddWithValue("@Id", entryId);
            cmd.Parameters.AddWithValue("@Now", DateTime.Now.ToString("O"));

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>删除一条长期记忆</summary>
        public async Task DeleteEntryAsync(string entryId)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM long_memory WHERE id = @Id";
            cmd.Parameters.AddWithValue("@Id", entryId);

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>清空所有长期记忆</summary>
        public async Task ClearAllAsync()
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM long_memory";
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>获取长期记忆总数</summary>
        public async Task<int> GetCountAsync()
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM long_memory";

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// <summary>晋升机制：将访问次数超过阈值的条目提升重要度</summary>
        public async Task<int> PromoteEntriesAsync(int accessThreshold, int importanceIncrement)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            UPDATE long_memory SET
                importance = MIN(100, importance + @Increment)
            WHERE access_count >= @Threshold AND importance < 100
            """;

            cmd.Parameters.AddWithValue("@Threshold", accessThreshold);
            cmd.Parameters.AddWithValue("@Increment", importanceIncrement);

            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>淘汰机制：删除重要度低于阈值且长期未访问的条目</summary>
        public async Task<int> EvictEntriesAsync(int minImportance, int maxInactiveDays)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();

            var cutoffDate = DateTime.Now.AddDays(-maxInactiveDays);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            DELETE FROM long_memory
            WHERE importance < @MinImportance AND last_accessed_at < @CutoffDate
            """;

            cmd.Parameters.AddWithValue("@MinImportance", minImportance);
            cmd.Parameters.AddWithValue("@CutoffDate", cutoffDate.ToString("O"));

            return await cmd.ExecuteNonQueryAsync();
        }

        private static LongMemoryEntryModel MapReaderToEntry(SqliteDataReader reader)
        {
            return new LongMemoryEntryModel
            {
                Id = reader.GetString(0),
                Keyword1 = reader.GetString(1),
                Keyword2 = reader.GetString(2),
                Keyword3 = reader.GetString(3),
                Description = reader.GetString(4),
                EventTimestamp = reader.GetString(5),
                Importance = reader.GetInt32(6),
                CreatedAt = reader.GetString(7),
                LastAccessedAt = reader.GetString(8),
                AccessCount = reader.GetInt32(9)
            };
        }
    }
}
