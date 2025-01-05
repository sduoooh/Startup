using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using static startup.SQL;

namespace startup
{
    public enum SQLStatus
    {
        Success,
        Noexists,
        CircleReference,
        OtherError
    }

    public class SQLResult<T> 
    {
        public SQLStatus status;
        public T result;

        public SQLResult(SQLStatus status, T result)
        {
            this.status = status;
            this.result = result;
        }
    }

    public class SQL
    {
        private string _dbPath;
        private SQLiteConnection _sqlConnetion;
        private readonly Dictionary<string, int> _columnIndexes = new Dictionary<string, int>();
        private SQLiteCommand _searchCommand;
        private SQLiteCommand _updateCountCommand;
        private SQLiteCommand _getCommandInfoCommand;
        private SQLiteCommand _getRawConfigsCommand;

        public SQL(string PluginPath)
        {
            _dbPath = Path.Combine(PluginPath, "plugins.db");
            _sqlConnetion = new SQLiteConnection($"Data Source={_dbPath}");

            // 保留搜索和更新计数的预编译命令
            _searchCommand = new SQLiteCommand(@"
                SELECT c.name, c.mode 
                FROM Commands c
                WHERE c.name LIKE @search 
                AND (@allowNsfw = 1 OR c.label == 'sfw')
                ORDER BY 
                    c.starred DESC,
                    c.count DESC
                LIMIT 8", _sqlConnetion);

            _searchCommand.Parameters.Add("@search", DbType.String);
            _searchCommand.Parameters.Add("@allowNsfw", DbType.Boolean);
            _searchCommand.Prepare();

            _updateCountCommand = new SQLiteCommand(@"
                UPDATE Commands 
                SET count = count + 1 
                WHERE name = @name", _sqlConnetion);
            
            _updateCountCommand.Parameters.Add("@name", DbType.String);
            _updateCountCommand.Prepare();

            _getCommandInfoCommand = new SQLiteCommand(@"
                WITH RECURSIVE CommandTree AS (
                    SELECT c.name,
                           c.mode,
                           1 AS depth,
                           0 AS idx,
                           c.name AS root_name,
                           CASE WHEN c.mode = 'proxy' THEN cc.waitable ELSE sc.waitable END AS proxy_waitable,
                           CASE WHEN c.mode = 'proxy' THEN cc.preset_input ELSE '' END AS pre_input
                      FROM Commands c
                           LEFT JOIN
                           CommandCombination cc ON c.name = cc.name                           
                           LEFT JOIN
                           SingleCommands sc ON c.name = sc.name
                     WHERE c.name = @name AND 
                           (@allowNsfw = 1 OR 
                            c.label == 'sfw') 
                    UNION ALL
                    SELECT c.name,
                           c.mode,
                           ct.depth + 1,
                           cc.idx,
                           ct.root_name,
                           CASE WHEN c.mode = 'proxy' THEN cc.waitable ELSE ct.proxy_waitable END AS proxy_waitable,
                           CASE WHEN c.mode = 'proxy' THEN cc.preset_input ELSE ct.pre_input END AS pre_input
                      FROM CommandTree ct
                           JOIN
                           CommandCombination cc ON ct.name = cc.name
                           JOIN
                           Commands c ON cc.child = c.name
                     WHERE ct.mode = 'proxy' AND 
                           (@allowNsfw = 1 OR 
                            c.label == 'sfw') 
                )
                SELECT CASE WHEN EXISTS (
                               SELECT 1
                                 FROM CommandTree
                                WHERE name = root_name AND 
                                      depth > 1
                           )
                       THEN 'cycle' ELSE 'valid' END AS status,
                       json_group_array(json_object('name', ct.name, 'proxy_waitable', ct.proxy_waitable, 'pre_input', ct.pre_input) ) AS command_info
                  FROM CommandTree ct
                 WHERE ct.mode = 'raw'
                 GROUP BY ct.root_name", _sqlConnetion);

            _getCommandInfoCommand.Parameters.Add("@name", DbType.String);
            _getCommandInfoCommand.Parameters.Add("@allowNsfw", DbType.Boolean);
            _getCommandInfoCommand.Prepare();

            _getRawConfigsCommand = new SQLiteCommand(@"
                SELECT 
                    c.name,
                    c.description,
                    c.label,
                    c.starred,
                    c.count,
                    sc.waitable,
                    sc.executable,
                    sc.execute_path,
                    sc.source_path,
                    sc.associatable
                FROM Commands c
                JOIN SingleCommands sc ON c.name = sc.name
                WHERE c.name IN (SELECT value FROM json_each(@names))
                AND (@allowNsfw = 1 OR c.label == 'sfw')", _sqlConnetion);

            _getRawConfigsCommand.Parameters.Add("@names", DbType.String);
            _getRawConfigsCommand.Parameters.Add("@allowNsfw", DbType.Boolean);
            _getRawConfigsCommand.Prepare();

            // 预先获取所有列索引
            _sqlConnetion.Open();
            using (var reader = _searchCommand.ExecuteReader(System.Data.CommandBehavior.SchemaOnly))
            {
                var schema = reader.GetSchemaTable();
                foreach (DataRow row in schema.Rows)
                {
                    string columnName = (string)row["ColumnName"];
                    int columnOrdinal = (int)row["ColumnOrdinal"];
                    _columnIndexes[columnName] = columnOrdinal;
                }
            }
            _sqlConnetion.Close();

        }

        public SQLResult<List<string>> Query(string input, bool nsfwAllowed)
        {
            try
            {
                _sqlConnetion.Open();
                _searchCommand.Parameters["@search"].Value = $"%{input}%";
                _searchCommand.Parameters["@allowNsfw"].Value = nsfwAllowed;

                var results = new List<string>();
                using (var reader = _searchCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(reader.GetString(reader.GetOrdinal("name")));
                    }
                }
                return new SQLResult<List<string>>(SQLStatus.Success, results);
            }
            finally
            {
                _sqlConnetion.Close();
            }
        }

        public SQLResult<char> IncrementCount(string name)
        {
            try
            {
                _sqlConnetion.Open();
                _updateCountCommand.Parameters["@name"].Value = name;
                return new SQLResult<char>(_updateCountCommand.ExecuteNonQuery() != 0 ? SQLStatus.Success : SQLStatus.Noexists, (char)0);
            }
            finally
            {
                _sqlConnetion.Close();
            }
        }

        public class CommandInfo
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("proxy_waitable")]
            public int ProxyWaitableInt { get; set; } 

            [JsonIgnore] 
            public bool ProxyWaitable => ProxyWaitableInt == 1; 

            [JsonPropertyName("pre_input")]
            public string PreInput { get; set; }
        }

        public SQLResult<Queue<RawConfigure>> GetCommandInfo(string name, bool nsfwAllowed)
        {
            List<CommandInfo> commandInfos; 
            try
            {
                _sqlConnetion.Open();
                _getCommandInfoCommand.Parameters["@name"].Value = name;
                _getCommandInfoCommand.Parameters["@allowNsfw"].Value = nsfwAllowed;

                using (var reader = _getCommandInfoCommand.ExecuteReader())
                {
                    if (!reader.Read())
                        return new SQLResult<Queue<RawConfigure>>(SQLStatus.Noexists, null);

                    var status = reader.GetString(0);
                    if (status == "cycle")
                        return new SQLResult<Queue<RawConfigure>>(SQLStatus.CircleReference, null);

                    string a = reader.GetString(1);

                    commandInfos = System.Text.Json.JsonSerializer.Deserialize<List<CommandInfo>>(
                        a)?.Where(info => info.Name != null).ToList() ?? new List<CommandInfo>();

                    if (commandInfos.Count == 0)
                        return new SQLResult<Queue<RawConfigure>>(SQLStatus.Noexists, null);
                }
            }
            catch (SQLiteException)
            {
                return new SQLResult<Queue<RawConfigure>>(SQLStatus.Noexists, null);
            }
            finally
            {
                _sqlConnetion.Close();
            }

            var rawConfigs = GetAllRawConfig(commandInfos.Select(c => c.Name).ToList(), nsfwAllowed);
            if (rawConfigs.status != SQLStatus.Success)
                return rawConfigs;

            var configQueue = rawConfigs.result;
            var configDict = commandInfos.ToDictionary(c => c.Name);

            // 应用proxy设置到raw配置
            var resultQueue = new Queue<RawConfigure>();
            while (configQueue.Count > 0)
            {
                var config = configQueue.Dequeue();
                if (configDict.TryGetValue(config.Name, out var proxyInfo))
                {
                    // 仅当配置本身的Waitable为true时，proxy的waitable才生效
                    config.Waitable = config.Waitable && proxyInfo.ProxyWaitable;
                    config.PresetInput = proxyInfo.PreInput;
                }
                resultQueue.Enqueue(config);
            }

            return new SQLResult<Queue<RawConfigure>>(SQLStatus.Success, resultQueue);
        }

        public SQLResult<Queue<RawConfigure>> GetAllRawConfig(List<string> names, bool nsfwAllowed)
        {
            if (names == null || !names.Any())
                return new SQLResult<Queue<RawConfigure>>(SQLStatus.Success, new Queue<RawConfigure>());

            try
            {
                _sqlConnetion.Open();
                var results = new Queue<RawConfigure>();

                for (int i = 0; i < names.Count; i += 20)
                {
                    var batch = names.Skip(i).Take(20).ToList();
                    var batchJson = System.Text.Json.JsonSerializer.Serialize(batch);
                    _getRawConfigsCommand.Parameters["@names"].Value = batchJson;
                    _getRawConfigsCommand.Parameters["@allowNsfw"].Value = nsfwAllowed;

                    try
                    {
                        using (var reader = _getRawConfigsCommand.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                results.Enqueue(new RawConfigure
                                {
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    Description = reader.GetString(reader.GetOrdinal("description")),
                                    Label = (ControlLabel)Enum.Parse(typeof(ControlLabel), 
                                        reader.GetString(reader.GetOrdinal("label"))),
                                    Starred = reader.GetBoolean(reader.GetOrdinal("starred")),
                                    Count = reader.GetInt32(reader.GetOrdinal("count")),
                                    Waitable = reader.GetBoolean(reader.GetOrdinal("waitable")),
                                    Executable = reader.GetBoolean(reader.GetOrdinal("executable")),
                                    ExecutePath = reader.GetString(reader.GetOrdinal("execute_path")),
                                    SourcePath = reader.GetString(reader.GetOrdinal("source_path")),
                                    Associatable = reader.GetBoolean(reader.GetOrdinal("associatable"))
                                });
                            }
                        }
                    }
                    catch (SQLiteException)
                    {
                        return new SQLResult<Queue<RawConfigure>>(SQLStatus.Noexists, new Queue<RawConfigure>());
                    }
                }
                
                return new SQLResult<Queue<RawConfigure>>(SQLStatus.Success, results);
            }
            finally
            {
                _sqlConnetion.Close();
            }
        }
    }
}
