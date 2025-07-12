using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

#region Script

if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    Log("Error", "This application is designed for Linux only.", ConsoleColor.Red);
    Environment.Exit(1);
}

if (!IsRunningAsRoot())
{
    Log("Error", "Please run this script as root. (use sudo)", ConsoleColor.Red);
    Environment.Exit(1);
}

const string resolvConfPath = "/etc/resolv.conf";
var providers = new Dictionary<string, string[]>
{
    { "Shecan", ["178.22.122.100", "185.51.200.2"] },
    { "Radar", ["10.202.10.10", "10.202.10.11"] },
    { "Electro", ["78.157.42.100", "78.157.42.101"] },
    { "Begzar", ["185.55.226.26", "185.55.226.25"] },
    { "DNS Pro", ["87.107.110.109", "87.107.110.110"] },
    { "403", ["10.202.10.202", "10.202.10.102"] },
    { "Google", ["8.8.8.8", "8.8.4.4"] },
    { "Cloudflare", ["1.1.1.1", "1.0.0.1"] },
    { "Reset to Default", ["127.0.0.53"] }
};

ShowCurrentDns();

while (true)
{
    ShowMenu();
    Console.Write($"Select (0-{providers.Count}): ");
    string? choiceInput = Console.ReadLine();
    Console.WriteLine();

    if (!int.TryParse(choiceInput, out int choice) || choice < 0 || choice > providers.Count)
    {
        Console.Clear();
        Log("Invalid input", "Please enter a valid choice.", ConsoleColor.Red);
        continue;
    }

    if (choice is 0)
    {
        break;
    }

    var (provider, providerDnsList) = providers.ElementAt(choice - 1);

    BackupResolvConf();
    UpdateResolvConf(provider, providerDnsList);
    RestartResolved();
    ShowCurrentDns();
    Console.WriteLine();
    break;
}

#endregion


bool IsRunningAsRoot()
{
    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName               = "id",
            Arguments              = "-u",
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        });
        process?.WaitForExit();
        string output = process?.StandardOutput.ReadToEnd().Trim() ?? string.Empty;
        return output == "0";
    }
    catch (Exception)
    {
        return false;
    }
}

void ShowCurrentDns()
{
    Console.WriteLine();
    try
    {
        var nameservers = File.ReadLines(resolvConfPath)
                              .Where(line => line.Trim().StartsWith("nameserver"))
                              .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])
                              .ToList();
        Log("Current DNS servers", nameservers.Count == 0 ? "None" : String.Join(", ", nameservers), ConsoleColor.Yellow);
    }
    catch (UnauthorizedAccessException)
    {
        Log("Error", $"Permission denied to read {resolvConfPath}.", ConsoleColor.Red);
    }
    catch (FileNotFoundException)
    {
        Log("Error", $"Could not find {resolvConfPath}. it doesn't exist!", ConsoleColor.Red);
    }
    catch (Exception ex)
    {
        Log("Error", $"Cannot read {resolvConfPath}. ({ex.GetType()})", ConsoleColor.Red);
    }
}

void ShowMenu()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Available DNS Providers:");
    Console.ResetColor();
    var index = 1;
    foreach (var (provider, dnsList) in providers)
    {
        var line = new StringBuilder()
                  .Append($"  {index++}) ")
                  .Append(provider.PadRight(18))
                  .Append(String.Join(", ", dnsList));
        Console.WriteLine(line);
    }

    Console.WriteLine("  0) Exit");
    Console.WriteLine();
}

void BackupResolvConf()
{
    var backupPath = $"{resolvConfPath}.bak.{DateTime.Now}";
    try
    {
        File.Copy(resolvConfPath, backupPath, true);
        Log("Backup created", backupPath, ConsoleColor.Green);
    }
    catch (FileNotFoundException)
    {
        Log("Warning", $"The file {resolvConfPath} did not exist. could not create backup.", ConsoleColor.Yellow);
    }
    catch (Exception ex)
    {
        Log("Error", $"Could not create backup! {ex.GetType()}", ConsoleColor.Red);
    }
}

void UpdateResolvConf(string provider, string[] dnsList)
{
    try
    {
        var newContents = new StringBuilder();
        foreach (var dns in dnsList)
        {
            newContents.Append("nameserver ")
                       .AppendLine(dns);
        }

        List<string>? existingLines = null;
        try
        {
            existingLines = File.ReadLines(resolvConfPath).ToList();
        }
        catch
        {
        }
        
        foreach (var line in existingLines ?? [])
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("nameserver") || trimmedLine.StartsWith("#"))
            {
                continue;
            }

            newContents.AppendLine(trimmedLine);
        }
        
        File.WriteAllText(resolvConfPath, newContents.ToString());
        SetFilePermissions(resolvConfPath, "644");
    }
    catch (Exception ex)
    {
        Log("Error", $"Could not update {resolvConfPath} {ex.GetType()}", ConsoleColor.Red);
    }
}

void RestartResolved()
{
    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName               = "systemctl",
            Arguments              = "restart systemd-resolved",
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        });
        process?.WaitForExit();
        if (process.ExitCode == 0)
        {
            Log("Restart", "systemd-resolved successful.", ConsoleColor.Green);
            return;
        }
    }
    catch (Exception)
    {
    }

    Log("Warning", "Failed to restart systemd-resolved", ConsoleColor.Red);
}

void SetFilePermissions(string filePath, string permissions)
{
    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName        = "chmod",
            Arguments       = $"{permissions} {filePath}",
            UseShellExecute = false,
            CreateNoWindow  = true
        });
        process?.WaitForExit();
        if (process?.ExitCode != 0)
        {
            Log("Warning", $"Failed to set permissions on {resolvConfPath}. {process.StandardError.ReadToEnd()}", ConsoleColor.Red);
        }
    }
    catch (Exception ex)
    {
        Log("Warning", $"Failed to set permissions on {resolvConfPath}. {ex.GetType()}", ConsoleColor.Red);
    }
}

void Log(string title, string message, ConsoleColor color)
{
    var origColor = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine($"{title}: {message}");
    Console.ForegroundColor = origColor;
}