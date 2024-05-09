using System.Globalization;

namespace SqlBackup
{
    public class ParsedArguments
    {
        /// <summary>
        /// Arguments that accept a database specifier
        /// </summary>
        private static readonly ArgumentOpMode[] DbModes =
        [
            ArgumentOpMode.Backup,     ArgumentOpMode.BackupInfo,
            ArgumentOpMode.ChangeMode, ArgumentOpMode.PurgeBackup,
            ArgumentOpMode.Restore,    ArgumentOpMode.Offline,
            ArgumentOpMode.Online,     ArgumentOpMode.DbInfo,
        ];

        /// <summary>
        /// Arguments that accept a dir/file specifier
        /// </summary>
        private static readonly ArgumentOpMode[] LocationModes =
        [
            ArgumentOpMode.Backup, ArgumentOpMode.BackupInfo,
            ArgumentOpMode.Restore
        ];

        private static readonly Dictionary<string, string> defaultConnstr = new(StringComparer.InvariantCultureIgnoreCase)
        {
            { "EXPRESS", DefaultConnectionStrings.SqlExpress },
            { "LOCAL", DefaultConnectionStrings.LocalDb }
        };
        public ArgumentOpMode Mode { get; private set; } = ArgumentOpMode.None;

        public DbRecoveryModel? RecoveryModel { get; private set; }

        public bool? UseAllDb { get; private set; }

        public List<string> Databases { get; } = [];

        public string? ConnectionString { get; private set; }

        public string? BackupLocation { get; private set; }

        public bool IsDirectory { get; private set; }

        public int FileIndex { get; private set; }

        public bool Verify { get; private set; }

        public bool DoLogBackup { get; private set; }

        public bool DismountDb { get; private set; }

        public void SetUseAllDb()
        {
            EnsureMode(DbModes);
            if (UseAllDb == null)
            {
                UseAllDb = true;
                return;
            }
            if (UseAllDb.Value)
            {
                throw new ParserException("Duplicate /ALL");
            }
            throw new ParserException("Cannot specify /ALL when /DB has already been specified");
        }

        public void ClearUseAllDb()
        {
            EnsureMode(DbModes);
            if (UseAllDb == null)
            {
                UseAllDb = false;
                return;
            }
            if (!UseAllDb.Value)
            {
                throw new ParserException("Duplicate /DB");
            }
            throw new ParserException("Cannot specify /DB when /ALL has already been specified");
        }

        public void AddDb(string dbName)
        {
            EnsureMode(DbModes);
            if (Databases.Contains(dbName, StringComparer.InvariantCultureIgnoreCase))
            {
                throw new ArgumentException($"Database '{dbName}' is already in the list");
            }
            Databases.Add(dbName);
        }

        public void SetConnectionString(string connStr)
        {
            EnsureMode();
            if (ConnectionString == null)
            {
                if (defaultConnstr.TryGetValue(connStr, out var value))
                {
                    connStr = value;
                }
                ConnectionString = connStr;
            }
            else
            {
                throw new ParserException($"Connection string already set when processing '{connStr}'");
            }
        }

        public void SetLocation(string location, bool isDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(location);
            EnsureMode(LocationModes);
            if (BackupLocation == null)
            {
                BackupLocation = location;
                IsDirectory = isDirectory;
            }
            else
            {
                throw new ParserException($"Backup location has already been set when processing '{location}'");
            }
        }

