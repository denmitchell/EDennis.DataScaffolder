using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;


namespace EDennis.DataScaffolder {

    /// <summary>
    /// This form generates a C# class called "DataFactory.cs", 
    /// which holds static methods that return data that
    /// reside in tables in one ore more databases.  The form 
    /// uses an appsettings.json file from a .NET Core project
    /// to identify one or more connection strings through 
    /// which data are pulled.  The appsettings.json file must
    /// have a top-level property called "ConnectionStrings"
    /// with a second-level property for each connection string.
    /// The DataFactory.cs file is saved in a Models folder
    /// (when it exists) or the same directory as the 
    /// appsettings.json file.
    /// 
    /// NOTE: __EFMigrationsHistory will be ignored
    /// NOTE: schemas like '%_history' and '_maintenance' will be ignored
    /// NOTE: views will be ignored
    /// NOTE: columns like 'sysstart%' and 'sysend%' will be ignored
    /// </summary>
    public partial class MainForm : Form {

        //variable to hold the current connection string key
        private string _currentConnectionStringName = "";
        private string _appsettingsFileName = "";
        private string _namespace = "";

        private List<Mapping> _mappings = new List<Mapping>();

        private const string DATA_FACTORY_FILE_NAME = "DataFactory.cs";

        public MainForm() {
            InitializeComponent();
        }

        /// <summary>
        /// Locates the appsettings.json file that contains the
        /// connection string(s) and initiates the data scaffolding
        /// process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartButton_Click(object sender, EventArgs e) {

            //open a file dialog that will be used to locate the target
            //appsettings.json file
            using (OpenFileDialog openFileDialog = new OpenFileDialog()) {

                //as a starting point, open the user's repos folder
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                openFileDialog.InitialDirectory = $"{userProfile}\\source\\repos";

                //only allow appsettings.json files to be selected
                openFileDialog.Filter = "appsettings*.json files (appsettings*.json)|appsettings*.json";

                //if a file is selected ...
                if (openFileDialog.ShowDialog() == DialogResult.OK) {

                    //get the file name and containing folder path
                    var fInfo = new FileInfo(openFileDialog.FileName);
                    var fileName = fInfo.Name;
                    _appsettingsFileName = fileName;

                    var projectPath = fInfo.DirectoryName;

                    //get a dictionary of connection string names/values
                    var connectionStrings = GetConnectionStrings(projectPath);

                    //calculate the destination path for the DataFactory.cs file
                    var destinationPath = GetDestinationPath(projectPath);

                    //get namespace
                    _namespace = GetNamespace(projectPath, destinationPath);

                    foreach(var connectionString in connectionStrings.Values)
                        BuildMappings(connectionString);

                    var nmspace = _mappings.FirstOrDefault()?.NamespaceName;
                    if (nmspace != null)
                        _namespace = nmspace;


                    //call the main method to query data and generate the file
                    GenerateClasses(connectionStrings, destinationPath);

                    //update the status text
                    lblStatus.Text = "Scaffolding Complete!";

                    //shift the start button to the left to make room for the close button
                    StartButton.Location = new Point(StartButton.Location.X - 70, StartButton.Location.Y);

                    //refresh the start button and call visible to force the UI to refresh
                    StartButton.Refresh();
                    StartButton.Visible = true;

                    //show the close button
                    CloseButton.Visible = true;

                }

            }
        }

        /// <summary>
        /// Initializes a stream writer to write the .cs file
        /// </summary>
        /// <param name="connectionStrings">Dictionary of connection string names/values</param>
        /// <param name="destinationPath">Path to the new .cs file</param>
        private void GenerateClasses(Dictionary<string, string> connectionStrings, string destinationPath) {
            // Create a file to write to.
            using (StreamWriter sw = File.CreateText($"{destinationPath}\\{DATA_FACTORY_FILE_NAME}")) {
                ScaffoldData(sw, connectionStrings);
            }
        }

        /// <summary>
        /// Calculates the path to the new .cs file.  If a Models
        /// folder exists, then the path will target that folder;
        /// otherwise, the path will be the project root.
        /// </summary>
        /// <param name="projectPath">project root directory</param>
        /// <returns></returns>
        private string GetDestinationPath(string projectPath) {

            //get the root directory associated with the project
            var dInfo = new DirectoryInfo(projectPath);

            //calculate the candidate path, targeting a Models subfolder
            var modelsPath = projectPath + "\\Models";

            //if the Models subfolder exists, use it; otherwise,
            //use the project root.
            if (Directory.Exists(modelsPath))
                return modelsPath;
            else
                return projectPath;

        }

