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
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace libfintx.Swift;

/// <summary>
/// Reads a statement of holdings in SWIFT MT535 format as FinTS returns it in HIWPD: the general
/// block, and per financial instrument (between :16R:FIN and :16S:FIN) its codes and name,
/// quantity and available quantity, price with kind, date and source, value, accrued interest,
/// exchange rate, acquisition price and, from the attributes block, denomination, maturity and
/// interest rate. Fields a bank leaves out stay empty; a value that cannot be read is skipped and
/// logged rather than failing the statement.
/// </summary>
public class MT535Parser
{
    private readonly SwiftFieldReader _reader;

    private static readonly Regex Isin = new Regex(@"^ISIN\s+([A-Z]{2}[A-Z0-9]{9}[0-9])", RegexOptions.Compiled);
    private static readonly Regex Wkn = new Regex(@"^/DE/([A-Z0-9]{6})", RegexOptions.Compiled);
    private static readonly Regex Quantity = new Regex(@"^:93B::AGGR//(UNIT|FAMT)/(N?)([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex Available = new Regex(@"^:93C::TAVI//(?:UNIT|FAMT)/AVAI/(N?)([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex PriceAmount = new Regex(@"^:90B::MRKT//([A-Z]{4})/([A-Z]{3})([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex PricePercent = new Regex(@"^:90A::MRKT//([A-Z]{4})/(N?)([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex PriceDate = new Regex(@"^:98([AC])::PRIC//([0-9]{8,14})", RegexOptions.Compiled);
    private static readonly Regex PriceSource = new Regex(@"^:94B::PRIC//[A-Z]{4}/(.+)$", RegexOptions.Compiled);
    private static readonly Regex HoldValue = new Regex(@"^:19A::HOLD//(N?)([A-Z]{3})([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex Accrued = new Regex(@"^:19A::ACRU//(N?)([A-Z]{3})([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex Exchange = new Regex(@"^:92B::EXCH//([A-Z]{3})/([A-Z]{3})/([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex Acquisition = new Regex(@"^:70E::HOLD//[^|]*\|2([0-9]*,[0-9]*)\+([A-Z]{3})", RegexOptions.Compiled);
    private static readonly Regex Denomination = new Regex(@"^:11A::DENO//([A-Z]{3})", RegexOptions.Compiled);
    private static readonly Regex Maturity = new Regex(@"^:98A::MATU//([0-9]{8})", RegexOptions.Compiled);
    private static readonly Regex Interest = new Regex(@"^:92A::INTR//(N?)([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex StatementDate = new Regex(@"^:98([AC])::STAT//([0-9]{8,14})", RegexOptions.Compiled);
    private static readonly Regex PreparationDate = new Regex(@"^:98([AC])::PREP//([0-9]{8,14})", RegexOptions.Compiled);
    private static readonly Regex Reference = new Regex(@"^:20C::SEME//(.+)$", RegexOptions.Compiled);
    private static readonly Regex Page = new Regex(@"^:28E:([0-9]+)/([A-Z]{4})", RegexOptions.Compiled);
    private static readonly Regex StatementType = new Regex(@"^:22F::STTY//(.+)$", RegexOptions.Compiled);
    private static readonly Regex Safekeeping = new Regex(@"^:97A::SAFE//(.+)$", RegexOptions.Compiled);
    private static readonly Regex Activity = new Regex(@"^:17B::ACTI//([YN])", RegexOptions.Compiled);

    /// <summary>Initializes the parser.</summary>
    /// <param name="loggerFactory">A logging factory to create the logging where to write warnings.</param>
    public MT535Parser(ILoggerFactory? loggerFactory = null)
    {
        _reader = new SwiftFieldReader(loggerFactory?.CreateLogger<MT535Parser>(), "MT535");
    }

    /// <summary>Reads the statement; text that holds no statement gives an empty one.</summary>
    public MT535 Parse(string? mt535)
    {
        var statement = new MT535();
        MT535Holding? current = null;
        foreach (string field in SwiftFieldReader.Fields(mt535 ?? string.Empty))
        {
            if (SwiftFieldReader.IsBlock(field, ":16R:FIN"))
            {
                current = new MT535Holding();
                continue;
            }
            if (SwiftFieldReader.IsBlock(field, ":16S:FIN"))
            {
                if (current != null)
                    statement.Holdings.Add(current);
                current = null;
                continue;
            }

            Match m;
            if (current == null)
            {
                if ((m = StatementDate.Match(field)).Success)
                    statement.StatementDate = _reader.Date(m.Groups[2].Value, field)?.Date;
                else if ((m = PreparationDate.Match(field)).Success)
                    statement.PreparationDate = _reader.Date(m.Groups[2].Value, field);
                else if ((m = Reference.Match(field)).Success)
                    statement.Reference = m.Groups[1].Value.Trim();
                else if ((m = Page.Match(field)).Success)
                {
                    statement.PageNumber = int.TryParse(m.Groups[1].Value, out int page) ? page : null;
                    statement.IsLastPage = m.Groups[2].Value is "LAST" or "ONLY";
                }
                else if ((m = StatementType.Match(field)).Success)
                    statement.StatementType = m.Groups[1].Value.Trim();
                else if ((m = Safekeeping.Match(field)).Success)
                    statement.SafekeepingAccount = m.Groups[1].Value.Trim();
                else if ((m = Activity.Match(field)).Success)
                    statement.HasHoldings = m.Groups[1].Value == "Y";
                else if ((m = HoldValue.Match(field)).Success)
                {
                    statement.TotalValue = _reader.Amount(m.Groups[3].Value, m.Groups[1].Value == "N", field);
                    statement.TotalValueCurrency = m.Groups[2].Value;
                }
                continue;
            }

            if (field.StartsWith(":35B:", StringComparison.Ordinal))
            {
                // ISIN on the first line, then optional code lines (/DE/123456), then the name.
                string[] lines = field.Substring(5).Split('|');
                if ((m = Isin.Match(lines[0])).Success)
                    current.Isin = m.Groups[1].Value;
                var nameLines = new List<string>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if ((m = Wkn.Match(lines[i])).Success)
                        current.Wkn = m.Groups[1].Value;
                    else if (!lines[i].StartsWith("/", StringComparison.Ordinal))
                        nameLines.Add(lines[i].Trim());
                }
                current.Name = string.Join(" ", nameLines);
            }
            else if ((m = Quantity.Match(field)).Success)
            {
                current.IsNominal = m.Groups[1].Value == "FAMT";
                current.Quantity = _reader.Amount(m.Groups[3].Value, m.Groups[2].Value == "N", field) ?? 0m;
            }
            else if ((m = Available.Match(field)).Success)
                current.AvailableQuantity = _reader.Amount(m.Groups[2].Value, m.Groups[1].Value == "N", field);
            else if ((m = PriceAmount.Match(field)).Success)
            {
                current.PriceType = m.Groups[1].Value;
                current.PriceCurrency = m.Groups[2].Value;
                current.Price = _reader.Amount(m.Groups[3].Value, false, field);
                current.PriceIsPercent = false;
            }
            else if ((m = PricePercent.Match(field)).Success)
            {
                current.PriceType = m.Groups[1].Value;
                current.Price = _reader.Amount(m.Groups[3].Value, m.Groups[2].Value == "N", field);
                current.PriceIsPercent = true;
            }
            else if ((m = PriceDate.Match(field)).Success)
                current.PriceDate = _reader.Date(m.Groups[2].Value, field)?.Date;
            else if ((m = PriceSource.Match(field)).Success)
                current.PriceSource = m.Groups[1].Value.Trim();
            else if ((m = HoldValue.Match(field)).Success)
            {
                current.Value = _reader.Amount(m.Groups[3].Value, m.Groups[1].Value == "N", field);
                current.ValueCurrency = m.Groups[2].Value;
            }
            else if ((m = Accrued.Match(field)).Success)
            {
                current.AccruedInterest = _reader.Amount(m.Groups[3].Value, m.Groups[1].Value == "N", field);
                current.AccruedInterestCurrency = m.Groups[2].Value;
            }
            else if ((m = Exchange.Match(field)).Success)
            {
                current.ExchangeRateCurrencies = m.Groups[1].Value + "/" + m.Groups[2].Value;
                current.ExchangeRate = _reader.Amount(m.Groups[3].Value, false, field);
            }
            else if ((m = Acquisition.Match(field)).Success)
            {
                current.AcquisitionPrice = _reader.Amount(m.Groups[1].Value, false, field);
                current.AcquisitionPriceCurrency = m.Groups[2].Value;
            }
            else if ((m = Denomination.Match(field)).Success)
                current.Denomination = m.Groups[1].Value;
            else if ((m = Maturity.Match(field)).Success)
                current.MaturityDate = _reader.Date(m.Groups[1].Value, field);
            else if ((m = Interest.Match(field)).Success)
                current.InterestRate = _reader.Amount(m.Groups[2].Value, m.Groups[1].Value == "N", field);
        }
        return statement;
    }
}
