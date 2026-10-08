using Officelings.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var rules = args.Length > 0 ? args : IsoEditorReviewCases.Rules;
        var passed = 0; var failed = 0;
        foreach (var rule in rules)
        {
            try { IsoEditorReviewCases.Run(rule); Console.WriteLine(rule + " PASS"); passed++; }
            catch (Exception error) { Console.WriteLine(rule + " FAIL: " + error); failed++; }
        }
        Console.WriteLine($"Editor review checks: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}