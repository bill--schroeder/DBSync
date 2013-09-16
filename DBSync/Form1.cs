using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DBSync
{
    public partial class Form1 : Form
    {

        long LastProcessedVersion = -1;
        bool IsSyncRunning = false;
        bool StopProcessing = false;

        DBSync dbSync = new DBSync();


        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            button2.Enabled = false;
            button3.Enabled = false;

            SyncData();

            button1.Enabled = true;
            button2.Enabled = true;
            button3.Enabled = true;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (button2.Text == "Stop")
            {
                button2.Text = "Auto Sync";
                button1.Enabled = true;
                button3.Enabled = true;

                dbSync.StopProcessing = true;

                timer1.Enabled = false;
            }
            else
            {
                richTextBox1.Text = DateTime.Now.ToString() + " - Auto Start \r\n" + richTextBox1.Text;

                button2.Text = "Stop";
                button1.Enabled = false;
                button3.Enabled = false;

                dbSync.StopProcessing = false;

                timer1.Enabled = true;
            }

        }


        private void timer1_Tick(object sender, EventArgs e)
        {
            if(!IsSyncRunning) SyncData();
        }


        private void button3_Click(object sender, EventArgs e)
        {
            if (button2.Text == "Stop")
            {
                button3.Text = "Thread Sync";
                button1.Enabled = true;
                button2.Enabled = true;

                dbSync.StopProcessing = true;
            }
            else
            {
                richTextBox1.Text = DateTime.Now.ToString() + " - Thread Start \r\n" + richTextBox1.Text;

                //button3.Text = "Stop";
                //button1.Enabled = false;
                //button2.Enabled = false;

                dbSync.StopProcessing = false;

                LastProcessedVersion = long.Parse(txtLastSyncVersion.Text);

                //LastProcessedVersion = dbSync.SyncData(LastProcessedVersion);
                LastProcessedVersion = SyncData(LastProcessedVersion);

                if (txtLastSyncVersion.Text == LastProcessedVersion.ToString())
                {
                    richTextBox1.Text = DateTime.Now.ToString() + " - No new data \r\n" + richTextBox1.Text;
                }
                else
                {
                    txtLastSyncVersion.Text = LastProcessedVersion.ToString();
                    richTextBox1.Text = DateTime.Now.ToString() + " - Processed new data ... " + LastProcessedVersion + " \r\n" + richTextBox1.Text;
                }

            }

        }


        private void SyncData()
        {
            IsSyncRunning = true;

            long CurrentVersion = -1;
            long MinValidVersion = -1;

            LastProcessedVersion = long.Parse(txtLastSyncVersion.Text);

            // connect to server database
            SqlConnection serverConn = new SqlConnection(ConfigurationManager.ConnectionStrings["SourceConnectionString"].ConnectionString);
            serverConn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = serverConn;
            cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

            using (SqlDataReader rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    CurrentVersion = long.Parse(rdr[0].ToString());
                }
            }

            //richTextBox1.Text = DateTime.Now.ToString() + " - " + CurrentVersion.ToString() + "\r\n" + richTextBox1.Text;

            if (CurrentVersion > LastProcessedVersion)
            {
                string TableName = txtSyncTable.Text;

                cmd = new SqlCommand();
                cmd.Connection = serverConn;
                cmd.CommandText = "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('" + TableName + "'))";

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        MinValidVersion = long.Parse(rdr[0].ToString());
                    }
                }

                LastProcessedVersion = SyncTable(TableName, MinValidVersion, serverConn);

                txtLastSyncVersion.Text = LastProcessedVersion.ToString();
            }
            else
            {
                richTextBox1.Text = DateTime.Now.ToString() + " - No new data \r\n" + richTextBox1.Text;
            }

            IsSyncRunning = false;
        }


        internal long SyncData(long currentVersion)
        {
            IsSyncRunning = true;

            //long CurrentVersion = -1;
            long MinValidVersion = -1;

            // connect to server database
            SqlConnection serverConn = new SqlConnection(ConfigurationManager.ConnectionStrings["SourceConnectionString"].ConnectionString);
            serverConn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = serverConn;
            cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

            using (SqlDataReader rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    currentVersion = long.Parse(rdr[0].ToString());
                }
            }

            //richTextBox1.Text = DateTime.Now.ToString() + " - " + CurrentVersion.ToString() + "\r\n" + richTextBox1.Text;

            if (currentVersion > LastProcessedVersion)
            {
                List<string> tableNames = dbSync.RetrieveTablesToSync();

                foreach (string TableName in tableNames)
                {
                    if (StopProcessing) break;

                    //string TableName = "MessageHistory";

                    cmd = new SqlCommand();
                    cmd.Connection = serverConn;
                    cmd.CommandText = "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('" + TableName + "'))";

                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            MinValidVersion = long.Parse(rdr[0].ToString());
                        }
                    }

                    long ProcessedVersion = SyncTable(TableName, MinValidVersion, serverConn);
                    if (ProcessedVersion > LastProcessedVersion) LastProcessedVersion = ProcessedVersion;
                }

            }
            else
            {
                //richTextBox1.Text = DateTime.Now.ToString() + " - No new data \r\n" + richTextBox1.Text;
                LastProcessedVersion = currentVersion;
            }

            IsSyncRunning = false;

            if (StopProcessing)
                LastProcessedVersion = currentVersion;

            return LastProcessedVersion;
        }


        internal long SyncTable(string tableName, long minValidVersion, SqlConnection sqlConnection)
        {
            richTextBox1.Text = DateTime.Now.ToString() + " - Sync Data: " + tableName + " ... - " + minValidVersion.ToString() + "\r\n" + richTextBox1.Text;

            long SYS_CHANGE_VERSION = -1;
            long MAX_SYS_CHANGE_VERSION = -1;
            string PrimaryColumn = string.Empty;

            minValidVersion--;

            SqlCommand cmd2 = new SqlCommand();
            StringBuilder ColumnNames = new StringBuilder();

            // connect to client database
            SqlConnection clientConn = new SqlConnection(ConfigurationManager.ConnectionStrings["DestinationConnectionString"].ConnectionString);
            clientConn.Open();


            StringBuilder command = new StringBuilder();
            command.Append("SELECT column_name");
            command.Append(" FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE");
            command.Append(" WHERE OBJECTPROPERTY(OBJECT_ID(constraint_name), 'IsPrimaryKey') = 1");
            command.Append(" AND table_name = '" + tableName + "'");

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = clientConn;
            cmd.CommandText = command.ToString();

            using (SqlDataReader rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    PrimaryColumn = rdr[0].ToString();
                }
            }

            command = new StringBuilder();
            //command.Append("SELECT CT.[" + PrimaryColumn + "], t.*, CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION");
            command.Append("SELECT t.*, CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION");
            command.Append(" FROM  " + tableName + " AS t with (nolock)");
            command.Append(" RIGHT OUTER JOIN");
            command.Append("    CHANGETABLE(CHANGES " + tableName + ", " + minValidVersion + ") AS CT");
            command.Append(" ON");
            command.Append("    t.[" + PrimaryColumn + "] = CT.[" + PrimaryColumn + "]");
            command.Append(" order by CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_VERSION");

            cmd = new SqlCommand();
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

                    string SYS_CHANGE_OPERATION = rdr["SYS_CHANGE_OPERATION"].ToString();

                    if (SYS_CHANGE_VERSION > LastProcessedVersion)
                    {

                        try
                        {
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

                                    command.Append(" [" + rdr.GetName(i) + "] = " + Value);
                                }

                                command.Append(" WHERE " + rdr.GetName(0) + " = " + rdr[0].ToString());

                                cmd2 = new SqlCommand();
                                cmd2.Connection = clientConn;
                                cmd2.CommandText = command.ToString();

                                cmd2.ExecuteNonQuery();

                            }
                            else
                            {
                                // Ignore Deletes
                            }

                            richTextBox1.Text = DateTime.Now.ToString() + " - new data to sync ... " + SYS_CHANGE_OPERATION + " - " + SYS_CHANGE_VERSION.ToString() + "\r\n" + richTextBox1.Text;

                        }
                        catch (Exception ex)
                        {
                            if (ex.Message.Contains("duplicate key"))
                            {
                                // ignore duplicate key inserts
                            }
                            else
                            {
                                richTextBox1.Text = DateTime.Now.ToString() + " - ERROR: " + ex.Message + "\r\n" + richTextBox1.Text;
                                //break;
                                //throw;
                            }
                        }

                        Application.DoEvents();
                    }
                    else
                    {
                    }

                }
            }

            richTextBox1.Text = DateTime.Now.ToString() + " - Sync Complete: " + tableName + " - " + MAX_SYS_CHANGE_VERSION.ToString() + "\r\n" + richTextBox1.Text;

            return MAX_SYS_CHANGE_VERSION;
        }

    }
}
