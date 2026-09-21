using System.Text;
using System.Xml;

namespace BdoGrindTracker.Ocr;

/// <summary>Reads BDO XML without changing its saved bytes or weakening XML validation.</summary>
internal static class GameVariableXmlReader
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal static XmlReader Open(string path, XmlReaderSettings settings, long maxBytes = 0)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (maxBytes > 0 && stream.Length > maxBytes)
            throw new XmlException("The game-variable XML exceeds the permitted size.");

        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int count;
        while ((count = stream.Read(block)) > 0)
        {
            // Check each read too: BDO can save the shared file while it is open.
            if (maxBytes > 0 && buffer.Length + count > maxBytes)
                throw new XmlException("The game-variable XML exceeds the permitted size.");
            buffer.Write(block, 0, count);
        }

        var bytes = buffer.ToArray();
        var text = DecodeUndeclaredXml(bytes);
        var ownedSettings = settings.Clone();
        ownedSettings.CloseInput = true;
        return text is null
            ? XmlReader.Create(new MemoryStream(bytes, writable: false), ownedSettings)
            : XmlReader.Create(new StringReader(text), ownedSettings);
    }

    private static string? DecodeUndeclaredXml(byte[] bytes)
    {
        var data = bytes.AsSpan();
        // Preserve XML's own encoding detection/validation for declared encodings,
        // Unicode BOMs and the zero-byte signatures of BOM-less UTF-16/UTF-32.
        if (data.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ||
            data.StartsWith(new byte[] { 0xff, 0xfe }) ||
            data.StartsWith(new byte[] { 0xfe, 0xff }) ||
            data.Length > 0 && data[0] == 0 || data.Length > 1 && data[1] == 0 ||
            data.StartsWith("<?xml"u8) && data.Length > 5 && data[5] is 9 or 10 or 13 or 32)
            return null;

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Legacy BDO saves can contain Windows-1252 notes/chat text without
            // an encoding declaration. Decode those bytes without rewriting the
            // user's file; structural XML errors still fail in the XML reader.
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
        }
    }
}
