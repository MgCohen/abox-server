using System.Text;
using System.Text.RegularExpressions;

namespace ABox.Governance.Hooks;

public static class Glob
{
    public static bool IsMatch(string pattern, string path)
    {
        var normalized = path.Replace('\\', '/');
        return Regex.IsMatch(normalized, "^" + Translate(pattern.Replace('\\', '/')) + "$");
    }

    private static string Translate(string glob)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                var doubleStar = i + 1 < glob.Length && glob[i + 1] == '*';
                if (doubleStar && i + 2 < glob.Length && glob[i + 2] == '/')
                {
                    sb.Append("(?:.*/)?");
                    i += 2;
                }
                else if (doubleStar)
                {
                    sb.Append(".*");
                    i += 1;
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }
        return sb.ToString();
    }
}
