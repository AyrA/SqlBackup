using System.Diagnostics;

namespace SqlBackup
{
    internal class Program
    {
        private delegate int ModeHandler(ParsedArguments args, DbService db);

        private static readonly Dictionary<ArgumentOpMode, ModeHandler> funcMap = new()
        {
            { ArgumentOpMode.Backup,      DoBackup },
            { ArgumentOpMode.BackupInfo,  DoBackupInfo },
            { ArgumentOpMode.ChangeMode,  DoMode },
            { ArgumentOpMode.DbInfo,      DoDbInfo },
            { ArgumentOpMode.ListDb,      DoList },
            { ArgumentOpMode.Offline,     DoOffline },
            { ArgumentOpMode.Online,      DoOnline },
            { ArgumentOpMode.PurgeBackup, DoPurge },
            { ArgumentOpMode.Restore,     DoRestore }
        };

        static int Main(string[] args)
        {
            ParsedArguments a;
            try
            {
                a = ArgumentParser.Parse(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to parse command line arguments");
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            if (a.Mode == ArgumentOpMode.Help)
            {
                Help();
                return 0;
            }
            ArgumentException.ThrowIfNullOrEmpty(a.ConnectionString);
            if (funcMap.TryGetValue(a.Mode, out var func))
            {
                using var conn = new DbService(a.ConnectionString);
                return func.Invoke(a, conn);
            }
            throw new NotImplementedException($"Mode not implemented: {a.Mode}");
        }

        private static string[] GetDbList(ParsedArguments args, DbService db)
        {
            if (args.UseAllDb == null)
            {
                return [];
            }
            if (args.UseAllDb == false)
            {
                return [.. args.Databases];
            }
            return db.GetDatabases()
                .Except(args.Databases, StringComparer.InvariantCultureIgnoreCase)
                .ToArray();
        }

        private static string GetBackupFile(ParsedArguments args, string db)
        {
            if (args.BackupLocation == null)
            {
                throw new InvalidOperationException("Backup path not set");
            }
            return args.IsDirectory
                ? DbService.GetBackupFileName(db, args.BackupLocation)
                : args.BackupLocation;
        }

        private static int DoList(ParsedArguments args, DbService db)
        {
            Console.WriteLine(string.Join(Environment.NewLine, db.GetDatabases()));
            return 0;
        }

        private static int DoMode(ParsedArguments args, DbService db)
        {
            int ret = 0;
            if (args.RecoveryModel == null)
            {
                throw new InvalidOperationException("Recovery mode not set");
            }
            foreach (var dbName in GetDbList(args, db))
            {
                Console.WriteLine("Setting '{0}' to {1}...", dbName, args.RecoveryModel);
                try
                {
                    db.SetRecoveryMode(dbName, args.RecoveryModel.Value);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to change mode of '{0}'", dbName);
                    Console.Error.WriteLine("  {0}", ex.Message);
                    ++ret;
                }
            }
            Console.WriteLine("Completed with {0} errors", ret);
            return ret;
        }

        private static int DoOffline(ParsedArguments args, DbService db)
        {
            int ret = 0;
            foreach (var dbName in GetDbList(args, db))
            {
                Console.WriteLine("Taking '{0}' offline...", dbName);
                try
                {
                    db.TakeOffline(dbName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to take '{0}' offline", dbName);
                    Console.Error.WriteLine("  {0}", ex.Message);
                    ++ret;
                }
            }
            Console.WriteLine("Completed with {0} errors", ret);
            return ret;
        }

        private static int DoOnline(ParsedArguments args, DbService db)
        {
            int ret = 0;
            foreach (var dbName in GetDbList(args, db))
            {
                Console.WriteLine("Taking '{0}' online...", dbName);
                try
                {
                    db.TakeOnline(dbName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to take '{0}' online", dbName);
                    Console.Error.WriteLine("  {0}", ex.Message);
                    ++ret;
                }
            }
            Console.WriteLine("Completed with {0} errors", ret);
            return ret;
        }

        private static int DoPurge(ParsedArguments args, DbService db)
        {
            int ret = 0;
            foreach (var dbName in GetDbList(args, db))
            {
                Console.WriteLine("Purging backup history of '{0}'...", dbName);
                try
                {
                    db.DeleteBackupHistory(dbName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to delete backup history of '{0}'", dbName);
                    Console.Error.WriteLine("  {0}", ex.Message);
                    ++ret;
                }
            }
            Console.WriteLine("Completed with {0} errors", ret);
            return ret;
        }

        private static int DoDbInfo(ParsedArguments args, DbService db)
        {
            int ret = 0;
            foreach (var dbName in GetDbList(args, db))
            {
                try
                {
                    var info = db.GetDatabaseInfo(dbName) ??
                        throw new ArgumentException($"Database '{dbName}' cannot be found on thew server");
                    Console.WriteLine(@"
Name :    {0}
State:    {1}
Access:   {2}
Readonly: {3}
Created:  {4:g}
Recovery: {5}",
                        info.DatabaseName,
                        info.State,
                        info.AccessType,
                        info.IsReadonly ? 'Y' : 'N',
                        info.CreatedAt,
                        info.RecoveryModel);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to get datatbase information for '{0}'", dbName);
                    Console.Error.WriteLine("  {0}", ex.Message);
                    ++ret;
                }
            }
            Console.WriteLine("Completed with {0} errors", ret);
            return ret;
        }

        private static int DoBackupInfo(ParsedArguments args, DbService db)
        {
            List<BackupInfo> infos = [];
            var dbNames = GetDbList(args, db);
            if (args.BackupLocation != null)
            {
                if (dbNames.Length > 0)
                {
                    foreach (var dbName in dbNames)
                    {
                        infos.AddRange(db.GetBackupInfoFromFile(dbName, GetBackupFile(args, dbName)));
                    }
                }
                else
                {
                    infos.AddRange(db.GetBackupInfoFromFile(args.BackupLocation));
                }
            }
            else
            {
                if (dbNames.Length > 0)
                {
                    foreach (var dbName in dbNames)
                    {
                        infos.AddRange(db.GetBackupInfoFromServer(dbName));
                    }
                    if (infos.Count == 0)
                    {
                        Console.WriteLine("No backup history is available on the server");
                    }
                }
                else
                {
                    throw new InvalidOperationException("No databases specified or found on the server. if the databases were deleted, use /FILE to specify a backup file to read information from.");
                }
            }
            foreach (var info in infos)
            {
                Console.WriteLine(@"
DB   : {0}
Date : {1:g}
Type : {2}
Size : {3}
Id   : {4}
Model: {5}",
                    info.DatabaseName,
                    info.BackupEnd,
                    info.BackupType,
                    FormatSize(info.Size),
                    info.FileId,
                    info.RecoveryModel);
            }
            return 0;
        }

        private static int DoBackup(ParsedArguments args, DbService db)
        {
            var total = Stopwatch.StartNew();
            int ret = 0;
            foreach (var dbName in GetDbList(args, db))
            {
                var sw = Stopwatch.StartNew();
                Console.WriteLine("Backing up database '{0}'...", dbName);
                try
                {
                    db.Backup(dbName, GetBackupFile(args, dbName), args.DoLogBackup ? BackupType.Log : BackupType.Database, args.Verify);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to perform backup.");
                    Console.Error.WriteLine($"  {ex.Message}");
                    ++ret;
                }
                Console.WriteLine("Completed backup of '{0}' after {1}", dbName, sw.Elapsed);
            }
            Console.WriteLine("Backup completed with {0} errors after {1}", ret, total.Elapsed);
            return ret;
        }

        private static int DoRestore(ParsedArguments args, DbService db)
        {
            var total = Stopwatch.StartNew();
            int ret = 0;
            foreach (var dbName in GetDbList(args, db))
            {
                var sw = Stopwatch.StartNew();
                Console.WriteLine("Restoring database '{0}'...", dbName);
                try
                {
                    db.Restore(dbName, GetBackupFile(args, dbName), args.DoLogBackup ? BackupType.Log : BackupType.Database, args.FileIndex);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Failed to perform restore.");
                    Console.Error.WriteLine($"  {ex.Message}");
                    ++ret;
                }
                Console.WriteLine("Completed restore of '{0}' after {1}", dbName, sw.Elapsed);
            }
            Console.WriteLine("Restore completed with {0} errors after {1}", ret, total.Elapsed);
            return ret;
        }

        private static string FormatSize(double size)
        {
            var sizes = "B,K,M,G,T,E".Split(',');
            int index = 0;
            while (size >= 1000)
            {
                ++index;
                size /= 1000;
            }
            return $"{size:0.00} {sizes[index]}";
        }

        private static void Help()
        {
            Console.WriteLine(@"SqlBackup.exe
An SQL backup utility designed to be simple.
The intended purpose is to backup simple/small databases.
It exclusively uses SQL server internal functionality for this.
Backups can be restored directly by this utility, but the backup
files can also be processed by the SQL Server itself.

SqlBackup.exe <mode> /C <connstr> [args]
Modes:
/LIST     - List all databases
/BACKUP   - Take a backup
/RESTORE  - Restore from a backup
/OFFLINE  - Take one or more databases offline
/ONLINE   - Take one or more databases online
/MODE     - Switch recovery mode of one or more databases
/DBINFO   - Show basic database info
/INFO     - Show backup info from file or database
/PURGE    - Delete backup history

-----

SQL Server:
All commands require a database connection.
The connection is specified using /C followed by the connection string.

Two aliases are supported:
LOCAL: Connects to '(localdb)\MSSQLLocalDB'
EXPRESS: Connects to '.\SQLEXPRESS'

Both aliases use Windows Authentication, and disable TLS encryption.
This should provide hassle free operation on local databases.

Examples:

/C LOCAL
/C ""Server=SQLSRV\NamedInstance;User Id=backupuser;password=backupuser""

Note: It's not very safe to pass credentials via command line arguments,
because they can potentially be read by other users for as long as the
process runs.

-----

Selecting databases:
Most commands can operate on one or multiple databases at once.
You can either specify /ALL to operate on all databases
or you can speficy /DB followed by a list of names (space separated).
Use /LIST to see all databases that /ALL would take.
/ALL can optionally be followed by a list of database names (space separated).
The given databases are excluded from the selected operation.
This makes /ALL the inverse to /DB.
The database names are case insensitive.

Examples:

/ALL              Operate on all databases
/ALL dev test     Operate on all databases except for 'dev' and 'test' db
/DB prod legacy   Operate on 'prod' and 'legacy' database only

-----

Backup directory or file:
Commands that read or write backups need a location specified.
This can be done using '/DIR <path>' or '/FILE <path>'.

If using /DIR, the file name will be dynamically constructed
from the database name. This is useful if you want to operate on
multiple databases, and want each database being backed up into
its own file.

/FILE makes all operations take place with one file.
Using /FILE with /ALL is an effective way to back up all datbases into
one file.

Note: The file name will be passed to the SQL server,
and therefore it must be local to the SQL server, not the machine
that runs the backup executable. This also puts limits on using
network paths or other storage systems that need credentials.

Examples:

/DIR D:\SqlBackup              Operate in D:\SqlBackup directory
/FILE D:\SqlBackup\backup.bak  Use only D:\SqlBackup\backup.bak

-----

Mode: /LIST
Arguments: none

Lists all databases, one per line.
The list excludes internal and system databases,
but does contain offline databases.

The list is identical to the database list used when /ALL is specified
for one of the operations that supprts it.

-----

Mode: /BACKUP
Arguments:
    Backup location
    Databases
    /LOG
    /VERIFY

Backs up one or more databases into the given directory or file,
creating one backup file per database.
If a file exists, the backup will be appended.
Specifying /LOG will backup the transaction log instead of the database.

Optionally, the backup can be verified using the /VERIFY option.
Verification is done by simulating a full restore of the last backup.
Because of this, verifying the backup will almost double backup time.

Databases can only be backed up if they're online.
Database backups are generally slower than transaction log backups.
Transaction log backups can only be done
if the database is in full or bulk-logged recovery mode.

Example:

/BACKUP /DIR D:\SqlBackup /ALL dev /VERIFY
Backs up all databases except for 'dev' into files at D:\SqlBackup,
and performs backup verification of each backup

-----

Mode: /RESTORE
Arguments:
    Backup location
    Databases
    /ID <num>
    /LOG

Restores one or more databases from the given backup.

By default, each database is restored from the latest backup,
but '/ID <num>' can be used to specify a different backup instead.
This only works if all selected databases have the given backup id.
By default, SQL server increments the id every time a backup is made.
This means if all databases are backed up together,
they should have the same ids in their backup sets.
If multiple databases are restored, it's better to use negative ids.

If the id is negative, it counts backwards from the latest backup id,
with -1 being the latest backup, and -2 the backup before that, etc.
This means '/ID -1' is the same as not specifying an id at all.

/LOG will restore a log backup instead of a database backup.

Note: If using /ALL, the list of databases is obtained from the server.
To restore a database that has been deleted, you must explicitly specify it
using the /DB switch instead.

Example:

/RESTORE /DIR D:\SqlBackup /DB dev test /ID -2
Restores the 'dev' and 'test' database to the second last backup taken

-----

Mode: /OFFLINE
Arguments:
    Databases

Takes the given databases offline.
Note: Offline databases cannot be backed up, but they can be restored.
Taking a database offline before a restore can help if a restore is failing
due to access problems, or if restoration gets stuck.

Example:
/OFFLINE /ALL prod
Takes all databases except 'prod' offline

-----

Mode: /ONLINE
Arguments:
    Databases

Takes the given databases online.
A database must be online to perform a backup,
but may be offline to perform a restore.

Example:
/ONLINE /ALL prod
Takes all databases except 'prod' online

-----

Mode: /MODE
Arguments:
    Databases
    /FULL
    /BULK
    /SIMPLE

Sets the recovery mode of the given databases.
You can specify one mode:

/FULL
    Configure full recovery mode.
    Enables precise point in time restores.
    Regular backups are mandatory, or the transaction logs
    will grow indefinitely in size.

    If the tail of the log is damaged,
    changes since the most recent log backup must be redone.

/BULK
    Comparable to /FULL but backups operations in bulk.
    A point in time restore is not possible.
    This is slightly more performant than /FULL, and will
    likely use less backup disk space.

    If the log is damaged or bulk-logged operations occurred
    since the most recent log backup, changes since that last
    backup must be redone.
    
/SIMPLE
    Disable all transaction logs.
    This also disables log backups and restores.
    Because of this, a point in time restore is not supported.

    Changes since the most recent backup are unprotected.
    In the event of a disaster, those changes must be redone.

/SIMPLE is recommended for development and test databases,
or databases that can trivially be reconstructed in the case of
total data loss, for example a database that contains imported
data from a 3rd party source that can simply be reimported.

/FULL is recommended for production environments,
with infrequent database backups, and frequent transaction log backups.

/BULK is recommended if a transaction log is desired,
but the point in time restore of /FULL is not required.

Note: Backing up transaction logs is usually faster than backing up
the entire database. Log backups are also less intrusive in regards
to performance.

When the mode is changed, it's generally a good idea to move old backups
into an archive storage, deleting them from the main backup location,
and purging existing backup logs (see /PURGE)

Example:
/MODE /ALL dev /FULL
Set all databases except for 'dev' into FULL recovery mode

-----

Mode: /DBINFO
Arguments:
    Databases

Shows information about the given databases.
Information shown includes:
- Database name
- Recovery model
- Latest known backup
- Database state (online/offline)

Example:
/DBINFO /ALL dev
Shows database information for all databases except 'dev'

-----

Mode: /INFO
Arguments:
    Databases
    Backup location

Shows backup information about the given databases.
If a backup location is provided, the information is extracted from files.
If the location is not provided, the information is obtained from the server.

-----

Mode: /PURGE
Arguments:
    Databases

Purges backup information about all given databases.
This WILL NOT delete backups or harm database recoverability,
it merely deletes the metadata that holds the backup history.
This metadata is beneficial when backing up to a slow media,
such as a tape, or a disk based media that's not always available.
For a disk based backup, reading the information from the files
rather than the server is comparable in performance.

Note:
Restore operations are always performed based on the information
contained in a backup file.

Example:
/PURGE /ALL prod
Deletes all backup metadata except for 'prod' database");
        }
    }
}
