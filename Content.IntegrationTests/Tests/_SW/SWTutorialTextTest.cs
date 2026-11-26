using System.Linq;
using System.Text.RegularExpressions;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._SW;

/// <summary>
/// Nobody reads a paragraph in a tutorial, so a line longer than this does not get in.
/// </summary>
[TestFixture]
public sealed class SWTutorialTextTest
{
    private const int MaxSentences = 2;
    private const int MaxWordsPerSentence = 8;

    private static readonly string[] Locales = { "ru-RU", "en-US" };
    private static readonly string[] Files = { "tutorial", "tutorial-civil", "tutorial-production", "tutorial-infantry", "global-war/coach", "global-war/aide" };

    private static readonly Regex Key = new(@"^([a-z][a-z0-9-]*)\s*=\s*(.*)$");
    private static readonly Regex Placeable = new(@"\{[^}]*\}");
    private static readonly Regex SentenceEnd = new(@"[.!?]+(\s|$)");

    [Test]
    public async Task TutorialLinesAreShort()
    {
        await using var pair = await PoolManager.GetServerClient();
        var resources = pair.Server.ResolveDependency<IResourceManager>();

        var checkedLines = 0;
        Assert.Multiple(() =>
        {
            foreach (var locale in Locales)
            {
                foreach (var file in Files)
                {
                    var path = new ResPath($"/Locale/{locale}/_SW/{file}.ftl");
                    foreach (var line in resources.ContentFileReadAllText(path).Split('\n'))
                    {
                        var match = Key.Match(line.TrimEnd('\r'));

                        // Entity names and the console help are not shown as tutorial lines.
                        if (!match.Success || match.Groups[1].Value.StartsWith("ent-") || match.Groups[1].Value.StartsWith("cmd-"))
                            continue;

                        checkedLines++;
                        var text = Placeable.Replace(match.Groups[2].Value, "X");
                        var sentences = SentenceEnd.Split(text).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                        Assert.That(sentences, Has.Length.LessThanOrEqualTo(MaxSentences), $"{path}: {line}");
                        foreach (var sentence in sentences)
                        {
                            var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                            Assert.That(words, Is.LessThanOrEqualTo(MaxWordsPerSentence), $"{path}: {line}");
                        }
                    }
                }
            }
        });

        Assert.That(checkedLines, Is.GreaterThan(60), "The files of the tutorial's lines moved.");
        await pair.CleanReturnAsync();
    }
}
