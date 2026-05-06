using Npgsql;
using System;
using System.Configuration;

namespace Check_Sheet_Online
{

    public static class GlobalConfig
    {
        public static readonly string schemaMain = ConfigurationManager.AppSettings["PostgresqlSchema_Main"];
        public static readonly string schemaCheckSheet = ConfigurationManager.AppSettings["PostgresqlSchema_CheckSheetOnline"];
    }

    public static class PostgreSqlDbConnection
    {
        private static readonly string connectionString;

        static PostgreSqlDbConnection()
        {
            try
            {
                connectionString = ConfigurationManager.ConnectionStrings["PostgreSqlDbConnectionTest"]?.ConnectionString;
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new ConfigurationErrorsException("Connection string 'PostgreSqlDbConnectionTest' is missing or empty.");
                }
            }
            catch (Exception ex)
            {
                // Log error or throw more descriptive exception
                throw new ConfigurationErrorsException("Failed to initialize PostgreSqlDbConnection: " + ex.Message, ex);
            }
        }

        public static NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(connectionString);
        }

        #region ตัวอย่าง
        //using (var conn = PostgreSqlDbConnection.GetConnection())
        #endregion 
    }

}