        /// <summary>
        /// Use Microsoft.Extensions.Configuration.ConfigurationBuilder
        /// to build an object graph representing the appsettings.json
        /// file.  From that object graph, build a dictionary of 
        /// connection string names/values
        /// </summary>
        /// <param name="appsettingsPath">Path to the appsettings.json file</param>
        /// <returns></returns>
        private Dictionary<string, string> GetConnectionStrings(string appsettingsPath) {

            //build object graph representing configurations from appsettings.json

            IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(appsettingsPath)
            .AddJsonFile(_appsettingsFileName)
            .Build();

            //declare a dictionary for holding connection string keys/value pairs
            var dict = new Dictionary<string, string>();

            //loop over all defined connection strings in appsettings.json, 
            //and add them to the dictionary
            foreach (var section in configuration.GetSection("ConnectionStrings").GetChildren()) {
                var key = CleanString(section.Key); //use a cleaned version of the string
                var value = section.Value;
                dict.Add(key, value);
            }

            return dict;
        }


        /// <summary>
        /// Cleans a string by removing all characters except letters
        /// and numbers.  Also removes all leading numbers
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private string CleanString(string input) {

            bool letterAdded = false;
            var sb = new StringBuilder();
            var chars = input.ToCharArray();
            foreach (char c in chars) {
                if ((c >= 64 && c <= 90) || (c >= 97 && c <= 122)) {
                    sb.Append(c);
                    letterAdded = true;
                } else if (c >= 48 && c <= 57 && letterAdded) {
                    sb.Append(c);
                }

            }
            //provide feedback to user and throw exception if the connection string
            //name has no characters left after removing all invalid characters
            if (sb.Length == 0) {
                var msg = $"The connection string name {input} cannot be transformed into a valid class name.";
                MessageBox.Show(msg);
                throw new ArgumentException(msg);
            }
            return sb.ToString();
        }


        /// <summary>
        /// Loops over all connection strings and tables and calls functions
        /// that generate the c# code for each property assignment
        /// </summary>
        /// <param name="sw">StreamWriter to which output is written</param>
        /// <param name="cxnStrings">dictionary of key/value pairs</param>
        public void ScaffoldData(StreamWriter sw, Dictionary<string, string> cxnStrings) {

            //show status text and progress bar
            lblStatus.Visible = true;
            ProgressBar1.Visible = true;



            //generate the first few lines of the .cs file
            ScaffoldFileStart(sw);

            //loop over each of the connection strings
            foreach (var cs in cxnStrings) {

                var connectionStringName = cs.Key;
                var connectionString = cs.Value;

                //update the current connection string name (key)
                _currentConnectionStringName = cs.Key;

                //build the class header line for the connection string
                sw.WriteLine($"    public static partial class {connectionStringName}DataFactory {{");

                //get the list of table names associated with the current connection string
                var tableNames = GetTableNames(connectionString);
                var ds = new DataSet();

                //loop over each of the table names, building a datatable
                //and scaffolding the table as c# property assignments
                foreach (var tableName in tableNames) {
                    AddDataTable(ref ds, connectionString, tableName);
                    ScaffoldTable(sw, ds.Tables[tableName]);
                }

                //end the class definition
                sw.WriteLine("    }");

            }

            //end the namespace definition
            ScaffoldFileEnd(sw);
        }

        /// <summary>
        /// Writes out the first few lines of the c# file.
        /// </summary>
        /// <param name="sw">StreamWriter used to write the output</param>
        private void ScaffoldFileStart(StreamWriter sw) {
            sw.WriteLine("// file generated by EDennis.DataScaffolder");
            sw.WriteLine($"// using connection string(s) from {_appsettingsFileName}");
            sw.WriteLine($"using System;");
            sw.WriteLine($"namespace {_namespace} {{");
        }


        private string GetNamespace(string projectPath, string destinationPath) {

            var file = Directory.EnumerateFiles(projectPath)
                .Where(f => Path.GetExtension(f) == ".csproj")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .FirstOrDefault();

            if (file != null) {
                if (destinationPath.EndsWith("Models"))
                    return file + ".Models";
                else
                    return file;
            }

            return "DataScaffolder";
        }


        /// <summary>
        /// Writes the end of the namespace
        /// </summary>
        /// <param name="sw">StreamWriter used to write the output</param>
        private void ScaffoldFileEnd(StreamWriter sw) {
            sw.WriteLine("}");
        }

