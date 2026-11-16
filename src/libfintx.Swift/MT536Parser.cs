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
/// Reads a statement of securities transactions in SWIFT MT536 format as FinTS returns it in
/// HIWDU: the general block, and per financial instrument (between :16R:FIN and :16S:FIN) its
/// codes and name and the transactions in it (between :16R:TRAN and :16S:TRAN) with quantity,
/// direction, payment, amount, price, dates and references. Fields a bank leaves out stay empty;
/// a value that cannot be read is skipped and logged rather than failing the statement.
/// </summary>
public class MT536Parser
{
    private readonly SwiftFieldReader _reader;

    private static readonly Regex Isin = new Regex(@"^ISIN\s+([A-Z]{2}[A-Z0-9]{9}[0-9])", RegexOptions.Compiled);
    private static readonly Regex Wkn = new Regex(@"^/DE/([A-Z0-9]{6})", RegexOptions.Compiled);
    private static readonly Regex Period = new Regex(@"^:69A::STAT//([0-9]{8})/([0-9]{8})", RegexOptions.Compiled);
    private static readonly Regex Reference = new Regex(@"^:20C::SEME//(.+)$", RegexOptions.Compiled);
    private static readonly Regex Page = new Regex(@"^:28E:([0-9]+)/([A-Z]{4})", RegexOptions.Compiled);
    private static readonly Regex Safekeeping = new Regex(@"^:97A::SAFE//(.+)$", RegexOptions.Compiled);
    private static readonly Regex Activity = new Regex(@"^:17B::ACTI//([YN])", RegexOptions.Compiled);
    private static readonly Regex Quantity = new Regex(@"^:36B::PSTA//(UNIT|FAMT)/(N?)([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex Direction = new Regex(@"^:22H::REDE//(RECE|DELI)", RegexOptions.Compiled);
    private static readonly Regex Payment = new Regex(@"^:22H::PAYM//(APMT|FREE)", RegexOptions.Compiled);
    private static readonly Regex TransactionType = new Regex(@"^:22F::TRAN//(?:[^/]*/)?([A-Z0-9]{4})", RegexOptions.Compiled);
    private static readonly Regex PostedAmount = new Regex(@"^:19A::PSTA//(N?)([A-Z]{3})([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex PriceAmount = new Regex(@"^:90B::DEAL//[A-Z]{4}/([A-Z]{3})([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex PricePercent = new Regex(@"^:90A::DEAL//[A-Z]{4}/(N?)([0-9,]+)", RegexOptions.Compiled);
    private static readonly Regex TradeDate = new Regex(@"^:98[AC]::TRAD//([0-9]{8,14})", RegexOptions.Compiled);
    private static readonly Regex EffectiveSettlement = new Regex(@"^:98[AC]::ESET//([0-9]{8,14})", RegexOptions.Compiled);
    private static readonly Regex Settlement = new Regex(@"^:98[AC]::SETT//([0-9]{8,14})", RegexOptions.Compiled);
    private static readonly Regex Related = new Regex(@"^:20C::RELA//(.+)$", RegexOptions.Compiled);
    private static readonly Regex BankReference = new Regex(@"^:20C::ASRF//(.+)$", RegexOptions.Compiled);
    private static readonly Regex Reversal = new Regex(@"^:25D::MOVE//REVE", RegexOptions.Compiled);

    /// <summary>Initializes the parser.</summary>
    /// <param name="loggerFactory">A logging factory to create the logging where to write warnings.</param>
    public MT536Parser(ILoggerFactory? loggerFactory = null)
    {
        _reader = new SwiftFieldReader(loggerFactory?.CreateLogger<MT536Parser>(), "MT536");
    }

    /// <summary>Reads the statement; text that holds no statement gives an empty one.</summary>
    public MT536 Parse(string? mt536)
    {
        var statement = new MT536();
        // The instrument of the FIN block the transactions belong to
        var instrument = new MT536Transaction();
        MT536Transaction? current = null;
        bool inInstrument = false;

        foreach (string field in SwiftFieldReader.Fields(mt536 ?? string.Empty))
        {
            if (SwiftFieldReader.IsBlock(field, ":16R:FIN"))
            {
                instrument = new MT536Transaction();
                inInstrument = true;
                continue;
            }
            if (SwiftFieldReader.IsBlock(field, ":16S:FIN"))
            {
                inInstrument = false;
                continue;
            }
            if (SwiftFieldReader.IsBlock(field, ":16R:TRAN"))
            {
                current = new MT536Transaction
                {
                    Isin = instrument.Isin,
                    Wkn = instrument.Wkn,
                    Name = instrument.Name,
                };
                continue;
            }
            if (SwiftFieldReader.IsBlock(field, ":16S:TRAN"))
            {
                if (current != null)
                    statement.Transactions.Add(Signed(current));
                current = null;
                continue;
            }

            Match m;
            if (field.StartsWith(":35B:", StringComparison.Ordinal))
            {
                // Banks name the instrument in the FIN block; one in a transaction applies to it alone.
                var target = current ?? instrument;
                ReadInstrument(field, target);
                continue;
            }

            if (current == null)
            {
                if (inInstrument)
                    continue;
                if ((m = Period.Match(field)).Success)
                {
                    statement.PeriodStart = _reader.Date(m.Groups[1].Value, field);
                    statement.PeriodEnd = _reader.Date(m.Groups[2].Value, field);
                }
                else if ((m = Reference.Match(field)).Success)
                    statement.Reference = m.Groups[1].Value.Trim();
                else if ((m = Page.Match(field)).Success)
                {
                    statement.PageNumber = int.TryParse(m.Groups[1].Value, out int page) ? page : null;
                    statement.IsLastPage = m.Groups[2].Value is "LAST" or "ONLY";
                }
                else if ((m = Safekeeping.Match(field)).Success)
                    statement.SafekeepingAccount = m.Groups[1].Value.Trim();
                else if ((m = Activity.Match(field)).Success)
                    statement.HasTransactions = m.Groups[1].Value == "Y";
                continue;
            }

            if ((m = Quantity.Match(field)).Success)
            {
                current.IsNominal = m.Groups[1].Value == "FAMT";
                current.Quantity = _reader.Amount(m.Groups[3].Value, m.Groups[2].Value == "N", field) ?? 0m;
            }
            else if ((m = Direction.Match(field)).Success)
                current.IsReceipt = m.Groups[1].Value == "RECE";
            else if ((m = Payment.Match(field)).Success)
                current.AgainstPayment = m.Groups[1].Value == "APMT";
            else if ((m = TransactionType.Match(field)).Success)
                current.TransactionType = m.Groups[1].Value;
            else if ((m = PostedAmount.Match(field)).Success)
            {
                current.Amount = _reader.Amount(m.Groups[3].Value, m.Groups[1].Value == "N", field);
                current.AmountCurrency = m.Groups[2].Value;
            }
            else if ((m = PriceAmount.Match(field)).Success)
            {
                current.PriceCurrency = m.Groups[1].Value;
                current.Price = _reader.Amount(m.Groups[2].Value, false, field);
                current.PriceIsPercent = false;
            }
            else if ((m = PricePercent.Match(field)).Success)
            {
                current.Price = _reader.Amount(m.Groups[2].Value, m.Groups[1].Value == "N", field);
                current.PriceIsPercent = true;
            }
            else if ((m = TradeDate.Match(field)).Success)
                current.TradeDate = _reader.Date(m.Groups[1].Value, field)?.Date;
            else if ((m = EffectiveSettlement.Match(field)).Success)
                current.SettlementDate = _reader.Date(m.Groups[1].Value, field)?.Date;
            else if ((m = Settlement.Match(field)).Success)
                current.SettlementDate ??= _reader.Date(m.Groups[1].Value, field)?.Date;
            else if ((m = Related.Match(field)).Success)
                current.RelatedReference = m.Groups[1].Value.Trim();
            else if ((m = BankReference.Match(field)).Success)
                current.BankReference = m.Groups[1].Value.Trim();
            else if (Reversal.IsMatch(field))
                current.IsReversal = true;
        }
        return statement;
    }

    // ISIN on the first line, then optional code lines (/DE/123456), then the name.
    private static void ReadInstrument(string field, MT536Transaction target)
    {
        string[] lines = field.Substring(5).Split('|');
        Match m;
        if ((m = Isin.Match(lines[0])).Success)
            target.Isin = m.Groups[1].Value;
        var nameLines = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            if ((m = Wkn.Match(lines[i])).Success)
                target.Wkn = m.Groups[1].Value;
            else if (!lines[i].StartsWith("/", StringComparison.Ordinal))
                nameLines.Add(lines[i].Trim());
        }
        target.Name = string.Join(" ", nameLines);
    }

    // The quantity comes unsigned with the direction beside it; a delivery leaves the account.
    private static MT536Transaction Signed(MT536Transaction transaction)
    {
        if (transaction.IsReceipt == false && transaction.Quantity > 0)
            transaction.Quantity = -transaction.Quantity;
        return transaction;
    }
}
