using Vibedotcom.Tests;

// The same regression checks run under xUnit when NuGet is available.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Action[] checks = [IsoV3RegressionChecks.SeatedContactsAndOcclusion,
            IsoV3RegressionChecks.TvScreenGlowStates, IsoV3RegressionChecks.NavigationAndTwelveAgents,
            IsoV3RegressionChecks.WalkersAndTransitions, IsoV3RegressionChecks.WpfPixelsAndBounds];
        if (args.Length > 0) checks = checks.Where(c => args.Contains(c.Method.Name)).ToArray();
        if (checks.Length == 0) throw new ArgumentException("Unknown ISO check.");
        var failed = 0;
        foreach (var check in checks)
        {
            try { check(); Console.WriteLine("PASS " + check.Method.Name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + check.Method.Name + ": " + e); }
        }
        Console.WriteLine($"ISO v3 checks: {checks.Length - failed} passed, {failed} failed. Synthetic data; no network.");
        return failed == 0 ? 0 : 1;
    }
}
