using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Requiem.Shared.Rules;

namespace CatalogImport;

/// <summary>A power as written to Seed/powers.json (the seeder's PowerSeed shape).</summary>
public sealed class PowerSeed
{
    public required string Group { get; init; }
    public int? Level { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }
    public string? Flavor { get; set; }
    public int? VitaeCost { get; set; }
    public int? WillpowerCost { get; set; }
    public string? CostText { get; set; }
    public string? DicePoolText { get; set; }
    public CharacterAttribute? DicePoolAttribute { get; set; }
    public Skill? DicePoolSkill { get; set; }
    public string? DicePoolDiscipline { get; set; }
    public string? OpposedBy { get; set; }
    public string? ActionText { get; set; }
    public string? Duration { get; set; }
    public string? CastingDuration { get; set; }
    public string? HowToLearn { get; set; }
    public string? DramaticFailureText { get; set; }
    public string? FailureText { get; set; }
    public string? SuccessText { get; set; }
    public string? ExceptionalSuccessText { get; set; }
    public string? Notes { get; set; }
    public int? SourcePage { get; set; }
    public List<BonusSeed>? BonusOptions { get; set; }
}

public sealed record BonusSeed(int SuccessCost, string Effect);

/// <summary>
/// Splits the cached OCR layout into powers. Each power group is read from its own page range
/// with the heading style that chapter uses; see <see cref="Groups"/>.
/// </summary>
public static partial class PowerParser
{
    private enum Mode { Dots, Sorcery, Ritae, Base }

    private sealed record GroupSpec(string Name, int From, int To, Mode Mode);

    // PDF page ranges (book page + 4). Each group starts on its own page; text before its first heading is the intro.
    private static readonly GroupSpec[] Groups =
    [
        new("Base Powers", 19, 19, Mode.Base),
        new("Auspex", 144, 146, Mode.Dots),
        new("Cleptes", 147, 149, Mode.Dots),
        new("Concord", 150, 152, Mode.Dots),
        new("Furor", 153, 155, Mode.Dots),
        new("Idolatry", 156, 158, Mode.Dots),
        new("Impetus", 159, 161, Mode.Dots),
        new("Investiture", 162, 162, Mode.Dots),
        new("Nightmare", 163, 167, Mode.Dots),
        new("Quietus", 168, 170, Mode.Dots),
        new("Dementation", 172, 175, Mode.Dots),
        new("Dominate", 176, 180, Mode.Dots),
        new("Du'l-Faqar", 181, 184, Mode.Dots),
        new("Majesty", 185, 188, Mode.Dots),
        new("Obfuscate", 189, 191, Mode.Dots),
        new("Protean", 192, 195, Mode.Dots),
        new("Tartarus", 196, 198, Mode.Dots),
        new("Hekate", 200, 207, Mode.Sorcery),
        new("Vishap", 208, 213, Mode.Sorcery),
        new("Mayaparisatya", 214, 218, Mode.Sorcery),
        new("Noctis", 219, 223, Mode.Sorcery),
        new("Necromancy", 224, 232, Mode.Sorcery),
        new("Auctoritas", 234, 237, Mode.Ritae),
        new("Ignobilis", 238, 240, Mode.Ritae),
    ];

    private static readonly string[] BasePowerNames = ["FORTIFY FORM", "MASQUERADING", "HEALING", "HEIGHTENED SENSES"];
    private static readonly string[] DisciplineNames = Groups.Where(g => g.Mode == Mode.Dots).Select(g => g.Name).ToArray();

