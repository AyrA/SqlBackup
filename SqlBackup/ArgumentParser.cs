namespace SqlBackup
{
    internal static class ArgumentParser
    {
        public static ParsedArguments Parse(string[] args)
        {
            var ret = new ParsedArguments();
            if (args == null || args.Length == 0)
            {
                ret.SetMode("/?");
                return ret;
            }
            ret.SetMode(args[0]);
            bool isReadingDatabases = false;
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i].Trim();
                if (arg.StartsWith('/'))
                {
                    isReadingDatabases = false;
                }
                switch (arg.ToUpperInvariant())
                {
                    case "/DB":
                        ret.ClearUseAllDb();
                        isReadingDatabases = true;
                        break;
                    case "/ALL":
                        ret.SetUseAllDb();
                        isReadingDatabases = true;
                        break;
                    case "/LOG":
                        ret.SetLogBackup();
                        break;
                    case "/FULL":
                    case "/BULK":
                    case "/SIMPLE":
                        ret.SetRecoveryModel(arg);
                        break;
                    case "/VERIFY":
                        ret.SetVerify();
                        break;
                    case "/C":
                        ret.SetConnectionString(args[++i]);
                        break;
                    case "/DIR":
                        ret.SetLocation(args[++i], true);
                        break;
                    case "/FILE":
                        ret.SetLocation(args[++i], false);
                        break;
                    case "/ID":
                        ret.SetFileIndex(args[++i]);
                        break;
                    case "/DISMOUNT":
                        ret.SetDismount();
                        break;
                    default:
                        if (isReadingDatabases)
                        {
                            ret.AddDb(arg);
                        }
                        else
                        {
                            throw new ParserException($"Unknown argument: '{arg}'");
                        }
                        break;
                }
            }
            ret.Validate();
            return ret;
        }
    }
}
