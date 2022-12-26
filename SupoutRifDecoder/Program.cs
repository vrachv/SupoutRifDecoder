using System.IO.Compression;
using System.Text;

namespace SupoutRifDecoder;

public class Program
{
    private const string B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

    private static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            WriteHelpAndReadKey();
            return;
        }

        var keyValueArgs = GetKeyValueArgs(args);
        if (keyValueArgs.Count == 0 || !keyValueArgs.ContainsKey("--path"))
        {
            WriteHelpAndReadKey();
            return;
        }

        var path = keyValueArgs["--path"];
        var split = keyValueArgs.ContainsKey("--split");
        var result = new Dictionary<string, string>();

        try
        {
            await using var file = File.OpenRead(path);
            using var reader = new StreamReader(file);
            var section = "";
            
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (line == "--BEGIN ROUTEROS SUPOUT SECTION")
                {
                    section = "";
                    continue;
                }

                if (line == "--END ROUTEROS SUPOUT SECTION")
                {
                    var (sectionName, sectionValue) = await DecodeAsync(section);
                    result.Add(sectionName, sectionValue);
                }

                section += line;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening RIF file: {ex.Message}");
            return;
        }

        var outPath = await SaveAsync(Path.GetDirectoryName(path)!, result, split);
        Console.WriteLine($"Saved to {outPath}");
    }

    private static Dictionary<string, string> GetKeyValueArgs(string[] args)
    {
        var result = new Dictionary<string, string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
            {
                continue;
            }
            
            var value = "";
            if (args.Length >= i + 2 && !args[i + 1].StartsWith("--"))
            {
                value = args[i + 1];
            }

            result.Add(args[i], value);
        }

        return result;
    }

    private static void WriteHelpAndReadKey()
    {
        Console.WriteLine("supout.rif path required. Use --path <path>. Use --split for split section per file.");
        Console.WriteLine("Press any key...");
        _ = Console.ReadKey();
    }

    private static async Task<string> SaveAsync(string path, Dictionary<string, string> result, bool split)
    {
        if (split)
        {
            path = Path.Combine(path, "supout_result_UTC_" + DateTime.UtcNow.ToString("dd.MM.yyyy HH.mm"));
            Directory.CreateDirectory(path);
            foreach (var (sectionName, sectionValue) in result)
            {
                await File.WriteAllTextAsync(Path.Combine(path, (sectionName.Replace(".", "") + ".txt")), sectionValue);
            }
        }
        else
        {
            path = Path.Combine(path, "supout_result_UTC_" + DateTime.UtcNow.ToString("dd.MM.yyyy HH.mm") + ".txt");
            await File.WriteAllTextAsync(path,
                string.Join("\r\n", result.Select(e => "=======SECTION======= " + e.Key + "\r\n" + e.Value)));
        }

        return path;
    }

    private static async Task<(string, string)> DecodeAsync(string sec)
    {
        var outBytes = new List<byte>(sec.Length * 3 / 12);
        for (var i = 0; i < sec.Length; i += 4)
        {
            var o = B64.IndexOf(sec[i + 3]) % 64 << 18 |
                    B64.IndexOf(sec[i + 2]) % 64 << 12 |
                    B64.IndexOf(sec[i + 1]) % 64 << 6 |
                    B64.IndexOf(sec[i]) % 64;
            
            outBytes.Add((byte)(o % 256));
            outBytes.Add((byte)((o >> 8) % 256));
            outBytes.Add((byte)((o >> 16) % 256));
        }
        
        var secSplit = Array.IndexOf(outBytes.ToArray(), (byte)0x0);
        var name = Encoding.UTF8.GetString(outBytes.Take(secSplit).ToArray());
        var data = outBytes.Skip(secSplit + 1).ToArray();
        
        using var dataStream = new MemoryStream(data);
        await using var zs = new ZLibStream(dataStream, CompressionMode.Decompress);
        
        using var uncompressedStream = new MemoryStream();
        await zs.CopyToAsync(uncompressedStream);
        
        var result = Encoding.UTF8.GetString(uncompressedStream.ToArray());

        return (name, result);
    }
}