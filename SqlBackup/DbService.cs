using Microsoft.Data.SqlClient;
using System.Data;

namespace SqlBackup
{
    internal class DbService : IDisposable
    {
        /// <summary>
        /// SQL statement strings
        /// </summary>
        private static class SqlStatements
        {
            /// <summary>
            /// Gets a list of all non-system databases
            /// </summary>
            public const string GetDb = @"SELECT [name]
FROM master.sys.databases
WHERE [owner_sid]<>1";

            public const string GetDbInfoInterpolated = @"SELECT
[name],create_date,user_access,[state],recovery_model,is_read_only
FROM master.sys.databases
WHERE [name]=@name";

            public const string BackupInterpolated = @"BACKUP DATABASE [%DB%]
TO DISK = @filename
WITH DESCRIPTION = @description,
NAME = @name,
CHECKSUM,
SKIP";

            public const string VerifyInterpolated = @"DECLARE @backupSetId AS INT
SELECT @backupSetId = position
FROM msdb..backupset
WHERE DATABASE_NAME=@name
    AND backup_set_id=(SELECT MAX(backup_set_id) FROM msdb..backupset WHERE DATABASE_NAME=@name )
IF @backupSetId IS NULL BEGIN RAISERROR(@error, 16, 1) END
RESTORE VERIFYONLY
FROM DISK = @filename
WITH FILE = @backupSetId";

            public const string TakeOfflineInterpolated = @"USE [master];
ALTER DATABASE [%DB%] SET OFFLINE";

            public const string TakeOnlineInterpolated = @"USE [master];
ALTER DATABASE [%DB%] SET ONLINE";

            public const string DisconnectOthersInterpolated = @"USE [master];
ALTER DATABASE [%DB%] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";

            public const string ReconnectOthersInterpolated = @"USE [master];
ALTER DATABASE [%DB%] SET MULTI_USER";

            public const string RestoreInterpolated = @"USE [master];
RESTORE DATABASE [%DB%]
FROM DISK = @filename WITH FILE = @backupid";

            public const string ReplaceInterpolated = @"USE [master];
RESTORE DATABASE [%DB%]
FROM DISK = @filename WITH FILE = @backupid, REPLACE";

            public const string BackupInfoInterpolated = @"USE [msdb];
SELECT
    bus.database_name, bus.backup_start_date, bus.backup_finish_date,
    bus.backup_size, bmf.physical_device_name, bus.type,
    bus.recovery_model, bus.position
FROM
    [backupset] [bus]
JOIN [backupmediafamily] bmf ON bus.media_set_id=bmf.media_set_id
WHERE database_name = @name AND bmf.device_type=2
ORDER BY bus.backup_start_date DESC, bus.position DESC";

            public const string BackupFileInfoInterpolated = @"RESTORE HEADERONLY FROM DISK = @filename";

            public const string DeleteBackupHistoryInterpolated = @"USE [msdb];
exec sp_delete_database_backuphistory @name";

            public const string SetDbBackupMode = @"ALTER DATABASE [%DB%] SET RECOVERY %MODE%";
        }

        private static readonly string[] systemTables = ["master", "msdb", "model"];
        private readonly SqlConnection conn;

        public DbService(string connstr)
        {
            conn = new SqlConnection(connstr);
            conn.Open();
        }

        public string[] GetDatabases()
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.GetDb;
            using var reader = cmd.ExecuteReader();
            var ret = new List<string>();
            while (reader.Read())
            {
                ret.Add(reader.GetString(0));
            }
            return [.. ret];
        }

