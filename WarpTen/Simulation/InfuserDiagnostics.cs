using System.Collections.Generic;
using System.Text;
using System;

namespace WarpTen.Simulation
{
    public static class InfuserDiagnostics
    {
        public static string Describe(double optimalPerInfuser, int installed, IEnumerable<string> issues,
            bool boundsBlocked, string progress, string advisory = null)
        {
            var text = new StringBuilder();
            text.Append($"Optimal coils: {optimalPerInfuser:F0} per infuser ({optimalPerInfuser * 2:F0} total)\n");
            text.Append($"This infuser: {installed} valid coils\n");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var issue in issues)
                if (!string.IsNullOrWhiteSpace(issue) && unique.Add(issue))
                    text.Append("\n• ").Append(issue);
            if (boundsBlocked)
                text.Append("\nPress F9 to show the no-placement bounds.");
            if (unique.Count == 0)
                text.Append("\nNo setup issues detected.");
            if (!string.IsNullOrWhiteSpace(advisory))
                text.Append("\n\n").Append(advisory);
            text.Append("\n\n").Append(progress);
            return text.ToString();
        }
    }
}