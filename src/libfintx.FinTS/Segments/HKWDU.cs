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

using System;
using System.Text;
using System.Threading.Tasks;
using libfintx.FinTS.Data;
using libfintx.FinTS.Message;
using libfintx.FinTS.Segments;
using Microsoft.Extensions.Logging;

namespace libfintx.FinTS
{
    public static class HKWDU
    {
        /// <summary>
        /// Securities transactions (Depotumsätze) in version 5: the securities account, no
        /// restriction to all accounts or one security, the period, and a touchdown point for the
        /// next part of a long statement.
        /// </summary>
        public static async Task<string> Init_HKWDU(FinTsClient client, string fromDate, string toDate, string touchdownPoint)
        {
            client.Logger.LogInformation("Starting job HKWDU: Request securities transactions");

            string segments = Segment(client, fromDate, toDate, touchdownPoint);

            if (client.BPD.IsTANRequired("HKWDU"))
            {
                client.SEGNUM = Convert.ToInt16(SEG_NUM.Seg4);
                segments = HKTAN.Init_HKTAN(client, segments, "HKWDU");
            }

            string message = FinTSMessage.Create(client, client.HNHBS, client.HNHBK, segments, client.HIRMS);
            string response = await FinTSMessage.Send(client, message);

            client.Parse_Message(response);

            return response;
        }

        /// <summary>The HKWDU segment for the client's account, without sending it.</summary>
        internal static string Segment(FinTsClient client, string fromDate, string toDate, string touchdownPoint)
        {
            var connectionDetails = client.ConnectionDetails;
            AccountInformation account = client.activeAccount ?? new AccountInformation
            {
                AccountNumber = connectionDetails.Account,
                AccountBankCode = connectionDetails.Blz.ToString(),
                SubAccountFeature = connectionDetails.SubAccount,
            };

            client.SEGNUM = Convert.ToInt16(SEG_NUM.Seg3);
            SEG sEG = new SEG();

            // Depot, all accounts (N), security (empty), from, to, maximum entries (empty), touchdown point
            string[] elements =
            {
                account.AccountNumber + DEG.Separator + account.SubAccountFeature + DEG.Separator
                    + SEG_COUNTRY.Germany + DEG.Separator + account.AccountBankCode,
                DEG.DeAdd,
                string.Empty,
                fromDate ?? string.Empty,
                toDate ?? string.Empty,
                string.Empty,
                touchdownPoint ?? string.Empty,
            };
            int used = elements.Length;
            while (used > 2 && elements[used - 1].Length == 0)
                used--;

            StringBuilder sb = new StringBuilder();
            sb.Append(string.Join(sEG.Delimiter, elements, 0, used));
            sb.Append(sEG.Terminator);

            return sEG.toSEG(new SEG_DATA
            {
                Header = "HKWDU",
                Num = client.SEGNUM,
                Version = client.HIWDUS,
                RefNum = 0,
                RawData = sb.ToString()
            });
        }
    }
}
