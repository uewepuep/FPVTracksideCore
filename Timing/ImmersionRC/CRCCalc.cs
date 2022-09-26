/*
 * CRC Calulator, following the standard required for the LapRF Interface

 * This file is part of LapRFCSharpDemo.
 *
 * LapRFCSharpDemo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * LapRFCSharpDemo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with LapRFCSharpDemo.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Diagnostics;
public class CRCCalc
{
    public UInt16[] crc16_table;

    //-------------------------------------------------------------------------------------------------------
    public CRCCalc()
    {
        init_crc16_table();

        unitTest();
    }

    //-------------------------------------------------------------------------------------------------------
    public void init_crc16_table()
    {
        UInt16 remainder = 0;
        crc16_table = new UInt16[256];

        for (UInt16 i = 0; i < 256; i += 1)
        {
            remainder = (UInt16)((i << 8) & 0xFF00);
            for (int j = 8; j > 0; --j)
            {
                if ((remainder & (UInt16)0x8000) == (UInt16)0x8000)
                    remainder = (UInt16) (((remainder << 1) & (UInt16)0xFFFF) ^ (UInt16)0x8005);
                else
                    remainder = (UInt16) (((remainder << 1) & 0xFFFF));
            }

            crc16_table[i] = remainder;
        }
    }

    //-------------------------------------------------------------------------------------------------------
    UInt16 reflect(UInt16 input, int nbits)
    {
        UInt16 output = 0;
        for (int i = 0; i < nbits; i++)
        {
            if ( (input & (UInt16)0x01) == (UInt16)0x01)
            {
                output |= (UInt16) (1 << ((nbits - 1) - i) );
            }

            input = (UInt16) (input >> 1);
        }
    
        return output;
    }

    //-------------------------------------------------------------------------------------------------------
    public UInt16 compute_crc16(Byte[] dataIn, int length)
    {
        UInt16 remainder = (UInt16)0x0000;

        for (int i = 0; i < length; i++)
        {
            UInt16 a = reflect(dataIn[i], 8);
            a &= 0xff;
            UInt16 b = (UInt16) ((remainder >> 8) & ((UInt16)0xFF));
            UInt16 c = (UInt16)((remainder << 8) & 0xFFFF);
            UInt16 data = (UInt16) ((int) a ^ (int) b);
            remainder = (UInt16) ((int) crc16_table[data] ^ (int) c);
        }

        return reflect(remainder, 16);
    }

    //-------------------------------------------------------------------------------------------------------
    public void unitTest()
    {
        Byte[] bytes = { 0x5a, 0x3d,
               0x00,0x00,0x00,0x0a,0xda,0x21,0x02,
               0x3c,0x0d,0x23,0x01,0x01,0x24,0x04,
               0x00,0x00,0x00,0x00,0x01,0x01,0x01,
               0x22,0x04,0x00,0x80,0x62,0x44,0x01,
               0x01,
               0x02,0x22,0x04,0x00,0x00,0x62,0x44,
               0x01,0x01,0x03,0x22,0x04,0x00,0x80,
               0x6a,
               0x44,0x01,0x01,0x04,0x22,0x04,0x00,
               0x00,0x62,0x44,0x03,0x02,0x00,0x00,
               0x5b};

        UInt16 retVal = compute_crc16(bytes, bytes.Length);
        if(retVal != 0x1b53)
        {
            Debug.Print("CRC Unit Test Mismatch");
        }
    }
}