        public void SetFileIndex(string index)
        {
            EnsureMode(ArgumentOpMode.Restore);
            if (!int.TryParse(index, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new ParserException($"Cannot process '{index}' as integer");
            }
            if (FileIndex != 0)
            {
                throw new ParserException("File number has already been set when parsing '{index}'");
            }
            if (parsed == 0)
            {
                throw new ParserException("Backup id cannot be zero");
            }
            FileIndex = parsed;
        }

        public void SetVerify()
        {
            EnsureMode(ArgumentOpMode.Backup);
            if (Verify)
            {
                throw new ParserException("Duplicate /VERIFY encountered");
            }
            Verify = true;
        }

        public void SetLogBackup()
        {
            EnsureMode(ArgumentOpMode.Backup);
            if (DoLogBackup)
            {
                throw new ParserException("Duplicate /LOG encountered");
            }
            DoLogBackup = true;
        }

        public void SetDismount()
        {
            EnsureMode(ArgumentOpMode.Restore);
            if (DismountDb)
            {
                throw new ParserException("Duplicate /DISMOUNT encountered");
            }
            DismountDb = true;
        }

        public void SetRecoveryModel(string arg)
        {
            EnsureMode(ArgumentOpMode.ChangeMode);
            if (RecoveryModel != null)
            {
                throw new ParserException($"Recovery model already set to '{RecoveryModel}' when parsing '{arg}'");
            }

            RecoveryModel = arg.ToUpperInvariant() switch
            {
                "/FULL" => DbRecoveryModel.Full,
                "/BULK" => DbRecoveryModel.BulkLogged,
                "/SIMPLE" => DbRecoveryModel.Simple,
                _ => throw new ParserException($"'{arg}' is not a valid recovery model argument"),
            };
        }

        public void SetMode(string arg)
        {
            if (Mode != ArgumentOpMode.None)
            {
                throw new ParserException($"Mode already set when processing '{arg}'");
            }
            Mode = arg.ToUpperInvariant() switch
            {
                "/?" => ArgumentOpMode.Help,
                "/HELP" => ArgumentOpMode.Help,
                "/LIST" => ArgumentOpMode.ListDb,
                "/BACKUP" => ArgumentOpMode.Backup,
                "/RESTORE" => ArgumentOpMode.Restore,
                "/MODE" => ArgumentOpMode.ChangeMode,
                "/INFO" => ArgumentOpMode.BackupInfo,
                "/DBINFO" => ArgumentOpMode.DbInfo,
                "/PURGE" => ArgumentOpMode.PurgeBackup,
                "/OFFLINE" => ArgumentOpMode.Offline,
                "/ONLINE" => ArgumentOpMode.Online,
                _ => throw new ParserException($"'{arg}' is not a valid recovery model argument"),
            };
        }

        public void Validate()
        {
            EnsureMode();
            switch (Mode)
            {
                case ArgumentOpMode.Invalid:
                case ArgumentOpMode.None:
                    throw new Exception("No mode specified. Use /? to get help");
                case ArgumentOpMode.Backup:
                    RequirePath();
                    RequireDb();
                    break;
                case ArgumentOpMode.Restore:
                    RequirePath();
                    RequireDb();
                    break;
                case ArgumentOpMode.ChangeMode:
                    RequireDb();
                    if (RecoveryModel == null)
                    {
                        throw new Exception("/MODE requires a recovery mode to be specified");
                    }
                    break;
                case ArgumentOpMode.BackupInfo:
                    RequireDbOrPath();
                    break;
                case ArgumentOpMode.DbInfo:
                    RequireDb();
                    break;
                case ArgumentOpMode.PurgeBackup:
                    RequireDb();
                    break;
                case ArgumentOpMode.Offline:
                    RequireDb();
                    break;
                case ArgumentOpMode.Online:
                    RequireDb();
                    break;
            }
            if (Mode != ArgumentOpMode.Help)
            {
                RequireConnstr();
            }
        }

        private void RequireDbOrPath()
        {
            try
            {
                RequireDb();
            }
            catch
            {
                try
                {
                    RequirePath();
                }
                catch
                {
                    throw new Exception("A database or a backup file is required");
                }
            }
        }

        private void RequirePath()
        {
            if (string.IsNullOrWhiteSpace(BackupLocation))
            {
                throw new Exception("/DIR or /FILE is required");
            }
        }

        private void RequireDb()
        {
            if (UseAllDb == null)
            {
                throw new Exception("/ALL or /DB is required");
            }
            if (UseAllDb == false && Databases.Count == 0)
            {
                throw new Exception("/DB requires at least one database. Did you mean to use /ALL instead?");
            }
        }

        private void RequireConnstr()
        {
            if (ConnectionString == null)
            {
                throw new Exception("/C is required");
            }
        }

        private void EnsureMode()
        {
            if (Mode == ArgumentOpMode.None)
            {
                throw new ParserException("Mode has not been specified when first mode specific argument was encountered");
            }
        }

        private void EnsureMode(params ArgumentOpMode[] modes)
        {
            EnsureMode();
            if (!modes.Contains(Mode))
            {
                throw new ParserException("An argument is not valid for the given mode. Use /? for help");
            }
        }
    }
}
