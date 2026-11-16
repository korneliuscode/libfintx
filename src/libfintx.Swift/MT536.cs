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

namespace libfintx.Swift;

/// <summary>
/// A statement of securities transactions in SWIFT MT536 format (Depotumsätze): the general block
/// with the period and the account, and the transactions, each with its financial instrument.
/// </summary>
public sealed class MT536
{
    /// <summary>The first day of the period (:69A::STAT), or null when the bank sends none.</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>The last day of the period (:69A::STAT), or null when the bank sends none.</summary>
    public DateTime? PeriodEnd { get; set; }

    /// <summary>The bank's reference of the statement (:20C::SEME), such as "NONREF".</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>The page of a statement sent in parts (:28E:), or null when the bank sends none.</summary>
    public int? PageNumber { get; set; }

    /// <summary>True when this is the last or only page (:28E: "LAST" or "ONLY").</summary>
    public bool IsLastPage { get; set; } = true;

    /// <summary>The safekeeping account the statement is for (:97A::SAFE), as the bank writes it.</summary>
    public string SafekeepingAccount { get; set; } = string.Empty;

    /// <summary>Whether the account had any transactions in the period (:17B::ACTI), or null when the bank does not say.</summary>
    public bool? HasTransactions { get; set; }

    /// <summary>The transactions in the order the bank sends them.</summary>
    public List<MT536Transaction> Transactions { get; } = new List<MT536Transaction>();
}

/// <summary>One transaction of a securities account, as far as the bank reports it.</summary>
public sealed class MT536Transaction
{
    /// <summary>The ISIN of the financial instrument (:35B:).</summary>
    public string Isin { get; set; } = string.Empty;

    /// <summary>The German securities code (WKN) from a "/DE/" line of :35B:, if the bank sends one.</summary>
    public string Wkn { get; set; } = string.Empty;

    /// <summary>The name of the security (:35B:, the lines after the codes).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Shares, or for bonds the nominal amount (see <see cref="IsNominal"/>) (:36B::PSTA): negative
    /// for a delivery, positive otherwise.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>True when <see cref="Quantity"/> is a nominal amount (FAMT) rather than a number of shares (UNIT).</summary>
    public bool IsNominal { get; set; }

    /// <summary>
    /// True for a receipt into the account (:22H::REDE//RECE), false for a delivery (DELI), null
    /// when the bank does not say.
    /// </summary>
    public bool? IsReceipt { get; set; }

    /// <summary>True when the transaction was settled against payment (:22H::PAYM//APMT), false when free of payment.</summary>
    public bool AgainstPayment { get; set; }

    /// <summary>The kind of transaction (:22F::TRAN), such as "SETT" for a trade or "CORP" for a corporate action.</summary>
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>The amount booked for the transaction (:19A::PSTA), or null when the bank sends none.</summary>
    public decimal? Amount { get; set; }

    /// <summary>The currency of <see cref="Amount"/>.</summary>
    public string AmountCurrency { get; set; } = string.Empty;

    /// <summary>The price of the deal, or null (:90B::DEAL or :90A::DEAL).</summary>
    public decimal? Price { get; set; }

    /// <summary>The currency of <see cref="Price"/>; empty for a price in percent.</summary>
    public string PriceCurrency { get; set; } = string.Empty;

    /// <summary>True when the price is a percentage of the nominal amount, as for bonds.</summary>
    public bool PriceIsPercent { get; set; }

    /// <summary>The day of the trade (:98A::TRAD or :98C::TRAD), or null.</summary>
    public DateTime? TradeDate { get; set; }

    /// <summary>The day the transaction was settled (:98A::ESET, else :98A::SETT), or null.</summary>
    public DateTime? SettlementDate { get; set; }

    /// <summary>The reference of the order the transaction belongs to (:20C::RELA), or empty.</summary>
    public string RelatedReference { get; set; } = string.Empty;

    /// <summary>The bank's reference of the transaction (:20C::ASRF), or empty.</summary>
    public string BankReference { get; set; } = string.Empty;

    /// <summary>True when the transaction reverses an earlier one (:25D::MOVE//REVE).</summary>
    public bool IsReversal { get; set; }
}
