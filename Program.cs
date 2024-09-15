using System.Data.SQLite;
using System.Diagnostics;

class Program {
    private static List<string> searchResults = new List<string>();

    static void Main(string[] args) {
        if (args.Length == 0) {
            helpMenu();
            return;
        }
        switch (args[0]) {
            case "-h" or "-help":
                helpMenu();
                break;
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
                    printErr("Error : No searchTerm provided after `-s`.");
                }
                break;
            default:
                    printErr("Error : Invalid command.");
                    Console.WriteLine("Please refer to help menu accessible throuhg `fzf -h` or `fzf -help`");
                break;
        }
        return;
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

    private static void printErr(string s) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(s);
        Console.ResetColor();
    }

    private static void createDb(bool silence = false) {
        string dbPath = getDbPath();

        if (!File.Exists(dbPath)) {
            SQLiteConnection.CreateFile(dbPath);
            using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
                connection.Open();
                using (var command = new SQLiteCommand("CREATE TABLE IF NOT EXISTS Files (Id INTEGER PRIMARY KEY AUTOINCREMENT, Path TEXT)", connection)) { command.ExecuteNonQuery(); }
            }
            if (silence) { return; }
            Console.WriteLine($"Database created succesfully, please use `fzf -updatedb to add enteries to the database`");
        } else {
            printErr("Error: Database already exists.");
            Console.WriteLine("Please use `fzf -cleardb -d` first and then try running this command again");
        }
    }

    private static void search(string[] searchTerms) {
        string dbPath = getDbPath();
        if (!File.Exists(dbPath)) {
            printErr("Error: No database found.");
            Console.WriteLine("Please first create a database with the `fzf -createdb`.");
            return;
        }

        string wd = Directory.GetCurrentDirectory() + "\\";
        List<string> stuffInWd = new List<string>();
        var segments = new List<List<string>>();

        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
            connection.Open();
            using (var command = new SQLiteCommand("SELECT Path FROM Files WHERE Path LIKE @wdPrefix", connection)) {
                command.Parameters.AddWithValue("@wdPrefix", $"{wd}%");
                using (var reader = command.ExecuteReader()) {
                    while (reader.Read()) { stuffInWd.Add(reader["Path"].ToString().Substring(wd.Length)); }
                }
            }
        }

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
    

        foreach (var result in searchResults) { Console.WriteLine( "\\" + result); }

        if (searchResults.Count == 0) { Console.WriteLine("No matching results found."); }

        searchResults.Clear(); segments.Clear(); stuffInWd.Clear();
    }

    private static void updateDb() {
        string dbPath = getDbPath();

        if (!File.Exists(dbPath)) {
            printErr("Error: No database found to update.");
            Console.WriteLine("Please use `fzf -createdb` first and then try running this command again.");
            return;
        }

        string wd = Directory.GetCurrentDirectory() + "\\";
        List<string> stuffInWd = new List<string>();
        string[] dirsInWd = Directory.GetDirectories(wd);
        stuffInWd.AddRange(Directory.GetFiles(wd));
        stuffInWd.AddRange(dirsInWd);

        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
            connection.Open();
            using (var command = new SQLiteCommand("DELETE FROM Files WHERE Path LIKE @wdPrefix", connection)) {
                command.Parameters.AddWithValue("@wdPrefix", $"{wd}%");
                command.ExecuteNonQuery();
            }
        }

        var stuff = new HashSet<string>(stuffInWd);

        void ProcessDirectory(string dir) {
            try {
                foreach (var file in Directory.GetFiles(dir)) {
                    stuff.Add(file);
                }
                foreach (var subDir in Directory.GetDirectories(dir)) {
                    ProcessDirectory(subDir);
                }
            } catch (UnauthorizedAccessException) {
                printErr($"Error : Access denied to directory {dir}, skipping it.");
            }
        }

        var dirsQueue = new Queue<string>(dirsInWd);
        
        void ThreadProcess() {
            while (true) {
                string dir;
                lock (dirsQueue) {
                    if (dirsQueue.Count == 0) break;
                    dir = dirsQueue.Dequeue();
                }
                ProcessDirectory(dir);
            }
        }

        var threads = new List<Thread>();
        for (int i = 0; i < 3; i++) {
            var thread = new Thread(ThreadProcess);
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads) {
            thread.Join();
        }

        void AddToDatabase(HashSet<string> paths) {
            using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
                connection.Open();
                foreach (var path in paths) {
                    using (var command = new SQLiteCommand("INSERT OR IGNORE INTO Files (Path) VALUES (@path)", connection)) {
                        command.Parameters.AddWithValue("@path", path);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        void AddStuffInWdToDatabase() {
            using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
                connection.Open();
                foreach (var path in stuffInWd) {
                    using (var command = new SQLiteCommand("INSERT OR IGNORE INTO Files (Path) VALUES (@path)", connection)) {
                        command.Parameters.AddWithValue("@path", path);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        var addToDatabaseThread1 = new Thread(() => AddToDatabase(stuff));
        var addToDatabaseThread2 = new Thread(AddStuffInWdToDatabase);

        addToDatabaseThread1.Start();
        addToDatabaseThread2.Start();

        addToDatabaseThread1.Join();
        addToDatabaseThread2.Join();

        stuff.Clear(); stuffInWd.Clear();

        Console.WriteLine("Database update complete.");
    }


    private static void clearDb(bool del) {
        string dbPath = getDbPath();
        string dbFolderPath = Path.GetDirectoryName(dbPath);

        if (!File.Exists(dbPath)) {
            printErr("Error: No database found to clear / delete.");
            return;
        }

        if (del) {
            Directory.Delete(dbFolderPath, true); Console.WriteLine("Deleted database and directory containing it.");
        } else {
            File.Delete(dbPath);
            createDb(true);
            Console.WriteLine("Database cleared, run -update db to update the databse.");
        }
    }
}