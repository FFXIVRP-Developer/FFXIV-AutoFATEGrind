using AutoFateGrind.Windows;
using clib.Utils;
using ECommons.DalamudServices;
using System.Numerics;
using System.Text;

namespace AutoFateGrind.Core.Game.Fates;

// Renders a FATE label from the user's token format; the format is parsed once per change, not per frame.
internal static class FateNameFormatter
{
    public const string DefaultFormat = "L{Level}   {Name}";
    public const int MaxFormatLength = 64;

    private enum Token : byte
    {
        Literal,
        Level,
        Name,
        Id,
        Progress,
        TimeRemaining,
        Distance,
        State,
    }

    private readonly record struct Segment(Token Token, string Literal);

    private static readonly (string Name, Token Token)[] TokenNames =
    [
        ("Level", Token.Level),
        ("Name", Token.Name),
        ("Id", Token.Id),
        ("Progress", Token.Progress),
        ("TimeRemaining", Token.TimeRemaining),
        ("Distance", Token.Distance),
        ("State", Token.State),
    ];

    private static readonly List<Segment> segments = new();
    private static readonly StringBuilder builder = new();
    private static string parsedFormat = string.Empty;

    public static string Format(PublicEvent fate)
    {
        var format = Plugin.Cfg.FateNameFormat;
        if (string.IsNullOrWhiteSpace(format))
        {
            format = DefaultFormat;
        }

        if (!string.Equals(format, parsedFormat, StringComparison.Ordinal))
        {
            Parse(format);
        }

        builder.Clear();
        for (var index = 0; index < segments.Count; index++)
        {
            Append(segments[index], fate);
        }

        return builder.ToString();
    }

    private static void Append(Segment segment, PublicEvent fate)
    {
        switch (segment.Token)
        {
            case Token.Level: builder.Append(fate.Level); break;
            case Token.Name: builder.Append(fate.Name); break;
            case Token.Id: builder.Append(fate.Id); break;
            case Token.Progress: builder.Append(fate.Progress).Append('%'); break;
            case Token.TimeRemaining: builder.Append(Formatting.Time(FateClock.Remaining(fate))); break;
            case Token.Distance: AppendDistance(fate); break;
            case Token.State: builder.Append(fate.State); break;
            default: builder.Append(segment.Literal); break;
        }
    }

    private static void AppendDistance(PublicEvent fate)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null)
        {
            builder.Append('?');
            return;
        }

        builder.Append((int)MathF.Round(Vector3.Distance(player.Position, fate.Position))).Append('y');
    }

    private static void Parse(string format)
    {
        segments.Clear();
        parsedFormat = format;
        var literalStart = 0;
        var position = 0;
        while (position < format.Length)
        {
            if (format[position] != '{')
            {
                position++;
                continue;
            }

            var close = format.IndexOf('}', position + 1);
            if (close < 0)
            {
                break;
            }

            var token = ResolveToken(format.AsSpan(position + 1, close - position - 1));
            if (token == Token.Literal)
            {
                position = close + 1;
                continue;
            }

            if (position > literalStart)
            {
                segments.Add(new Segment(Token.Literal, format[literalStart..position]));
            }

            segments.Add(new Segment(token, string.Empty));
            position = close + 1;
            literalStart = position;
        }

        if (literalStart < format.Length)
        {
            segments.Add(new Segment(Token.Literal, format[literalStart..]));
        }
    }

    private static Token ResolveToken(ReadOnlySpan<char> name)
    {
        for (var index = 0; index < TokenNames.Length; index++)
        {
            if (name.Equals(TokenNames[index].Name, StringComparison.OrdinalIgnoreCase))
            {
                return TokenNames[index].Token;
            }
        }

        return Token.Literal;
    }
}
