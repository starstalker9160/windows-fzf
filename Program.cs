using System.Data.SQLite;

class Program {
    private static List<string> searchResults = new List<string>();

    static void Main(string[] args) {
        if (args.Length == 0) {
            helpMenu();
            return;
        }
        switch (args[0]) {
            case "-createdb":
                createDb();
                break;
            case "-updatedb":
                updateDb();
                break;
            case "-cleardb":
                clearDb(args.Length > 1 && args.Contains("-d"));
                break;
            case "-s":
                if (args.Length > 1) {
                    search(args.Skip(1).ToArray());
                } else {
                    Console.WriteLine("Please provide search terms after '-s'.");
                }
                break;
        }
        return;
    }

    private static void search(string[] searchTerms) {
        string dbPath = getDbPath();
        if (!File.Exists(dbPath)) {
            Console.WriteLine("No database found. Please first create a database with the `fzf -createdb`.");
            return;
        }

        string wd = Directory.GetCurrentDirectory() + "\\";
        List<string> stuffInWd = new List<string>();

        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
            connection.Open();
            string query = "SELECT Path FROM Files WHERE Path LIKE @wdPrefix";
            using (var command = new SQLiteCommand(query, connection)) {
                command.Parameters.AddWithValue("@wdPrefix", $"{wd}%");
                using (var reader = command.ExecuteReader()) {
                    while (reader.Read()) { stuffInWd.Add(reader["Path"].ToString().Substring(wd.Length)); }
                }
            }
        }

        var segments = new List<List<string>>();
        for (int i = 0; i < stuffInWd.Count; i += 50) {
            segments.Add(stuffInWd.Skip(i).Take(50).ToList());
        }

        void SearchSegment(List<string> segment, string searchTerm) {
            foreach (var path in segment) { 
                if (path.ToLower().Contains(searchTerm.ToLower())) { searchResults.Add(path); }
            }
        }

        void ProcessSegments() {
            while (true) {
                List<string> segment;
                lock (segments) {
                    if (segments.Count == 0) break;
                    segment = segments[0];
                    segments.RemoveAt(0);
                }
                SearchSegment(segment, searchTerms[0]);
                segment = null;
            }
        }

        var threads = new List<Thread>();
        for (int i = 0; i < 3; i++) {
            var thread = new Thread(ProcessSegments);
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads) { thread.Join(); }
    
        foreach (var result in searchResults) { Console.WriteLine(result); }
    }

    public static bool onlyFile(string directoryPath) {
        if (string.IsNullOrEmpty(directoryPath) || string.IsNullOrEmpty("database.db")) return false;
        var files = Directory.GetFiles(directoryPath);
        return files.Length == 1 && Path.GetFileName(files[0]) == "database.db";
    }

    private static string getDbPath() {
        string dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "windows-fzf");
        Directory.CreateDirectory(dbFolder);
        return Path.Combine(dbFolder, "database.db");
    }

    private static void helpMenu() {
        Console.WriteLine(@"
Usage: fzf [command] [options]

Commands:
  -s [searchTerm]    Search for files in the database.
  -createdb          Create a new database with files to search within the db.
  -updatedb          Update the database with paths of all files in the current directory and subdirectories.
  -cleardb           Clear the database or delete the folder based on the provided options.

Options for -cleardb:
  -d                 Delete the folder and database (will fail if there are other items in the folder).           

Example Usage:
  fzf -createdb
  fzf -updatedb
  fzf -cleardb -d
        ");
    }


    private static void createDb() {
        string dbPath = getDbPath();

        if (!File.Exists(dbPath)) {
            SQLiteConnection.CreateFile(dbPath);
            using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
                connection.Open();
                string createTableQuery = "CREATE TABLE IF NOT EXISTS Files (Id INTEGER PRIMARY KEY AUTOINCREMENT, Path TEXT)";
                using (var command = new SQLiteCommand(createTableQuery, connection)) { command.ExecuteNonQuery(); }
            }
            Console.WriteLine($"Database created succesfully");
        } else {
            Console.WriteLine("Database already exists, please use `fzf -cleardb -d` to remove it, then run this command again.");
        }
    }

    private static void updateDb() {
        string dbPath = getDbPath();

        if (!File.Exists(dbPath)) {
            Console.WriteLine("No database found to update, please use `fzf -createdb` to first make a database");
            return;
        }

        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
            connection.Open();

            // Create a set of all file paths currently in the database
            var currentFilesInDb = new HashSet<string>();
            string selectQuery = "SELECT Path FROM Files";
            using (var command = new SQLiteCommand(selectQuery, connection)) {
                using (var reader = command.ExecuteReader()) {
                    while (reader.Read()) {
                        currentFilesInDb.Add(reader.GetString(0));
                    }
                }
            }

            // Iterate over all the files in the current directory and subdirectories
            foreach (var file in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories)) {
                if (!currentFilesInDb.Contains(file)) {
                    // If the file is not already in the database, insert it
                    string insertQuery = "INSERT INTO Files (Path) VALUES (@path)";
                    using (var insertCommand = new SQLiteCommand(insertQuery, connection)) {
                        insertCommand.Parameters.AddWithValue("@path", file);
                        insertCommand.ExecuteNonQuery();
                    }
                }
            }

            // Optionally, remove entries from the database for files that no longer exist
            var currentFilesOnDisk = new HashSet<string>(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories));
            foreach (var fileInDb in currentFilesInDb) {
                if (!currentFilesOnDisk.Contains(fileInDb)) {
                    string deleteQuery = "DELETE FROM Files WHERE Path = @path";
                    using (var deleteCommand = new SQLiteCommand(deleteQuery, connection)) {
                        deleteCommand.Parameters.AddWithValue("@path", fileInDb);
                        deleteCommand.ExecuteNonQuery();
                    }
                }
            }
            Console.WriteLine("Database updated with file paths.");
        }
    }


    private static void clearDb(bool del) {
        string dbPath = getDbPath();
        string dbFolderPath = Path.GetDirectoryName(dbPath);

        if (!File.Exists(dbPath)) {
            Console.WriteLine("No database found to clear / delete.");
            return;
        }

        if (del) {
            Directory.Delete(dbFolderPath, true); Console.WriteLine("Other files did exist, deleted them all.");
        } else {
            File.Delete(dbPath);
            createDb();
            Console.WriteLine("Database cleared, run -update db to update the databse.");
        }
    }
}