        /// <summary>
        /// Writes all c# code associated with a datatable as a method
        /// that generates the data as collection initializer code 
        /// </summary>
        /// <param name="sw">StreamWriter used to write the output</param>
        /// <param name="dt">The current datatable</param>
        private void ScaffoldTable(StreamWriter sw, DataTable dt) {

            //short-circuit if the table has no data
            if (dt.Rows.Count == 0)
                return;

            //display the current connection string name and table name
            //and reset the progress bar
            lblStatus.Text = $"Scaffolding {_currentConnectionStringName}:{dt.TableName}";
            ProgressBar1.Value = 0;

            string[] schemaAndTable = dt.TableName.Split('.')
                .Select(t => t.Trim('[', ']'))
                .ToArray();

            var fullTableName = dt.TableName.Replace(".", "_");
            var schemaName = schemaAndTable[0];
            var tableName = schemaAndTable[1];
            var className = tableName;

            var recs = _mappings.Where(x => x.SchemaName == schemaName && x.TableName == tableName);

            if (recs.Count() > 0) {
                className = recs.FirstOrDefault()?.ClassName;
            }

            //write out initial lines for the factory method and collection initializer
            sw.WriteLine($"        public static {className}[] {fullTableName}Records {{ get; set; }}");
            sw.WriteLine($"            = new {className}[] {{");

            //set the progress bar's maximum value to the count of records
            ProgressBar1.Maximum = dt.Rows.Count;

            //loop over all records in the table
            foreach (DataRow row in dt.Rows) {

                //write the initial code that creates a new object
                sw.WriteLine($"                new {className} {{");

                //filter the columns to not display SysStart* and SysEnd* columns
                var cols = dt.Columns.Cast<DataColumn>();
                cols = cols
                    //.Where(c => !c.ColumnName.ToLower().StartsWith("sysstart") && !c.ColumnName.ToLower().StartsWith("sysend"))
                    .OrderBy(c => c.Ordinal);

                //loop over all columns, writing out the property assignments
                foreach (DataColumn col in cols) {

                    var propertyName = col.ColumnName;
                    if (recs.Count() > 0)
                        propertyName = recs.Where(x=>x.ColumnName == col.ColumnName).FirstOrDefault()?.PropertyName;


                    var strVal = GetStringValue(row, col);
                    sw.WriteLine($"                        {propertyName} = {strVal},");
                }

                //write the end of the object
                sw.WriteLine($"                }},");

                //update the progress bar
                ProgressBar1.Value++;
            }

            //write the end of the factory method
            sw.WriteLine($"            }};");
        }

        /// <summary>
        /// Get the property assignment value as a string
        /// </summary>
        /// <param name="row">the current datarow</param>
        /// <param name="col">the current datacolumn</param>
        /// <returns>string representation of value part of C# property assignment code</returns>
        private string GetStringValue(DataRow row, DataColumn col) {

            //get the current cell's value
            var obj = row[col];

            //short-circuit if null
            if (obj == DBNull.Value)
                return "null";

            //based upon the column's data type, generate the
            //appropriate c# code
            switch (col.DataType.Name) {
                case nameof(DateTime):
                    var d = (DateTime)obj;
                    return $"new DateTime({d.Year},{d.Month},{d.Day},{d.Hour},{d.Minute},{d.Second})";
                case nameof(Boolean):
                    var b = (bool)obj;
                    return b ? "true" : "false";
                case nameof(Char):
                    var c = (char)(obj);
                    return $"'{c}'";
                case nameof(String):
                    var s = obj.ToString();
                    return $"\"{s}\"";
                case nameof(Decimal):
                    var dc = (decimal)(obj);
                    return $"{dc}M";
                default:
                    var v = obj.ToString();
                    return $"{v}";
            }
        }

        /// <summary>
        /// Populates a dataset with all data from a table
        /// </summary>
        /// <param name="ds">dataset</param>
        /// <param name="connectionString">connection string value</param>
        /// <param name="tableName">table name</param>
        private void AddDataTable(ref DataSet ds, string connectionString, string tableName) {
            using (var cxn = new SqlConnection(connectionString)) {
                cxn.Open();
                var sql = $"select * from {tableName};";
                var cols = GetColumnNames(connectionString, tableName);
                var colsJoined = string.Join(",", cols);
                sql = sql.Replace("*", colsJoined);
                var adapter = new SqlDataAdapter(sql, cxn);
                var dt = new DataTable(tableName);
                adapter.Fill(dt);
                ds.Tables.Add(dt);
            }
        }

        internal class Mapping {
            public string SchemaName { get; set; }
            public string TableName { get; set; }
            public string ColumnName { get; set; }
            public string NamespaceName { get; set; }
            public string ClassName { get; set; }
            public string PropertyName { get; set; }
        } 


