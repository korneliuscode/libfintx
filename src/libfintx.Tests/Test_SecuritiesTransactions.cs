using System;
using libfintx.FinTS;
using libfintx.FinTS.Data;
using libfintx.Swift;
using Xunit;

namespace libfintx.Tests;

/// <summary>
/// The securities transactions: the HKWDU request and the MT536 answer. The message is made up in
/// the MT536 layout German banks send in HIWDU: a general block, then per instrument its
/// transactions.
/// </summary>
[Collection("UPD")]
public class Test_SecuritiesTransactions
{
    private const string Statement =
        ":16R:GENL\r\n" +
        ":28E:1/ONLY\r\n" +
        ":13A::STAT//001\r\n" +
        ":20C::SEME//NONREF\r\n" +
        ":23G:NEWM\r\n" +
        ":69A::STAT//20260101/20261116\r\n" +
        ":22F::CODE//COMP\r\n" +
        ":97A::SAFE//10050000/1234567890\r\n" +
        ":17B::ACTI//Y\r\n" +
        ":16S:GENL\r\n" +
        ":16R:SUBSAFE\r\n" +
        ":16R:FIN\r\n" +
        ":35B:ISIN IE00B4L5Y983\r\n" +
        "/DE/A0RPWH\r\n" +
        "ISHSIII-CORE MSCI WORLD U.ETF\r\n" +
        ":16R:TRAN\r\n" +
        ":16R:LINK\r\n" +
        ":20C::RELA//ORDER-4711\r\n" +
        ":16S:LINK\r\n" +
        ":16R:TRANSDET\r\n" +
        ":36B::PSTA//UNIT/12,5\r\n" +
        ":19A::PSTA//NEUR1301,50\r\n" +
        ":22F::TRAN//SETT\r\n" +
        ":22H::REDE//RECE\r\n" +
        ":22H::PAYM//APMT\r\n" +
        ":98A::TRAD//20260310\r\n" +
        ":98A::ESET//20260312\r\n" +
        ":90B::DEAL//ACTU/EUR104,12\r\n" +
        ":20C::ASRF//B-1\r\n" +
        ":16S:TRANSDET\r\n" +
        ":16S:TRAN\r\n" +
        ":16R:TRAN\r\n" +
        ":16R:TRANSDET\r\n" +
        ":36B::PSTA//UNIT/2,\r\n" +
        ":19A::PSTA//EUR230,\r\n" +
        ":22F::TRAN//SETT\r\n" +
        ":22H::REDE//DELI\r\n" +
        ":22H::PAYM//APMT\r\n" +
        ":98C::ESET//20261020120000\r\n" +
        ":16S:TRANSDET\r\n" +
        ":16S:TRAN\r\n" +
        ":16S:FIN\r\n" +
        ":16R:FIN\r\n" +
        ":35B:ISIN DE0001102580\r\n" +
        "BUNDESREP.DEUTSCHLAND ANL.V.2022(2032)\r\n" +
        ":16R:TRAN\r\n" +
        ":16R:TRANSDET\r\n" +
        ":36B::PSTA//FAMT/5000,\r\n" +
        ":22F::TRAN//CORP\r\n" +
        ":22H::REDE//RECE\r\n" +
        ":22H::PAYM//FREE\r\n" +
        ":98A::SETT//20260601\r\n" +
        ":90A::DEAL//PRCT/97,85\r\n" +
        ":25D::MOVE//REVE\r\n" +
        ":16S:TRANSDET\r\n" +
        ":16S:TRAN\r\n" +
        ":16S:FIN\r\n" +
        ":16S:SUBSAFE\r\n" +
        "-";

    [Fact]
    public void TheGeneralBlock_GivesPeriodAndAccount()
    {
        MT536 statement = new MT536Parser().Parse(Statement);

        Assert.Equal(new DateTime(2026, 1, 1), statement.PeriodStart);
        Assert.Equal(new DateTime(2026, 11, 16), statement.PeriodEnd);
        Assert.Equal("NONREF", statement.Reference);
        Assert.Equal(1, statement.PageNumber);
        Assert.True(statement.IsLastPage);
        Assert.Equal("10050000/1234567890", statement.SafekeepingAccount);
        Assert.True(statement.HasTransactions);
        Assert.Equal(3, statement.Transactions.Count);
    }