    // OCR splits and slips in heading names, fixed by hand after checking the page images.
    private static readonly Dictionary<string, string> NameFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brother 's Gift"] = "Brother's Gift",
        ["Dark Met Amorphosis"] = "Dark Metamorphosis",
        ["Udhiyah"] = "Udhiyah",
        ["In voke a Spell"] = "Invoke a Spell",
        ["Adapta tion"] = "Adaptation",
        ["Mirrors tep"] = "Mirrorstep",
        ["Oblivion 's Sight"] = "Oblivion's Sight",
        ["Apanchomene 's Song"] = "Apanchomene's Song",
        ["Naoash Shahida an"] = "Naqash Shahidaan",
    };

    private const int ParagraphGap = 62;
    private const int FooterY = 2150;

    public static List<PowerSeed> Parse(List<LayoutLine> layout)
    {
        var powers = new List<PowerSeed>();
        foreach (var group in Groups)
        {
            var lines = ReadingOrder(layout.Where(l => l.Page >= group.From && l.Page <= group.To));
            powers.AddRange(ParseGroup(group, lines));
        }
        powers.AddRange(GhoulPowers());

        foreach (var power in powers) Finish(power);
        var duplicates = powers.GroupBy(p => (p.Group, p.Name)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) throw new InvalidOperationException($"Duplicate powers: {string.Join(", ", duplicates)}");
        return powers;
    }

    public static void Write(string path, List<PowerSeed> powers)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter() },
        };
        var header = "// Generated by tools/CatalogImport from the VtR rulebook OCR (PDF pages; book page + 4).\n"
            + "// Seeded insert-only and unreviewed: the GM checks each entry on the Powers review screen.\n";
        File.WriteAllText(path, header + JsonSerializer.Serialize(powers, options) + "\n");
        foreach (var group in powers.GroupBy(p => p.Group))
            Console.WriteLine($"{group.Key,-14} {group.Count(),3}  {string.Join(", ", group.Select(p => p.Level is { } l ? $"{l}:{p.Name}" : p.Name))}");
        Console.WriteLine($"{powers.Count} powers -> {path}");
    }

    /// <summary>One merged line in reading order, with the gap to the line above in the same column.</summary>
    private sealed record Line(LayoutLine Source, string Text, bool ParagraphStart)
    {
        public int Page => Source.Page;
        public bool Sidebar => Source.Background is >= 234 and <= 249;
    }

    private static List<Line> ReadingOrder(IEnumerable<LayoutLine> lines)
    {
        var result = new List<Line>();
        foreach (var block in lines.Where(l => l.Y0 < FooterY && l.Height < 40)
                     .GroupBy(l => (l.Page, l.Column)).OrderBy(g => g.Key.Page).ThenBy(g => g.Key.Column))
        {
            // Merge fragments the OCR split off the same printed row ("+1" | "effect", text around an em dash).
            var rows = new List<List<LayoutLine>>();
            foreach (var line in block.OrderBy(l => l.Y0))
            {
                if (rows.Count > 0 && Math.Abs(rows[^1][0].Y0 - line.Y0) <= 10) rows[^1].Add(line);
                else rows.Add([line]);
            }

            int? previousY = null;
            foreach (var row in rows)
            {
                var ordered = row.OrderBy(l => l.X0).ToList();
                var first = ordered[0] with { X1 = ordered[^1].X1, Height = ordered.Max(l => l.Height) };
                var text = string.Join(' ', ordered.Select(l => l.Text));
                result.Add(new Line(first, text, previousY is { } y && first.Y0 - y > ParagraphGap));
                previousY = first.Y0;
            }
        }
        return result;
    }

    private static IEnumerable<PowerSeed> ParseGroup(GroupSpec group, List<Line> lines)
    {
        var builders = new List<PowerBuilder>();
        PowerBuilder? current = null;
        string? category = null;
        var collecting = group.Mode != Mode.Sorcery; // Sorcery chapters list Eruditions before their Rituals.

        foreach (var line in lines)
        {
            var source = line.Source;
            if (IsBand(source))
            {
                var (dots, name) = SplitDots(source);
                switch (group.Mode)
                {
                    case Mode.Dots when dots > 0:
                        builders.Add(current = new PowerBuilder(group.Name, dots, name, null, source.Page));
                        continue;
                    case Mode.Ritae:
                        builders.Add(current = new PowerBuilder(group.Name, null, name, null, source.Page));
                        continue;
                    case Mode.Base:
                        current = BasePowerNames.Contains(name)
                            ? builders.AddAndReturn(new PowerBuilder(group.Name, null, name, null, source.Page))
                            : null;
                        continue;
                    case Mode.Sorcery:
                        if (name == "ERUDITIONS") { collecting = false; current = null; }
                        else if (name == "RITUALS") { collecting = true; category = null; current = null; }
                        else if (name == "SHADES") { collecting = false; current = null; } // Necromancy's rules for ghosts, not rituals.
                        else { collecting = true; category = TitleCase(name); current = null; } // Necromancy's Animus / Mortuus
                        continue;
                }
            }

            if (group.Mode == Mode.Sorcery && collecting)
            {
                if (IsRitualTitle(source))
                {
                    builders.Add(current = new PowerBuilder(group.Name, null, line.Text, category, source.Page));
                    continue;
                }
                if (IsSubheading(source, line.Text))
                {
                    if (current is null) category = line.Text;
                    else current.Add(line); // A subheading inside a ritual (e.g. "Antipathia") stays with it.
                    continue;
                }
            }

            current?.Add(line);
        }
        return builders.Select(b => b.Build());
    }

    private static T AddAndReturn<T>(this List<T> list, T item) { list.Add(item); return item; }

    private static bool IsBand(LayoutLine l)
    {
        if (l.Background is < 205 or > 222 || l.Height is < 17 or > 34) return false;
        var columnLeft = l.Column == 1 ? 926 : 106;
        if (l.X0 < columnLeft + 50) return false; // band headings are centred; striped table rows start at the margin
        var letters = l.Text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return l.Dots > 0; // four dots and the name "23"
        return letters.Count >= 3 && letters.Count(char.IsUpper) >= letters.Count * 0.7;
    }

    private static (int Dots, string Name) SplitDots(LayoutLine l)
    {
        // OCR occasionally reads the dots as zeros ("000 ENTRANCEMENT").
        var match = LeadingZeros().Match(l.Text);
        return match.Success ? (Math.Max(l.Dots, match.Groups[1].Length), l.Text[match.Length..].Trim()) : (l.Dots, l.Text.Trim());
    }

    private static bool IsRitualTitle(LayoutLine l)
    {
        var columnLeft = l.Column == 1 ? 926 : 106;
        var words = l.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return l.Red && l.Background >= 235 && Math.Abs(l.X0 - columnLeft) <= 12
               && words.Length <= 7 && char.IsUpper(l.Text[0]) && !l.Text.EndsWith('.') && !l.Text.Contains(':');
    }

    private static bool IsSubheading(LayoutLine l, string text) =>
        l.Height >= 27 && l.Background >= 250 && !l.Red && text.Split(' ').Length <= 3 && char.IsUpper(text[0]) && !text.Contains(':');

    private static IEnumerable<PowerSeed> GhoulPowers()
    {
        // VtR p.62 describes ghoul powers in prose rather than as headed entries; transcribed by hand.
        yield return new PowerSeed
        {
            Group = "Ghoul Powers", Name = "Healing", SourcePage = 66,
            Flavor = "Ghouls can spend blood to heal just as a vampire can.",
            CostText = "Vitae, as for a vampire (see Base Powers: Healing)",
        };
        yield return new PowerSeed
        {
            Group = "Ghoul Powers", Name = "Fortify Form", SourcePage = 66, VitaeCost = 1, CostText = "1 Vitae",
            Flavor = "A ghoul may spend 1 point of Vitae to gain a temporary dot of Potence, Celerity or Fortitude.",
            Duration = "The same as it lasts for a vampire",
            Notes = "The maximum is 1 in each.",
        };
        yield return new PowerSeed
        {
            Group = "Ghoul Powers", Name = "Immortality", SourcePage = 66,
            Flavor = "While a ghoul has the blood of a vampire in their veins, they do not age. Each day 1 point of Vitae is scoured from their body; when none is left, the ghoul instantly ages to their proper mortal lifetime.",
            Notes = "Ghouls can drink up to 8 points of Vitae in one night. It must be fresh from a vampire's veins, and not from a vampire who fed on them that night.",
        };
    }

    /// <summary>Accumulates one power's text, routing each line to the field its label names.</summary>
    private sealed class PowerBuilder(string group, int? level, string rawName, string? category, int page)
    {
        private enum Field
        {
            Flavor, Cost, VitaeCost, DicePool, Action, Duration, Casting, HowToLearn,
            Dramatic, Failure, Success, Exceptional, Opposed, Bonus, Notes,
        }

        private static readonly (string Label, Field Field)[] Labels =
        [
            ("Vitae Cost", Field.VitaeCost), ("Cost", Field.Cost),
            ("Attack Dice Pool", Field.Notes), ("Dice Pool", Field.DicePool), ("Roll", Field.DicePool),
            ("Action", Field.Action), ("Duration", Field.Duration), ("Casting Time", Field.Casting),
            ("How to Learn", Field.HowToLearn), ("How it's Performed", Field.Action), ("How its Performed", Field.Action),
            ("How it Works", Field.Success),
            ("Dramatic Failure", Field.Dramatic), ("Exceptional Success", Field.Exceptional),
            ("Failure", Field.Failure), ("Success", Field.Success), ("Results", Field.Success),
            ("Opposed By", Field.Opposed), ("Contested By", Field.Opposed), ("Resisted By", Field.Opposed),
            ("Bonus Successes", Field.Bonus),
        ];

        // Labels that keep their own name in the Notes text.
        private static readonly string[] NoteLabels =
        [
            "Note", "Notes", "NOTES", "Prerequisite", "Activate", "Range", "Special Effect", "Narrative Effect",
            "Dramatic Success", "System", "Attack Dice Pool",
        ];

        private readonly Dictionary<Field, List<StringBuilder>> _text = [];
        private readonly List<BonusSeed> _bonus = [];
        private Field _field = Field.Flavor;
        private bool _inSidebar;
        private bool _boxedBody;

        public void Add(Line line)
        {
            var text = line.Text.Trim();
            if (text.Length == 0) return;

            // Sidebar boxes are extra rules for the power; keep them whole in Notes.
            // A power printed entirely inside a box has no body yet, so its box is the body.
            if (_text.Count == 0 && line.Sidebar) _boxedBody = true;
            if (line.Sidebar != _inSidebar && !_boxedBody)
            {
                _inSidebar = line.Sidebar;
                _field = Field.Notes;
                AppendLine(Field.Notes, text, paragraphStart: true);
                return;
            }
            if (_inSidebar) { AppendLine(Field.Notes, text, line.ParagraphStart); return; }

            if (text is "Roll Results" or "Roll Results:") return;

            var label = MatchLabel(text);
            if (label is { } found)
            {
                var (name, field, rest) = found;
                if (NoteLabels.Contains(name))
                {
                    _field = Field.Notes;
                    Paragraph(Field.Notes);
                    Append(Field.Notes, $"{name}: {rest}");
                    return;
                }
                if (field != Field.Bonus && _text.ContainsKey(field))
                {
                    // A second "Success:" etc. belongs to a sub-roll the book describes later (extended actions, resistance).
                    _field = Field.Notes;
                    Paragraph(Field.Notes);
                    Append(Field.Notes, text);
                    return;
                }
                _field = field;
                if (field == Field.Bonus) return;
                Paragraph(field);
                if (rest.Length > 0) Append(field, rest);
                return;
            }

            if (_field == Field.Bonus)
            {
                if (text.StartsWith('(')) return; // "(Choose one per bonus success spent)"
                var plus = BonusRow().Match(text);
                if (plus.Success)
                    _bonus.Add(new BonusSeed(int.Parse(plus.Groups[1].Value), plus.Groups[2].Value.Trim()));
                else if (Indented(line) && (_bonus.Count == 0 || line.ParagraphStart))
                    _bonus.Add(new BonusSeed(1, text)); // bullet rows without a "+N" cost one success each
                else if (_bonus.Count > 0 && !line.ParagraphStart)
                    _bonus[^1] = _bonus[^1] with { Effect = Join(_bonus[^1].Effect, text) };
                else
                {
                    // Text back at the margin after a gap ends the table.
                    _field = Field.Notes;
                    AppendLine(Field.Notes, text, paragraphStart: true);
                }
                return;
            }

            // Short fields end at a paragraph break or a finished sentence; unlabelled text after them is
            // the power's effect (common in the sorcery chapters) or, once that is set, rules detail.
            if (_field is Field.Cost or Field.VitaeCost or Field.DicePool or Field.Duration or Field.Casting or Field.Opposed
                && (line.ParagraphStart || EndsSentence(_field)))
                _field = _text.ContainsKey(Field.Success) ? Field.Notes : Field.Success;
            else if (_field == Field.Action && line.ParagraphStart)
                _field = Field.Notes;

            AppendLine(_field, text, line.ParagraphStart);
        }

        private const int ShortFieldLimit = 300;

        /// <summary>Keeps a short field's first sentence and moves the rest to the effect (or Notes once that is set).</summary>
        private void SplitLong(Field field)
        {
            if (Get(field) is not { Length: > ShortFieldLimit } text) return;
            var end = text.IndexOf(". ", StringComparison.Ordinal);
            if (end < 0 || end > ShortFieldLimit) end = text.LastIndexOf(' ', ShortFieldLimit);
            var head = text[..(end + 1)].Trim();
            var tail = text[(end + 1)..].Trim();

            _text[field] = [new StringBuilder(head)];
            var target = _text.ContainsKey(Field.Success) ? Field.Notes : Field.Success;
            if (!_text.TryGetValue(target, out var paragraphs)) _text[target] = paragraphs = [];
            paragraphs.Insert(0, new StringBuilder(tail));
        }

        private bool EndsSentence(Field field) =>
            _text.TryGetValue(field, out var paragraphs) && paragraphs.Count > 0
            && paragraphs[^1].ToString().TrimEnd() is { Length: > 0 } last && last[^1] is '.' or '!' or '?';

        private static bool Indented(Line line) => line.Source.X0 - (line.Source.Column == 1 ? 926 : 106) >= 30;

        private void AppendLine(Field field, string text, bool paragraphStart)
        {
            if (paragraphStart) Paragraph(field);
            Append(field, text);
        }

        private void Paragraph(Field field)
        {
            if (!_text.TryGetValue(field, out var paragraphs)) _text[field] = paragraphs = [];
            if (paragraphs.Count == 0 || paragraphs[^1].Length > 0) paragraphs.Add(new StringBuilder());
        }

        private void Append(Field field, string text)
        {
            if (!_text.TryGetValue(field, out var paragraphs) || paragraphs.Count == 0) Paragraph(field);
            var sb = _text[field][^1];
            if (sb.Length == 0) sb.Append(text);
            else if (sb[^1] == '-' && sb.Length > 1 && char.IsLetter(sb[^2])) sb.Append(text); // "long-" + "term"
            else sb.Append(' ').Append(text);
        }

        private static (string Name, Field Field, string Body)? MatchLabel(string text)
        {
            foreach (var (label, field) in Labels)
            {
                if (!text.StartsWith(label, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = text[label.Length..];
                if (rest.StartsWith(':')) return (label, field, rest[1..].Trim());
                if (rest.Length == 0 && field is Field.Bonus or Field.Action or Field.Success or Field.HowToLearn) return (label, field, "");
                if (field == Field.Bonus) return (label, field, "");
            }
            foreach (var label in NoteLabels)
                if (text.StartsWith(label + ":", StringComparison.Ordinal)) return (label, Field.Notes, text[(label.Length + 1)..].Trim());
            return null;
        }

        private string? Get(Field field) =>
            _text.TryGetValue(field, out var paragraphs)
                ? Clean(string.Join("\n\n", paragraphs.Select(p => p.ToString().Trim()).Where(p => p.Length > 0)))
                : null;

        public PowerSeed Build()
        {
            foreach (var field in new[] { Field.Cost, Field.VitaeCost, Field.DicePool, Field.Duration, Field.Casting, Field.Opposed })
                SplitLong(field);

            var vitaeCostText = Get(Field.VitaeCost);
            var costText = Get(Field.Cost) ?? (vitaeCostText is null ? null : Regex.IsMatch(vitaeCostText, @"^\d+$") ? $"{vitaeCostText} Vitae" : vitaeCostText);
            var power = new PowerSeed
            {
                Group = group,
                Level = level,
                Name = FixName(rawName),
                Category = category,
                Flavor = Get(Field.Flavor),
                CostText = costText,
                DicePoolText = Get(Field.DicePool),
                OpposedBy = Get(Field.Opposed),
                ActionText = Get(Field.Action),
                Duration = Get(Field.Duration),
                CastingDuration = Get(Field.Casting),
                HowToLearn = Get(Field.HowToLearn),
                DramaticFailureText = Get(Field.Dramatic),
                FailureText = Get(Field.Failure),
                SuccessText = Get(Field.Success),
                ExceptionalSuccessText = Get(Field.Exceptional),
                Notes = Get(Field.Notes),
                SourcePage = page,
                BonusOptions = _bonus.Count > 0 ? _bonus.Select(b => b with { Effect = Clean(b.Effect)! }).ToList() : null,
            };
            return power;
        }
    }

    /// <summary>Derives the structured fields (costs, dice pool traits) from the printed text.</summary>
    private static void Finish(PowerSeed power)
    {
        if (power.CostText is { } cost)
        {
            power.VitaeCost ??= Number(VitaeAmount().Match(cost));
            power.WillpowerCost ??= Number(WillpowerAmount().Match(cost));
            if (power.VitaeCost is null && Regex.IsMatch(cost, @"^(None|0|Free)\b", RegexOptions.IgnoreCase)) power.VitaeCost = 0;
        }

        if (power.DicePoolText is { } pool)
        {
            var opposed = OpposedSplit().Match(pool);
            var own = opposed.Success ? pool[..opposed.Index] : pool;
            if (opposed.Success) power.OpposedBy ??= Clean(pool[(opposed.Index + opposed.Length)..].Trim().TrimEnd('.'));

            power.DicePoolAttribute = FirstWord<CharacterAttribute>(own);
            power.DicePoolSkill = FirstWord<Skill>(own);
            power.DicePoolDiscipline = DisciplineNames
                .Where(d => Regex.IsMatch(own, $@"\b{Regex.Escape(d)}\b", RegexOptions.IgnoreCase))
                .OrderBy(d => d == power.Group ? 0 : 1).FirstOrDefault();
        }
    }

    private static T? FirstWord<T>(string text) where T : struct, Enum =>
        Enum.GetValues<T>()
            .Select(v => (Value: v, Match: Regex.Match(text, $@"\b{v}\b", RegexOptions.IgnoreCase)))
            .Where(x => x.Match.Success).OrderBy(x => x.Match.Index)
            .Select(x => (T?)x.Value).FirstOrDefault();

    private static int? Number(Match match) => match.Success ? int.Parse(match.Groups[1].Value) : null;

    private static string FixName(string raw)
    {
        var name = Regex.Replace(raw.Trim(), @"\s+", " ");
        if (name.Count(char.IsLetter) > 0 && name.Where(char.IsLetter).Count(char.IsUpper) >= name.Count(char.IsLetter) * 0.7)
            name = TitleCase(name);
        return NameFixes.TryGetValue(name, out var fixedName) ? fixedName : name.Replace(" 's", "'s");
    }

    private static readonly HashSet<string> SmallWords = ["of", "the", "a", "an", "and", "to", "in", "on", "from", "for"];

    private static string TitleCase(string upper)
    {
        var words = upper.ToLowerInvariant().Split(' ');
        for (var i = 0; i < words.Length; i++)
            if (i == 0 || !SmallWords.Contains(words[i]))
                words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i]);
        return string.Join(' ', words).Replace(" 's", "'s").Replace("'S", "'s");
    }

    /// <summary>Repairs the OCR's usual slips: I/l for 1, IO for 10, stray spaces before apostrophes.</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u201C', '"').Replace('\u201D', '"');
        text = OneDTen().Replace(text, "1d10");
        text = TenAlone().Replace(text, "10");
        text = OneBeforeUnit().Replace(text, "1");
        text = MinusOne().Replace(text, "${sign}1");
        text = Regex.Replace(text, @" ('s)\b", "$1");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return text.Trim();
    }

    private static string Join(string a, string b) => a.EndsWith('-') ? a + b : a + " " + b;

    [GeneratedRegex(@"^([0O]{1,5})\s+")] private static partial Regex LeadingZeros();
    [GeneratedRegex(@"^\+\s?(\d)\s+(.+)$")] private static partial Regex BonusRow();
    [GeneratedRegex(@"(\d+)\s*(?:points? of\s+)?Vitae", RegexOptions.IgnoreCase)] private static partial Regex VitaeAmount();
    [GeneratedRegex(@"(\d+)\s*(?:points? of\s+)?Willpower", RegexOptions.IgnoreCase)] private static partial Regex WillpowerAmount();
    [GeneratedRegex(@"\s+(?:minus|vs\.?|versus|contested by|resisted by|-)\s+", RegexOptions.IgnoreCase)] private static partial Regex OpposedSplit();
    [GeneratedRegex(@"\b[Il1]d[Il1][O0]\b")] private static partial Regex OneDTen();
    [GeneratedRegex(@"(?<=\s|^)I[O0](?=\b)")] private static partial Regex TenAlone();
    [GeneratedRegex(@"(?<=(?:^|[\s(+\-–—]))[Il](?=\s+(?:Vitae|Willpower|points?|dice|die|dots?|minutes?|hours?|turns?|rounds?|success(?:es)?|nights?|weeks?|scenes?|levels?|Aggravated|Lethal|Bashing|bonus|penalty|through|or more)\b)")] private static partial Regex OneBeforeUnit();
    [GeneratedRegex(@"(?<sign>[\-–—])l\b")] private static partial Regex MinusOne();
}
