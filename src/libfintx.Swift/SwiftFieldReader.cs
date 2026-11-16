/*	
 * 	
 *  This file is part of libfintx.
 *  
 *  Copyright (C) 2016 - 2022 Torsten Klinger
 * 	E-Mail: torsten.klinger@googlemail.com
 *  
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU Lesser General Public
 *  License as published by the Free Software Foundation; either
 *  version 3 of the License, or (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 *  Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program; if not, write to the Free Software Foundation,
 *  Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 * 	
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace libfintx.Swift;

/// <summary>
/// Reads the fields of the securities messages FinTS returns (MT535, MT536): splits the text into
/// fields and reads amounts and dates. A value that cannot be read is skipped and logged rather
/// than failing the message.
/// </summary>
internal sealed class SwiftFieldReader
{
    /// <summary>SWIFT amounts always use a decimal comma and no thousands separator, independent of the current culture.</summary>
    private static readonly NumberFormatInfo AmountFormat = NumberFormatInfo.ReadOnly(new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "" });

    private readonly ILogger? _logger;
    private readonly string _messageType;

    /// <param name="logger">Where to write the warnings, or null.</param>
    /// <param name="messageType">The message type the warnings name, such as "MT535".</param>
    public SwiftFieldReader(ILogger? logger, string messageType)
    {
        _logger = logger;
        _messageType = messageType;
    }

    /// <summary>Whether the field opens or closes the block: ":16R:FIN" opens FIN, ":16R:FINA" does not.</summary>
    public static bool IsBlock(string field, string tag) =>
        field.StartsWith(tag, StringComparison.Ordinal) && (field.Length == tag.Length || !char.IsLetter(field[tag.Length]));

    /// <summary>
    /// The fields of the message, each starting with ":"; continuation lines are joined to their
    /// field with "|". A line "-" ends a message.
    /// </summary>
    public static IEnumerable<string> Fields(string text)
    {
        string? current = null;
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
                continue;
            if (line.StartsWith(":", StringComparison.Ordinal))
            {
                if (current != null)
                    yield return current;
                current = line;
            }
            else if (line.StartsWith("-", StringComparison.Ordinal))
            {
                if (current != null)
                    yield return current;
                current = null;
            }
            else if (current != null)
                current += "|" + line;
        }
        if (current != null)
            yield return current;
    }

    /// <summary>An amount with a comma as decimal mark, which may end with it ("100,").</summary>
    public decimal? Amount(string swift, bool negative, string field)
    {
        string text = swift.TrimEnd(',');
        if (text.Length == 0)
            text = "0";
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, AmountFormat, out decimal value))
        {
            _logger?.LogWarning("{MessageType}: amount \"{Amount}\" in field {Field} cannot be read and is skipped.", _messageType, swift, field);
            return null;
        }
        return negative ? -value : value;
    }

    /// <summary>A date (yyyyMMdd) or a date with time (yyyyMMddHHmmss).</summary>
    public DateTime? Date(string text, string field)
    {
        string format = text.Length >= 14 ? "yyyyMMddHHmmss" : "yyyyMMdd";
        string value = text.Length >= 14 ? text.Substring(0, 14) : text.Substring(0, 8);
        if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            return date;
        _logger?.LogWarning("{MessageType}: date \"{Date}\" in field {Field} cannot be read and is skipped.", _messageType, text, field);
        return null;
    }
}