        public DbInfo? GetDatabaseInfo(string dbName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.GetDbInfoInterpolated;
            cmd.Parameters.AddWithValue("@name", dbName);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new DbInfo(
                    reader.GetString(0),
                    reader.GetDateTime(1),
                    (AccessType)reader.GetByte(2),
                    (DbState)reader.GetByte(3),
                    (DbRecoveryModel)reader.GetByte(4),
                    reader.GetBoolean(5)
                    );
            }
            return null;
        }

        public void BackupSystem(string fileName)
        {
            foreach (var tbl in systemTables)
            {
                Backup(tbl, fileName, BackupType.Database, true);
            }
        }

        public void Backup(string dbName, string fileName, BackupType backupType, bool verify)
        {
            var info = GetDatabaseInfo(dbName)
                ?? throw new ArgumentException("Database cannot be found");
            if (info.State != DbState.Online)
            {
                throw new InvalidOperationException($"Database is not online. Current state is '{info.State}'");
            }
            string sql;
            string backupName;
            string backupDesc;
            if (backupType == BackupType.Database)
            {
                sql = SqlStatements.BackupInterpolated;
                backupName = $"{dbName}-Full Database Backup";
                backupDesc = "Full backup of the entire database";
            }
            else if (backupType == BackupType.Log)
            {
                sql = SqlStatements.BackupInterpolated.Replace("BACKUP DATABASE", "BACKUP LOG");
                backupName = $"{dbName}-Transaction Log Backup";
                backupDesc = "Transaction log backup";
            }
            else
            {
                throw new ArgumentException("Invalid backup type");
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql
                .Replace("%DB%", dbName);
            cmd.Parameters.AddWithValue("@filename", fileName);
            cmd.Parameters.AddWithValue("@name", backupName);
            cmd.Parameters.AddWithValue("@description", backupDesc);
            cmd.ExecuteNonQuery();
            if (verify)
            {
                Verify(dbName, fileName);
            }
        }

        public void Verify(string dbName, string fileName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.VerifyInterpolated;
            cmd.Parameters.AddWithValue("@name", dbName);
            cmd.Parameters.AddWithValue("@filename", fileName);
            cmd.Parameters.AddWithValue("@error", $"Backup verification of {dbName} failed");
            cmd.ExecuteNonQuery();
        }

        public void RestoreSystem(string fileName)
        {
            foreach (var tbl in systemTables)
            {
                Restore(tbl, fileName);
            }
        }

        public void Restore(string dbName, string fileName)
        {
            var infos = GetBackupInfoFromFile(dbName, fileName);
            var latest = infos.MaxBy(m => m.BackupEnd)
                ?? throw new InvalidOperationException("Table has no backup file can not be restored");
            Restore(dbName, fileName, latest.BackupType, latest.FileId);
        }

        public void Restore(string dbName, string fileName, BackupType backupType, int backupId)
        {
            var info = GetDatabaseInfo(dbName);
            using var cmd = conn.CreateCommand();
            bool disconnectAll = false;
            var commands = new List<string>();
            string sql;

            var backupInfo = GetBackupInfoFromFile(fileName)
                .Where(m => m.BackupType == backupType)
                .OrderByDescending(m => m.FileId)
                .ToArray();
            if (backupInfo.Length == 0)
            {
                throw new InvalidOperationException($"Backup file '{fileName}' does not contain a backup that matches database {dbName}' and backup type '{backupType}'");
            }
            if (backupId < 1)
            {
                if (backupId == 0 || backupId == -1)
                {
                    backupId = backupInfo[0].FileId;
                }
                else
                {
                    var match = backupInfo.Skip(Math.Abs(backupId) - 1).FirstOrDefault()
                        ?? throw new ArgumentOutOfRangeException($"Negative offset '{backupId}' is too big");
                    backupId = match.FileId;
                }
            }

            if (info == null)
            {
                //If db cannot be found, be willing to overwrite old DB files
                sql = SqlStatements.ReplaceInterpolated;
            }
            else
            {
                sql = SqlStatements.RestoreInterpolated;
            }
            if (backupType == BackupType.Database)
            {
                //NOOP. String is already for DB Backup
            }
            else if (backupType == BackupType.Log)
            {
                sql = sql.Replace("RESTORE DATABASE", "RESTORE LOG");
            }
            else
            {
                throw new ArgumentException("Invalid backup type");
            }
            if (info != null)
            {
                disconnectAll = info.State == DbState.Online;
            }
            if (disconnectAll)
            {
                commands.Add(SqlStatements.DisconnectOthersInterpolated);
            }
            commands.Add(sql);
            if (disconnectAll)
            {
                commands.Add(SqlStatements.ReconnectOthersInterpolated);
            }
            cmd.CommandText = string.Join(";", commands).Replace("%DB%", dbName);
            cmd.Parameters.AddWithValue("@filename", fileName);
            cmd.Parameters.AddWithValue("@backupid", backupId);
            cmd.ExecuteNonQuery();
        }

        public void SetRecoveryMode(string dbName, DbRecoveryModel recoveryModel)
        {
            if (!Enum.IsDefined(recoveryModel))
            {
                throw new ArgumentException("Recovery model value is invalid");
            }
            var info = GetDatabaseInfo(dbName)
                ?? throw new ArgumentException("Database does not exist");
            //Don't set mode that already is
            if (info.RecoveryModel == recoveryModel)
            {
                return;
            }
            var keywords = new Dictionary<DbRecoveryModel, string>
            {
                { DbRecoveryModel.Simple, "SIMPLE" },
                { DbRecoveryModel.BulkLogged, "BULK_LOGGED" },
                { DbRecoveryModel.Full, "FULL" }
            };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.SetDbBackupMode
                .Replace("%DB%", dbName)
                .Replace("%MODE%", keywords[recoveryModel]);
            cmd.ExecuteNonQuery();
        }

        public void TakeOffline(string dbName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.TakeOfflineInterpolated.Replace("%DB%", dbName);
            cmd.ExecuteNonQuery();
        }

        public void TakeOnline(string dbName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.TakeOnlineInterpolated.Replace("%DB%", dbName);
            cmd.ExecuteNonQuery();
        }

        public void DeleteBackupHistory(string dbName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.DeleteBackupHistoryInterpolated;
            cmd.Parameters.AddWithValue("@name", dbName);
            cmd.ExecuteNonQuery();
        }

        public BackupInfo[] GetBackupInfoFromServer(string dbName)
        {
            var backupTypes = new Dictionary<string, BackupType>
            {
                ["D"] = BackupType.Database,
                ["L"] = BackupType.Log,
                ["I"] = BackupType.Database
            };
            var recoveryModels = new Dictionary<string, DbRecoveryModel>
            {
                ["SIMPLE"] = DbRecoveryModel.Simple,
                ["BULK-LOGGED"] = DbRecoveryModel.BulkLogged,
                ["FULL"] = DbRecoveryModel.Full
            };
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.BackupInfoInterpolated;
            cmd.Parameters.AddWithValue("@name", dbName);
            using var reader = cmd.ExecuteReader();
            var ret = new List<BackupInfo>();
            while (reader.Read())
            {
                ret.Add(new BackupInfo(
                    reader.GetString(0),
                    reader.GetDateTime(1),
                    reader.GetDateTime(2),
                    (long)reader.GetDecimal(3),
                    reader.GetString(4),
                    backupTypes[reader.GetString(5)],
                    recoveryModels[reader.GetString(6)],
                    reader.GetInt32(7)));
            }
            return [.. ret];
        }

        public BackupInfo[] GetBackupInfoFromFile(string fileName)
        {
            var recoveryModels = new Dictionary<string, DbRecoveryModel>
            {
                ["SIMPLE"] = DbRecoveryModel.Simple,
                ["BULK-LOGGED"] = DbRecoveryModel.BulkLogged,
                ["FULL"] = DbRecoveryModel.Full
            };

            var backupTypes = new Dictionary<int, BackupType>
            {
                [1] = BackupType.Database,
                [2] = BackupType.Log,
                [5] = BackupType.Database //Differential DB backup
            };

            using var cmd = conn.CreateCommand();
            cmd.CommandText = SqlStatements.BackupFileInfoInterpolated;
            cmd.Parameters.AddWithValue("@filename", fileName);
            using var reader = cmd.ExecuteReader();
            var ret = new List<BackupInfo>();
            while (reader.Read())
            {
                ret.Add(new BackupInfo(
                    reader.GetString("DatabaseName"),
                    reader.GetDateTime("BackupStartDate"),
                    reader.GetDateTime("BackupFinishDate"),
                    reader.GetInt64("BackupSize"),
                    fileName,
                    backupTypes[reader.GetByte("BackupType")],
                    recoveryModels[reader.GetString("RecoveryModel")],
                    reader.GetInt16("Position")));
            }
            return [.. ret];
        }

        public BackupInfo[] GetBackupInfoFromFile(string dbName, string fileName)
        {
            return GetBackupInfoFromFile(fileName)
                .Where(m => m.DatabaseName.Equals(dbName, StringComparison.InvariantCultureIgnoreCase))
                .ToArray();
        }

        public void Dispose()
        {
            conn?.Dispose();
        }

        public static string GetBackupFileName(string dbName, string directory)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                dbName = dbName.Replace(c, '_');
            }
            return Path.Combine(directory, dbName.Trim() + ".db.bak");
        }
    }
}
