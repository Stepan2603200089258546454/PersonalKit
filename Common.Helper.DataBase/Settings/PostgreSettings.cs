using System;
using System.Collections.Generic;
using System.Text;

namespace Common.Helper.DataBase.Settings
{
    public class PostgreSettings
    {
        internal const string ConfigSectionName = "DBConfiguration";

        public string DefaultConnection { get; set; } = "Host=localhost;Database=PGDB;Username=postgres;Password=Password;Port=5433;" +
            "Maximum Pool Size=200;Minimum Pool Size=10;Connection Idle Lifetime=300;";
        public string Version { get; set; } = "18.3.0";
        public int PoolSize { get; set; } = 128;
    }
}
