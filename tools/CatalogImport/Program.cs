using CatalogImport;

// Usage:
//   layout <pdf> <fromPage> <toPage> <layout.jsonl> [pngDir]   OCR pages and cache line geometry
//   dump <layout.jsonl> <fromPage> [toPage]                    print cached lines with their visual flags
//   parse <layout.jsonl> <powers.json>                          build the powers seed file
switch (args.FirstOrDefault())
{
    case "layout" when args.Length >= 5:
        var lines = await Layout.ReadAsync(Path.GetFullPath(args[1]),
            Enumerable.Range(int.Parse(args[2]), int.Parse(args[3]) - int.Parse(args[2]) + 1), args.ElementAtOrDefault(5));
        Layout.Save(args[4], lines);
        Console.WriteLine($"{lines.Count} lines → {args[4]}");
        return 0;

    case "dump" when args.Length >= 3:
        var from = int.Parse(args[2]);
        var to = args.Length > 3 ? int.Parse(args[3]) : from;
        foreach (var l in Layout.Load(args[1]).Where(l => l.Page >= from && l.Page <= to).OrderBy(l => l.Page).ThenBy(l => l.Column).ThenBy(l => l.Y0))
            Console.WriteLine($"{l.Page} c{l.Column,2} y{l.Y0,5} x{l.X0,5} h{l.Height,3} bg{l.Background,4} d{l.Dots} {(l.Red ? "R" : " ")} {l.Text}");
        return 0;

    case "parse" when args.Length >= 3:
        var powers = PowerParser.Parse(Layout.Load(args[1]));
        PowerParser.Write(args[2], powers);
        return 0;

    default:
        Console.Error.WriteLine("usage: layout <pdf> <from> <to> <out.jsonl> [pngDir] | dump <layout.jsonl> <from> [to] | parse <layout.jsonl> <powers.json>");
        return 1;
}
