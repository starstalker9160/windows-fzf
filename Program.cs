using System;
using System.IO;
using System.Data.SQLite;

class Program {
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
                clearDb(args.Length > 1 && args.Contains("-d"), args.Length > 1 && args.Contains("-f"));
                break;
            default:
                search();
                break;
        }
        return;
    }

    private static void search() {}

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
  -createdb      Create a new database with files to search within the db.
  -updatedb      Update the database with paths of all files in the current directory and subdirectories.
  -cleardb       Clear the database or delete the folder based on the provided options.

Options for -cleardb:
  -d             Delete the folder and database (will fail if there are other items in the folder).
  -f             Force delete the folder and all its contents, including the database, regardless of whether the folder is empty.

Example Usage:
  fzf -createdb
  fzf -updatedb
  fzf -cleardb -d
  fzf -cleardb -d -f

Note: 
- If using -cleardb with -d and -f, the folder will be deleted even if it contains other files.
- Ensure you use the correct commands and options to avoid unintended data loss.
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
            Console.WriteLine("No database found to update. Please create a database with command -createdb.");
            return;
        }

        using (var connection = new SQLiteConnection($"Data Source={dbPath};Version=3;")) {
            connection.Open();

            string deleteQuery = "DELETE FROM Files";
            using (var command = new SQLiteCommand(deleteQuery, connection)) {
                command.ExecuteNonQuery();
            }

            foreach (var file in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories)) {
                string insertQuery = "INSERT INTO Files (Path) VALUES (@path)";
                using (var command = new SQLiteCommand(insertQuery, connection)) {
                    command.Parameters.AddWithValue("@path", file);
                    command.ExecuteNonQuery();
                }
            }

            Console.WriteLine("Database updated with file paths.");
        }
    }

    private static void clearDb(bool del, bool force) {
        string dbPath = getDbPath();
        string dbFolderPath = Path.GetDirectoryName(dbPath);

        if (!File.Exists(dbPath)) {
            Console.WriteLine("No database found to clear / delete.");
            return;
        }

        if (del) {
            if (onlyFile(dbFolderPath)) {
                Directory.Delete(dbFolderPath, true);
                Console.WriteLine("Folder and database within were deleted successfully.");
            }
            else if (!onlyFile(dbFolderPath) && force) { Directory.Delete(dbFolderPath, true); Console.WriteLine("Other files did exist, deleted them all.");} 
            else { Console.WriteLine("Folder has other files in it, aborted deletion.\nUse `fzf -cleardb -d -f` to force delete everything"); }
        } else {
            File.Delete(dbPath);
            createDb();
            if (force) { Console.WriteLine("Adding -f was not neccesary as this action does not delete any files rather just clears the databse"); }
            Console.WriteLine("Database cleared, run -update db to update the databse.");
        }
    }
}