    [Fact]
    public void APurchase_HasItsInstrumentQuantityAmountPriceAndDates()
    {
        MT536Transaction purchase = new MT536Parser().Parse(Statement).Transactions[0];

        Assert.Equal("IE00B4L5Y983", purchase.Isin);
        Assert.Equal("A0RPWH", purchase.Wkn);
        Assert.Equal("ISHSIII-CORE MSCI WORLD U.ETF", purchase.Name);
        Assert.Equal(12.5m, purchase.Quantity);
        Assert.False(purchase.IsNominal);
        Assert.True(purchase.IsReceipt);
        Assert.True(purchase.AgainstPayment);
        Assert.Equal("SETT", purchase.TransactionType);
        Assert.Equal(-1301.50m, purchase.Amount);
        Assert.Equal("EUR", purchase.AmountCurrency);
        Assert.Equal(104.12m, purchase.Price);
        Assert.Equal("EUR", purchase.PriceCurrency);
        Assert.Equal(new DateTime(2026, 3, 10), purchase.TradeDate);
        Assert.Equal(new DateTime(2026, 3, 12), purchase.SettlementDate);
        Assert.Equal("ORDER-4711", purchase.RelatedReference);
        Assert.Equal("B-1", purchase.BankReference);
        Assert.False(purchase.IsReversal);
    }

    [Fact]
    public void ASale_LeavesTheAccount_WithANegativeQuantity()
    {
        MT536Transaction sale = new MT536Parser().Parse(Statement).Transactions[1];

        Assert.Equal("IE00B4L5Y983", sale.Isin);
        Assert.False(sale.IsReceipt);
        Assert.Equal(-2m, sale.Quantity);
        Assert.Equal(230m, sale.Amount);
        Assert.Equal(new DateTime(2026, 10, 20), sale.SettlementDate);
        Assert.Null(sale.Price);
    }

    [Fact]
    public void ABondReversal_HasItsNominalPercentPriceAndSettlementDay()
    {
        MT536Transaction bond = new MT536Parser().Parse(Statement).Transactions[2];

        Assert.Equal("DE0001102580", bond.Isin);
        Assert.Equal("BUNDESREP.DEUTSCHLAND ANL.V.2022(2032)", bond.Name);
        Assert.True(bond.IsNominal);
        Assert.Equal(5000m, bond.Quantity);
        Assert.False(bond.AgainstPayment);
        Assert.Equal("CORP", bond.TransactionType);
        Assert.True(bond.PriceIsPercent);
        Assert.Equal(97.85m, bond.Price);
        Assert.Equal(new DateTime(2026, 6, 1), bond.SettlementDate);
        Assert.True(bond.IsReversal);
        Assert.Null(bond.Amount);
    }

    [Fact]
    public void AQuantityWithoutDirection_StaysAsTheBankSendsIt()
    {
        var statement = new MT536Parser().Parse(
            ":16R:FIN\n:35B:ISIN DE0008404005\nALLIANZ SE\n:16R:TRAN\n:16R:TRANSDET\n:36B::PSTA//UNIT/3,\n:16S:TRANSDET\n:16S:TRAN\n:16S:FIN\n-");

        MT536Transaction transaction = Assert.Single(statement.Transactions);
        Assert.Null(transaction.IsReceipt);
        Assert.Equal(3m, transaction.Quantity);
    }

    [Fact]
    public void AnAmountThatCannotBeRead_IsSkipped_NotTheStatement()
    {
        var statement = new MT536Parser().Parse(
            ":16R:FIN\n:35B:ISIN DE0008404005\nALLIANZ SE\n:16R:TRAN\n:16R:TRANSDET\n:36B::PSTA//UNIT/3,\n:19A::PSTA//EUR1,2,3\n:22H::REDE//RECE\n:16S:TRANSDET\n:16S:TRAN\n:16S:FIN\n-");

        MT536Transaction transaction = Assert.Single(statement.Transactions);
        Assert.Equal(3m, transaction.Quantity);
        Assert.Null(transaction.Amount);
    }

    [Fact]
    public void TheMt536_IsTakenFromTheBinaryOfHiwdu()
    {
        string mt536 = ":16R:FIN\r\n:35B:ISIN DE0008404005\r\nALLIANZ\r\n:16S:FIN\r\n-";
        string response = "HIRMG:2:2+0010::Nachricht entgegengenommen.'HIWDU:4:5:3+@" + mt536.Length + "@" + mt536 + "'HNHBS:5:1+1'";

        Assert.Equal(mt536 + "\n", FinTsClient.SwiftOf(response, "HIWDU"));
    }

    [Fact]
    public void TheRequest_NamesTheSecuritiesAccountAndThePeriod()
    {
        var client = TestHelper.CreateTestClient(new ConnectionDetails { Account = "1234567890", Blz = 10050000 });
        client.HIWDUS = 5;

        Assert.Equal("HKWDU:3:5+1234567890::280:10050000+N'", HKWDU.Segment(client, null, null, null));
        Assert.Equal("HKWDU:3:5+1234567890::280:10050000+N++20260101+20261116'", HKWDU.Segment(client, "20260101", "20261116", null));
        Assert.Equal("HKWDU:3:5+1234567890::280:10050000+N++20260101+20261116++ABC123'", HKWDU.Segment(client, "20260101", "20261116", "ABC123"));
    }
}