        public void BuildMappings(string connectionString) {
            var sql =
@"
select
	f.SchemaName, 
	f.TableName, 
	f.ColumnName, 
	replace(f.Name,'efcore:','') NamespaceName,
	substring(f.value,1,charindex('.',f.value)-1) ClassName, 
	substring(f.value,charindex('.',f.value)+1, len(f.value)) PropertyName 
	from (
	select
	c.table_schema SchemaName, 
	c.table_name TableName, 
	c.column_name ColumnName, 
	convert(varchar(255),f.Name) Name,
	convert(varchar(255),f.Value) Value
	from INFORMATION_SCHEMA.columns c
		cross apply
	::fn_listextendedproperty(
		default,
		'schema', c.table_schema,
		'table',c.table_name,
		'column',c.column_name) f
		
		where f.Name is not null 
			and f.Value is not null
			and convert(varchar(255),f.Name) like 'efcore:%'
			and convert(varchar(255),f.value) like '%.%'
	) f
";
            //connect to the database and retrieve the list of tables
            using (var cxn = new SqlConnection(connectionString)) {
                cxn.Open();

                //initialize a dataadapter, and use it to fill a dataset
                var adapter = new SqlDataAdapter(sql, cxn);
                var dt = new DataTable();
                adapter.Fill(dt);

                //looping over each row from the information_schema.tables table,
                //build a list of table names
                if(dt.Rows.Count > 0)
                    foreach (DataRow row in dt.Rows) {
                        _mappings.Add(new Mapping {
                            SchemaName = row["SchemaName"].ToString(),
                            TableName = row["TableName"].ToString(),
                            ColumnName = row["ColumnName"].ToString(),
                            NamespaceName = row["NamespaceName"].ToString(),
                            ClassName = row["ClassName"].ToString(),
                            PropertyName = row["PropertyName"].ToString()
                        });
                    }


            }



        }



            /// <summary>
            /// Gets a list of tables from the information_schema
            /// </summary>
            /// <param name="connectionString">the connection string value</param>
            /// <returns>a list of tables</returns>
            private List<string> GetTableNames(string connectionString) {

            //initialize the list of tables
            var tableNames = new List<string>();

            //connect to the database and retrieve the list of tables
            using (var cxn = new SqlConnection(connectionString)) {
                cxn.Open();
                var sql =
        @"select t.table_schema + '.' + t.table_name table_name
    from information_schema.tables t
	where t.table_schema not like '[_]%'
		and t.table_type = 'BASE TABLE' 
		and t.table_name <> '__EFMigrationsHistory'
	order by t.table_schema, t.table_name;";

                //initialize a dataadapter, and use it to fill a dataset
                var adapter = new SqlDataAdapter(sql, cxn);
                var dt = new DataTable();
                adapter.Fill(dt);

                //looping over each row from the information_schema.tables table,
                //build a list of table names
                foreach (DataRow row in dt.Rows) {
                    tableNames.Add(row["table_name"].ToString());
                }
            }
            return tableNames;
        }


        /// <summary>
        /// Gets a list of relevant columns from the information_schema
        /// </summary>
        /// <param name="connectionString">the connection string value</param>
        /// <returns>a list of column names</returns>
        private List<string> GetColumnNames(string connectionString, string tableName) {

            //initialize the list of tables
            var columnNames = new List<string>();

            //connect to the database and retrieve the list of tables
            using (var cxn = new SqlConnection(connectionString)) {
                cxn.Open();
                var sql =
        $@"select c.name 
	from sys.columns c 
	inner join sys.objects o
		on c.object_id = o.object_id
	inner join sys.schemas s
		on s.schema_id = o.schema_id
	inner join information_schema.columns ic
		on ic.column_name = c.name
		and ic.table_name = o.name
		and ic.table_schema = s.name
	where c.object_id = object_id('{tableName}','U') 
		and is_computed = 0 
		and generated_always_type_desc = 'NOT_APPLICABLE'	
	order by ic.ordinal_position;";

                //initialize a dataadapter, and use it to fill a dataset
                var adapter = new SqlDataAdapter(sql, cxn);
                var dt = new DataTable();
                adapter.Fill(dt);

                //looping over each row from the information_schema.tables table,
                //build a list of table names
                foreach (DataRow row in dt.Rows) {
                    columnNames.Add(row["name"].ToString());
                }
            }
            return columnNames;
        }


        /// <summary>
        /// Exits the application
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CloseButton_Click(object sender, EventArgs e) {
            Application.Exit();
        }
    }
}
