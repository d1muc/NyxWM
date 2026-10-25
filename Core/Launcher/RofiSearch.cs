using System;
using System.Collections.Generic;

namespace nyxWM
{
    /// <summary>
    /// Filters the application list for the Rofi menu. Prefix matches rank above
    /// word-start matches, which rank above matches in the middle of a name.
    /// </summary>
    internal static class RofiSearch
    {
        internal static IReadOnlyList<RofiApp> Filter(IReadOnlyList<RofiApp> apps, string? query)
        {
            if (apps == null || apps.Count == 0)
                return Array.Empty<RofiApp>();

            string trimmed = query?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
                return apps;

            var matches = new List<Match>(apps.Count);

            foreach (RofiApp app in apps)
            {
                int rank = Rank(app.Name, trimmed);
                if (rank >= 0)
                    matches.Add(new Match(app, rank));
            }

            matches.Sort(CompareMatches);

            var result = new RofiApp[matches.Count];
            for (int i = 0; i < matches.Count; i++)
                result[i] = matches[i].App;

            return result;
        }

        private static int Rank(string name, string query)
        {
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 0;

            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 1;

            int index = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return -1;

            bool wordStart = index > 0 && IsSeparator(name[index - 1]);
            return wordStart ? 2 : 3;
        }

        private static bool IsSeparator(char value)
        {
            return value == ' ' || value == '-' || value == '_' || value == '.';
        }

        private static int CompareMatches(Match left, Match right)
        {
            int rank = left.Rank.CompareTo(right.Rank);
            if (rank != 0)
                return rank;

            int length = left.App.Name.Length.CompareTo(right.App.Name.Length);
            if (length != 0)
                return length;

            return string.Compare(left.App.Name, right.App.Name, StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct Match
        {
            internal Match(RofiApp app, int rank)
            {
                App = app;
                Rank = rank;
            }

            internal RofiApp App { get; }
            internal int Rank { get; }
        }
    }
}
