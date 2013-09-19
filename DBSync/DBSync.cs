using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBSync
{
    internal class DBSync
    {

        public long LastProcessedVersion = -1;
        public bool IsSyncRunning = false;
        public bool StopProcessing = false;


        internal long SyncData()
        {
            IsSyncRunning = true;

            long CurrentVersion = -1;
            //long MinValidVersion = -1;
            long tempLastProcessedVersion = -1;
            long ProcessedVersion = -1;

            // connect to server database
            SqlConnection serverConn = new SqlConnection(ConfigurationManager.ConnectionStrings["SourceConnectionString"].ConnectionString);
            serverConn.Open();

            using (SqlCommand cmd = new SqlCommand())
            {
                cmd.Connection = serverConn;
                cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        CurrentVersion = long.Parse(rdr[0].ToString());
                    }
                }
            }

            //richTextBox1.Text = DateTime.Now.ToString() + " - " + CurrentVersion.ToString() + "\r\n" + richTextBox1.Text;

            if (CurrentVersion > LastProcessedVersion)
            {
                List<string> tableNames = RetrieveTablesToSync();

                Parallel.ForEach(tableNames, TableName =>
                //foreach (string TableName in tableNames)
                {
                    //if (StopProcessing) break;

                    //cmd = new SqlCommand();
                    //cmd.Connection = serverConn;
                    //cmd.CommandText = "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('" + TableName + "'))";

                    //using (SqlDataReader rdr = cmd.ExecuteReader())
                    //{
                    //    while (rdr.Read())
                    //    {
                    //        MinValidVersion = long.Parse(rdr[0].ToString());
                    //    }
                    //}

                    //ProcessedVersion = SyncTable(TableName, MinValidVersion, serverConn);
                    ProcessedVersion = SyncTable(TableName, LastProcessedVersion, serverConn);
                    if (ProcessedVersion > tempLastProcessedVersion) tempLastProcessedVersion = ProcessedVersion;
                }
                );

            }
            else
            {
                //richTextBox1.Text = DateTime.Now.ToString() + " - No new data \r\n" + richTextBox1.Text;
                tempLastProcessedVersion = CurrentVersion;
            }

            IsSyncRunning = false;

            if (!StopProcessing)
                LastProcessedVersion = tempLastProcessedVersion;

            return LastProcessedVersion;
        }


        internal List<string> RetrieveTablesToSync()
        {
            List<string> tableNames = new List<string>();

            // connect to server database
            SqlConnection serverConn = new SqlConnection(ConfigurationManager.ConnectionStrings["SourceConnectionString"].ConnectionString);
            serverConn.Open();

            StringBuilder command = new StringBuilder();
            command.Append("select t.name, i.name");
            command.Append(" from sys.internal_tables i");
            command.Append("	inner join sys.tables t on i.parent_object_id = t.object_id");
            command.Append(" where i.internal_type_desc = 'CHANGE_TRACKING'");
            command.Append(" order by t.name");

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = serverConn;
            cmd.CommandText = command.ToString();

            using (SqlDataReader rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    tableNames.Add(rdr[0].ToString());
                }
            }

            return tableNames;
        }


        internal long SyncTable(string tableName, long minValidVersion, SqlConnection sqlConnection)
        {
            //richTextBox1.Text = DateTime.Now.ToString() + " - Sync Data ... " + minValidVersion.ToString() + "\r\n" + richTextBox1.Text;

            long SYS_CHANGE_VERSION = -1;
            long MAX_SYS_CHANGE_VERSION = -1;
            string PrimaryColumn = string.Empty;
            string SYS_CHANGE_OPERATION = string.Empty;

            minValidVersion--;

            SqlCommand cmd2 = new SqlCommand();
            StringBuilder ColumnNames = new StringBuilder();
            StringBuilder command = new StringBuilder();

            // connect to client database
            SqlConnection clientConn = new SqlConnection(ConfigurationManager.ConnectionStrings["DestinationConnectionString"].ConnectionString);
            clientConn.Open();

            using (SqlCommand cmd = new SqlCommand())
            {
                command = new StringBuilder();
                command.Append("SELECT column_name");
                command.Append(" FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE");
                command.Append(" WHERE OBJECTPROPERTY(OBJECT_ID(constraint_name), 'IsPrimaryKey') = 1");
                command.Append(" AND table_name = '" + tableName + "'");

                cmd.Connection = clientConn;
                cmd.CommandText = command.ToString();

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        PrimaryColumn = rdr[0].ToString();
                    }
                }
            }

            //using (SqlCommand cmd = new SqlCommand())
            //{
            //    cmd.Connection = sqlConnection;
            //    cmd.CommandText = "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('" + tableName + "'))";

            //    using (SqlDataReader rdr = cmd.ExecuteReader())
            //    {
            //        while (rdr.Read())
            //        {
            //            minValidVersion = long.Parse(rdr[0].ToString());
            //        }
            //    }
            //}

            using (SqlCommand cmd = new SqlCommand())
            {
                command = new StringBuilder();
                //command.Append("SELECT CT.[" + PrimaryColumn + "], t.*, CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION");
                command.Append("SELECT t.*, CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION");
                command.Append(" FROM  " + tableName + " AS t with (nolock)");
                command.Append(" RIGHT OUTER JOIN");
                command.Append("    CHANGETABLE(CHANGES " + tableName + ", " + minValidVersion + ") AS CT");
                command.Append(" ON");
                command.Append("    t.[" + PrimaryColumn + "] = CT.[" + PrimaryColumn + "]");
                command.Append(" order by CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION");

                cmd.Connection = sqlConnection;
                cmd.CommandText = command.ToString();

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    //progressBar1.Maximum = rdr.Cast<object>().Count();

                    while (rdr.Read())
                    {
                        if (StopProcessing) break;

                        SYS_CHANGE_VERSION = long.Parse(rdr["SYS_CHANGE_VERSION"].ToString());
                        if (MAX_SYS_CHANGE_VERSION < SYS_CHANGE_VERSION) MAX_SYS_CHANGE_VERSION = SYS_CHANGE_VERSION;

                        // this is purposely commented out, since with it there seemed to be some missing data synchronizing
                        //if (SYS_CHANGE_VERSION > LastProcessedVersion)
                        {

                            try
                            {
                                SYS_CHANGE_OPERATION = rdr["SYS_CHANGE_OPERATION"].ToString();

                                if (string.Compare(SYS_CHANGE_OPERATION, "I", true) == 0)
                                {
                                    command = new StringBuilder();
                                    command.Append("SET IDENTITY_INSERT " + tableName + " ON;");
                                    command.Append(" INSERT INTO " + tableName + " (");
                                    if (string.IsNullOrWhiteSpace(ColumnNames.ToString()))
                                    {
                                        for (int i = 0; i < rdr.FieldCount - 2; i++)
                                        {
                                            if (i > 0) ColumnNames.Append(",");
                                            ColumnNames.Append("[" + rdr.GetName(i) + "]");
                                        }
                                    }
                                    command.Append(ColumnNames.ToString());
                                    command.Append(") VALUES (");
                                    for (int i = 0; i < rdr.FieldCount - 2; i++)
                                    {
                                        if (i > 0) command.Append(",");

                                        string Value = GetSqlValueString(rdr, i);

                                        command.Append(Value);
                                    }
                                    command.Append("); ");
                                    command.Append("SET IDENTITY_INSERT " + tableName + " OFF;");

                                    cmd2 = new SqlCommand();
                                    cmd2.Connection = clientConn;
                                    cmd2.CommandText = command.ToString();

                                    cmd2.ExecuteNonQuery();

                                }
                                else if (string.Compare(SYS_CHANGE_OPERATION, "U", true) == 0)
                                {
                                    command = new StringBuilder();
                                    command.Append(" UPDATE " + tableName + " SET ");

                                    for (int i = 1; i < rdr.FieldCount - 2; i++)
                                    {
                                        if (i > 1) command.Append(",");

                                        string Value = GetSqlValueString(rdr, i);

                                        command.Append(" [" + rdr.GetName(i) + "] = " + Value);
                                    }

                                    command.Append(" WHERE [" + rdr.GetName(0) + "] = '" + rdr[0].ToString() + "'");

                                    cmd2 = new SqlCommand();
                                    cmd2.Connection = clientConn;
                                    cmd2.CommandText = command.ToString();

                                    cmd2.ExecuteNonQuery();

                                }
                                else
                                {
                                    // Ignore Deletes
                                }

                                //richTextBox1.Text = DateTime.Now.ToString() + " - new data to sync ... " + SYS_CHANGE_OPERATION + " - " + SYS_CHANGE_VERSION.ToString() + "\r\n" + richTextBox1.Text;

                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.Contains("duplicate key"))
                                {
                                    // ignore duplicate key inserts
                                }
                                else
                                {
                                    //richTextBox1.Text = DateTime.Now.ToString() + " - ERROR: " + ex.Message;
                                    throw;
                                }
                            }

                            //Application.DoEvents();
                        }
                        //else
                        //{
                        //}

                    }
                }
            }

            //richTextBox1.Text = DateTime.Now.ToString() + " - Sync Complete ... " + MAX_SYS_CHANGE_VERSION.ToString() + "\r\n" + richTextBox1.Text;

            return MAX_SYS_CHANGE_VERSION;
        }


        internal static string GetSqlValueString(SqlDataReader rdr, int i)
        {
            bool UseQuotes = false;
            if (string.Compare(rdr.GetFieldType(i).Name, "string", true) == 0) UseQuotes = true;
            if (string.Compare(rdr.GetFieldType(i).Name, "datetime", true) == 0) UseQuotes = true;
            if (string.Compare(rdr.GetFieldType(i).Name, "guid", true) == 0) UseQuotes = true;
            if (string.Compare(rdr.GetFieldType(i).Name, "boolean", true) == 0) UseQuotes = true;
            string Value = rdr[i].ToString();
            if (UseQuotes)
            {
                Value = Value.Replace("'", "`");
                Value = "'" + Value + "'";
            }
            //if (UseQuotes) Value = @"""" + Value + @"""";
            if ((!UseQuotes) && string.IsNullOrWhiteSpace(Value)) Value = "null";
            return Value;
        }

    }
}
