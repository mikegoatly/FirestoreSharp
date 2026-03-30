using System.Text.RegularExpressions;

var msgFile = Args[0];
var rawLines = File.ReadAllText(msgFile).Split('\n');

var subject = "";
foreach (var line in rawLines)
{
    var trimmed = line.Trim();
    if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
    {
        subject = trimmed;
        break;
    }
}

var pattern = @"^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\(.+\))?(!)?: .{1,}$";

if (!Regex.IsMatch(subject, pattern))
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Invalid commit message. Must follow Conventional Commits format:");
    Console.Error.WriteLine("  <type>[optional scope]: <description>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Types: feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert");
    Console.Error.WriteLine("Examples:");
    Console.Error.WriteLine("  feat: add filesystem store");
    Console.Error.WriteLine("  fix(server): correct transaction isolation");
    Console.Error.WriteLine("  feat!: breaking API change");
    Console.Error.WriteLine();
    Environment.Exit(1);
}
